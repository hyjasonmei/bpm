# Tasks

## 1. Spec layer — NotifyRecipientRef

- [ ] 1.1 Update `spec_schema.md` §2.6: replace the loose `NotifyRecipient` definition with `NotifyRecipientRef` discriminated union (4 variants: submitter / current_approver / current_assignee / actor)
- [ ] 1.2 Add migration cheat-sheet from legacy 4-type recipient to NotifyRecipientRef:
  - `'submitter'` → `{ type: 'submitter' }`
  - `'current_approver'` → `{ type: 'current_approver' }`
  - `'role:X'` → `{ type: 'actor', inner: { type: 'role', code: 'X' } }`
  - `'user:X'` → `{ type: 'actor', inner: { type: 'user', id: 'X' } }`
- [ ] 1.3 Bump `meta.schemaVersion` to v1.4 (after v1.3 from line-items)

## 2. Backend — domain types

- [ ] 2.1 Create `bpm-svc/src/Domain/Spec/NotifyRecipientRef.cs` — abstract record with 4 derived (`SubmitterRecipient`, `CurrentApproverRecipient`, `CurrentAssigneeRecipient`, `ActorRecipient(ActorRef Inner)`)
- [ ] 2.2 Add JSON converter `NotifyRecipientRefJsonConverter` (polymorphic on `type` field)
- [ ] 2.3 Round-trip serialization tests
- [ ] 2.4 Create `bpm-svc/src/Domain/Spec/NotificationContext.cs` record with FlowInstanceId, SubmitterUserId, CurrentApproverUserId, CurrentAssigneeUserId, Variables

## 3. Backend — notification entities

- [ ] 3.1 Create `bpm-svc/src/Domain/Entities/Notification/DeliveryStatus.cs` enum: Queued, Sent, Read, Dismissed, Failed
- [ ] 3.2 Create `bpm-svc/src/Domain/Entities/Notification/NotificationDelivery.cs`: Id, TenantId, FlowInstanceId (nullable Guid), NotificationSpecId (string), NotificationSpecRevision (int), TargetUserId (Guid), Channel (string), Subject, Body, VariablesJson, Trigger, Status, Attempts, LastAttemptAt, NextAttemptAt, ErrorReason, CreatedAt, ReadAt, DismissedAt
- [ ] 3.3 Create `bpm-svc/src/Domain/Entities/Notification/NotificationDispatchAudit.cs`: Id, TenantId, FlowInstanceId, NotificationSpecId, Trigger, ResolvedRecipientCount, ContextJson, RequestedAt, CompletedAt, Status, ErrorReason
- [ ] 3.4 Inherit AuditableEntity where appropriate (CreatedAt comes from base)

## 4. Backend — persistence

- [ ] 4.1 EF configurations under `bpm-svc/src/Persistence/Configurations/Notification/`: NotificationDeliveryConfiguration, NotificationDispatchAuditConfiguration
- [ ] 4.2 Indexes: NotificationDelivery (TargetUserId + Status + CreatedAt for inbox query), (Status + NextAttemptAt for worker poll); NotificationDispatchAudit (RequestedAt for ordered audit)
- [ ] 4.3 Add DbSets to `BpmDbContext`: NotificationDeliveries, NotificationDispatchAudits
- [ ] 4.4 Generate migration `AddNotificationEngine`; verify with `sqlite3 bpm.db .schema "NotificationDeliveries"`

## 5. Backend — Mustache renderer

- [ ] 5.1 Add NuGet `Stubble.Core` (latest 1.10.x) to `bpm-svc/src/Application/Application.csproj`
- [ ] 5.2 Create `bpm-svc/src/Application/Notifications/IMustacheRenderer.cs` with `RenderResult Render(string template, IReadOnlyDictionary<string, JsonElement> variables)`
- [ ] 5.3 Implement `MustacheRenderer.cs` using Stubble; collect unbound placeholders by parsing template AST or fallback regex
- [ ] 5.4 Unit tests:
  - happy path: `"Hi {{name}}"` + `{ name: "Mary" }` → `"Hi Mary"`, no unbound
  - unbound: `"Hi {{name}}"` + `{}` → returns `"Hi {{name}}"`, unbound = `["name"]`
  - nested: `"Days: {{leave.days}}"` + `{ leave: { days: 5 } }` → `"Days: 5"`
  - section: `"{{#items}}{{name}},{{/items}}"` + `{ items: [{name:"a"},{name:"b"}] }` → `"a,b,"`
  - escape: `{{name}}` does NOT HTML-escape (we send plain text email; HTML body is a future flag)

## 6. Backend — recipient resolver

- [ ] 6.1 Create `bpm-svc/src/Application/Notifications/INotifyRecipientResolver.cs`
- [ ] 6.2 Implement `NotifyRecipientResolver.cs`:
  - `submitter` → `Set { ctx.SubmitterUserId }` (empty if null)
  - `current_approver` → `Set { ctx.CurrentApproverUserId }`
  - `current_assignee` → `Set { ctx.CurrentAssigneeUserId }`
  - `actor` → delegate to `IActorResolver.Resolve(inner, actor-ctx-built-from-NotificationContext)`
- [ ] 6.3 Build an `ActorContext` from `NotificationContext` (initiator = submitter; current_approver feeds the actor resolver's similar field; form_data = Variables)
- [ ] 6.4 Unit tests for each variant; ensure `submitter` returns empty set (not error) when `ctx.SubmitterUserId = null`

## 7. Backend — channel adapters

- [ ] 7.1 Create `bpm-svc/src/Application/Notifications/INotificationChannel.cs`: `Task<DeliveryAttemptResult> AttemptAsync(NotificationDelivery delivery, CancellationToken ct)`
- [ ] 7.2 Create `bpm-svc/src/Infrastructure/Notifications/InAppNotificationChannel.cs`: marks delivery `Status = Sent`, returns success (no external I/O)
- [ ] 7.3 Create `IEmailSender` interface + `EmailMessage` record + `EmailSendResult` record
- [ ] 7.4 Create `SmtpEmailSender.cs` using MailKit; reads `BPM_SMTP_HOST` (default `localhost`) and `BPM_SMTP_PORT` (default 1025)
- [ ] 7.5 Create `ResendEmailSender.cs`: HttpClient POST to `https://api.resend.com/emails` with `Authorization: Bearer ${RESEND_API_KEY}` env
- [ ] 7.6 Create `EmailNotificationChannel.cs`: builds `EmailMessage` from delivery row, calls `IEmailSender.SendAsync`, maps result to delivery status
- [ ] 7.7 Add NuGet `MailKit` to `Bpm.Infrastructure.csproj`

## 8. Backend — dispatcher

- [ ] 8.1 Create `bpm-svc/src/Application/Notifications/INotificationDispatcher.cs`: `Task DispatchAsync(NotificationSpec spec, NotificationContext ctx, CancellationToken ct)`
- [ ] 8.2 Implement `NotificationDispatcher.cs`:
  - resolve recipients via `INotifyRecipientResolver`
  - render subject + body via `IMustacheRenderer`; if unbound placeholders → write `Failed` audit + abort (do NOT send)
  - for each (resolved user, channel) tuple: insert NotificationDelivery row with `Status = Queued`, `NextAttemptAt = now`
  - insert one NotificationDispatchAudit row recording the dispatch
- [ ] 8.3 Wire through DI in `Application/DependencyInjection.cs` and `Infrastructure/DependencyInjection.cs`

## 9. Backend — dispatch worker

- [ ] 9.1 Create `bpm-svc/src/Infrastructure/Notifications/NotificationDispatchWorker.cs` as `IHostedService` (or `BackgroundService`)
- [ ] 9.2 Loop: every 30s, query `NotificationDelivery` where `Status = Queued` and `NextAttemptAt <= now`, take batch of 50
- [ ] 9.3 For each delivery: pick channel via `Channel` value; call `INotificationChannel.AttemptAsync`; update Status / Attempts / LastAttemptAt / NextAttemptAt accordingly
- [ ] 9.4 On failure with attempts < 3: backoff (2 min, 10 min, 60 min depending on attempt count); set Status back to `Queued`, NextAttemptAt = now + backoff
- [ ] 9.5 On failure with attempts ≥ 3: terminal `Failed`; insert a meta-notification to all admins (`Action required: notification delivery failed for spec X to user Y`)
- [ ] 9.6 Use `IDbContextFactory<BpmDbContext>` to scope DB per iteration; SQLite EXCLUSIVE transaction wraps the dequeue
- [ ] 9.7 Register hosted service in `Api/Program.cs`; gated on env `BPM_NOTIFICATION_WORKER=on` (default `on` in dev/prod, `off` in tests)

## 10. Backend — API endpoints

- [ ] 10.1 Create `bpm-svc/src/Api/Notifications/NotificationDispatchController.cs`:
  - `POST /api/notifications/dispatch` — admin-authz; body `{ specId, trigger, context, flowInstanceId? }`
  - `POST /api/notifications/dev-fire` — `[Authorize(Policy="DevOnly")]`; body `{ notification, context }` (inline notification, skips spec lookup)
  - `POST /api/notifications/{id}/retry` — admin; force-retries a Failed delivery
- [ ] 10.2 Create `bpm-svc/src/Api/Notifications/NotificationInboxController.cs`:
  - `GET /api/notifications/inbox?unread=true&limit=50` — current user; in_app deliveries
  - `POST /api/notifications/{id}/read` — current user owns the row, set ReadAt
  - `POST /api/notifications/{id}/dismiss` — current user owns, set DismissedAt
  - `GET /api/notifications/{id}` — fetch by id (auth: target user OR admin)
- [ ] 10.3 Add policy `DevOnly` (active when `BPM_AUTH_MODE=dev`)
- [ ] 10.4 Integration tests for each endpoint

## 11. Frontend — notification types + API

- [ ] 11.1 Update `bpm-ui/src/lib/onboarding.ts`:
  - Replace existing `NotifyRecipient` with `NotifyRecipientRef` discriminated union (TS mirror of backend)
  - Update `Notification` interface accordingly
- [ ] 11.2 Update validators: `validators.notify` becomes substantive (subject required, body required, recipients non-empty, body's `{{x}}` placeholder set === variables[] set, channel non-empty)
- [ ] 11.3 Add `bpm-ui/src/lib/notifications.ts`:
  - `fetchInbox(opts)`, `markRead(id)`, `markDismissed(id)`, `devFireNotification(notification, context)`
  - Hook `useNotificationPolling({ intervalMs = 60000 })` returning `{ unread, list, refresh, markRead, markDismissed }`

## 12. Frontend — wizard StepNotify

- [ ] 12.1 Update `bpm-ui/src/screens/onboarding/steps/StepNotify.tsx`:
  - Replace `RecipientsEditor` body with `NotifyRecipientEditor`
  - Add "Auto-detect variables" button: parses subject + body for `{{...}}`, rewrites `notification.variables`
  - Add "Preview" button: collects sample variables (use form schema as hints), calls `devFireNotification`, shows rendered subject/body in dialog with target list
- [ ] 12.2 Create `bpm-ui/src/components/wizard/NotifyRecipientEditor.tsx`:
  - Type picker: 申請人 (submitter) / 當前審核者 (current_approver) / 當前 assignee (current_assignee) / 進階 (actor wrap)
  - When `actor`, render `<ActorRefEditor>` for the inner ref
- [ ] 12.3 Create `bpm-ui/src/components/wizard/NotificationPreviewDialog.tsx`: shows rendered template, list of resolved targets, channel breakdown
- [ ] 12.4 Update `EMPTY_DRAFT` notifications array template to use the new shape

## 13. Frontend — Bell dropdown + Notifications screen

- [ ] 13.1 Update `bpm-ui/src/components/AppLayout.tsx`: Bell button gets `onClick` that toggles a dropdown panel; show unread badge when `count > 0`
- [ ] 13.2 Create `bpm-ui/src/components/NotificationBellDropdown.tsx`:
  - Mounts `useNotificationPolling`
  - Renders newest 20 unread; each row: subject, relative time, ✓ button to mark read
  - "View all →" footer link → `/notifications`
- [ ] 13.3 Create `bpm-ui/src/screens/Notifications.tsx`: full inbox table with filters (date range, trigger, status); supports mark-read / mark-dismissed bulk actions
- [ ] 13.4 Wire `Notifications` into `AppLayout` screen routing

## 14. Sample specs migration

- [ ] 14.1 Update `sample_specs/leave_v1.json` notifications: legacy 4-type → new typed
- [ ] 14.2 Update `sample_specs/purchase_v1.json` likewise
- [ ] 14.3 Add `notify_*` blocks to the new samples from prior proposals (`expense_employee_v1`, `it_request_v1`, `travel_request_v1`)
- [ ] 14.4 Run `openspec validate` chain to ensure all samples parse

## 15. Prompt template

- [ ] 15.1 Update `prompt_template_v1.md`:
  - "Choosing recipient type": decision tree (申請人？當前審核者？某個團隊？某個角色？)
  - 4 worked examples covering each variant of NotifyRecipientRef
  - Convention for variables: prefix domain words (`applicant.name`, `leave.days`, `caseUrl`)
  - Reject ambiguous AI output (low confidence) by emitting `{ type: 'actor', inner: { type: 'unresolved', ... } }` (composes with the unresolved type from prior proposal)

## 16. Configuration + environment

- [ ] 16.1 Add to `appsettings.Development.json`:
  - `Notifications:DispatchWorker:Enabled = true`
  - `Notifications:Email:Backend = "dev-mailhog"`
  - `Notifications:Email:Smtp = { Host: "localhost", Port: 1025 }`
- [ ] 16.2 Document in `SETUP.md`:
  - Running MailHog: `docker run -d --name mailhog -p 1025:1025 -p 8025:8025 mailhog/mailhog`
  - Inspecting captured emails at `http://localhost:8025`
  - For prod-like testing: `BPM_EMAIL_BACKEND=prod-resend` + `RESEND_API_KEY=...`
- [ ] 16.3 Add `RESEND_API_KEY` placeholder to `.env.example` (do NOT commit a real key)

## 17. End-to-end verification

- [ ] 17.1 `dotnet build bpm-svc.slnx` clean
- [ ] 17.2 All backend unit tests + integration tests pass (`dotnet test`)
- [ ] 17.3 Apply migration on fresh `bpm.db`; verify NotificationDeliveries / NotificationDispatchAudits exist
- [ ] 17.4 Boot bpm-svc; with `BPM_AUTH_MODE=dev`, hit `POST /api/notifications/dev-fire` with a hand-crafted payload, verify:
  - Mustache renders correctly
  - One row per recipient × channel inserted in NotificationDeliveries
  - NotificationDispatchAudits row recorded
  - In-app deliveries marked `Sent` immediately
  - Email deliveries picked up by worker, sent to MailHog (visible at `localhost:8025`)
- [ ] 17.5 Boot bpm-ui (`tsc -p tsconfig.app.json`; `npm run dev`); login as a persona who is the target of a delivery; verify:
  - Bell badge shows correct unread count
  - Dropdown shows the new delivery
  - Mark-read updates the badge + persists across refresh
- [ ] 17.6 Manual: in wizard StepNotify, build a notification with `{ type: 'actor', inner: { type: 'functional_members', function_tag: 'finance' } }`, click Preview — verify the dialog lists every active finance-tagged user
- [ ] 17.7 Manual: verify failure path — set Body to `Hi {{ghost}}`, click Preview, verify Preview dialog shows "Unbound placeholder: ghost"; verify no delivery rows inserted
- [ ] 17.8 **Demo guard**: confirm `Home.tsx`, `forms/*.tsx`, `Search.tsx`, `Report.tsx`, `lib/workflow.ts` were NOT modified
- [ ] 17.9 Coverage: walk a full notification lifecycle for each of the 9 mock-up flows — verify every flow's intended notifications can be authored, previewed, and (via dev-fire) delivered

## 18. Docs + commit

- [ ] 18.1 Update `bpm-svc/CLAUDE.md` with notification engine architecture diagram + env vars + worker schedule
- [ ] 18.2 Update `SETUP.md` with MailHog setup + Resend key rotation note
- [ ] 18.3 Add `bpm-svc/README.md` section on testing notifications via dev-fire
- [ ] 18.4 Commit in chunks (spec layer + types + persistence; renderer + resolver; channels + worker; API endpoints; wizard recipient editor; Bell dropdown + screen; samples + prompts; verification)
- [ ] 18.5 Push via GitKraken (Claude does not push to BPM repo)
