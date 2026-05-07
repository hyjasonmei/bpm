## Why

The spec layer defines `Notification` (trigger / channel / recipients / template) since v1.0 and the wizard's `StepNotify` lets users author them. But the system has no way to *actually deliver* a notification:

- **Backend**: zero notification entities / services / channels. `bpm-svc/src/**/*.cs` has no `Notification`, no `INotificationDispatcher`, no `IEmailSender`, no in-app delivery table. Notifications exist only as spec JSON; runtime emission was never implemented.
- **Wizard**: `RecipientsEditor` only supports 4 recipient types (`submitter`, `current_approver`, `role`, `user`) — but `spec_schema.md` declares `recipients = ActorRef`, which is 6 types today and will be 10 after the three pending proposals land. AI can suggest "通知財務部所有人" via chat but the wizard has no UI to capture it.
- **Validator**: `validators.notify` always returns valid — empty recipients pass, body Mustache placeholders not in `variables[]` pass, missing subject passes.
- **Frontend Bell**: `AppLayout` has a Bell icon for show — no dropdown, no list, no unread count, no API behind it.
- **Channels**: no SMTP, no Teams webhook, no in-app fan-out, no audit log.

In other words: nine flows can describe their notifications in spec.json, but no one will ever receive one. This change adds a working notification engine that delivers in-app and email today (Teams deferred), with a Mustache renderer, a delivery audit trail, and a wizard / Bell UI that exposes the new capability end-to-end.

## What Changes

### Spec layer alignment (`bpm-actor-dsl`)

Notification recipients SHALL use the same ActorRef discriminated union the rest of the spec uses, with two notification-specific runtime types added — analogous to `ViewerRef` in the userTask change:

```typescript
type NotifyRecipientRef =
  | { type: 'submitter' }                  // runtime — flow initiator
  | { type: 'current_approver' }           // runtime — whoever holds the open approval at trigger time
  | { type: 'current_assignee' }           // runtime — whoever currently holds an open userTask
  | { type: 'actor', inner: ActorRef }     // any ActorRef (role / functional_head / collection / ...)
```

This unifies the vocabulary with `ViewerRef` (from `extend-usertask-assignee-by-role`) — same pattern: three runtime-scoped types plus `actor` wrapping a regular ActorRef. The validator rejects unknown `type` values; the resolver knows how to handle each.

`spec_schema.md` §2.6 updated. The legacy 4-type wizard format migrates per cheat-sheet (importer auto-translates).

### Notification engine (NEW capability `bpm-notification-engine`)

Backend gets the missing pieces:

**Entities** (in `Bpm.Domain.Entities.Notification`):
- `NotificationDelivery` — one row per (notification_spec_id, target_user_id, channel) emission. Columns: `Id, FlowInstanceId (nullable until ProcessRuntime ships), NotificationSpecId, NotificationSpecRevision, TargetUserId, Channel, Subject, Body, Variables (JSON), Trigger, Status (Queued/Sent/Failed/Read/Dismissed), Attempts, LastAttemptAt, NextAttemptAt, ErrorReason, CreatedAt, ReadAt, DismissedAt`
- `NotificationDispatchAudit` — append-only audit per dispatch call (independent of delivery rows; one dispatch may produce many delivery rows). Columns: `Id, FlowInstanceId, NotificationSpecId, Trigger, ResolvedRecipientCount, ContextJson, RequestedAt, CompletedAt, Status, ErrorReason`

Both tables include `tenant_id` even though we are single-tenant today — keeps the schema consistent for the future multi-tenant change.

**Services** (in `Bpm.Application.Notifications`):
- `INotificationDispatcher` — entry point. Given `(notificationSpec, ctx)` it: (a) resolves recipients via `IActorResolver` (extended for the notify-recipient runtime types), (b) renders subject/body via `IMustacheRenderer`, (c) writes one `NotificationDelivery` row per (target, channel) tuple in `Queued` status, (d) writes one `NotificationDispatchAudit` row, (e) hands the queued deliveries to channel adapters.
- `IMustacheRenderer` — wraps Stubble. Renders `subject`/`body` against the variables map, returns the rendered string + a list of unbound placeholders (so we can fail loud rather than silently leave `{{foo}}` in the email).
- `INotificationChannel` — port for channel adapters. Implementations:
  - `InAppNotificationChannel` — writes to `NotificationDelivery` with `Status = Sent` + `ReadAt = null` (the polling endpoint surfaces unread to the Bell dropdown).
  - `EmailNotificationChannel` — sends via `IEmailSender` abstraction; on success → `Status = Sent`, on failure → `Status = Failed` + `ErrorReason`.
- `IEmailSender` — port. Two implementations:
  - `SmtpEmailSender` (development, default) — talks to MailHog at `localhost:1025` for local testing without leaking real emails.
  - `ResendEmailSender` (production) — uses Resend HTTP API with `RESEND_API_KEY` env var. Why Resend? Free tier (3000 emails / month), single API key, modern dx, no SPF mess for proof-of-concept. Swap to SendGrid / SES later if customer compliance demands.

Trigger event hook is *deferred* until ProcessRuntime ships. In the interim, the dispatcher is callable directly via `POST /api/notifications/dispatch` (admin-only, internal) so the future ProcessRuntime can plug in. A development helper `POST /api/notifications/dev-fire` lets us exercise dispatching against a sample spec without a running flow instance.

**API endpoints** (in `Bpm.Api.Notifications`):
- `POST /api/notifications/dispatch` — admin/internal. Body: `{ specId, trigger, contextJson, flowInstanceId? }`. Loads the notification spec, resolves recipients, dispatches.
- `POST /api/notifications/dev-fire` — `BPM_AUTH_MODE=dev` only. Body: `{ notificationJson, contextJson }`. Skips spec lookup; takes the notification inline. For wizard preview / smoke tests.
- `GET /api/notifications/inbox?unread=true&limit=50` — current user's inbox; returns delivery rows for `TargetUserId = current user, Channel = "in_app"`.
- `POST /api/notifications/{id}/read` — marks delivery as read.
- `POST /api/notifications/{id}/dismiss` — marks dismissed (removes from Bell dropdown).

**Background processing**: a hosted service `NotificationDispatchWorker` polls `NotificationDelivery` for `Status = Queued, NextAttemptAt <= now`, dispatches them through the appropriate channel, retries on failure with exponential backoff (2 min → 10 min → 1 hr; cap 3 attempts before `Failed` terminal state). Single-instance for now; lock via SQLite `BEGIN EXCLUSIVE` or a marker row.

### Wizard upgrade (`bpm-form-stepper`)

`StepNotify` recipient editor swaps to `<NotifyRecipientEditor>` — built on top of `ActorRefEditor` from the prior proposals:

- Type picker shows: 申請人 / 當前審核者 / 當前 assignee / 角色 / 群組 / 上級主管 / 部門功能主管 / 部門功能成員 / 條件式 / 合議 / 待釐清 (when AI inserts)
- The first three (`submitter` / `current_approver` / `current_assignee`) are runtime-scoped chips; the rest dispatch to existing `ActorRefEditor` child editors
- `expr` / `functional_head` / `functional_members` reuse the editors from the prior proposals — zero new sub-component effort

`StepNotify` validator becomes substantive (`bpm-form-stepper` capability):

- Subject required (zh-TW)
- Body required (zh-TW)
- Recipients non-empty
- Body's `{{var}}` placeholders must equal `variables[]` (set equality, not subset — wizard offers an "auto-detect variables" button that re-extracts from subject + body)
- Channel non-empty (at least one of email / in_app / teams)
- Trigger value in the enum

In-place "Preview" button calls `/api/notifications/dev-fire` with sample variables and shows the rendered subject/body in a dialog — instant feedback without leaving the wizard.

### Bell dropdown (`bpm-ui-shell`)

`AppLayout` Bell icon becomes interactive:

- Click → dropdown panel listing the user's unread `in_app` deliveries (newest first, capped at 20 in panel; "View all" → `/notifications` page)
- Each row: subject (one-line truncated) + relative timestamp + a "✓" mark-read button
- Unread count badge on the bell icon (shown when > 0)
- Polling: every 60s while the app is open; immediate refresh after the user marks one read
- New `Notifications` screen (`bpm-ui/src/screens/Notifications.tsx`) for full inbox with filtering by date/trigger

Polling beats SignalR for v1 — no infrastructure dependency, easy to reason about, fast enough for SME-scale (10s of notifications/day).

### Sample specs

Update the existing samples (`leave_v1`, `purchase_v1`) to migrate the 4-legacy-type recipients to the new `NotifyRecipientRef` shape (importer handles the transformation; samples updated for clarity). Add three new sample specs covering more recipient shapes:

- `expense_employee_v1` (from previous proposal): `notify_finance_team` uses `{ type: 'actor', inner: { type: 'functional_members', function_tag: 'finance' } }`
- `it_request_v1` (from previous proposal): `notify_requester` uses `submitter`; `notify_it_team` uses `functional_members:it`
- `travel_request_v1` (from previous proposal): `notify_admin` uses `functional_members:general_affairs`

### Out of scope (future changes)

- Teams webhook channel — design is in place (port + adapter), implementation deferred until a customer asks
- Per-user notification preferences (mute / digest / channel pick-and-mix) — needs a UserPreferences table
- SignalR / WebSocket push (replaces polling) — needs infra
- Cross-tenant tenant_id propagation enforcement (multi-tenant change)
- ProcessRuntime trigger hooks (process-instance change)
- Notification retry policy customization per spec
- Email DKIM / SPF setup — Resend handles for the test domain
- Notification i18n switching (subject/body picks zh-TW or en based on user locale) — current samples are zh-TW only; minimal hook in renderer for follow-up
- Attachment handling on emails (file fields in forms) — schema allows it via `variables` referencing file IDs but renderer doesn't yet attach

## Capabilities

### New Capabilities

- `bpm-notification-engine` — entities (NotificationDelivery, NotificationDispatchAudit), dispatcher service, Mustache renderer, channel adapters (in-app, email), email senders (SMTP/MailHog dev, Resend prod), background worker, REST endpoints (dispatch, dev-fire, inbox, read, dismiss).

### Modified Capabilities

- `bpm-actor-dsl` — adds `NotifyRecipientRef` discriminated union (4 variants); aligns spec_schema.md §2.6 with ActorRef vocabulary.
- `bpm-workflow-resolver` — extends resolver to handle the three runtime-scoped notify recipient types (`submitter`, `current_approver`, `current_assignee`) by reading from `NotificationContext`; the `actor` variant delegates to existing `IActorResolver`.
- `bpm-form-stepper` — `StepNotify` recipient editor switches to `NotifyRecipientEditor` (composing existing `ActorRefEditor`); validator becomes substantive (subject / body / recipients / variable-set / channel / trigger checks); inline "Preview" button.
- `bpm-ui-shell` — `AppLayout` Bell icon becomes interactive (dropdown + unread badge + polling); new `Notifications` screen for full inbox; new `apiFetch` helpers for the inbox endpoints.

## Impact

- **bpm-svc/src/Domain/Entities/Notification/**: new `NotificationDelivery.cs`, `NotificationDispatchAudit.cs`, `DeliveryStatus.cs` enum
- **bpm-svc/src/Domain/Spec/NotifyRecipientRef.cs**: new discriminated union + JSON converter
- **bpm-svc/src/Application/Notifications/**: `INotificationDispatcher.cs`, `NotificationDispatcher.cs`, `IMustacheRenderer.cs`, `MustacheRenderer.cs`, `INotificationChannel.cs`, `IEmailSender.cs`, `NotifyRecipientResolver.cs` (extends IActorResolver semantics for runtime types)
- **bpm-svc/src/Persistence/Configurations/Notification/**: EF configs for the two new tables; migration `AddNotificationEngine`
- **bpm-svc/src/Infrastructure/Notifications/**: `InAppNotificationChannel.cs`, `EmailNotificationChannel.cs`, `SmtpEmailSender.cs`, `ResendEmailSender.cs`, `NotificationDispatchWorker.cs` (hosted service)
- **bpm-svc/src/Api/Notifications/**: `NotificationDispatchController.cs`, `NotificationInboxController.cs`
- **bpm-ui/src/lib/onboarding.ts**: `NotifyRecipient` type extended; existing 4-type union replaced with `NotifyRecipientRef` (TypeScript discriminated union)
- **bpm-ui/src/screens/onboarding/steps/StepNotify.tsx**: `RecipientsEditor` rewritten to use `NotifyRecipientEditor`; validator strengthened; "Preview" button calling `/api/notifications/dev-fire`
- **bpm-ui/src/components/wizard/NotifyRecipientEditor.tsx**: NEW — wraps `ActorRefEditor` plus the three runtime chips
- **bpm-ui/src/components/AppLayout.tsx**: Bell icon gets onClick + dropdown content
- **bpm-ui/src/components/NotificationBellDropdown.tsx**: NEW component
- **bpm-ui/src/screens/Notifications.tsx**: NEW screen for full inbox
- **bpm-ui/src/lib/notifications.ts**: NEW — API client (`fetchInbox`, `markRead`, `markDismissed`); polling hook `useNotificationPolling`
- **bpm-svc/src/Api/Program.cs**: register dispatcher, channels, worker, EmailSender impl based on `BPM_EMAIL_BACKEND` env (`dev-mailhog` | `prod-resend`)
- **spec_schema.md** §2.6: updated `NotifyRecipient` shape; bilingual subject/body remains; migration cheat-sheet from old 4-type to new typed shape
- **prompt_template_v1.md**: new section "When to use which recipient type" with worked examples
- **sample_specs/**: 2 updated, 3 already-pending samples now exercise the full recipient vocabulary
- **DB migration** `AddNotificationEngine` is purely additive (no changes to existing tables)
- **bpm-svc dependencies added**: `Stubble.Core` (Mustache), `MailKit` (SMTP for dev), `System.Net.Http.Json` (Resend); roughly +3 NuGet packages
- **bpm-ui dependencies**: none new (no SignalR; polling uses existing `apiFetch`)
- **No breaking change to running 9-flow demo** — `Home.tsx`, `forms/*`, `Search.tsx`, `Report.tsx`, `lib/workflow.ts` not modified. `AppLayout`'s Bell icon visual stays the same when there are 0 notifications (badge is hidden).

## Coverage check

After this change + the three pending proposals:

| Flow | Notifications expressible? |
|---|---|
| LEAVE | ✅ on_assign → current_approver; on_complete → submitter; on_reject → submitter |
| GEE / GEV / APE / TEO | ✅ on_assign → current_approver; on_complete → submitter; on_sla_breach → functional_head:finance (escalation reminder) |
| HWP / ITPR | ✅ on_assign → current_approver per stage; on_complete → submitter + functional_members:procurement (PO ready) |
| TRQ | ✅ on_complete → functional_members:general_affairs (so admin team prepares travel) |
| EXTOB | ✅ on_assign → functional_members:hr; on_complete → submitter (manager) + the new hire (via expr:`form.new_hire_email_field`) |

All nine flow notification shapes become expressible *and* deliverable end-to-end through in-app + email channels.
