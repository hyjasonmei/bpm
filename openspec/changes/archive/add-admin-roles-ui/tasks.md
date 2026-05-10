# Tasks

## 1. Domain — audit row

- [ ] 1.1 Create `Domain/Entities/Authz/RoleAssignmentChange.cs` (Id, ActorUserId, TargetUserId, RoleId, RoleCodeSnapshot, Action enum {Assign, Revoke}, AssignmentScope, ScopeRef?, ImpersonatedByUserId?, CreatedAt) — implements IImpersonable
- [ ] 1.2 Create `RoleAssignmentChangeAction` enum

## 2. Persistence

- [ ] 2.1 EF configuration; index (TargetUserId, CreatedAt DESC), (ActorUserId, CreatedAt DESC)
- [ ] 2.2 DbSet in AppDbContext
- [ ] 2.3 Migration `AddRoleAssignmentAudit`
- [ ] 2.4 Apply locally; verify schema

## 3. Application service

- [ ] 3.1 `Application/Admin/IRoleAdminService.cs` interface
  - `ListRolesAsync()` → IList<RoleSummaryDto>
  - `ListUsersAsync(query, page, pageSize, roleCodeFilter)` → PagedResult<UserSummaryDto>
  - `GetUserDetailAsync(userId)` → UserDetailDto (profile + assignments)
  - `AssignRoleAsync(actorId, targetUserId, roleCode, scope?, scopeRef?)` → AssignmentDto
  - `RevokeAssignmentAsync(actorId, targetUserId, assignmentId)` → void
- [ ] 3.2 DTOs: `RoleSummaryDto` (code, name, scope, assignedCount), `UserSummaryDto` (id, fullName, email, dept, isActive, roleCount, lastActivityAt), `UserDetailDto` (UserSummaryDto + assignments[]), `AssignmentDto` (id, roleCode, roleName, scope, scopeRef, assignedAt, assignedBy)
- [ ] 3.3 Impl in `Persistence/Admin/RoleAdminService.cs`
- [ ] 3.4 RevokeAsync guards:
  - target.Id == actor.Id AND roleCode == "admin" AND target's other admin assignments == 0 → ForbiddenException("cannot revoke your own last admin role")
  - tenant active admin count would drop to 0 after revoke → ConflictException("cannot revoke last admin in tenant")
- [ ] 3.5 Both AssignAsync and RevokeAsync write a `RoleAssignmentChange` audit row in same transaction
- [ ] 3.6 Register in DI

## 4. API

- [ ] 4.1 `Api/Admin/RolesAdminController.cs` with `[Authorize(Roles = "admin")]` at class level
- [ ] 4.2 `GET /api/admin/roles`
- [ ] 4.3 `GET /api/admin/users?q=&page=1&pageSize=50&roleCode=`
- [ ] 4.4 `GET /api/admin/users/{id}`
- [ ] 4.5 `POST /api/admin/users/{userId}/roles` body `{ roleCode, scope?, scopeRef? }` → 201 with AssignmentDto
- [ ] 4.6 `DELETE /api/admin/users/{userId}/roles/{assignmentId}` → 204
- [ ] 4.7 Map exceptions to 403/404/409 cleanly

## 5. Tests

- [ ] 5.1 Unit: ListUsersAsync with q matches by FullName partial
- [ ] 5.2 Unit: ListUsersAsync filtered by roleCode returns only users with that role
- [ ] 5.3 Unit: AssignRoleAsync creates RoleAssignment + RoleAssignmentChange audit row
- [ ] 5.4 Unit: RevokeAssignmentAsync of own last admin → ForbiddenException
- [ ] 5.5 Unit: RevokeAssignmentAsync that would leave tenant with zero admins → ConflictException
- [ ] 5.6 Integration: under impersonation, AssignRoleAsync audit row carries ImpersonatedByUserId

## 6. Frontend (in bpm-admin-ui)

- [ ] 6.1 `bpm-admin-ui/src/types/adminRoles.ts` mirroring DTOs
- [ ] 6.2 `bpm-admin-ui/src/lib/api/adminRoles.ts` (5 functions)
- [ ] 6.3 `bpm-admin-ui/src/screens/admin/UsersRoles.tsx`:
  - Layout: 2-column grid, left = list, right = detail
  - Left: search input, role filter chips, paginated user list (click row → set selectedUserId)
  - Right: when no selection → empty state; when selected → load detail, render profile + assignments table + Add Role button
- [ ] 6.4 Add Role modal: role select, scope select (auto-driven by role.Scope), scopeRef input (only for Tenant scope), Confirm
- [ ] 6.5 Revoke confirm dialog: red tone, special warning for `admin` role
- [ ] 6.6 Wire into AdminLayout sidebar as "Users & Roles"

## 7. Verify

- [ ] 7.1 typecheck + build clean for both bpm-admin-ui and bpm-svc
- [ ] 7.2 Manual E2E:
  - Open as admin in bpm-admin-ui
  - Search for Wilson, select
  - Add `hr` role to Wilson → row appears
  - Switch persona to Wilson, see he can now act as HR in bpm-ui
  - Revoke `hr` from Wilson → row gone
  - Try to revoke own admin role → 403 with friendly message
- [ ] 7.3 Browser screenshot for `dogfood-screenshots/`
