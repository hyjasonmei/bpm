## ADDED Requirements

### Requirement: Tenant carries a SandboxMode flag and per-channel config

The system SHALL persist `SandboxMode` (bool, default false) and `SandboxConfigJson` (text, nullable) on the `Tenant` entity. The config SHALL be a JSON object with optional keys `emailRecipients` (string[]), `webhookUrl` (string), `smsRecipients` (string[]). Toggling the flag MUST take effect immediately for subsequent dispatch attempts; in-flight dispatches already past the gate are unaffected.

#### Scenario: Toggle on takes effect on next dispatch

- **GIVEN** tenant `acme` has SandboxMode=false
- **WHEN** an admin sets SandboxMode=true via `PUT /api/sandbox/status`
- **AND** a notification dispatch is triggered 1 second later
- **THEN** the dispatch is intercepted by the gate

#### Scenario: Empty config still allows toggle

- **GIVEN** SandboxConfigJson is null
- **WHEN** an admin sets SandboxMode=true with no config update
- **THEN** the toggle persists; subsequent dispatches per channel apply the "no recipient → drop" rule

### Requirement: All outbound dispatchers route through IOutboundGate

The system SHALL define an `IOutboundGate` interface with `Apply` overloads for `EmailMessage`, `WebhookDelivery`, and (future) `SmsMessage`. Every outbound dispatcher (EmailDispatcher, WebhookDispatcher, SmsDispatcher) MUST call `gate.Apply()` before invoking the underlying transport. Bypassing the gate is a defect.

#### Scenario: Sandbox OFF passes message through unchanged

- **GIVEN** tenant SandboxMode=false
- **WHEN** EmailDispatcher calls `gate.Apply(message)` with To=[alice@acme.com]
- **THEN** the returned message has identical To, identical body, and no audit row is written

#### Scenario: Sandbox ON rewrites email recipients

- **GIVEN** tenant SandboxMode=true with `emailRecipients = ["uat@acme.com"]`
- **WHEN** EmailDispatcher calls `gate.Apply(message)` with To=[alice@acme.com], Cc=[bob@acme.com], Subject="Leave approved"
- **THEN** the returned message has To=[uat@acme.com], Cc=[], Bcc=[]
- **AND** body (HTML and plaintext) starts with a banner naming "alice@acme.com, bob@acme.com" as original recipients
- **AND** a `SandboxRedirect` row is written with channel=Email, originalTargets=[alice@acme.com, bob@acme.com], redirectedTargets=[uat@acme.com], action=Redirected, sampleSubject="Leave approved"

#### Scenario: Sandbox ON with empty email recipients drops the email

- **GIVEN** tenant SandboxMode=true with `emailRecipients = []`
- **WHEN** EmailDispatcher calls `gate.Apply(message)`
- **THEN** the gate returns a Dropped marker (caller MUST NOT call SMTP send)
- **AND** a SandboxRedirect row is written with action=Dropped, redirectedTargets=[]

### Requirement: Webhook redirects preserve original URL in header

When sandbox is ON and the channel is webhook, the gate SHALL change the destination URL to `sandbox.webhookUrl` and add HTTP header `X-BPM-Sandbox-Original-Url: <originalUrl>`. The payload body SHALL NOT be modified.

#### Scenario: Webhook redirected with header

- **GIVEN** SandboxMode=true with `webhookUrl = "https://webhook.site/abc123"`
- **WHEN** WebhookDispatcher calls `gate.Apply(delivery)` with Url="https://acme.com/hook", Payload={...}
- **THEN** the gated delivery has Url="https://webhook.site/abc123", header `X-BPM-Sandbox-Original-Url: https://acme.com/hook`, and identical Payload

### Requirement: Audit log of every sandbox redirect

The system SHALL persist a `SandboxRedirect` row for every dispatch attempt that passes through the gate WHILE sandbox is ON, regardless of action (Redirected or Dropped). Rows SHALL include channel, original targets, redirected targets, action, dispatchedAt (UTC), and a sampleSubject (max 200 chars). The full message body SHALL NOT be persisted.

#### Scenario: Audit row written for every redirected dispatch

- **GIVEN** SandboxMode=true; 3 emails dispatched
- **WHEN** the dispatches complete
- **THEN** 3 SandboxRedirect rows exist for the tenant in chronological order

#### Scenario: Audit row written for dropped dispatch

- **GIVEN** SandboxMode=true with `emailRecipients=[]`; 1 email dispatched
- **WHEN** the dispatch passes through the gate
- **THEN** 1 SandboxRedirect row exists with action=Dropped

### Requirement: Sandbox banner is shown on every page when active

When `tenant.SandboxMode = true`, the bpm-ui frontend SHALL render a non-dismissible banner at the top of every page reading "🧪 SANDBOX MODE ACTIVE — outbound emails / webhooks are being redirected to test recipients" with red background. Admins MAY dismiss for the current page render but the banner SHALL re-appear on the next navigation or refresh.

#### Scenario: Banner shows when sandbox is on

- **GIVEN** SandboxMode=true
- **WHEN** any user loads any page
- **THEN** a red banner is rendered above the page header

#### Scenario: Banner hidden when sandbox is off

- **GIVEN** SandboxMode=false
- **WHEN** any user loads any page
- **THEN** no sandbox banner is rendered

### Requirement: Sandbox toggle requires admin role

The endpoints `PUT /api/sandbox/status`, `GET /api/sandbox/redirects` and the UI's Site Settings → Sandbox section SHALL be accessible only to users with role `admin`. Non-admin attempts MUST receive 403.

#### Scenario: Non-admin cannot toggle

- **GIVEN** Wilson (employee) is authenticated
- **WHEN** he calls `PUT /api/sandbox/status`
- **THEN** the response is 403

#### Scenario: Admin can toggle

- **GIVEN** an admin user is authenticated
- **WHEN** they call `PUT /api/sandbox/status` with `{ "enabled": true, "config": { "emailRecipients": ["uat@acme.com"] } }`
- **THEN** the response is 200 and SandboxMode persists as true

### Requirement: Toggle action is itself audited

Every toggle of `SandboxMode` (on or off) SHALL write an audit entry recording: actor user id, previous value, new value, timestamp, and config diff (if any). This is separate from per-dispatch SandboxRedirect rows.

#### Scenario: Toggle audit row

- **GIVEN** SandboxMode=false
- **WHEN** admin toggles to true at 14:32 UTC
- **THEN** an audit row is written with `actor=<admin id>`, `previous=false`, `new=true`, `at=14:32 UTC`
