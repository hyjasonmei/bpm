# flowcook-principal-model Specification

## Purpose

Define a unified **Principal** abstraction (user / dept / group) with role assignment, inheritance flags, and a user-to-user delegation overlay. Replaces the legacy `bpm-org-model`, `bpm-roles-and-permissions`, and `bpm-delegation` specs. The model is the single source of truth for actor resolution in spec `ActorRef`s and runtime task assignment.

## Requirements

### Requirement: Three principal types share one identity pool

The system SHALL provide a `Principal` entity with a `Type` discriminator of `user | dept | group`. All three types SHALL draw their `Id` from the same pool. Junction tables that reference "any actor" SHALL use one `principal_id` foreign key.

#### Scenario: Principal id is unique across types
- **WHEN** a user row and a dept row both exist
- **THEN** their `Id` values are unique within the `Principal` table

#### Scenario: GroupMember references any principal type
- **WHEN** a `GroupMember` row stores `member_principal_id = <userId>`
- **AND** another `GroupMember` row stores `member_principal_id = <groupId>`
- **THEN** both rows are valid without a discriminator column on GroupMember

### Requirement: User ↔ Dept is many-to-many with optional primary

The system SHALL allow a user to belong to multiple departments via the `UserDept` table. A user MAY have at most one primary dept (`is_primary = true`); primary is OPTIONAL.

#### Scenario: User in two departments
- **WHEN** a user is added to "Engineering" and "Product" with one row each
- **AND** "Engineering" row has `is_primary = true`
- **THEN** queries for `user.primary_dept` return Engineering
- **AND** queries for `user.all_depts` return both

#### Scenario: User with no primary dept
- **WHEN** a user has UserDept rows but none has `is_primary = true`
- **THEN** `user.primary_dept` returns null without error

### Requirement: Dept hierarchy is a strict tree

The system SHALL enforce that each dept has at most one parent via `DeptParent`. The dept relation SHALL form a strict tree, not a DAG.

#### Scenario: Dept cannot have two parents
- **WHEN** a dept tries to insert a second DeptParent row
- **THEN** the insert fails with a uniqueness violation

#### Scenario: Cross-department concerns use groups
- **WHEN** an organization needs "Security committee members across all depts"
- **THEN** the modeler SHALL create a `group` principal whose `GroupMember` rows include the relevant users / depts
- **AND** SHALL NOT attempt to give the dept a second parent

### Requirement: Dept can contain users or sub-depts; groups can contain anything

The system SHALL enforce container rules:

| Container | May contain |
|---|---|
| dept | user, dept |
| group | user, dept, group |
| user | nothing |

#### Scenario: Dept rejects group as child
- **WHEN** a dept attempts to add a group as a child
- **THEN** the operation fails with a validation error

#### Scenario: Group can nest groups
- **WHEN** a group `g1` adds another group `g2` as a member
- **THEN** the insert succeeds
- **AND** users of `g2` are transitively included when resolving `g1`'s members

### Requirement: Group nesting must not form cycles

The system SHALL detect and reject membership additions that would create a cycle in the group graph.

#### Scenario: Direct cycle is rejected
- **WHEN** group `g1` contains `g2`, and someone attempts to add `g1` as a member of `g2`
- **THEN** the operation fails

#### Scenario: Indirect cycle is rejected
- **WHEN** `g1 → g2 → g3` and someone attempts to add `g1` as a member of `g3`
- **THEN** the operation fails

### Requirement: Role assignments carry a per-assignment inherit flag

The system SHALL allow any role to be assigned to any principal via `PrincipalRole`. Each assignment SHALL carry `inherit_to_members: bool`. When true, the role inherits down the dept tree and group graph to all descendant users.

#### Scenario: Role assigned to dept with inherit
- **WHEN** "Engineering" dept is assigned role "Approver" with `inherit_to_members = true`
- **THEN** every user reachable through the dept (directly or via sub-depts) SHALL have the "Approver" role in their effective set

#### Scenario: Role assigned to dept without inherit
- **WHEN** "Engineering" dept is assigned role "DeptInbox" with `inherit_to_members = false`
- **THEN** the dept itself can be the actor of a task
- **AND** individual users in the dept SHALL NOT have "DeptInbox" in their effective set unless granted separately

### Requirement: Effective role resolution combines direct + inherited

The system SHALL compute each user's effective roles as the union of:

1. Roles assigned directly to the user
2. Roles assigned with `inherit_to_members = true` to any dept reachable from the user (walking UserDept and DeptParent up the tree)
3. Roles assigned with `inherit_to_members = true` to any group containing the user (transitively through GroupMember)

#### Scenario: Effective set computed on demand
- **WHEN** a caller invokes `GetEffectiveRolesAsync(userId)`
- **THEN** the resolver returns the union of direct + dept-inherited + group-inherited roles

#### Scenario: Source principal is preserved
- **WHEN** an effective role is returned
- **THEN** the result MAY include `source_principal_id` (the principal whose assignment produced the role) for audit / debugging

### Requirement: Delegation is a user-to-user overlay layered on top of Principal resolution

The system SHALL provide a `Delegation` table that records `delegator_principal_id × delegate_to_user_id × start_at × end_at × active × reason?`. When resolving an actor, the system SHALL first resolve to a Principal, then check Delegation for an active record; if present, the delegate user's effective roles SHALL be used instead.

#### Scenario: Active delegation redirects roles
- **WHEN** delegator `alice` has an active Delegation to `bob` covering the current time
- **AND** a task assigned to `alice` is being resolved
- **THEN** the runtime SHALL surface the task to `bob`
- **AND** record both `original_assignee_id = alice` and `actual_assignee_id = bob` on the task

#### Scenario: Delegation target must be a user
- **WHEN** the system attempts to insert a Delegation with `delegate_to_user_id` pointing to a dept or group principal
- **THEN** the insert fails with a validation error

#### Scenario: Delegation is not transitive
- **WHEN** `alice → bob` and `bob → carol` both have active Delegations
- **AND** a task assigned to `alice` is being resolved
- **THEN** the task SHALL go to `bob` (not transitively to `carol`)
- **AND** if `bob` is genuinely unable to act, the customer SHALL configure a direct `alice → carol` Delegation

### Requirement: Soft-delete only

The system SHALL implement soft-deletion (`deleted_at`) for `Principal` and related rows. Hard deletion SHALL NOT be exposed through any API in production.

#### Scenario: Soft-deleted principal hides from queries
- **WHEN** a principal row has `deleted_at` set
- **THEN** all standard queries filter it out via EF global query filter

#### Scenario: SeedCli reset bypasses soft delete
- **WHEN** `dotnet run --project SeedCli -- clear` is invoked in development
- **THEN** the underlying DB is dropped and recreated (not soft-deleted), but this command is gated by the development environment guard
