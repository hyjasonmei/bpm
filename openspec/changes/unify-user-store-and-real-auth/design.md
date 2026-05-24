# Design notes — unify-user-store-and-real-auth

## Identity model after the change

```
                ┌─────────────────────────────────────┐
                │      Admin_Principals (TPT)         │
                │   ├── Admin_Users      (people)     │
                │   ├── Admin_Departments(org units)  │
                │   └── Admin_Groups     (role bags)  │
                └─────┬─────────┬─────────┬───────────┘
                      │         │         │
       ┌──────────────┘         │         └────────────────┐
       │                        │                           │
       ▼                        ▼                           ▼
 Admin_UserCredentials   Admin_UserDepts /          Admin_PrincipalRoles
 (PasswordHash, etc)     Admin_DeptParents          → Admin_Roles
                                                       (Code: admin, manager,
                                                        finance, it, hr,
                                                        employee, plus flow-
                                                        scoped role codes
                                                        from spec library)
                  ▲                  ▲                       ▲
                  │                  │                       │
                  │     bpm-svc reads these tables           │
                  │     via SharedX entity mappings,         │
                  │     drops its own Principals/Users.      │
                  │                                          │
                bpm-svc runtime tables (UNCHANGED schema)    │
                ─ ProcessInstance, ProcessTask, TaskHistory  │
                  carry user-id columns that now reference   │
                  Admin_Principals.Id values (still Guid)    │
                ─ ImpersonationSessions stays in bpm-svc;    │
                  references admin user ids                  │
                ─ ActorResolutionAudit ditto                 │
```

Key constraint: **Admin_Principals.Id is the single canonical user id
in the system.** Runtime tables continue to store user ids as Guid;
no migration of payload data is needed because the bpm-svc tables
never had a FK to bpm's old `Principals` table — they stored Guids
that the runtime resolved via `IOrgChartReader`. Same Guid space,
same column types, just a different source.

## New form-prop contract (bpm-ui)

### Before

```ts
type FormProps = {
  persona: PersonaCode        // 'employee' | 'manager' | 'finance' | 'it' | 'hr' | 'admin'
  mode?: 'create' | 'task'
  taskId?: string | null
  onSubmitted?: () => void
}

// gate
{persona === 'hr' && <HrOnlyButton />}

// canAct
function canAct(formCode, step, persona) {
  return FORMS[formCode].ownerByStep[step] === persona
}
```

### After

```ts
type AuthedUser = {
  id: string                  // Admin_Principals.Id
  fullName: string
  email: string
  roles: string[]             // role codes from Admin_PrincipalRoles
  departmentCode: string | null
  isImpersonating: boolean
  impersonatorId?: string | null
  impersonatorName?: string | null
}

type FormProps = {
  user: AuthedUser
  mode?: 'create' | 'task'
  taskId?: string | null
  onSubmitted?: () => void
}

// gate
{user.roles.includes('hr_reviewer') && <HrOnlyButton />}

// canAct
function canAct(formCode, step, user) {
  return FORMS[formCode].ownerByStep[step].some(role => user.roles.includes(role))
}
```

### Why this shape

- **AuthedUser is what the JWT carries.** Decoding the JWT yields
  everything the UI needs; no further `/api/me` round-trip on each
  form render.
- **`roles: string[]` is the source of truth for gating.** A real
  user can have multiple roles (manager + hr_reviewer for a small
  org); persona enum can't model that.
- **`ownerByStep: RoleCode[][]`** — each step lists the role codes
  that may act. Union semantics. Matches spec `actor` shapes which
  are already unions.
- **`isImpersonating + impersonatorId`** — derived from JWT's
  `impersonated_by` claim; lets UI components render the
  "acting as" marker without reading from `lib/impersonationToken`.

## Migration sequence

Order matters because we drop tables that the runtime references.
Boot sequence:

1. Wipe POC db: `rm db/bpm.db*`
2. Boot admin-svc → creates `Admin_*` tables + seeds users +
   credentials (idempotent; existing admin auto-seed pattern)
3. Boot bpm-svc → runs bpm-svc EF migrations
   - Existing migrations run (no-op on fresh db)
   - New migration `DropBpmIdentityTables.cs` runs → no-op (tables
     never existed on fresh db)
   - bpm-svc startup check: `SharedUsers.Any()` → true ✓

For an **existing** dev db, the destructive migration drops tables
that may have rows. POC has no production data — wipe-and-reseed is
the standard footgun-acknowledged path per the runbook.

## Test impact

Estimated impact on the 313 bpm-svc tests (per current CLAUDE.md):

| Category | Count | Action |
|---|---|---|
| Tests that hard-code `wilson@acme.test` / persona seed emails | ~30 | Rewrite to use unified seed (`@acme.example`) |
| `AllFlowsRealE2ETests` 22 sub-tests | 22 | Update seed lookups; flow semantics unchanged |
| `ImpersonationServiceTests` | ~10 | Confirm admin-role check passes against `Admin_PrincipalRoles` |
| `ActorResolverTests` | ~15 | Update OrgChartReader test doubles to mirror new tables |
| `RoleAssignmentTests` (RBAC layer tests) | ~8 | Move to admin-svc OR delete (the new RBAC source is admin-svc) |
| Other (spec / runtime / sandbox / bundle) | ~228 | Unaffected |

Frontend has no test framework; coverage is `tsc` + manual boot
verification per current convention.

## Chef skill diff — what v2 teaches differently

**`conventions.md` — Form props section**

```diff
-Every form takes:
-  persona: PersonaCode               // role enum
-  mode?, taskId?, onSubmitted?
+Every form takes:
+  user: AuthedUser                   // decoded from JWT, carries
+                                     // id / fullName / email /
+                                     // roles[] / departmentCode /
+                                     // isImpersonating
+  mode?, taskId?, onSubmitted?
+
+Role codes you'll commonly see in user.roles:
+  - admin               full visibility, all flows
+  - employee            base role; everyone gets this
+  - manager             approves direct-report cases
+  - finance, it, hr     queue-bearing roles for the matching
+                        review steps; you'll see flow-scoped
+                        variants like hr_reviewer / hr_records
+  - flow-scoped         derived from spec userTask.actor blocks;
+                        e.g. LEAVE_manager_approver, HWP_it_spec_reviewer
```

**`workflow.md` step 6 — UI codegen**

```diff
 // Template form
 function MyFlowForm({
-  persona, mode, taskId, onSubmitted
+  user, mode, taskId, onSubmitted
 }: FormProps) {
   // …
-  if (persona !== 'manager') return <NotAuthorized />
+  if (!user.roles.includes('manager')) return <NotAuthorized />
   // …
 }
```

**Worked example diff** lives in `chef/skill/examples/leave-v1-form.md`
with the old vs new full file shown side-by-side. Future chef
sessions read this if they're unsure which contract to emit.

## Risks + mitigations

| Risk | Mitigation |
|---|---|
| EF model builder rejects cross-context entity mappings | Use `.ToTable("Admin_X")` + standalone DbContext-scoped configurations; don't share configuration classes across services |
| Admin-svc seed runs after bpm-svc boots, leaving bpm-svc in a "no users" state momentarily | bpm-svc startup just logs a warning; first login attempt re-checks. Boot ordering: admin-svc first (already convention) |
| Test fixtures across both services share seed assumptions | Add a small `Bpm.Testing.Fixtures` helper that exposes well-known user emails as constants; both test projects consume it |
| Chef sessions in flight (Jason on a separate machine) keep emitting v1 contract | Chef skill version bump (v1 → v2) is visible in skill header; Jason kills + restarts any in-flight session after merge |
| `leave-test-1` reset destroys 7 chef commits | Acceptable per the chef-iteration-loop memory: testbed is mutable, never wholesale-merged to main; re-cook is the verification |
| Login page security weak (any POST can spam) | Out of scope for POC; rate-limiting + lockout is a `harden-auth` follow-up |
