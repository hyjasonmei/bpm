## ADDED Requirements

### Requirement: Admin can list and search users

The system SHALL provide `GET /api/admin/users?q=&page=1&pageSize=50&roleCode=` returning a paginated list of users in the tenant. The query string `q` matches `FullName` or `Email` (case-insensitive partial). `roleCode` filters to users having that role. The endpoint SHALL be admin-only.

#### Scenario: Search by name fragment

- **GIVEN** users include "Wilson You", "Will Smith", "William Tang"
- **WHEN** admin calls `GET /api/admin/users?q=will`
- **THEN** the response contains all three users (case-insensitive partial)

#### Scenario: Filter by role

- **GIVEN** Amy has role `hr`; nobody else does
- **WHEN** admin calls `GET /api/admin/users?roleCode=hr`
- **THEN** the response contains only Amy

#### Scenario: Non-admin rejected

- **WHEN** Wilson (employee) calls the endpoint
- **THEN** 403

### Requirement: Admin can assign and revoke roles

The system SHALL provide:

- `POST /api/admin/users/{userId}/roles` body `{ roleCode, scope?, scopeRef? }` to create a `RoleAssignment` row
- `DELETE /api/admin/users/{userId}/roles/{assignmentId}` to delete it

Both SHALL be admin-only and SHALL write a `RoleAssignmentChange` audit row in the same transaction. The audit row SHALL include actor, target, role code (snapshot), action, scope, scopeRef, timestamp, and `ImpersonatedByUserId` when applicable.

#### Scenario: Assign creates row + audit

- **GIVEN** Wilson has no `hr` role
- **WHEN** admin calls assign with `{ roleCode: "hr" }`
- **THEN** a new RoleAssignment row exists for (Wilson, hr role)
- **AND** a RoleAssignmentChange audit row exists with action=Assign, target=Wilson, actor=admin

#### Scenario: Revoke removes row + audit

- **GIVEN** Wilson has `hr` role
- **WHEN** admin calls revoke for that assignment id
- **THEN** the RoleAssignment row is deleted
- **AND** a RoleAssignmentChange audit row exists with action=Revoke

### Requirement: Cannot revoke own last admin role

The system SHALL reject `DELETE /api/admin/users/{userId}/roles/{assignmentId}` with HTTP 403 when ALL of the following are true:

- Caller's id == `userId` (revoking self)
- The role being revoked has `code = "admin"`
- The caller has no other active `admin` role assignment

#### Scenario: Self-revoke last admin blocked

- **GIVEN** admin Sandy has exactly one `admin` role assignment
- **WHEN** Sandy calls revoke targeting her own admin assignment
- **THEN** 403 with detail `cannot revoke your own last admin role`

#### Scenario: Self-revoke when other admin assignments exist allowed

- **GIVEN** Sandy has two `admin` assignments (one System scope, one Tenant scope)
- **WHEN** Sandy revokes one of them
- **THEN** 200 (she still has the other)

### Requirement: Cannot revoke last admin in tenant

The system SHALL reject revocation that would leave the tenant with zero active users holding the `admin` role. Returns HTTP 409 with detail `cannot revoke last admin in tenant`. (POC single-tenant: globally; multi-tenant later: per tenant.)

#### Scenario: Last admin in tenant blocked

- **GIVEN** the tenant has exactly one admin user (Sandy); admin Bob is trying to revoke Sandy's admin role (he has no admin role himself — wait, this scenario is for two admins one revoking the other's last admin)
- **GIVEN** the tenant has admins {Sandy, Bob}; Sandy revokes Bob's admin assignment → ok (Sandy remains). Now the tenant has only {Sandy}
- **WHEN** Sandy tries to revoke her own admin assignment via another admin's endpoint
- **THEN** 409

(Operationally: the very-last-admin guard catches both self-revoke-own-last and other-admin-revoke-last cases.)

### Requirement: Roles list shows assignment counts

`GET /api/admin/roles` SHALL return all `Role` rows with their code, name, scope, and a count of currently assigned active users.

#### Scenario: Counts reflect current state

- **GIVEN** roles [admin (1 user), hr (1 user), designer (3 users), viewer (5 users)]
- **WHEN** admin calls `GET /api/admin/roles`
- **THEN** response includes 4 entries with the matching counts

### Requirement: User detail returns full assignment list

`GET /api/admin/users/{id}` SHALL return the user's profile (id, fullName, email, dept, isActive, lastActivityAt) plus an array of all current role assignments. Each assignment SHALL include id, roleCode, roleName, scope, scopeRef, assignedAt, and assignedBy (user id of the actor who created the assignment, derived from RoleAssignmentChange).

#### Scenario: Detail includes assignments

- **GIVEN** Wilson has assignments to roles [viewer, hr]
- **WHEN** admin calls `GET /api/admin/users/{wilson.Id}`
- **THEN** the response contains 2 assignment entries with their metadata

### Requirement: Admin UI page for managing roles

The `bpm-admin-ui` SHALL include a sidebar item `Users & Roles` that renders a master-detail page:

- Left: search input, role filter chips, paginated user list
- Right: profile + role assignments table + Add Role button + Revoke buttons per row

Revoking the `admin` role MUST open a red confirm dialog. Self-revoke attempts MUST show a clearer warning before submission, and the backend's 403 MUST be surfaced as a friendly toast (not raw error).

#### Scenario: User selects from list and sees roles

- **GIVEN** admin opens Users & Roles, types "wilson" in search
- **WHEN** they click Wilson's row in the list
- **THEN** the right pane loads Wilson's profile and current role assignments

#### Scenario: Add role roundtrip

- **GIVEN** Wilson has only `viewer`
- **WHEN** admin clicks Add Role, picks `hr`, confirms
- **THEN** the assignments table refreshes showing the new `hr` row
- **AND** the toast says "Role hr assigned to Wilson"

#### Scenario: Self-revoke last admin shows friendly error

- **GIVEN** Sandy is the only admin in the tenant; her own user detail is open
- **WHEN** she clicks the X next to her admin assignment and confirms
- **THEN** the backend returns 403; the UI shows a toast "Cannot revoke your own last admin role"
- **AND** the assignment is still listed
