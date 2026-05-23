# unify-user-store-and-real-auth

## Why

Today bpm-svc and bpm-admin-svc carry **two parallel identity stores**
on a shared SQLite file:

| | bpm-svc | bpm-admin-svc |
|---|---|---|
| Principal table | `Principals` | `Admin_Principals` |
| User table | `Users` | `Admin_Users` |
| Credentials | none | `Admin_UserCredentials` (BCrypt via ASP.NET PasswordHasher) |
| Seed users | wilson@acme.test, yang@acme.test … (13) | alice@acme.example, bob@acme.example … (11) |
| Login | `POST /api/dev/login {personaCode}` mints persona JWT, no password | `POST /api/auth/login {email, password}` real auth |
| Role model | `RoleAssignment` keyed on `Principals.Id` | `PrincipalRole` keyed on `Admin_Principals.Id` |

The split exists for historical reasons: bpm-svc came first with persona
shortcuts for the demo; admin-svc came later with real auth for the
flowcook team using the configurator. Customer-facing reality is the
**opposite shape** — *all* identities (flowcook team, customer admins,
customer employees) live in one tenant, sourced from Entra ID or
CSV-imported HR data. Two parallel stores will:

1. Block the bpm employee app from having real password login —
   customers can't ship a product whose "login" is a persona dropdown
2. Force duplicate seed maintenance (changing who's a manager means
   editing two seeders, with risk they drift)
3. Block the `useActivePersona` JWT-derive refactor (proposal task 4.5
   from `bpm-dev-cleanup-employee-app`): the persona enum exists only
   because the bpm-svc identity layer has no roles attached. Without
   real `RoleAssignment` rows for the authed user, the UI has nothing
   to gate against
4. Break the chef MVP iteration loop: chef-generated form code
   (`LEAVE_V1_LeaveForm.tsx`) imports `PersonaCode` directly; every
   future cook entrenches the demo contract further

This proposal collapses the two stores into one
(`Admin_Principals` / `Admin_Users` / `Admin_UserCredentials` win;
bpm-svc reads them), wires real password login on bpm-svc that
proxies the unified credentials, refactors `useActivePersona` to
derive identity + roles from JWT claims (closing task 4.5), and
updates the chef skill so all future cooks write the new contract.

End state Jason wants:

- A customer employee opens bpm-ui → sees a login page → enters
  `wilson@acme.example` / `flowcook2026` → lands on Home with their
  real cases. No persona shortcut, no demo dropdown in the production
  build.
- The flowcook team uses the same login page (or admin-ui's, same
  endpoint) to access admin tooling.
- Impersonation works from the unified user list.
- Chef cooks form code that consumes `user: AuthedUser` (roles[],
  departmentCode, fullName) and gates via `user.roles.includes('hr_reviewer')`,
  not `persona === 'hr'`.
- The dev-only Switch-identity dropdown survives behind
  `import.meta.env.DEV` for local testing speed.

## What Changes

### bpm-svc — point identity DbSets at admin's tables

- New keyless entity mappings in `Bpm.Persistence` that mirror
  `Admin_Principals`, `Admin_Users`, `Admin_UserCredentials`,
  `Admin_PrincipalRoles`, `Admin_Roles`, `Admin_UserDepts`,
  `Admin_DeptParents`. The bpm-svc `AppDbContext` exposes these via
  new DbSets (`SharedPrincipals`, `SharedUsers`, etc) and stops
  declaring its own `Principals` / `Users` / `RoleAssignments` /
  `Roles`.
- `IOrgChartReader` rewritten to read from `Admin_Principals` +
  `Admin_UserDepts` + `Admin_DeptParents`.
- `ActorResolver` retargets the new reader. `ActorResolutionAudit`
  rows still emit (now referencing admin principal ids).
- `RoleAssignment` rows for runtime ownership move into
  `Admin_PrincipalRoles`. The runtime concept of "which role can
  approve at this step" becomes a lookup against the unified role
  table.
- Migrations: drop bpm-svc's `Principals`, `Users`, `RoleAssignments`,
  `Roles`, `RoleAssignmentChanges` tables. This is **destructive** —
  POC db must be wiped (`rm db/bpm.db*`) before the migration runs,
  per runbook footgun #1.

### bpm-svc — real login endpoint

- `POST /api/auth/login` (allow-anonymous): accepts `{email, password}`,
  loads `Admin_UserCredentials` by email-joined-`Admin_Users`, verifies
  hash via lifted `PasswordHasher` (same impl admin-svc uses), mints a
  JWT with claims `{sub, email, full_name, roles[], dept_code, exp}`.
- `roles` claim is the list of role codes attached to the user via
  `Admin_PrincipalRoles` → `Admin_Roles.Code`.
- `dept_code` claim is the user's primary department code from
  `Admin_UserDepts` (the first row; multi-dept users will need an
  explicit primary marker — future work, out of scope).
- `POST /api/auth/logout` clears server-side session state (just a
  204 today; no server session stored — the JWT TTL governs).
- `/api/dev/login` stays gated on `BPM_AUTH_MODE=dev`. It now mints
  a JWT for a *seeded* admin user (e.g. `wilson@acme.example` for
  persona=employee) so the dev shortcut maps onto the real identity
  store instead of the bpm-svc-private one.

### bpm-svc — seed alignment

- Delete `PersonaSeedService`. The 13 wilson/yang/etc users in
  `Users` go away with the table.
- The admin-svc `Seeder` becomes the single seed source. It is
  extended to cover the customer-employee personas that the runtime
  + tests + demo flows depend on (wilson the employee, elton the
  manager, jean the finance reviewer, mark the IT spec reviewer, amy
  the HR reviewer, pat the admin). Naming switches to `@acme.example`
  to match the rest of the admin seed. The role codes attached match
  what bpm-svc's `ownerByStep` arrays expect (`manager`, `finance`,
  `it`, `hr`, `admin`).
- `AllFlowsRealE2ETests` and any other tests that hard-code seed
  emails/ids retarget the unified seed.
- `BPM_SEED_ON_STARTUP` becomes a no-op on bpm-svc — seed is owned
  by admin-svc. bpm-svc startup verifies the seed exists and logs a
  warning if not.

### bpm-svc — impersonation rewire

- `ImpersonationService` already reads `RoleAssignments` for the
  admin-role check. Retarget to `Admin_PrincipalRoles` +
  `Admin_Roles.Code == "admin"`.
- `Admin_ImpersonationSessions` already lives in admin-svc; either
  share that table or keep bpm-svc's `ImpersonationSessions` table and
  reference `Admin_Principals.Id` directly. Default: keep bpm-svc's
  table for now (audit trail stays local to the runtime that issued
  the session); revisit in a follow-up if the audit needs unifying.

### bpm-ui — Login page + auth flow

- New `screens/Login.tsx`: email + password fields, "Sign in" button,
  greyed-out "Sign in with Microsoft" placeholder (real Entra wiring
  later via `add-sso-oidc`), error message line, "Forgot password?"
  link that opens a `mailto:` to flowcook support for the POC.
- `lib/api/auth.ts`: `login(email, password)`, `logout()`. Stores the
  returned JWT via existing `setJwt()`.
- `App.tsx` (or a new `AuthGate`): when `getJwt()` returns null,
  render `<Login />` instead of `<AppLayout>`. After successful login,
  reload to pick up auth-aware children.
- The current zero-click bootstrap (useActivePersona's effect that
  calls `/api/dev/login` for the saved persona when no JWT exists) is
  removed. In dev mode the bootstrap is the Login page filled in
  manually, or the Switch-identity dropdown after the first login.

### bpm-ui — useActivePersona refactor (closes task 4.5)

- Drop the `PersonaCode` enum from `lib/role.ts`. Replace `PERSONAS`
  with a `useAuthedUser()` hook that decodes the active JWT and
  returns `{ id, fullName, email, roles: string[], departmentCode,
  isImpersonating, ... }`.
- Every form's prop signature shifts from `persona: PersonaCode` to
  `user: AuthedUser`. The form mode contract in `bpm-ui/CLAUDE.md`
  updates correspondingly.
- `lib/workflow.ts`:
  - `ownerByStep: PersonaCode[]` → `ownerByStep: RoleCode[][]`
    (each step is a list of role codes that may act; the union of all
    `Admin_Roles.Code` values used in the spec library).
  - `canAct(formCode, step, persona)` → `canAct(formCode, step, user)`
    that returns `ownerByStep[step].some(role => user.roles.includes(role))`.
  - `FORMS` map retained for display metadata; the boundary doc from
    `bpm-dev-cleanup-employee-app` task 3 still applies.
- All 11 `Reference_*Form.tsx` and chef-shipped `LEAVE_V1_LeaveForm.tsx`
  get a mechanical rewrite (sed + tsc):
  - Imports: `PersonaCode` → `AuthedUser`
  - Prop name: `persona` → `user`
  - Gates: `persona === 'hr'` → `user.roles.includes('hr_reviewer')` (or
    whichever role code applies)
- `RoleSwitcher` rewritten as `IdentitySwitcher`:
  - Dev-only (`import.meta.env.DEV`) quick-switch list. Reads from
    `/api/admin/users` (already wired). Click → `/api/dev/login`
    proxies to the unified seed.
  - Impersonation modal + banner kept as-is (already real).

### Chef skill update (`chef/skill/SKILL.md` + supporting files)

- `chef/skill/conventions.md`: update form props convention from
  `persona: PersonaCode` → `user: AuthedUser`. Add a one-paragraph
  primer on role codes + how chef should pick them when emitting
  `ownerByStep`.
- `chef/skill/workflow.md` step 6 (UI codegen): the form template
  example switches to the new contract. The example shows `useFormRuntime`
  returning `user`, gating UI via `user.roles.includes(...)`.
- `chef/skill/SKILL.md` index regenerated to point at the updated
  files. Versioned bump in the skill header (`v1` → `v2`) so old
  chef sessions know to re-read.
- Add a worked-example diff: "old chef output (LEAVE_V1)" vs "new
  chef output (LEAVE_V1)" so the skill carries a concrete reference
  rather than abstract guidance.

### Re-cook LEAVE V1 (Jason runs)

- After the proposal lands on `main`, reset `leave-test-1` from
  current main, kick off a fresh chef session against the LEAVE
  bundle, verify the regenerated form uses the new `user` contract
  + role-based gates, cherry-pick the final feature commits back to
  `main`. (This validates the chef skill update end-to-end.)

## Out of Scope

- Real Entra ID SSO wiring — `add-sso-oidc` owns that. Login page
  ships with a disabled Microsoft button as visual placeholder only.
- Real password-reset flow / forgot-password email — `mailto:` link
  to support is sufficient for POC. `add-password-reset` follow-up.
- Multi-tenant identity (one credential row, many tenants) — not
  modelled today and not needed for POC.
- Real session storage server-side. JWT TTL + logout is sufficient
  for POC.
- Per-multi-dept primary-department marker — first
  `Admin_UserDepts` row wins; multi-dept users are rare in the
  POC seed.

## Design Notes

See `design.md` for:

- The unified identity model diagram (Admin_Principals + edges into
  bpm-svc's runtime tables).
- The new form-prop contract + canAct rewrite.
- Migration sequence (which migrations run, in which order, against
  a wiped POC db).
- Chef skill diff and the worked-example reference.
- Test impact map: which existing tests fail, which need rewriting,
  which can be deleted.

## References

- `openspec/changes/bpm-dev-cleanup-employee-app/proposal.md` — task
  4.5 deferral, the impetus for this change
- `openspec/specs/flowcook-architecture/spec.md` — 4-service model
- `chef/skill/SKILL.md` + `chef/skill/conventions.md` — chef contract
  files this change updates
- `bpm-svc/CLAUDE.md` — `/api/dev/login` + persona seed sections
  (both rewritten by this change)
- `bpm-admin-svc/src/Bpm.Admin.Persistence/Seed/Seeder.cs` — single
  seed source after this change
- `add-sso-oidc` (separate openspec change) — future Entra wiring
- Commits this proposal builds on:
  - `8b65e8b` Phase 1.1 db merge (made the unification possible)
  - `f87e5d2` admin auto-seed (existing single-seed pattern)
  - `d96e4c1` bpm-dev real-user picker impersonation (already
    consumes admin's user list — half the proof this works)
