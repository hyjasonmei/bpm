# Tasks

## 1. Solution skeleton (PR #1)

- [x] 1.1 `dotnet new sln` for `bpm-admin-svc.sln` (created as `bpm-admin-svc.slnx`)
- [x] 1.2 Create src projects: `Bpm.Admin.Api`, `Bpm.Admin.Application`, `Bpm.Admin.Domain`, `Bpm.Admin.Persistence`, `Bpm.Admin.SeedCli`
- [x] 1.3 Create test projects: `Bpm.Admin.Application.Tests`, `Bpm.Admin.Persistence.Tests`, `Bpm.Admin.Api.Tests`
- [x] 1.4 Set up project references (Domain ← Application ← Api / Persistence; SeedCli → Persistence + Application)
- [x] 1.5 CI workflow: build + test + lint (`.github/workflows/bpm-admin-svc.yml`)

## 2. AdminDbContext + first migration (PR #2)

- [x] 2.1 SQLite connection string in appsettings.Development.json
- [x] 2.2 `AdminDbContext` with empty DbSets and Postgres-ready conventions
- [x] 2.3 Add `Principal` entity + initial migration
- [x] 2.4 `ISoftDeletable` interface + EF global filter in `OnModelCreating`
- [x] 2.5 Smoke test: create / read / soft-delete a Principal

## 3. Remaining six tables (PR #3)

- [x] 3.1 Add `UserDept`, `DeptParent`, `GroupMember`, `Role`, `PrincipalRole`, `Delegation` entities
- [x] 3.2 Configure composite PKs / FKs / indexes
- [x] 3.3 Single migration covering all six (`CoreEntities`)
- [x] 3.4 Cycle-detection on GroupMember insert (`GroupMembershipService`)

## 4. EffectiveRoleResolver + tests (PR #4)

- [x] 4.1 `IEffectiveRoleResolver.GetEffectiveRolesAsync(userId)` in Application layer
- [x] 4.2 Direct + dept-inherited + group-inherited union
- [x] 4.3 Unit tests covering: direct only / dept inherit / dept ancestor / group inherit / nested group / mixed / inherit_to_members=false / unknown user
- [x] 4.4 Document the algorithm (`src/Bpm.Admin.Application/Roles/EffectiveRoleAlgorithm.md`)

## 5. Principal API CRUD + integration tests (PR #5)

- [x] 5.1 `PrincipalsController`: GET list (with `type` filter) / GET by id / POST / PUT / DELETE (soft)
- [x] 5.2 UserDept / DeptParent / GroupMember sub-resource endpoints (cycle-detection on dept parent + group member)
- [x] 5.3 Audit hooks on every mutating endpoint (via `IAuditLogger`)
- [x] 5.4 Integration tests via `TestServer` and in-memory SQLite (`PrincipalsApiTests`)

## 6. Role / PrincipalRole / Delegation API + tests (PR #6)

- [x] 6.1 `RolesController` GET / POST
- [x] 6.2 PrincipalRole assignment endpoints (with `inherit_to_members` checkbox)
- [x] 6.3 `DelegationsController` GET / POST / DELETE
- [x] 6.4 `GET /api/principals/{userId}/effective-roles` returning the resolved set (added on `PrincipalsController`)
- [x] 6.5 Integration tests including delegation-active scenario (`RolesAndDelegationApiTests`)

## 7. SeedCli (PR #7)

- [x] 7.1 `seed clear` subcommand
- [x] 7.2 `seed --org` subcommand with sample principals (13 users / 6 depts / 1 group / 14 roles / sample delegation)
- [x] 7.3 Dev-only environment guard (`ASPNETCORE_ENVIRONMENT=Development` or `FLOWCOOK_ALLOW_SEED=1`)
- [x] 7.4 Default password `flowcook2026` assigned to every seeded user
- [x] 7.5 README + inline help (`src/Bpm.Admin.SeedCli/README.md`)

## 8. Authentication (PR #8)

- [x] 8.1 `UserCredential` and `UserSession` entities + migration
- [x] 8.2 Password hasher service (ASP.NET Identity `PasswordHasher<TUser>` wrapped behind `IPasswordHasher`)
- [x] 8.3 `AuthController` login / logout / me endpoints
- [x] 8.4 Cookie middleware (`SessionAuthMiddleware` with HttpOnly cookie)
- [x] 8.5 Login success / fail / logout audit events
- [x] 8.6 Integration tests: success / wrong password / no credential / cookie-protected endpoint (`AuthApiTests`)
- [x] 8.7 Session expiry handling (ResolveSessionAsync rejects expired + cleans up)

## 9. Audit logger + interceptor (PR #9)

- [x] 9.1 `AuditEvent` entity + migration (seven-column schema)
- [x] 9.2 `IAuditLogger` abstraction (manual action-style audit calls from controllers / AuthService)
- [x] 9.3 Append-only enforcement (DbContext rejects Modified/Deleted on `AuditEvent`)
- [x] 9.4 EF SaveChanges interceptor (`AuditingSaveChangesInterceptor`) auto-captures before/after for `IAuditable` entities (Principal first; future entities adopt the marker)
- [x] 9.5 Unit tests: append-only block (update/delete) + interceptor created/updated capture + source_system=admin + non-IAuditable not audited (`AuditTests`)
