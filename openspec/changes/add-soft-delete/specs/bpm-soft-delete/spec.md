## ADDED Requirements

### Requirement: Soft-deletable entities carry DeletedAt + DeletedByUserId

The system SHALL extend the following entities with `DeletedAt` (DateTime?, nullable) and `DeletedByUserId` (Guid?, nullable): Department, Group, Role, Permission, RolePermission, RoleAssignment, BusinessCalendar, CalendarException, WebhookSubscription, SsoConfiguration, EntraSyncConfiguration. Setting DeletedAt to a non-null value soft-deletes the row; default queries SHALL exclude soft-deleted rows via EF Core query filter.

#### Scenario: Default query excludes deleted

- **GIVEN** a Department with DeletedAt set
- **WHEN** code calls `_db.Departments.ToListAsync()`
- **THEN** the deleted department is NOT in the result

#### Scenario: Admin override returns deleted

- **WHEN** code calls `_db.Departments.IgnoreQueryFilters().ToListAsync()`
- **THEN** all departments including soft-deleted are returned

### Requirement: Delete with dependents blocked

When an admin attempts to soft-delete an entity that has active dependents, the API SHALL reject with a clear error message listing the blocking dependents. Examples:

- Cannot delete Department with active members (must reassign first)
- Cannot delete Role with active RoleAssignments (must revoke first)
- Cannot delete Group with active GroupMember rows (must remove first)

#### Scenario: Department with members blocks delete

- **GIVEN** Department D has 5 active users assigned
- **WHEN** admin DELETEs the department
- **THEN** 409 Conflict with message "5 users still in this department; reassign them first"

#### Scenario: Empty Department deletes successfully

- **GIVEN** Department D has no active users
- **WHEN** admin DELETEs
- **THEN** DeletedAt set; row stays in DB; user lists exclude it

### Requirement: Restore endpoint clears DeletedAt

`POST /api/admin/{entity}/{id}/restore` SHALL clear DeletedAt and DeletedByUserId, making the entity active again. The operation SHALL be idempotent (restoring an already-active entity is a no-op success).

#### Scenario: Restore brings back

- **GIVEN** a soft-deleted Department
- **WHEN** admin POSTs /restore
- **THEN** DeletedAt = null; subsequent default queries include it

#### Scenario: Idempotent restore

- **WHEN** admin restores an already-active entity
- **THEN** 200 OK; no-op

### Requirement: Audit on delete and restore

Soft-delete and restore SHALL emit AuditEvent rows via the AuditEventCaptureInterceptor with appropriate Action: `<entity>.deleted` or `<entity>.restored`. The actor user id is recorded.

#### Scenario: Delete audited

- **WHEN** admin soft-deletes a Role
- **THEN** an AuditEvent with Action = "role.deleted", ActorUserId = admin id is inserted

#### Scenario: Restore audited

- **WHEN** admin restores
- **THEN** an AuditEvent with Action = "role.restored" is inserted

### Requirement: References to deleted entities remain visible

Historical queries (completed ProcessInstances, past audit events, archived comments) SHALL continue to resolve references to soft-deleted entities. The UI SHALL render such references with a visual indicator ("已刪除" / "deleted") so users understand the context is historical.

#### Scenario: Completed instance shows deleted dept context

- **GIVEN** a completed instance whose initiator's department was soft-deleted yesterday
- **WHEN** an admin opens the instance detail
- **THEN** the department name is shown with "已刪除" tag; the page still loads correctly
