# Design notes

## 1. Why a new capability rather than extending bpm-workflow-resolver

Notification dispatch is *more* than recipient resolution. It includes:

- Template rendering (Mustache → string)
- Channel adapters (SMTP / HTTP / DB write)
- Persistence + retry (delivery audit, queued state, exponential backoff)
- API surface (inbox, mark-read, dispatch)

Lumping all that into `bpm-workflow-resolver` would inflate the resolver's scope. A dedicated `bpm-notification-engine` capability keeps the resolver tight (resolve actor → user set) and the engine focused (turn a notification spec + context into actual deliveries).

The resolver gets a small extension only for the three runtime-scoped recipient types (`submitter` / `current_approver` / `current_assignee`) — these can't be looked up from the DB graph, they need a `NotificationContext` from the dispatcher.

## 2. NotifyRecipientRef vs ViewerRef vs ActorRef

We now have three flavors of "who":

| Ref type | Where used | Runtime types |
|---|---|---|
| `ActorRef` | approver, userTask.assignee | none — all resolve at spec-load against org graph |
| `ViewerRef` | userTask.viewers | `self`, `submitter`, `current_assignee` + `actor` wrap |
| `NotifyRecipientRef` | notification.recipients | `submitter`, `current_approver`, `current_assignee` + `actor` wrap |

The pattern: each context has its own runtime set, plus an `actor` variant that wraps a regular ActorRef. They look similar but resolve in different contexts:

- `ViewerRef.self` ≠ `NotifyRecipientRef.submitter` semantically: viewers can include the *approver* as well, while notification `submitter` is always the original flow initiator.
- `current_approver` exists in NotifyRecipientRef but not ViewerRef because viewers default to whoever holds the open task, while notifications can target the approval-step holder specifically (different from the userTask holder when both are open simultaneously).

Could we unify them into one DSL? Probably yes in v2 — but the two domains evolved separately and the semantics are distinct enough that forcing unification risks losing precision. Keep them parallel and documented; revisit if a customer use case spans both.

## 3. NotificationContext shape

The dispatcher needs runtime context to resolve the three runtime types:

```csharp
public sealed record NotificationContext(
    Guid? FlowInstanceId,            // nullable until ProcessRuntime ships
    Guid? SubmitterUserId,           // who started the flow
    Guid? CurrentApproverUserId,     // who's holding the open approval at trigger time
    Guid? CurrentAssigneeUserId,     // who's holding the open userTask
    IReadOnlyDictionary<string, JsonElement> Variables);  // for Mustache + form_field_ref
```

Source of these fields:

- For `dev-fire`: caller passes them as JSON.
- For `dispatch` (admin): caller passes flow_instance_id, server reads from runtime tables (deferred — for now, caller passes them).
- For real ProcessRuntime: hooks at state-transition points populate the context and call `dispatch`.

## 4. Mustache renderer choice — Stubble

Reasons:

- Pure .NET, no native binaries
- ~5 MB footprint
- Implements Mustache spec (sufficient for `{{var}}`, `{{#if}}`, `{{#each}}`)
- Active maintenance (last release 2024)

Alternatives ruled out:
- **HandlebarsDotNet** — heavier, adds Handlebars-specific helpers we don't need
- **Hand-rolled regex** — fine for `{{var}}` only but breaks on conditionals and arrays (which we'll need for repeater-aware emails: "您的費用 {{count}} 筆")

The renderer outputs `(string rendered, IReadOnlyList<string> unboundPlaceholders)`. Unbound placeholders are NOT replaced with empty string; they're collected and reported. The dispatcher fails the delivery (status `Failed`, errorReason listing unbound placeholders) rather than silently sending `Hello {{name}}` to the user.

## 5. Email sender abstraction

```csharp
public interface IEmailSender
{
    Task<EmailSendResult> SendAsync(EmailMessage message, CancellationToken ct);
}

public sealed record EmailMessage(
    string To, string Subject, string BodyText, string? BodyHtml = null,
    string? FromName = null, IReadOnlyList<EmailAttachment>? Attachments = null);

public sealed record EmailSendResult(bool Success, string? ProviderId, string? ErrorReason);
```

`SmtpEmailSender` (dev): connects to `localhost:1025` (MailHog). MailHog catches all outgoing email, lets us inspect at `localhost:8025` web UI. Dev workflow: spin up MailHog via `docker run -p 1025:1025 -p 8025:8025 mailhog/mailhog` (added to `dogfood.command` or a separate `dev-up.sh`).

`ResendEmailSender` (prod): POST to `https://api.resend.com/emails` with bearer auth. Free tier (3000/month) covers Phase A pilot customers. Switch via env: `BPM_EMAIL_BACKEND=dev-mailhog | prod-resend`.

Why not SES? Customer compliance often requires "your team's emails not on a shared sender". Resend's `onboarding@yourdomain` story is simpler than SES domain verification + DKIM dance. Migrate to SES if customer needs >3000/month or specific compliance.

## 6. NotificationDelivery state machine

```
Queued ─┬─→ Sent ──→ Read ──→ Dismissed  (terminal: Read or Dismissed)
        ├─→ Failed (non-retryable: bad email, validation error)
        └─→ Failed-Retryable → Queued (loop, attempts++ until cap)
                            └─→ Failed (after attempts ≥ 3)
```

- `Sent`: channel adapter returned success
- `Read`: only meaningful for `in_app` channel; user clicked
- `Dismissed`: user explicitly dismissed (in-app); for email, equivalent to `Sent` (no read tracking without tracking pixel — out of scope)
- `Failed`: terminal; admin can re-queue manually via `POST /api/notifications/{id}/retry`

Worker: every 30s polls `Queued` rows where `next_attempt_at <= now`. SQLite single-instance: an `EXCLUSIVE` transaction wraps the SELECT...UPDATE pattern. If we ever scale to >1 instance, swap to a Postgres advisory lock.

Backoff: 2 min, 10 min, 60 min. After 3 attempts → `Failed` terminal. Admin gets a "delivery failed" entry in their own inbox (meta-notification) when this happens — surface failures rather than silently swallow.

## 7. Subscription model — passive only

This change does NOT support user-managed subscriptions ("I want to know whenever the procurement team raises a request"). Recipients are explicit per notification spec. Subscriptions are a future feature; they need:

- UserSubscription table
- A subscription matcher running at dispatch time
- Privacy filter (can user X actually see this flow?)
- Per-user mute list

Out of scope. Current model: spec author lists the recipients explicitly; runtime resolves them.

## 8. Why polling for Bell, not SignalR

SignalR + WebSocket adds:

- Service registration, hub, client library (~100KB)
- Connection lifecycle handling (reconnect on tab idle, etc.)
- Auth token refresh during long-lived connections
- A bug surface on dev / behind corporate proxies

For SME scale (10s of notifications/day), 60s polling is invisible to users and infra-free. If a customer demands real-time delivery, swap implementation behind `useNotificationPolling` hook — the API stays the same.

## 9. Trigger event semantics — what fires `on_assign`?

In the future Process Runtime, the answer is:

- `on_submit` — flow instance created
- `on_assign` — Task row created with assigned user
- `on_approve` / `on_reject` — Approval status changes
- `on_complete` — flow instance reaches end_event
- `on_sla_breach` — SLA timer expires

Today (no ProcessRuntime), these are all dormant. The dispatcher accepts the trigger value and dispatches blindly; no automated firing. The wizard's "Preview" button + `/api/notifications/dev-fire` exercises the engine without runtime.

When ProcessRuntime lands (separate change), it will:

1. At each state transition, look up notifications matching the trigger
2. Build a `NotificationContext` from the running instance
3. Call `INotificationDispatcher.DispatchAsync(notification, ctx)`

This change documents the contract; ProcessRuntime change wires it.

## 10. Variable namespace — convention, not enforcement

Sample notifications use `{{applicant.name}}`, `{{leave.days}}`, `{{caseUrl}}`. There's no schema enforcing that `applicant.name` exists in the variables dict — it's a convention with the spec author.

The renderer reports unbound placeholders so failures are loud. The wizard's "auto-detect variables" button parses subject + body, extracts every `{{...}}` token, and rewrites `notification.variables[]` to match — this is the sanity-checking UX, not a strong contract.

A v2 enhancement: type-safe variable references (`{{form.amount: number}}`) tied to the userTask field schema. Not in this change.

## 11. Why NotificationDispatchAudit separate from NotificationDelivery

One dispatch may produce N deliveries (one per resolved recipient × channel combination). The audit captures the *intent* (which spec, which trigger, what context); the deliveries capture the *outcome* per recipient.

If we collapsed into one table, every delivery row would duplicate the dispatch context (~100 bytes JSON). For a flow with 5 notifications × 10 recipients × 2 channels = 100 rows per state transition, the redundancy adds up. Two tables, joined when needed.

Both tables are append-only — TaskHistory-style. No UPDATE except on Status / ReadAt / DismissedAt of NotificationDelivery, which are explicit lifecycle transitions, not edits.

## 12. Open questions

- **i18n picking**: today subject/body have `zh-TW` and optional `en`. Which one fires? Probably user.preferred_locale; add a `User.PreferredLocale` column in the org-model change later. v1: always zh-TW.
- **Attachment support on emails**: spec already allows file fields; dispatcher could attach them. Not in this change — needs `IFileStorage` abstraction first.
- **Throttling**: should we cap "user X cannot receive >50 emails / hour" to prevent runaway loops if a spec mis-fires? Add later via `NotificationDeliveryRateLimiter`. v1: trust the spec.
- **HTML body**: subject/body are plain text only in v1. HTML can land via `bodyHtml` field in NotifyTemplate; renderer handles both. Defer — plain text is sufficient for SME.
- **In-app notification grouping**: 5 deliveries from the same flow → one Bell row or five? Currently five (each delivery one row). UI groups by flow_instance_id at render time if needed (deferred polish).
- **Test fixture for recipient resolution**: ensure that `current_approver` resolves to the exact user holding the open approval at the moment of dispatch — could be flaky if the resolve-then-dispatch window opens long enough for a delegate to take over. Take a snapshot of the approver at trigger time. Document this.
