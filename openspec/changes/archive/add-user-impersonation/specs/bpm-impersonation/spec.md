## ADDED Requirements

### Requirement: Only admins can start impersonation

The system SHALL allow `POST /api/impersonation/start` only when the caller's authenticated identity has role `admin`. Non-admin callers MUST receive 403.

#### Scenario: Non-admin rejected

- **GIVEN** Wilson (employee) is authenticated
- **WHEN** he calls `POST /api/impersonation/start` with `{ targetUserId: <bob>, reason: "test" }`
- **THEN** the response is 403

#### Scenario: Admin accepted

- **GIVEN** an admin user is authenticated
- **WHEN** they call start with valid target and reason
- **THEN** the response is 200 with a JWT in the body

### Requirement: Impersonation has hard guards

The system SHALL reject `POST /api/impersonation/start` when:

- `targetUserId` does not exist or `IsActive=false` → 404
- `targetUserId == caller.Id` → 409 ("cannot impersonate self")
- The caller already has an active impersonation session → 409 ("already impersonating; end first")
- The caller's current JWT carries `impersonated_by` claim (nested impersonation) → 409 ("cannot nest impersonation")
- `reason` is missing or whitespace → 400

#### Scenario: Self impersonation rejected

- **WHEN** admin calls start with `targetUserId = caller.Id`
- **THEN** 409 with message naming "cannot impersonate self"

#### Scenario: Nested impersonation rejected

- **GIVEN** admin's current JWT already has `impersonated_by` claim (i.e., currently acting as someone)
- **WHEN** they call start
- **THEN** 409

#### Scenario: Empty reason rejected

- **WHEN** admin calls start with `reason: ""`
- **THEN** 400

### Requirement: Impersonation token claims and lifetime

A successful start SHALL return a JWT with claims:

- `sub` = `targetUserId`
- `email` = `target.Email`
- `roles` = roles of the target (NOT the admin)
- `impersonated_by` = `caller.Id`
- `imp_session_id` = `ImpersonationSession.Id`
- `exp` = now + 30 minutes

#### Scenario: Token claims set correctly

- **GIVEN** admin Sandy starts impersonation of Bob who has roles `[viewer, hr]`
- **WHEN** decoded
- **THEN** `sub == Bob.Id`, `roles == [viewer, hr]`, `impersonated_by == Sandy.Id`, `exp` is ~30 minutes from now

#### Scenario: Backend treats request as the target

- **GIVEN** admin holds the impersonation token
- **WHEN** they call `GET /api/hr-flows/mine`
- **THEN** the response contains Bob's flows (not Sandy's)
- **AND** subsequent state-changing calls (e.g., approve) are recorded with `ActorUserId = Bob.Id`

### Requirement: Audit trails carry impersonator id

All audit-row entities (HrFlowAction, ActorResolutionAudit, future TaskHistory) SHALL include a nullable `ImpersonatedByUserId` column. When a state-changing operation is performed under an impersonation token, the audit row SHALL be persisted with `ActorUserId = target.Id` AND `ImpersonatedByUserId = impersonator.Id`. When not impersonating, `ImpersonatedByUserId` SHALL be null.

#### Scenario: Approve under impersonation tags both ids

- **GIVEN** admin Sandy is impersonating manager Elton; an HrFlow instance is awaiting Elton's approval
- **WHEN** Sandy (as Elton) approves
- **THEN** the resulting HrFlowAction row has `ActorUserId = Elton.Id`, `ImpersonatedByUserId = Sandy.Id`

#### Scenario: Approve without impersonation has null impersonator

- **GIVEN** Elton is logged in normally (no impersonation)
- **WHEN** Elton approves
- **THEN** the resulting HrFlowAction row has `ActorUserId = Elton.Id`, `ImpersonatedByUserId = null`

### Requirement: Active sessions are tracked and uniquely scoped per impersonator

The system SHALL persist one `ImpersonationSession` row per start with `EndedAt = null`. At any time, an admin SHALL have at most one active session (rows with `EndedAt = null`). When the admin ends or the session expires, `EndedAt` and `EndReason` MUST be set.

#### Scenario: One active per admin

- **GIVEN** admin starts impersonating Bob
- **WHEN** they immediately try to start impersonating Carol
- **THEN** the second start is rejected with 409 until the first ends

#### Scenario: End sets EndedAt + reason

- **GIVEN** admin has an active session
- **WHEN** they call `POST /api/impersonation/end`
- **THEN** the session row has `EndedAt` set to now and `EndReason = ManualExit`

### Requirement: 30-minute token expiry triggers automatic end

The impersonation JWT SHALL expire 30 minutes after issue. When the frontend receives 401 on a request bearing an expired impersonation token, it SHALL silently restore the pre-impersonation token. The backend SHALL lazily mark the session `EndedAt = now`, `EndReason = AutoExpired` on the next admin lookup that observes the expired session.

#### Scenario: Expired token returns 401

- **GIVEN** an impersonation token issued 31 minutes ago
- **WHEN** the frontend uses it on any endpoint
- **THEN** the response is 401

#### Scenario: Lazy AutoExpired marking

- **GIVEN** an active session whose JWT has expired but EndedAt is still null
- **WHEN** an admin calls `GET /api/impersonation/sessions`
- **THEN** the system marks the row EndedAt=now, EndReason=AutoExpired before returning history

### Requirement: UI shows non-dismissible banner during impersonation

While the frontend holds an impersonation token, it SHALL render a non-dismissible red banner above the application header containing: target user's full name, impersonator id (or fullName if available), countdown of remaining time (mm:ss), and an Exit button. The banner SHALL update its countdown every second and turn amber in the last 5 minutes. The banner MUST NOT be hideable.

#### Scenario: Banner present during impersonation

- **GIVEN** admin holds an active impersonation token for Alice
- **WHEN** they navigate to any page
- **THEN** a red banner is shown with text matching `ACTING AS Alice ... · [time] left · [Exit]`

#### Scenario: Banner absent in normal session

- **GIVEN** admin is in a normal (non-impersonation) session
- **WHEN** they load any page
- **THEN** no impersonation banner is rendered

### Requirement: Exit endpoint and UI button restore admin context

The Exit button SHALL call `POST /api/impersonation/end` and then swap the localStorage JWT back to the pre-impersonation value (saved at start time under key `bpm_jwt_pre_impersonation`). The frontend SHALL then reload to ensure all components re-render with the admin role.

#### Scenario: Exit restores admin context

- **GIVEN** admin is impersonating Bob
- **WHEN** they click the banner's Exit button
- **THEN** end endpoint is called, localStorage `bpm_jwt` is restored to the admin's original JWT, and the page reloads
- **AND** the next API call uses the admin JWT

### Requirement: Admin can revoke another admin's session

The endpoint `POST /api/impersonation/sessions/{id}/revoke` SHALL allow an admin to forcibly end any active session (their own or another admin's). The session row's `EndReason` MUST be set to `AdminRevoked` and `EndedAt` to now.

#### Scenario: Admin B revokes admin A's session

- **GIVEN** admin A has an active session impersonating Carol
- **WHEN** admin B calls revoke on that session id
- **THEN** the session is marked EndedAt=now, EndReason=AdminRevoked
- **AND** admin A's next API call (with the now-defunct token) receives 401 once the JWT is rejected by the backend's session-id validity check (or, since JWT itself is unchanged, only audit reflects revocation; admin A continues until JWT expires)

> Note: revocation marks the session row but does not invalidate the still-valid JWT. The defense-in-depth check (compare imp_session_id against an active sessions table on each request) is **out of scope for this change** — added in a follow-up if needed. The audit value of revocation is preserved (you can prove an admin tried to stop the session).
