# bpm-acceptance-sandbox Specification

## Purpose
TBD - created by archiving change add-acceptance-sandbox. Update Purpose after archive.
## Requirements
### Requirement: Sandbox is a tenant-level toggle observable to every UI surface

The system SHALL store sandbox mode as a single boolean per tenant (`TenantSettings.SandboxMode`). When ON, every UI surface (`bpm-admin-ui` and `bpm-ui`) SHALL render an unmissable `SandboxBanner` showing: sandbox is on, current sandbox-time offset (e.g., "+ 2 days"), and unread captured-message counts. When OFF, the banner is hidden and all sandbox-only endpoints return 400 / 403.

#### Scenario: Banner visible everywhere when sandbox on

- **GIVEN** sandbox mode is ON for the current tenant
- **WHEN** the user loads any screen in `bpm-admin-ui` or `bpm-ui`
- **THEN** the SandboxBanner is rendered at the top of the layout
- **AND** the banner shows the clock offset and captured-count summary

#### Scenario: Sandbox endpoints refuse in production mode

- **GIVEN** sandbox mode is OFF
- **WHEN** a client calls `POST /api/sandbox/clock/advance`
- **THEN** the response is 400 with body `{ error: "sandbox_off" }`

### Requirement: Sandbox toggle requires admin role and emits audit

The endpoint `PUT /api/sandbox/status` SHALL require the caller to have admin role and SHALL emit an audit-log entry on every toggle (both ON and OFF) recording: actor user id, previous mode, new mode, timestamp, optional reason string from the request body. The toggle endpoint MAY be disabled entirely via the env var `BPM_SANDBOX_TOGGLE_DISABLED=true` (returns 403) for prod deploys that want to lock the toggle off.

#### Scenario: Non-admin cannot toggle

- **GIVEN** a JWT with role = "manager"
- **WHEN** the user PUTs `/api/sandbox/status { Enabled: true }`
- **THEN** the response is 403

#### Scenario: Toggle audited

- **GIVEN** an admin toggles sandbox ON
- **WHEN** the request completes
- **THEN** an audit-log entry exists with action = "sandbox_enabled", actor = the admin's user id, timestamp = the toggle moment

#### Scenario: Production lock prevents toggle even from admin

- **GIVEN** the environment variable `BPM_SANDBOX_TOGGLE_DISABLED=true` is set
- **WHEN** an admin attempts to PUT `/api/sandbox/status`
- **THEN** the response is 403 with body `{ error: "sandbox_toggle_disabled" }`

### Requirement: Sandbox persona issues a JWT carrying the actual actor id

`POST /api/sandbox/persona { userId }` SHALL be available only when sandbox mode is ON, and only to admin callers. On success it SHALL return a fresh JWT whose `sub` claim equals the requested persona user id and whose `actual_actor_id` claim equals the calling admin's user id. The `sandbox_actor` claim SHALL be set to `true`. Subsequent requests using this JWT SHALL be processed as if the persona issued them, EXCEPT that audit / history rows SHALL also persist the `actual_actor_id` so post-UAT audits can prove "this was Jason testing as Mary, not Mary herself."

#### Scenario: Admin switches to persona, approves, history records both

- **GIVEN** sandbox is ON and admin Jason calls `POST /api/sandbox/persona { userId: Mary.Id }`
- **WHEN** Jason uses the returned JWT to approve a task
- **THEN** the resulting TaskHistory row has `ActorUserId = Mary.Id`
- **AND** the row also persists `SandboxActualActor = Jason.Id`

#### Scenario: Persona endpoint refuses when sandbox off

- **GIVEN** sandbox is OFF
- **WHEN** an admin calls `POST /api/sandbox/persona { userId: <any> }`
- **THEN** the response is 400 with body `{ error: "sandbox_off" }`
- **AND** no JWT is issued

#### Scenario: Persona endpoint refuses non-admin

- **GIVEN** sandbox is ON, requester role = "manager"
- **WHEN** they call `POST /api/sandbox/persona { userId: ... }`
- **THEN** the response is 403

### Requirement: Sandbox-aware clock applies a forward-only offset

The system SHALL provide a `SandboxClock` decorator over the system clock. When sandbox is OFF, the clock pass-throughs the real time. When sandbox is ON, the clock SHALL add `TenantSettings.SandboxClockOffsetSeconds` to every read of `UtcNow`. The offset SHALL be modifiable only by `POST /api/sandbox/clock/advance` (positive deltas) or `POST /api/sandbox/clock/reset` (clears to 0). Negative deltas SHALL be refused with 400 — backward time travel is forbidden because it confuses in-flight comparisons.

#### Scenario: Clock advance moves sandbox time forward

- **GIVEN** sandbox is ON, current offset = 0
- **WHEN** an admin calls `POST /api/sandbox/clock/advance { hours: 48 }`
- **THEN** the response is 200 with `{ offsetSeconds: 172800, sandboxNow: <real now + 48h> }`
- **AND** subsequent calls to `GET /api/sandbox/clock` reflect the same offset

#### Scenario: Backward delta refused

- **GIVEN** sandbox is ON, offset = 100
- **WHEN** the admin calls `POST /api/sandbox/clock/advance { hours: -1 }`
- **THEN** the response is 400 with body `{ error: "negative_delta_forbidden" }`
- **AND** the offset is unchanged

#### Scenario: Clock reset clears offset

- **GIVEN** offset is 172800 (48h forward)
- **WHEN** an admin calls `POST /api/sandbox/clock/reset`
- **THEN** the offset becomes 0
- **AND** an audit row is written with action = `Reset`, oldOffsetSeconds = 172800, newOffsetSeconds = 0

### Requirement: Clock advance kicks time-sensitive workers immediately

`POST /api/sandbox/clock/advance` SHALL synchronously kick the SLA, webhook, and notification dispatch workers via `IBackgroundJobScheduler.KickAsync` so testers see the consequence of time advance without waiting for the next worker tick (which can be up to 60 seconds away).

#### Scenario: SLA breach captured immediately after advance

- **GIVEN** an instance is at a userTask whose SLA breaches in 24h
- **AND** sandbox is ON
- **WHEN** an admin advances the clock by 25 hours
- **THEN** within 2 seconds of the advance returning, a `SandboxCapturedMessage` row exists for the SLA-breach notification
- **AND** the user does not have to wait for the next worker tick

### Requirement: Reset is hard-delete and refuses outside sandbox

`POST /api/sandbox/reset/instance/{id}` and `POST /api/sandbox/reset/all` SHALL hard-delete the targeted rows (no soft-delete). They SHALL refuse with 400 when sandbox is OFF. The all-reset SHALL require admin role. Specs, bundles, org chart data, and tenant settings SHALL NOT be touched by either reset.

#### Scenario: Reset instance preserves other instances

- **GIVEN** two instances A and B exist, each with captured mail
- **WHEN** an admin resets only A
- **THEN** A's ProcessInstance / ProcessTasks / TaskHistory / SandboxCapturedMessages are deleted
- **AND** B's data is untouched

#### Scenario: Reset all preserves spec / org / bundle

- **GIVEN** the tenant has 5 specs, 3 bundles, an org with 50 users, and 12 instances with captured data
- **WHEN** an admin calls `POST /api/sandbox/reset/all`
- **THEN** all 12 instances + their tasks / history / captured messages are deleted
- **AND** all 5 specs, 3 bundles, and 50 users are untouched

