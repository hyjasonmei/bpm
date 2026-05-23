# Tasks

Estimate: ~5–7 engineering hours plus a chef re-cook (Jason's keyboard).
Order matters — section 1 unblocks the rest; section 5 (chef skill)
must land in the same merge as the runtime changes so the next cook
sees the new contract.

## 1. bpm-svc — point identity DbSets at admin's tables

- [ ] 1.1 New keyless / read-write entity classes in
       `Bpm.Persistence.SharedIdentity/` that map onto
       `Admin_Principals`, `Admin_Users`, `Admin_UserCredentials`,
       `Admin_PrincipalRoles`, `Admin_Roles`, `Admin_UserDepts`,
       `Admin_DeptParents`. Use `.ToTable("Admin_XYZ")` in EF config
       so the existing admin migrations remain the schema owner.
- [ ] 1.2 `AppDbContext` adds `DbSet<SharedPrincipal>`,
       `DbSet<SharedUser>`, etc. Verify EF model builder doesn't
       complain about overlapping concepts.
- [ ] 1.3 Migration `DropBpmIdentityTables.cs`: drops `Principals`,
       `Users`, `RoleAssignments`, `Roles`, `RoleAssignmentChanges`.
       Runbook footgun: must run on a wiped POC db (`rm db/bpm.db*`).
- [ ] 1.4 Verify `dotnet build` clean across Application + Persistence
       + Api projects; references in `IOrgChartReader` /
       `ActorResolver` / `ImpersonationService` / `PersonaSeedService`
       all break at this point — they get rewritten in §2.

## 2. bpm-svc — rewire runtime services to read shared identity

- [ ] 2.1 `IOrgChartReader` impl reads from `SharedPrincipals`,
       `SharedUserDepts`, `SharedDeptParents`. Update method
       signatures if helpful (`Guid` ids unchanged — admin tables
       use Guid too).
- [ ] 2.2 `ActorResolver` retargets the rewritten reader.
       `ActorResolutionAudit` rows now carry `Admin_Principals.Id`
       values; audit table schema unchanged (`Guid` works either
       way).
- [ ] 2.3 `ImpersonationService` admin-role check reads
       `SharedPrincipalRoles` + `SharedRoles.Code == "admin"`.
- [ ] 2.4 Decide: keep `Admin_ImpersonationSessions` or bpm's
       `ImpersonationSessions`. Default: keep bpm's (audit lives
       with the runtime). Leave a TODO + comment for future merge.
- [ ] 2.5 Delete `PersonaSeedService.cs`. Delete the call in
       `Program.cs` that runs it. Replace with a startup check
       that logs a warning if `SharedUsers.Any()` is false.

## 3. bpm-svc — `/api/auth/login` endpoint

- [ ] 3.1 New file `bpm-svc/src/Api/Auth/AuthController.cs`:
       - `POST /api/auth/login` accepts `{email, password}`, returns
         `{token, expiresAt, user: {id, fullName, email, roles[],
         departmentCode}}` or 401 with `{error: "invalid_credentials"}`
       - `POST /api/auth/logout` returns 204 (server-stateless; JWT
         TTL governs)
- [ ] 3.2 Lift `PasswordHasher` from admin-svc into
       `Bpm.Application.Auth.PasswordHasher` (same impl;
       can also extract into a `Bpm.SharedAuth` package if both
       services should consume one copy — discuss).
- [ ] 3.3 `JwtTokenService` extended to mint a token from
       `(SharedUser, role codes, dept code)` instead of from a
       persona record. Existing claim names retained where
       compatible (`sub`, `full_name`, `email`); add `roles`
       claim (array of role codes) and `dept_code` claim.
- [ ] 3.4 `/api/dev/login` (DevLoginController) rewritten: still
       takes `personaCode`, but now maps each persona to a *seeded
       admin-svc user* (e.g. `personaCode=employee` →
       `wilson@acme.example` from the admin seed) and mints a real
       JWT for that user. Keeps the BPM_AUTH_MODE=prod 404 guard.

## 4. admin-svc seeder — absorb bpm employee personas

- [ ] 4.1 Extend `Bpm.Admin.Persistence.Seed.Seeder` to add the
       customer-employee personas the runtime + tests + demo flows
       depend on. Naming: switch from `wilson@acme.test` →
       `wilson@acme.example` to match the rest of the admin seed.
       Map each persona to its role code(s) (`employee`,
       `manager`, `finance`, `it`, `hr`, `admin`).
- [ ] 4.2 Add the corresponding role rows if they don't exist
       (`Admin_Roles` table). Confirm role codes match what
       `bpm-ui/src/lib/workflow.ts` `ownerByStep` will reference
       after §6.
- [ ] 4.3 Seed `UserCredential` for each new user with
       `Seeder.DemoPassword = "flowcook2026"` — single source of
       truth, no per-user override needed for POC.
- [ ] 4.4 Run admin-svc boot + verify all expected users land via
       `select email from Admin_Users order by email`.

## 5. Chef skill update

- [ ] 5.1 `chef/skill/conventions.md`: rewrite the "form props"
       section. Old `persona: PersonaCode` → new `user: AuthedUser`.
       Add a one-page primer on role codes and the
       `user.roles.includes(...)` gating pattern.
- [ ] 5.2 `chef/skill/workflow.md` step 6 (UI codegen): template
       example switches to new contract. Show how chef picks
       ownerByStep role codes from spec userTask `actor` blocks.
- [ ] 5.3 `chef/skill/SKILL.md` bumps the skill version header from
       `v1` to `v2` so a running chef session knows to re-read.
       Index regenerated; "what changed in v2" call-out at the top.
- [ ] 5.4 Add a worked-example diff under `chef/skill/examples/`:
       old chef LEAVE_V1_LeaveForm.tsx (PersonaCode) vs new
       expected output (AuthedUser). Reference from
       `conventions.md`.

## 6. bpm-ui — Login page + AuthGate

- [ ] 6.1 `screens/Login.tsx`: email + password inputs, submit
       button, greyed "Sign in with Microsoft" mock button, error
       row, `mailto:support@flowcook` "Forgot password?" link. Use
       the existing `<Input>` / `<Button>` / `<Field>` primitives.
- [ ] 6.2 `lib/api/auth.ts`: `login(email, password)`, `logout()`.
- [ ] 6.3 `App.tsx` (or new `<AuthGate>`): when `getJwt()` is null,
       render `<Login />`. After successful submit, the login fn
       triggers `setJwt()` then a `window.location.reload()` so all
       hooks rerun under the new identity.
- [ ] 6.4 Remove the auto-mint effect in `useActivePersona` (was
       calling `/api/dev/login` on no-JWT mount). Login is now
       explicit.

## 7. bpm-ui — useActivePersona refactor (closes deferred task 4.5)

- [ ] 7.1 `lib/role.ts`: drop `PersonaCode` enum + `PERSONAS` map.
       Replace with `useAuthedUser()` hook returning `{ id,
       fullName, email, roles[], departmentCode, isImpersonating,
       impersonatorId, ... }` derived from the active JWT.
- [ ] 7.2 `lib/workflow.ts`: `ownerByStep: PersonaCode[]` →
       `ownerByStep: RoleCode[][]` (string[][]). `canAct(formCode,
       step, persona)` → `canAct(formCode, step, user)` using
       `ownerByStep[step].some(role => user.roles.includes(role))`.
       Update all 11 FORMS entries with the new role-code arrays.
- [ ] 7.3 Mechanical rewrite of `bpm-ui/src/screens/forms/Reference_*.tsx`
       (11 files): import `AuthedUser`, prop `user` not `persona`,
       gates rewritten role-based. Script-assisted; eyeball each.
- [ ] 7.4 Same rewrite for chef-shipped `LEAVE_V1_LeaveForm.tsx` on
       `leave-test-1` — but this branch will be reset + re-cooked
       in §9, so the rewrite here is throwaway. Leave it; just
       update `main` files.
- [ ] 7.5 `RoleSwitcher` → `IdentitySwitcher`: dev-only quick-switch
       list reading `/api/admin/users`. Real impersonation flow +
       banner kept.
- [ ] 7.6 `bpm-ui/CLAUDE.md` form-mode-contract section rewritten.

## 8. Verify

- [ ] 8.1 `tsc -p tsconfig.app.json --noEmit` clean (bpm-ui).
- [ ] 8.2 `dotnet build` clean (bpm-svc).
- [ ] 8.3 `dotnet test` on bpm-svc — `AllFlowsRealE2ETests` expected
       to need test-fixture updates (seeded emails changed). Update
       fixtures, target passing-with-changes.
- [ ] 8.4 Wipe db (`rm db/bpm.db*`), boot admin-svc + bpm-svc +
       bpm-ui + bpm-admin-ui. Verify:
       - bpm-ui lands on Login screen (no auto-JWT).
       - Login as `wilson@acme.example` / `flowcook2026` succeeds,
         lands on Home with Wilson's identity.
       - 0 Quick Actions (no manifests on main yet).
       - Switch-identity dropdown visible (dev mode), hidden in
         production build (`vite build && vite preview` test).
       - Impersonation modal lists unified users.
- [ ] 8.5 Screenshot diff: old persona-dropdown UI vs new Login
       page + Identity switcher — for the runbook.

## 9. Re-cook LEAVE V1 (Jason runs)

- [ ] 9.1 Jason resets `leave-test-1` from current main
       (`git reset --hard main` on the branch).
- [ ] 9.2 Jason kicks off a fresh chef session against the LEAVE
       bundle. Chef reads the v2 skill, emits form code with the
       new `user` contract.
- [ ] 9.3 Verify regenerated `LEAVE_V1_LeaveForm.tsx` uses
       `user: AuthedUser`, role-based gates, no `PersonaCode`
       import.
- [ ] 9.4 Run the LEAVE flow end-to-end as `wilson@acme.example`,
       through `elton@acme.example` (manager), `amy@acme.example`
       (hr) — confirm task assignment, approve, archive all work
       on real auth + unified identity.
- [ ] 9.5 Cherry-pick final commits back to `main`. `bpm` Home now
       shows 1 real flow ("請假申請") wired against real users.

## 10. Merge + handoff

- [ ] 10.1 PR `unify-user-store-and-real-auth` → `main` via
       GitKraken; ultrareview encouraged (architecture change).
- [ ] 10.2 After merge, update `docs/MVP_DEMO_RUNBOOK.md`:
       - Login page is now the entry point
       - Single seed source (admin-svc Seeder)
       - `flowcook2026` password noted for demo
       - `BPM_SEED_ON_STARTUP` retired on bpm-svc
- [ ] 10.3 Archive `bpm-dev-cleanup-employee-app` change folder
       (task 4.5 closed by this change).
- [ ] 10.4 Delete `unify-user-store-and-real-auth` change folder
       after archive (or move to `openspec/changes/archive/`).
