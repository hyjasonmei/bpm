## ADDED Requirements

### Requirement: Outbound gate captures full payload before deciding outcome

When sandbox is ON, `IOutboundGate.ApplyAsync` for any channel (Email / Webhook / SMS) SHALL persist a `SandboxCapturedMessage` row containing the full payload BEFORE returning a `GateOutcome`. The default sandbox outcome SHALL be the new `Captured` variant — workflow continues as if delivery succeeded; nothing leaves the system. The legacy `Rewrote`-to-fallback-address mode SHALL remain available as opt-in via `TenantSettings.SandboxConfigJson.LegacyRewriteEnabled = true`, but the new default is capture-only.

#### Scenario: Email captured with full body

- **GIVEN** sandbox is ON with `LegacyRewriteEnabled = false`
- **WHEN** the workflow dispatches an email "Subject: 請假已送出, BodyHtml: <p>...</p>" to Mary
- **THEN** the gate returns `GateOutcome.Captured(message)`
- **AND** a `SandboxCapturedMessage` row exists with `Channel = Email`, `Subject = "請假已送出"`, `BodyHtml = "<p>...</p>"`, `IntendedRecipientsJson` containing Mary's user id
- **AND** no SMTP connection was made

#### Scenario: Webhook captured with fake 200 OK

- **GIVEN** sandbox is ON
- **WHEN** the workflow dispatches a webhook to `https://erp.acme.com/hooks/leave-approved` with payload `{ caseId: ..., status: "approved" }`
- **THEN** the gate returns `GateOutcome.Captured` with `IsFakeOk = true`
- **AND** a `SandboxCapturedMessage` row exists with `Channel = Webhook`, `Url`, `PayloadJson`, `EventType = "leave-approved"`, `IsFakeOk = true`
- **AND** no HTTP request was made to acme.com

#### Scenario: Non-sandbox mode unaffected

- **GIVEN** sandbox is OFF
- **WHEN** the workflow dispatches an email
- **THEN** the gate behaves identically to today (PassThrough or real-mode logic)
- **AND** no `SandboxCapturedMessage` row is created

### Requirement: Captured messages link back to workflow context

Each `SandboxCapturedMessage` SHALL persist `ProcessInstanceId`, `TaskId`, `IntendedRecipients` (the user ids the dispatch was meant for), and `OriginatingNotificationId` / `OriginatingWebhookSubscriptionId` (the spec rule that fired). These fields enable the mailbox UI to filter by who would have received and which spec rule fired, and they enable bundle test-cases to assert against specific notifications/webhooks.

#### Scenario: Mailbox filter by intended recipient

- **GIVEN** instance X has dispatched 3 emails: 2 to Mary, 1 to Tony
- **WHEN** the mailbox UI requests `GET /api/sandbox/captured?recipientUserId=Mary.Id`
- **THEN** the response contains exactly 2 entries
- **AND** each entry's `IntendedRecipients` includes Mary's user id

#### Scenario: Originating notification link surfaces in detail view

- **GIVEN** a captured email originated from notification id `N_MANAGER_REVIEW`
- **WHEN** the user opens the detail modal
- **THEN** the modal shows `OriginatingNotificationId: N_MANAGER_REVIEW`
- **AND** the link deep-links into the spec at the notification rule definition

### Requirement: Captured messages have a TTL and a cron-driven cleanup

The system SHALL retain `SandboxCapturedMessage` rows for `TenantSettings.SandboxConfigJson.CaptureRetentionDays` (default 30) days after `CapturedAt`. A daily background job SHALL hard-delete rows older than the retention window. The full-reset endpoint SHALL hard-delete all rows regardless of age. Soft-delete is explicitly NOT used for captured rows because sandbox data is disposable.

#### Scenario: Old rows deleted

- **GIVEN** retention = 30 days
- **AND** a captured row exists with `CapturedAt` = 35 days ago
- **WHEN** the daily cleanup cron runs
- **THEN** the row is hard-deleted

#### Scenario: Recent rows preserved

- **GIVEN** retention = 30 days
- **AND** a captured row with `CapturedAt` = 10 days ago
- **WHEN** cleanup runs
- **THEN** the row is unchanged

### Requirement: Per-user read tracking on captured messages

Captured messages SHALL track which users have read them via `ReadByUserIdsJson` (array of user ids). `POST /api/sandbox/captured/{id}/read` SHALL append the calling user's id to the array, idempotent. The mailbox UI SHALL surface unread filters and unread counts based on this field for the *currently-logged-in* user — even when that user is using a sandbox-persona JWT, read tracking attaches to the persona id (because what was unread for "Mary persona" is the relevant question, not "what was unread for Jason as Mary").

#### Scenario: Mark read is idempotent

- **GIVEN** captured message X has `ReadByUserIdsJson = []`
- **WHEN** Mary calls `POST /captured/X/read` twice
- **THEN** `ReadByUserIdsJson = [Mary.Id]` (not `[Mary.Id, Mary.Id]`)

#### Scenario: Read tracking attaches to persona

- **GIVEN** Jason is using a sandbox-persona JWT for Mary
- **WHEN** Jason marks captured message X as read
- **THEN** `ReadByUserIdsJson` includes Mary's id (not Jason's id)

### Requirement: Mailbox API exposes list / detail / read / count operations

The system SHALL expose under `/api/sandbox/captured`:

- `GET /` — list captured messages with optional filters: `channel`, `recipientUserId`, `processInstanceId`, `unread`, `since`. Returns paged results sorted by `CapturedAt DESC`.
- `GET /{id}` — full payload (HTML body, headers, JSON), respecting current persona's read state
- `POST /{id}/read` — mark as read by current user (idempotent)
- `GET /unread-count?byChannel=true` — returns `{ email: n, webhook: m, sms: k }` for use by the SandboxBanner counter
- All endpoints MUST refuse with 400 when sandbox is OFF (so prod loads of these endpoints are cheap no-ops).

#### Scenario: List filters by channel

- **GIVEN** 5 emails, 3 webhooks, 1 SMS captured
- **WHEN** the user GETs `/api/sandbox/captured?channel=webhook`
- **THEN** 3 entries returned, all with `Channel = Webhook`

#### Scenario: Unread count returns zero when sandbox off

- **GIVEN** sandbox is OFF
- **WHEN** the SandboxBanner polls `/api/sandbox/captured/unread-count`
- **THEN** the response is 400 with `{ error: "sandbox_off" }`
- **AND** the banner falls back to hidden / no-poll state
