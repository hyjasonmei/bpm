## ADDED Requirements

### Requirement: NotificationDelivery is the durable record of a per-recipient send attempt

The system SHALL persist one `NotificationDelivery` row per (notification spec, target user, channel) emission. Each row carries the rendered subject + body, the channel, and the lifecycle Status (`Queued`, `Sent`, `Read`, `Dismissed`, `Failed`). NotificationDelivery rows MUST NOT be edited except for explicit lifecycle transitions (`Status`, `Attempts`, `LastAttemptAt`, `NextAttemptAt`, `ErrorReason`, `ReadAt`, `DismissedAt`); the rendered subject/body MUST NOT be mutated after insert.

#### Scenario: One delivery per recipient × channel

- **GIVEN** a notification spec with `channel = ["email", "in_app"]` and resolved recipients `{u_a, u_b}`
- **WHEN** the dispatcher runs
- **THEN** four `NotificationDelivery` rows are inserted: `(u_a, email)`, `(u_a, in_app)`, `(u_b, email)`, `(u_b, in_app)`

#### Scenario: Subject/body are immutable post-insert

- **WHEN** the dispatcher inserts a delivery with `Subject = "Hi Mary"`
- **AND** the subject template is later changed in the spec
- **AND** the dispatcher fires again (different trigger event)
- **THEN** the prior delivery row's `Subject` remains `"Hi Mary"` (snapshot at dispatch time)

### Requirement: NotificationDispatchAudit is one row per dispatch invocation

The system SHALL persist one `NotificationDispatchAudit` row per call to `INotificationDispatcher.DispatchAsync`. The row records the spec id, trigger, full context JSON, recipient count, and overall status (`Success`, `PartialFailure`, `Failure`). Audit rows are append-only — NO UPDATE or DELETE permitted.

#### Scenario: Audit captures the dispatch intent

- **GIVEN** a dispatch call with `(specId = N1, trigger = on_assign, ctx = {submitter: u_a})`
- **WHEN** the dispatcher resolves 3 recipients × 2 channels = 6 deliveries
- **THEN** one audit row is inserted with `ResolvedRecipientCount = 3`, `ContextJson` capturing the full input ctx, and Status reflecting overall success

#### Scenario: Audit row never deleted

- **WHEN** an admin deletes a NotificationDelivery (hypothetically — should not be allowed in API; system constraint)
- **THEN** the corresponding audit row remains for compliance traceability

### Requirement: INotificationDispatcher orchestrates resolution + rendering + persistence

The dispatcher SHALL, given a notification spec and a NotificationContext, perform in order:

1. Resolve recipients via `INotifyRecipientResolver` against the context
2. Render subject + body via `IMustacheRenderer`; if any placeholder is unbound → fail dispatch (NO deliveries inserted), write a `Failed` audit row with the unbound placeholder list in ErrorReason
3. For each (recipient, channel) tuple, insert a `NotificationDelivery` row with `Status = Queued`, `NextAttemptAt = now`
4. Insert one `NotificationDispatchAudit` row capturing the dispatch outcome

If recipient resolution returns zero users, the dispatcher SHALL write an audit row with `Status = Success`, `ResolvedRecipientCount = 0` and insert no delivery rows. (Empty resolution is not an error — it means the targeted role / dept genuinely has no members at this moment.)

#### Scenario: Unbound placeholder fails dispatch

- **WHEN** dispatch is called with `Body = "Hello {{ghost}}"` and ctx variables `{}` (no `ghost`)
- **THEN** zero `NotificationDelivery` rows are inserted
- **AND** one `NotificationDispatchAudit` row is inserted with Status = Failure and ErrorReason listing `"unbound: ghost"`

#### Scenario: Empty recipients does not error

- **GIVEN** a notification with recipients `{ type: 'actor', inner: { type: 'functional_members', function_tag: 'audit' } }`
- **AND** no Department is tagged `audit`
- **WHEN** dispatch is called
- **THEN** the dispatch completes with audit `Status = Success, ResolvedRecipientCount = 0`, no delivery rows inserted

### Requirement: IMustacheRenderer reports unbound placeholders

The renderer SHALL accept `(template, variables)` and return `(rendered, unboundPlaceholders)`. The renderer MUST NOT silently substitute empty strings for unbound `{{var}}` placeholders. The dispatcher uses the unbound list to decide whether to abort the dispatch (per the dispatcher requirement).

#### Scenario: Bound placeholder substituted

- **WHEN** rendering `"Hi {{name}}"` with `{ name: "Mary" }`
- **THEN** result is `("Hi Mary", [])`

#### Scenario: Unbound placeholder reported

- **WHEN** rendering `"Hi {{name}}, balance: {{balance}}"` with `{ name: "Mary" }`
- **THEN** result is `("Hi Mary, balance: {{balance}}", ["balance"])` — the placeholder remains in the output as a marker

#### Scenario: Nested path bound

- **WHEN** rendering `"Days: {{leave.days}}"` with `{ leave: { days: 5 } }`
- **THEN** result is `("Days: 5", [])`

### Requirement: INotificationChannel adapters perform a single send attempt

Each `INotificationChannel` implementation SHALL accept a `NotificationDelivery` and return a `DeliveryAttemptResult` (Success or Failure with optional ErrorReason). Channels SHALL NOT mutate the delivery row directly — that's the worker's responsibility. Channels SHALL be idempotent on retry: a re-send for the same delivery row with the same content is acceptable behavior (mail provider may de-dupe; in-app channel writes the same row's status without insert).

#### Scenario: InAppNotificationChannel marks Sent without I/O

- **WHEN** the worker invokes `InAppNotificationChannel.AttemptAsync(delivery)`
- **THEN** the result is `Success` (the delivery's row IS the artifact users see in the inbox)

#### Scenario: EmailNotificationChannel calls IEmailSender

- **WHEN** the worker invokes `EmailNotificationChannel.AttemptAsync(delivery)` for a delivery whose target user has email `mary@acme.com`
- **THEN** `IEmailSender.SendAsync` is called with `To = "mary@acme.com"`, Subject and Body from the delivery row

### Requirement: NotificationDispatchWorker retries failed deliveries with backoff

The worker SHALL poll for `NotificationDelivery` rows where `Status = Queued` and `NextAttemptAt <= now`. For each row it invokes the channel; on failure with `Attempts < 3`, the worker SHALL set `NextAttemptAt = now + backoff(Attempts)` (backoff: 2 min for attempt 1, 10 min for 2, 60 min for 3) and revert `Status` to `Queued`. On `Attempts >= 3`, the worker SHALL set `Status = Failed` (terminal) and create a meta-notification to system admins informing them of the delivery failure.

#### Scenario: First retry after 2 minutes

- **WHEN** a delivery's first attempt fails at T+0
- **THEN** the next attempt is scheduled for T+2 minutes; Attempts = 1; Status = Queued

#### Scenario: After 3 failed attempts, terminal Failed

- **WHEN** the third attempt also fails
- **THEN** `Status = Failed`; a meta-notification is created targeting system admins with the original delivery's spec id and target user info

### Requirement: Inbox endpoint returns the current user's in-app deliveries

`GET /api/notifications/inbox` SHALL return `NotificationDelivery` rows where `TargetUserId = current authenticated user`, `Channel = "in_app"`, and (when `unread=true`) `ReadAt IS NULL` AND `DismissedAt IS NULL`. Results SHALL be ordered by `CreatedAt DESC`, capped by `limit` (default 50, max 100). The endpoint MUST NOT return rows for other users.

#### Scenario: Mary fetches her unread inbox

- **GIVEN** Mary has 3 unread + 2 read in-app deliveries
- **WHEN** Mary calls `GET /api/notifications/inbox?unread=true`
- **THEN** the response contains 3 rows, all unread, newest first

#### Scenario: Cross-user inbox forbidden

- **GIVEN** John has unread in-app deliveries
- **WHEN** Mary (logged in) calls `GET /api/notifications/inbox`
- **THEN** none of John's rows appear

### Requirement: Mark-read and mark-dismissed are user-scoped

`POST /api/notifications/{id}/read` and `/dismiss` SHALL succeed only when the row's `TargetUserId` matches the current user. For another user's row, the endpoint SHALL return 404 (do not leak existence). On success, the relevant timestamp (`ReadAt` / `DismissedAt`) is set; the row remains queryable but no longer appears in `unread=true` filters.

#### Scenario: Mary marks her own delivery read

- **GIVEN** delivery `D1` targets Mary
- **WHEN** Mary calls `POST /api/notifications/D1/read`
- **THEN** the response is 200; `D1.ReadAt = now`; subsequent `GET /api/notifications/inbox?unread=true` does not include D1

#### Scenario: Marking another user's delivery returns 404

- **GIVEN** delivery `D2` targets John
- **WHEN** Mary calls `POST /api/notifications/D2/read`
- **THEN** the response is 404 with no leakage of D2 existing

### Requirement: Dev-fire endpoint is gated to dev mode

`POST /api/notifications/dev-fire` SHALL be available only when `BPM_AUTH_MODE = dev`. In `prod` mode, the endpoint SHALL return 404 (not 401, not 403) — completely hidden. The dev endpoint accepts an inline notification spec (no DB lookup) for wizard preview and smoke-testing.

#### Scenario: Dev-fire works in dev mode

- **GIVEN** `BPM_AUTH_MODE=dev`
- **WHEN** an authorized user calls `POST /api/notifications/dev-fire` with valid payload
- **THEN** the dispatch runs identically to the `dispatch` endpoint, with the inline notification spec

#### Scenario: Dev-fire returns 404 in prod

- **GIVEN** `BPM_AUTH_MODE=prod`
- **WHEN** any client calls `POST /api/notifications/dev-fire`
- **THEN** the response is 404 with no body indicating the endpoint exists in dev mode

### Requirement: Email backend is configurable via environment

The `IEmailSender` implementation registered SHALL be selected at startup via `BPM_EMAIL_BACKEND` env var (`dev-mailhog` selects `SmtpEmailSender` pointed at `localhost:1025`; `prod-resend` selects `ResendEmailSender` requiring `RESEND_API_KEY` env var). On `prod-resend` without `RESEND_API_KEY` set, startup MUST fail fast with a clear error message.

#### Scenario: Dev mailhog sender by default

- **GIVEN** `BPM_EMAIL_BACKEND=dev-mailhog` (default in dev)
- **WHEN** the app starts
- **THEN** `IEmailSender` is `SmtpEmailSender` connected to `localhost:1025`

#### Scenario: Prod resend sender requires API key

- **GIVEN** `BPM_EMAIL_BACKEND=prod-resend` and `RESEND_API_KEY` is unset
- **WHEN** the app starts
- **THEN** startup fails with `"RESEND_API_KEY required when BPM_EMAIL_BACKEND=prod-resend"`
