## ADDED Requirements

### Requirement: SandboxClock decorates the system clock with a persistent offset

The system SHALL register `SandboxClock` as the `IClock` implementation, decorating the underlying `SystemClock`. When sandbox is OFF, `UtcNow` SHALL return the wrapped real time. When sandbox is ON, it SHALL return real time + `TenantSettings.SandboxClockOffsetSeconds`. The offset SHALL persist in the database so it survives `bpm-svc` restarts (a tester who advanced 48h yesterday returns today still at +48h, as expected for resuming UAT). The offset SHALL be read with per-request caching to avoid SQL traffic on every timestamp call.

#### Scenario: Pass-through when sandbox off

- **GIVEN** sandbox is OFF
- **WHEN** any code reads `IClock.UtcNow`
- **THEN** the returned value is identical to `DateTimeOffset.UtcNow` (within microseconds)

#### Scenario: Offset applied when sandbox on

- **GIVEN** sandbox is ON, offset = 86400 (1 day)
- **WHEN** any code reads `IClock.UtcNow`
- **THEN** the returned value is real now + 1 day

#### Scenario: Offset survives restart

- **GIVEN** offset = 172800 (2 days), sandbox is ON
- **WHEN** bpm-svc restarts
- **THEN** the next `IClock.UtcNow` read after restart is still real now + 2 days

#### Scenario: Per-request cache

- **WHEN** a single HTTP request reads `IClock.UtcNow` 100 times
- **THEN** at most 1 SQL query loads `TenantSettings.SandboxClockOffsetSeconds`

### Requirement: Clock advance API is forward-only and audited

`POST /api/sandbox/clock/advance` SHALL:
- Accept `{ days?, hours?, minutes? }` (all integers)
- Refuse with 400 if all are omitted or sum to ≤ 0
- Refuse with 400 if sandbox is OFF
- Compute new offset = old offset + delta seconds
- Persist new offset to `TenantSettings`
- Write a `SandboxClockEvent` audit row with `Action = Advance`, `OldOffsetSeconds`, `NewOffsetSeconds`, `ActorUserId`, `ChangedAt`
- Trigger `IBackgroundJobScheduler.KickAsync(["SlaTimer", "WebhookDispatch", "NotificationDispatch"])` synchronously before responding so testers see consequences immediately
- Return `{ offsetSeconds, sandboxNow, kickedJobs[] }`

#### Scenario: Advance rejects zero

- **WHEN** the admin calls `POST /api/sandbox/clock/advance { days: 0, hours: 0, minutes: 0 }`
- **THEN** the response is 400 with body `{ error: "delta_must_be_positive" }`

#### Scenario: Advance triggers worker pass

- **GIVEN** an instance with an SLA breaching at real now + 1h
- **WHEN** the admin advances clock by 2h
- **THEN** before the response returns, the SLA timer worker has been kicked once
- **AND** within 2 seconds of the advance returning, a captured email exists for the SLA-breach notification

### Requirement: Clock reset clears offset to zero and is audited

`POST /api/sandbox/clock/reset` SHALL set `SandboxClockOffsetSeconds = 0`, write a `SandboxClockEvent` row with `Action = Reset`, and return `{ offsetSeconds: 0, sandboxNow: <real now> }`. Reset SHALL be available regardless of current offset value (no-op if already zero, but still emits audit row noting the request).

#### Scenario: Reset from non-zero offset

- **GIVEN** offset = 172800
- **WHEN** an admin calls `POST /api/sandbox/clock/reset`
- **THEN** the offset becomes 0
- **AND** a `SandboxClockEvent` row exists with `OldOffsetSeconds = 172800`, `NewOffsetSeconds = 0`, `Action = Reset`

#### Scenario: Reset when already zero still audits

- **GIVEN** offset = 0
- **WHEN** an admin calls reset
- **THEN** the response is 200
- **AND** an audit row exists with `OldOffsetSeconds = 0`, `NewOffsetSeconds = 0`, `Action = Reset`

### Requirement: Per-instance reset hard-deletes one workflow's data

`POST /api/sandbox/reset/instance/{id}` SHALL:
- Refuse with 400 when sandbox is OFF
- Hard-delete the ProcessInstance + all ProcessTasks + all TaskHistory rows + all SandboxCapturedMessages whose `ProcessInstanceId = id`
- Leave all other ProcessInstances, captured messages, sandbox-clock state, specs, bundles, and org data untouched
- Write an audit row recording the reset (action = "instance_reset", target = instance id, actor = caller)
- Return 204 No Content on success

#### Scenario: Adjacent instance untouched

- **GIVEN** instances A and B both exist with captured data
- **WHEN** an admin resets only A
- **THEN** A's row count = 0 in ProcessInstances / Tasks / History / Captured (for A's id)
- **AND** B's row count is unchanged

### Requirement: Full reset hard-deletes all transient sandbox data

`POST /api/sandbox/reset/all` SHALL:
- Refuse with 400 when sandbox is OFF
- Refuse with 403 when caller is not admin
- Hard-delete: all ProcessInstances, all ProcessTasks, all TaskHistory, all SandboxCapturedMessages, all SandboxClockEvents older than now (keeping only the just-written reset audit), reset `SandboxClockOffsetSeconds = 0`
- Leave specs, bundles, org chart, tenant settings (other than the offset), users, roles, permissions untouched
- Return 204 No Content

#### Scenario: Bundles preserved

- **GIVEN** the tenant has 5 spec bundles installed
- **WHEN** an admin calls full reset
- **THEN** all 5 spec bundles still exist in the Flow Library after reset

#### Scenario: Org untouched

- **GIVEN** the tenant has 50 users in 10 departments
- **WHEN** an admin calls full reset
- **THEN** all 50 users and 10 departments still exist

#### Scenario: Clock offset reset to zero

- **GIVEN** sandbox-clock offset = 172800 (2 days)
- **WHEN** an admin calls full reset
- **THEN** the offset becomes 0
- **AND** subsequent `IClock.UtcNow` reads return real time

### Requirement: Reset audit always preserved

The audit row written by any reset action (instance / all / clock) SHALL itself be exempt from the reset and SHALL persist as historical evidence. Only one audit row is created per reset call (the reset itself); the deleted history rows are gone. This is intentional: sandbox is for testing, post-test forensics is "we did this reset at 14:32, here's by whom, that's all."

#### Scenario: Reset-all audit row survives the reset it records

- **WHEN** an admin calls `POST /api/sandbox/reset/all`
- **THEN** in the same transaction, an audit row recording the reset is committed
- **AND** the row remains present after the reset operation completes
- **AND** subsequent calls to the audit log API show this row
