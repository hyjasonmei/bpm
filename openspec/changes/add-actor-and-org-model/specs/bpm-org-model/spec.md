## ADDED Requirements

### Requirement: Principal abstraction with shared PK pool

The system SHALL provide a `Principal` abstraction such that `User`, `Department`, and `Group` entities each have an `Id` drawn from the same pool. Junction tables that need to refer to "any actor" (e.g., `GroupMember`, `RoleAssignment`) SHALL use a single `principal_id` foreign key that resolves to the matching `User`, `Department`, or `Group` row.

#### Scenario: Principal id is unique across types
- **WHEN** a `User` row and a `Department` row are inserted
- **THEN** their `Id` values are guaranteed unique within the `Principal` table (not just within their own subtype table)

#### Scenario: GroupMember references any principal type
- **WHEN** a `GroupMember` row is inserted with `principal_id = <user_id>`
- **AND** a second `GroupMember` row is inserted with `principal_id = <group_id>`
- **THEN** both inserts succeed without a discriminator column on `GroupMember`

### Requirement: User entity with manager and department linkage

The system SHALL persist users with `email` (unique), `full_name`, optional `manager_id` self-FK, optional `department_id` FK, and `is_active` flag. The `manager_id` SHALL form a directed graph that the resolver can walk upward.

#### Scenario: User has a manager
- **WHEN** a User has `manager_id` set to another User's id
- **THEN** querying "manager of user X" returns that User row

#### Scenario: User without manager
- **WHEN** a User has `manager_id = null` (e.g., the CEO)
- **THEN** queries traversing `submitter.manager` from this user return an empty result with a structured "no manager" reason, not an exception

### Requirement: Department entity with parent and head

The system SHALL persist departments with `code` (unique), `name`, optional `parent_id` self-FK forming a tree, and optional `head_user_id` FK to `User`. The department tree SHALL support walks of at least 2 levels upward (`department.parent.parent`).

#### Scenario: Department head resolves to a User
- **WHEN** a Department has `head_user_id` set
- **AND** the resolver is asked for `submitter.department.head`
- **THEN** the resolver returns the head user's id

#### Scenario: Department without head
- **WHEN** a Department's `head_user_id = null`
- **THEN** resolution of `submitter.department.head` returns empty with a structured "department head unset" reason

### Requirement: Group entity supports nested membership

The system SHALL persist groups with `code` (unique), `name`, `description`, and a many-to-many `GroupMember` relation to `Principal`. Group members MAY themselves be groups (transitive membership). Membership expansion MUST detect and abort on cycles without infinite recursion.

#### Scenario: Group with direct user members
- **WHEN** a Group `g1` has GroupMember rows for users `u1, u2, u3`
- **AND** the resolver expands `g1`
- **THEN** the resolver returns `{u1, u2, u3}`

#### Scenario: Group containing another group
- **WHEN** a Group `g_outer` has a member that is `g_inner`
- **AND** `g_inner` has user members `u1, u2`
- **THEN** expanding `g_outer` returns `{u1, u2}` (transitive)

#### Scenario: Cyclic group membership
- **WHEN** `g_a` includes `g_b` as a member, and `g_b` includes `g_a` as a member
- **AND** the resolver tries to expand either
- **THEN** the resolver returns a `Cycle` error with the cycle path, not an infinite loop or stack overflow

### Requirement: Org-chart query helper service

The system SHALL provide an `IOrgChartReader` service exposing methods: `GetUser(userId)`, `GetManager(userId)`, `GetDepartmentOf(userId)`, `GetDepartmentParent(deptId)`, `GetDepartmentHead(deptId)`, `ExpandGroup(groupId)`. Each method SHALL return null/empty when the link is missing rather than throwing.

#### Scenario: GetManager on a user with no manager
- **WHEN** `GetManager(userId)` is called on a user whose `manager_id` is null
- **THEN** the method returns `null` (not throws)

#### Scenario: ExpandGroup is transitive
- **WHEN** `ExpandGroup(groupId)` is called on a group with nested group members
- **THEN** the returned set contains all transitively-reachable users
