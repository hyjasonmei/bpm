## Context

The BPM platform currently has no abstraction over actors. The `add-bpm-frontend` change shipped a wizard that produces a spec.json, but every place that needs to refer to "the approver" today has to write a static string. Real BPM workflows (the partner's 10+ flows brought back from his prior employer) routinely need to express:

- "submitter's manager" (request-time lookup of the org chart)
- "head of submitter's parent department" (multi-segment walk)
- Conditional routing ("if amount > 50000 then CEO else manager")
- Collective approval (any 2 of 3, or all 5 must sign)

The org chart itself does not exist yet — there is no `User`, no `Department`, no `Group`. This change introduces both the data model AND the language for referring to actors built on top of it. The two are tightly coupled: the DSL's `submitter.department.head` only makes sense if `Department.head_user_id` exists, and the resolver only works if `User.manager_id` is queryable.

Stakeholders:
- **Jason** (developer) — writes the DSL + resolver + wizard editors
- **Partner** (sales) — the dynamic approver expressiveness is the wedge versus generic SaaS BPM
- **Future tenants** — their HR data feeds in via the (deferred) AD-sync change

Constraints:
- .NET 10 Clean Architecture (Domain/Application/Persistence/Api), EF Core, SQLite for POC
- React 18 + Tailwind v4 + shadcn for wizard UI
- Bilingual (zh-TW + en) labels on all UI surfaces
- The DSL must be **round-trip safe** through Claude Code — i.e. the LLM must be able to read a spec.json and emit one back without breaking ActorRef nodes (this drives the typed-discriminator-vs-sigil choice)

## Goals / Non-Goals

**Goals:**
- Express the org chart in a normalized, EF-friendly schema that aligns with Entra ID concepts (so a future AD-sync change is mostly mechanical mapping)
- Provide a single ActorRef DSL that covers atomic references, conditional routing, and collective approval
- Make all paths used by `expr` explicitly enumerable so a validator can reject typos at spec-load time
- Make the resolver return a uniform `Set<UserId>` regardless of the actor type, so downstream consumers (engine, notify dispatcher) don't branch on actor shape
- Make every resolution failure observable (audit log + structured error), not silently swallowed
- Provide a wizard editor that authors the typed object form directly — not a free-text field that gets parsed
- Round-trip safety: a spec.json round-tripped through Claude Code must produce semantically identical ActorRef nodes

**Non-Goals:**
- Wiring the workflow engine itself to consume the resolver (separate change)
- AD / Entra ID sync (separate change; this change provides only the target schema)
- Tenant-admin UI for managing roles, permissions, role assignments (seed via SQL/JSON for now)
- Dynamic group membership rules ("all users where department=Sales auto-join SalesGroup")
- Multi-tenancy scoping at the entity level (single-tenant POC; tenant_id columns can be added in a later refactor)
- A separate "expression engine" for arbitrary expressions — `conditional.condition` is intentionally a tiny, fixed set of operators, not a general expression language

## Decisions

### 1. TPT (Table-per-Type) for the Principal hierarchy

**Decision:** `User`, `Department`, `Group` each get their own table sharing the same PK as `Principal`. EF Core configured with TPT.

**Why:**
- Junction tables (`GroupMember.principal_id`, `RoleAssignment.principal_id`) need a single FK column that can refer to any principal type. TPT makes the shared PK pool natural.
- Mirrors Entra ID's `directoryObject` model — when AD sync ships, mapping is direct.
- Avoids polymorphic-association ugliness in SQL (no `principal_type` discriminator on every junction).

**Alternatives considered:**
- **TPH** (single table, discriminator column): Saves a join but mixes wildly different columns (User has email, Department has parent_id, Group has nothing) → many nullable columns, harder to evolve. Rejected.
- **Three separate tables, no Principal abstraction** (option 🅰️ from the schema discussion): Forces 3 junction tables per relation (`role_users`, `role_groups`, `role_departments`), and queries like "effective members of role X" become 3-way unions. Rejected.

### 2. Hardcoded path whitelist for `expr`

**Decision:** The set of valid `path` strings is a fixed enum in the validator and resolver, not a parsed mini-language.

Allowed segments (max depth 4):
- `submitter`
- `submitter.manager`, `submitter.manager.manager`, `submitter.manager.manager.manager`
- `submitter.department`, `submitter.department.head`
- `submitter.department.parent`, `submitter.department.parent.head`
- `submitter.department.parent.parent.head`

**Why:**
- A general expression language invites runtime explosions (`submitter.manager.manager.manager.manager...`) and is hard for the LLM to emit correctly.
- A whitelist lets the spec validator reject typos at load time — runtime never has to handle "I don't know this path".
- New paths can be added in future changes by extending the enum, with explicit review.

**Alternatives considered:**
- **JSONLogic-style `{"$user": {"$path": "submitter.manager"}}`**: More general, but the `$` sigil syntax is a known LLM round-trip failure mode (gets stripped, escaped, or rewritten). Rejected.
- **JSONata or a custom DSL with operators**: Overkill for the actual usage patterns observed in the partner's 10+ real flows. Rejected.

### 3. Discriminated union for ActorRef (typed `type` field)

**Decision:** Every ActorRef is `{ "type": "<one of expr|role|group|user|conditional|collection>", ...fields }`.

**Why:**
- Easy for the LLM to round-trip: the `type` field is a literal token, not a structural sigil.
- Easy for the wizard UI: switch on `type` to pick the editor component.
- Easy for the resolver: pattern-match on `type`, dispatch to handler.
- Schema evolution is easy: add a new variant by adding a new `type` value + handler.

**Alternatives considered:** see decision 2.

### 4. Resolver returns `Set<UserId>`, never single user

**Decision:** Even atomic resolutions like `expr` (which conceptually points to one person) return `Set<UserId>`.

**Why:**
- Composite types (`collection`) inherently produce sets.
- Uniform return type lets downstream code (notify dispatcher, approval gate) be agnostic.
- Edge cases produce empty sets cleanly: a department with no head, a manager not set, a group with no members.

**Trade-off:** Callers needing "the one approver" have to assert `.Count == 1`. Acceptable — explicit is better than implicit.

### 5. Cycle detection in resolver

**Decision:** All graph walks (group nesting, manager chain, department parent chain) carry a visited-set; a cycle aborts with a structured error.

**Why:**
- Real-world AD data has cycles more often than you'd expect (manager loops after re-orgs; nested groups that include their own parent).
- A silent infinite loop blocks an approval forever — worst possible failure mode.

**Implementation:** `HashSet<Guid> visited` passed through walk functions; on detected cycle, emit `ResolutionError(kind: Cycle, path: [...visited.ToList()])`.

### 6. Failure modes as first-class results

**Decision:** Resolver returns `ResolutionResult` = `Success(Set<UserId>) | Failure(ResolutionError)` (discriminated). Each `ResolutionError` has a `kind` enum (`PathUnresolved` / `RoleEmpty` / `GroupEmpty` / `Cycle` / `ConditionalBranchEmpty` / `ValidationFailed`) and a human-readable `reason`.

**Why:**
- Forces callers to handle failures explicitly (no exception-throwing for routine "not found" cases).
- Audit log captures the structured error, not just "exception".
- Spec authors get actionable error messages in the wizard preview.

**Fallback support:** Each ActorRef may optionally carry a `fallback: ActorRef` field — if primary resolution returns empty/error, try fallback. Bounded recursion (max one fallback chain).

### 7. Audit logging strategy

**Decision:** Every resolver invocation writes one `ActorResolutionAudit` row: `(timestamp, request_id, actor_ref_json, context_summary, result_kind, resolved_user_ids, error_kind, error_reason)`. Single table; one row per top-level resolve, not per recursion.

**Why:**
- Per-recursion logging would explode log volume on conditional/collection trees.
- Top-level row + `actor_ref_json` (full input) is enough to replay/diagnose any failure.

### 8. Wizard `ActorRefEditor` is a recursive component

**Decision:** A single `<ActorRefEditor value={...} onChange={...} />` component handles all six types via internal type-switch. `conditional.then` / `conditional.else` and `collection.actors[i]` recursively render `<ActorRefEditor>`.

**Why:**
- DRY: one component, six render branches, no duplicated logic.
- Drag-and-drop reorganization (future enhancement) becomes uniform.

### 9. JWT bearer auth + dev-login persona endpoint

**Decision:** Replace the demo-bearer-token scheme (commit `38efd5b`) with JWT. Provide a `/api/dev/login` endpoint that mints a JWT for a chosen persona, gated by `BPM_AUTH_MODE=dev`. In `prod` mode the endpoint disappears (404) and the JWT validator expects tokens from an external IdP (Entra ID later).

JWT claims:
- `sub` = `User.Id` (Guid)
- `persona_code` = the persona that minted this token (debug aid only — authorization checks use `roles[]`)
- `tenant_id` = placeholder (single-tenant POC)
- `roles[]` = role codes from `RoleAssignment` joined to `Role.code` where `scope = system`
- `exp` = 8h dev / 1h prod default
- Signed HS256 with `BPM_JWT_SECRET` (length-checked at startup, ≥ 32 bytes)

**Why:**
- Existing demo-bearer is a single shared secret — useless once we have multiple personas / users.
- JWT is the natural pre-cursor to Entra ID integration; keeping the same bearer header shape means swapping the issuer is the only change later.
- Dev-login endpoint lets the wizard's `RoleSwitcher` actually authenticate as different users instead of faking a flag in localStorage. This makes API-level authorization testable now.
- `BPM_AUTH_MODE` switch keeps prod from accidentally exposing the dev-login back door.

**Persona-to-user mapping:** seed fixture creates 10 users; a static `persona_code → user_id` map (in `appsettings.Development.json`) tells the dev-login service which seed user to mint a JWT for. Personas: `employee`, `manager`, `finance`, `it`, `hr`, `admin`. Each persona maps to a real `User` row that fits in the org chart (e.g., `employee` is `manager`'s direct report — so `submitter.manager` resolves correctly when employee files a request).

**Alternatives considered:**
- **Keep demo bearer + persona header** (`X-Demo-Persona: manager`): Trivially spoofable by client; no signature; doesn't generalize to prod. Rejected.
- **OAuth dev provider** (e.g., spin up Keycloak): Heavy; just for personas in POC. Rejected.

**Trade-offs:**
- Symmetric HS256 means anyone with `BPM_JWT_SECRET` can forge tokens — fine for POC, swap to RS256 when Entra ID arrives.
- Persona role membership is computed at JWT-mint time, not refreshed mid-session. If an admin changes a role assignment, the user has to re-login. Acceptable for POC.

### 10. Tenant scoping deferred

**Decision:** No `tenant_id` column on entities in this change. Single-tenant POC.

**Why:**
- Adding `tenant_id` everywhere now without a tenant story (tenant onboarding, isolation policy, query filters) is premature.
- When multi-tenancy ships, it'll be a uniform refactor (add column, add EF query filter, migrate data into a default tenant).

## Risks / Trade-offs

- **[Risk] LLM emits invalid path** → validator rejects at spec-load with a clear "valid paths are: ..." message. The 3-5 worked examples in `prompt_template_v1.md` make this rare in practice.
- **[Risk] Cyclic group/manager data poisons resolution** → cycle detection aborts with structured error; audit captures the cycle for HR data cleanup.
- **[Risk] Path whitelist becomes a maintenance bottleneck** → low-cost change to extend; new segments require updating the enum + adding a resolver branch (~10 lines). Acceptable as long as additions are deliberate.
- **[Risk] Wizard editor for `conditional` becomes deeply nested and unusable** → cap nesting depth at 3 in the UI (matches schema validator); past depth 3, force the user to model with `collection`. UX-tested with the partner's 10+ flows before locking the cap.
- **[Trade-off] TPT adds joins** → for high-traffic resolver queries, denormalize via projection (e.g., a materialized view of `user_id, manager_id, dept_id, dept_head_id`) later if profiling shows a bottleneck. Don't pre-optimize.
- **[Trade-off] No general expression engine** → some hypothetical future flow ("approver is the requester's mentor's manager") cannot be expressed. Accepted: the partner's real flows don't need it; revisit if a real customer brings a counter-example.
- **[Trade-off] `fallback` only one level deep** → keeps schema explicit and prevents `fallback.fallback.fallback...` chains. If a multi-level fallback is needed, model with nested `conditional`.

## Migration Plan

This change is purely additive — new tables, no column changes to existing tables. Steps:

1. Create EF Core migration `AddPrincipalAndOrgModel` adding all 9 tables + indexes + FKs
2. Run `dotnet ef database update` (POC SQLite — `bpm-svc/src/Persistence/bpm.db` regenerates trivially; for any persisted dev DB, the migration runs forward)
3. Seed fixture (`scripts/seed-org-fixture.sql` or a `dotnet run -- seed-org` CLI subcommand) populating ~10 users, 3 departments, 2 groups, system roles (admin/designer/viewer)
4. Update `spec_schema.md` and `prompt_template_v1.md`
5. Migrate `sample_specs/leave_v1.json` and `sample_specs/purchase_v1.json` to use `ActorRef` (this verifies the schema works on real specs)
6. Ship the wizard `ActorRefEditor` and replace the old plain-text inputs in StepApprovers/StepDecisions/StepNotify
7. Add a "spec contains legacy approver field" warning at spec import — non-blocking, but visible

**Rollback:** drop the migration's tables + revert the wizard components. No production data exists yet.

## Open Questions

- **Q1**: For `collection.mode = "any"`, is `min_approvals = N` capped at `actors.length`, or do we allow `min_approvals > actors.length` (always blocked)? **Decision needed before specs lock**: cap at `actors.length` and validator-reject otherwise.
- **Q2**: Do we resolve `role` references at spec-author time (snapshot the user list into the spec) or at runtime (always re-query)? **Leaning runtime** — that's the whole point of dynamic routing. Snapshot mode might be useful for "frozen" approved specs but defer until needed.
- **Q3**: Should `expr` paths be case-sensitive? The whitelist makes this moot in practice (only one casing is allowed), but document explicitly. **Decision**: lowercase-only, validator rejects otherwise.
- **Q4**: `RoleAssignment.scope = "step"` was included for forward compatibility but no current flow needs it. Keep or strip? **Decision**: keep — the column is cheap and removing later is harder than adding now.
