## ADDED Requirements

### Requirement: Role entity with system-vs-flow scope

The system SHALL persist roles with `code` (unique), `name`, `scope` (`"system"` or `"flow"`), and `flow_code` (nullable; required when `scope = "flow"`, must be null when `scope = "system"`). System roles apply across all flows; flow roles apply only when the named flow is being processed.

#### Scenario: System role applies globally
- **WHEN** a Role has `scope = "system"` and `code = "admin"`
- **AND** an authorization check asks "does user X have role admin"
- **THEN** any RoleAssignment of role `admin` to user X grants the role regardless of flow context

#### Scenario: Flow role applies only in matching flow
- **WHEN** a Role has `scope = "flow"`, `code = "finance_manager"`, `flow_code = "purchase_v1"`
- **AND** an authorization check asks "does user X have role finance_manager in flow leave_v1"
- **THEN** the check returns false (assignments to this role only count for `purchase_v1`)

#### Scenario: Flow role missing flow_code rejected
- **WHEN** an attempt is made to insert a Role with `scope = "flow"` and `flow_code = null`
- **THEN** persistence rejects the row (DB constraint or domain validation)

### Requirement: Permission as (action, resource) pair

The system SHALL persist permissions as `(action, resource)` strings (e.g., `("approve", "leave_v1")`, `("submit", "purchase_v1")`, `("manage", "users")`). Permissions SHALL be granted to roles via a `RolePermission` n-n relation.

#### Scenario: A role grants multiple permissions
- **WHEN** Role `designer` has RolePermission rows for `("read", "specs")` and `("write", "specs")`
- **THEN** querying "permissions of role designer" returns both pairs

### Requirement: Role assignment to any principal type with scope

The system SHALL persist role assignments as `(role_id, principal_id, scope, scope_ref)` where `scope` is one of `"tenant"`, `"flow"`, `"step"`. `scope_ref` is null for `tenant`, the flow code for `flow`, and `flow_code:step_code` for `step`. Because `principal_id` references the `Principal` table, a role MAY be assigned to a User, a Group, or a Department.

#### Scenario: Role assigned to a Group cascades to members
- **WHEN** Role `designer` is assigned to Group `engineering_team` with `scope = "tenant"`
- **AND** an authorization check asks "does user X (a member of engineering_team) have role designer"
- **THEN** the check returns true

#### Scenario: Role assigned to a Department cascades to its users
- **WHEN** Role `viewer` is assigned to Department `finance` with `scope = "tenant"`
- **AND** an authorization check asks "does user Y (whose department_id = finance.id) have role viewer"
- **THEN** the check returns true

#### Scenario: Step-scoped role assignment
- **WHEN** Role `auditor` is assigned to user Z with `scope = "step"`, `scope_ref = "purchase_v1:approval_step_2"`
- **AND** the workflow engine asks "does user Z have role auditor in step approval_step_2 of purchase_v1"
- **THEN** the check returns true; queries for any other step return false

### Requirement: Effective-roles query collapses indirect grants

The system SHALL provide a query "effective roles for user X (optionally scoped to flow F)" that returns the union of:
- Direct RoleAssignments to X
- RoleAssignments to any Group X is a transitive member of
- RoleAssignments to the Department X belongs to (if `scope` is compatible)

#### Scenario: Direct + group-inherited roles combined
- **WHEN** user X has direct RoleAssignment for `viewer` AND is a member of a group with RoleAssignment for `designer`
- **AND** the effective-roles query is run on X
- **THEN** the result contains both `viewer` and `designer`

### Requirement: Role assignment changes are audited

The system SHALL persist a `RoleAssignmentChange` row for every successful role grant or revoke, capturing: `ActorUserId` (who initiated the change), `TargetUserId` (who the role applies to), `RoleId` + `RoleCodeSnapshot` (snapshot resilient to role rename), `Action` (`Assign` / `Revoke`), `Scope`, `ScopeRef`, optional `ImpersonatedByUserId` (when the actor was acting under impersonation).

`RoleAssignmentChange` SHALL implement `IImpersonable` so the impersonation interceptor populates `ImpersonatedByUserId` automatically when applicable. The audit row SHALL be append-only — no UPDATE / DELETE permitted at the application layer.

#### Scenario: Direct grant audited

- **GIVEN** admin Jason grants role "finance_manager" to user Mary at tenant scope
- **WHEN** the grant is persisted
- **THEN** a RoleAssignmentChange row exists with `ActorUserId = Jason`, `TargetUserId = Mary`, `RoleCodeSnapshot = "finance_manager"`, `Action = Assign`, `Scope = Tenant`, `ScopeRef = null`

#### Scenario: Revoke audited

- **WHEN** admin Jason revokes Mary's "finance_manager" role
- **THEN** a separate RoleAssignmentChange row exists with `Action = Revoke`

#### Scenario: Role rename does not break audit history

- **GIVEN** a RoleAssignmentChange row with `RoleCodeSnapshot = "finance_manager"` and `RoleId = R1`
- **WHEN** an admin renames role R1 from "finance_manager" to "fin_mgr"
- **THEN** the snapshot field still reads "finance_manager" — historical audit reflects what the role was *named at the time of the change*

#### Scenario: Impersonated grant records both actors

- **GIVEN** Jason is impersonating Mary, and "Mary" grants role X to user Z
- **WHEN** the grant is persisted
- **THEN** the row has `ActorUserId = Mary`, `ImpersonatedByUserId = Jason`
