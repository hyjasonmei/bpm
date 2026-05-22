## Why

BPM workflows need to express dynamic approvers like "submitter's manager", "head of submitter's parent department", or "if amount > 50000 then CEO else manager". Today the platform has no concept of an org chart, no abstraction over user/group/department, and no language to refer to an actor in spec.json — every flow's approver routing would have to be hardcoded. Without this layer, the partner's real-world processes (請假, 採購, 差旅, 公告) cannot be expressed as data, and the spec → code pipeline cannot generate runnable engines for them.

## What Changes

### Backend (`bpm-svc`)
- Introduce a **Principal** abstraction: `User`, `Department`, and `Group` all share a single PK pool via EF Core TPT. Junction tables (`GroupMember`, `RoleAssignment`) FK to `Principal` so a single column can refer to any actor type (mirrors Entra ID).
- New entities:
  - `User` — email, full_name, `manager_id` (self-FK), `department_id`, `is_active`
  - `Department` — code, name, `parent_id` (self-FK), `head_user_id` (FK → User)
  - `Group` — code, name, description; can contain User / Group / Department members
  - `GroupMember` — composite key `(group_id, principal_id)`; nested groups allowed
  - `Role` — code, name, `scope` (`system` | `flow`), `flow_code` (nullable for system scope)
  - `Permission` — `(action, resource)` pair
  - `RolePermission` — n-n
  - `RoleAssignment` — (`role_id`, `principal_id`, `scope` `tenant`/`flow`/`step`, `scope_ref` nullable)
- New **Workflow Resolver** service: takes an `ActorRef` + runtime context, returns `Set<UserId>`, with structured failure modes (no silent swallowing) and audit-log entries on every resolution.

### Spec / DSL (cross-cutting)
- New **Actor Reference DSL** — discriminated-union JSON used wherever spec.json refers to an actor (approver, assignee, notify_to). Six types:
  - Atomic: `expr` (path walk), `role`, `group`, `user` (testing-only, validator warns)
  - Composite: `conditional` (if/then/else), `collection` (any/all + min_approvals)
- **Path whitelist** for `expr` paths, with explicit max-depth segments. The schema validator rejects unknown segments at spec-load time so runtime never sees an invalid path.
- `spec_schema.md` updated to document `ActorRef` and replace any `approver_id` / `approver_role` field with `ActorRef`.
- `prompt_template_v1.md` updated to teach Claude Code the discriminated-union shape, the path whitelist, and 3-5 worked examples for round-trip safety.

### Frontend (`bpm-ui`)
- **BREAKING (wizard)**: `StepApprovers`, `StepDecisions`, `StepNotify` switch from a single string field to an `ActorRefEditor` component:
  - Type picker dropdown (上級主管 / 部門主管 / 角色 / 群組 / 條件式 / 合議)
  - `expr` → constrained path picker
  - `conditional` → recursive editor
  - `collection` → list of atomic actors + `min_approvals` input
  - Persists into spec.json as the typed object form
- **RoleSwitcher rewire**: the existing top-bar `RoleSwitcher` (currently a localStorage flag in `add-bpm-frontend`) starts calling a backend dev-login endpoint to obtain a real JWT for the chosen persona. All `apiFetch` calls pass `Authorization: Bearer <jwt>`.

### Auth (cross-cutting)
- **JWT bearer auth** for all API endpoints (replacing the demo bearer token from commit `38efd5b` for dev/prod usage):
  - JWT carries `sub` (user_id), `persona_code`, `tenant_id` (placeholder, single-tenant), `roles[]`, `exp`
  - Signed HS256 with `BPM_JWT_SECRET` env var; symmetric is fine for POC, swap to RS256 when Entra ID lands
- **Dev login endpoint** `POST /api/dev/login` (only enabled when `BPM_AUTH_MODE=dev`):
  - Body: `{ "persona_code": "employee" | "manager" | "finance" | "it" | "hr" | "admin" }`
  - Looks up the seed user mapped to that persona, mints a JWT, returns `{ "token": "...", "user": {...} }`
  - Disabled (returns 404) when `BPM_AUTH_MODE=prod`
- **Seed data**: ~10 fake users / 3 departments / 2 groups / system roles wired up so personas resolve to real `User` rows that fit into the org chart; loaded via SQL fixture at startup if DB is empty (`scripts/seed-org-fixture.sql` or `dotnet run -- seed-org`)
- **Prod auth mode** (placeholder): `BPM_AUTH_MODE=prod` expects JWTs from an external IdP (Entra ID later). The dev endpoint goes 404; everything else still validates the bearer signature.

### Out of scope (deferred)
- AD / Entra ID sync program — design later as a per-tenant import (manual review + custom sync; on-prem variant runs inside customer network and pushes data out)
- Tenant-admin UI for managing Roles / Permissions / RoleAssignments — for now seed via SQL/JSON fixture
- Dynamic group membership rules (Entra-style "department = X" auto-membership)

## Capabilities

### New Capabilities
- `bpm-org-model`: Principal abstraction + User / Department / Group entities, manager and parent_id self-references, dept head, group membership (nested + transitive), org-chart query helpers
- `bpm-roles-and-permissions`: Role / Permission / RolePermission / RoleAssignment with system-vs-flow scope and tenant/flow/step assignment scope
- `bpm-actor-dsl`: Discriminated-union ActorRef JSON schema, path whitelist, conditional + collection composites, validator
- `bpm-workflow-resolver`: Service that resolves any ActorRef + runtime context into `Set<UserId>`, with structured failures, audit logging, and cycle detection
- `bpm-auth-jwt`: JWT bearer authentication, dev-login persona endpoint, seed-data persona mapping, `BPM_AUTH_MODE` switch for dev vs. prod
- `bpm-wizard-actor-editor`: React `ActorRefEditor` and child editors (path picker, conditional editor, collection editor) used by `StepApprovers` / `StepDecisions` / `StepNotify`; rewired `RoleSwitcher` calling the dev-login endpoint

### Modified Capabilities
<!-- None — first BPM-svc / spec-DSL change in the openspec tree; the prior `add-bpm-frontend` change was UI-only and did not introduce these capabilities. -->

## Impact

- **bpm-svc/src/Domain**: 8 new entity classes (Principal, User, Department, Group, GroupMember, Role, Permission, RolePermission, RoleAssignment) + value object for `ActorRef`
- **bpm-svc/src/Persistence**: New EF Core configurations (TPT for Principal hierarchy), new DbSets, initial migration creating ~9 tables + indexes + FK constraints
- **bpm-svc/src/Application**: `IActorResolver` + `ActorResolver` implementation, `IOrgChartReader` for dept/manager walks, `ActorRefValidator`, `IPersonaLoginService`
- **bpm-svc/src/Api**: New `DevLoginController` (gated on `BPM_AUTH_MODE=dev`); JWT bearer middleware via `Microsoft.AspNetCore.Authentication.JwtBearer`; updated `Authorization` policies; existing `BPM_DEMO_TOKEN` middleware deprecated (kept temporarily, removed when JWT path is verified end-to-end); seed endpoint or CLI for fixture import in scope
- **bpm-ui/src/lib/apiFetch.ts**: replaced demo bearer with JWT from `localStorage.bpm_jwt`; on 401 → clear and redirect to dev login
- **bpm-ui/src/components/RoleSwitcher.tsx**: calls `POST /api/dev/login` and stores returned JWT
- **spec_schema.md**, **prompt_template_v1.md**, **sample_specs/leave_v1.json**, **sample_specs/purchase_v1.json**: documentation + samples updated to use `ActorRef`
- **bpm-ui/src/screens/wizard/**: `StepApprovers.tsx`, `StepDecisions.tsx`, `StepNotify.tsx` updated; new `src/components/wizard/ActorRefEditor.tsx` + supporting child components; bilingual labels (zh-TW + en)
- **bpm-ui/src/lib/spec.ts** (or equivalent): TypeScript types for `ActorRef`, validator port mirroring backend whitelist
- **No runtime API breakage** — the workflow engine itself is wired in a separate change; this change ships the schema, resolver, DSL, and the UI to author specs that use it
- **DB migration** is additive (all new tables, no column changes to existing tables)
- **Dependencies added**: none beyond what's already in `bpm-svc.csproj` and `bpm-ui/package.json`
