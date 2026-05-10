## ADDED Requirements

### Requirement: Two specCodes are accepted; all others rejected

The system SHALL accept `RESIGN` and `DEPTX` as the only valid `specCode` values for HR flow endpoints in this capability. Any other value MUST be rejected with HTTP 400. This capability is intentionally a hard-coded interim implementation; the generic process runtime (separate change) handles arbitrary spec codes.

#### Scenario: Valid specCode accepted

- **GIVEN** Wilson is authenticated and has a manager
- **WHEN** he calls `POST /api/hr-flows/RESIGN` with valid form data
- **THEN** the response is 201 with a new `HrFlowInstance` payload, `Status = PendingManager`

#### Scenario: Unknown specCode rejected

- **WHEN** he calls `POST /api/hr-flows/PURCHASE` with any body
- **THEN** the response is 400 with a validation error naming the unsupported specCode

### Requirement: Initiator must have a resolved manager at start

The system SHALL look up the initiator's `ManagerUserId` at instance start and persist it on the instance as `ResolvedManagerUserId`. If the initiator has no manager, the start MUST fail with HTTP 422 and a clear error. The resolved manager value SHALL NOT change for the lifetime of the instance, even if the org chart later changes.

#### Scenario: Manager exists at start

- **GIVEN** Wilson's manager is Yang
- **WHEN** Wilson starts a RESIGN instance
- **THEN** `instance.ResolvedManagerUserId = Yang`

#### Scenario: No manager → start fails

- **GIVEN** Wilson has no `ManagerUserId`
- **WHEN** he calls `POST /api/hr-flows/RESIGN`
- **THEN** the response is 422 with message "no manager assigned; cannot start HR flow"

#### Scenario: Manager change mid-flight does not retarget

- **GIVEN** Wilson's instance is `PendingManager` with `ResolvedManagerUserId = Yang`
- **WHEN** an admin reassigns Wilson's manager to Lin
- **AND** Yang then approves
- **THEN** the approve succeeds (Yang is still the resolved manager)
- **AND** Lin attempting to approve the same instance is rejected with 403

### Requirement: State transitions follow the fixed table

The system SHALL transition `HrFlowInstance.Status` only per the following rules:

| From Status | Action | Required Actor | To Status | To Step |
|---|---|---|---|---|
| (none) | Start | Initiator | PendingManager | ManagerApprove |
| PendingManager | Approve | ResolvedManager | PendingHr | HrApprove |
| PendingManager | Return | ResolvedManager | Returned | Apply |
| PendingHr | Approve | Any user with role `hr` | Completed | Closed |
| Returned | Resubmit | Initiator | PendingManager | ManagerApprove |
| Any non-Completed | Cancel | Initiator | Cancelled | unchanged |

Any other (status, action) pair MUST be rejected. Manager attempting Return at HrApprove step MUST fail. HR attempting Return MUST fail. Initiator attempting Approve MUST fail.

#### Scenario: Manager approve advances to HR

- **GIVEN** Wilson started RESIGN, Yang is resolved manager, status = PendingManager
- **WHEN** Yang calls `POST /api/hr-flows/{id}/approve`
- **THEN** Status becomes PendingHr, CurrentStep becomes HrApprove
- **AND** an `HrFlowAction` row is written with Action=Approve, FromStep=ManagerApprove, ToStep=HrApprove, ActorUserId=Yang

#### Scenario: HR approve completes the instance

- **GIVEN** the instance is PendingHr
- **WHEN** any HR user calls approve
- **THEN** Status becomes Completed, CurrentStep becomes Closed, CompletedAt is set

#### Scenario: Return at HrApprove rejected

- **GIVEN** the instance is PendingHr
- **WHEN** an HR user calls `POST /api/hr-flows/{id}/return`
- **THEN** the response is 409 with message indicating Return is not allowed at this step

#### Scenario: Initiator cannot self-approve

- **GIVEN** Wilson started the instance
- **WHEN** Wilson calls approve
- **THEN** the response is 403

### Requirement: Return requires a non-empty comment

The system SHALL reject `POST /api/hr-flows/{id}/return` if `comment` is missing or empty/whitespace. Approve and Cancel allow optional comment.

#### Scenario: Return without comment

- **WHEN** Yang calls `POST /api/hr-flows/{id}/return` with `{ "comment": "" }`
- **THEN** the response is 400

#### Scenario: Return with comment

- **WHEN** Yang calls return with `{ "comment": "缺離職日" }`
- **THEN** the action succeeds and the comment is persisted in the `HrFlowAction` row

### Requirement: Resubmit replaces form data wholesale

When the initiator resubmits a Returned instance, the system SHALL replace `FormDataJson` entirely with the new payload (no merge). Status returns to `PendingManager`, step returns to `ManagerApprove`. The instance's `ResolvedManagerUserId` SHALL NOT be re-resolved (still the original).

#### Scenario: Resubmit overrides earlier data

- **GIVEN** original RESIGN payload had `expectedLastDay = 2026-06-30`
- **AND** Yang returned the instance with comment "改晚一點"
- **WHEN** Wilson resubmits with `expectedLastDay = 2026-07-15` (and no other fields)
- **THEN** stored FormDataJson now contains only `expectedLastDay = 2026-07-15` and other fields are gone (wholesale replace)

### Requirement: HR pool is first-come-first-served, no claim step

The system SHALL allow ANY user with role `hr` in the same tenant to approve an instance in `PendingHr`. There is no claim step; the first successful approve transitions the instance to Completed; subsequent approve attempts by other HR users on the same instance MUST fail with 409 (status no longer PendingHr).

#### Scenario: First HR wins

- **GIVEN** the tenant has HR users {Anna, Beth}; instance is PendingHr
- **WHEN** Anna and Beth both call approve concurrently
- **THEN** exactly one of them succeeds (200) and the other receives 409
- **AND** the instance has exactly one `HrFlowAction` row of type `Approve` from `HrApprove` to `Closed`

### Requirement: Audit actions are append-only

The system SHALL persist one `HrFlowAction` row per state-changing event in the same DB transaction as the state change. `HrFlowAction` rows MUST NOT be updated or deleted by the application. Attempting to modify an existing row MUST throw at SaveChanges time. (When the generic process runtime supersedes this capability, audit migrates to `TaskHistory`.)

#### Scenario: Each action writes one row

- **GIVEN** the happy path: Start → ManagerApprove → HrApprove
- **THEN** the instance has exactly 3 `HrFlowAction` rows in chronological order with types `Submit`, `Approve`, `Approve`

#### Scenario: Update of action row blocked

- **WHEN** code loads an `HrFlowAction` row, mutates `Comment`, calls SaveChanges
- **THEN** an exception is thrown and the change is not persisted

### Requirement: Permission model on read endpoints

The system SHALL allow `GET /api/hr-flows/{id}` for: the initiator, the `ResolvedManagerUserId`, and (only when `Status = PendingHr`) any user with role `hr`. All other callers MUST receive 403. `GET /api/hr-flows/mine` returns only instances initiated by the caller. `GET /api/hr-flows/todo` returns only instances awaiting the caller's action (PendingManager where caller = ResolvedManager, OR PendingHr where caller has role hr).

#### Scenario: Random user cannot read

- **GIVEN** the instance was started by Wilson; Yang is resolved manager
- **WHEN** Lin (unrelated user) calls `GET /api/hr-flows/{id}`
- **THEN** the response is 403

#### Scenario: HR can read only while PendingHr

- **GIVEN** instance is PendingManager (not yet at HR)
- **WHEN** Anna (HR) calls `GET /api/hr-flows/{id}`
- **THEN** the response is 403

- **GIVEN** instance is PendingHr
- **WHEN** Anna calls `GET /api/hr-flows/{id}`
- **THEN** the response is 200

### Requirement: Capability is sunset-marked

The system SHALL document, in code header comments on `HrFlowsController`, `HrFlowService`, and `HrFlowInstance`, that this capability is interim and SHALL be replaced by `add-process-runtime` when that change ships. Once the runtime is in production, this capability's spec MUST be archived and its endpoints MUST be removed (or kept as redirect shims for one release cycle).

#### Scenario: Sunset marker present

- **WHEN** a developer opens `HrFlowsController.cs`
- **THEN** the file's top-level comment block names `add-process-runtime` as the successor and explains the migration plan
