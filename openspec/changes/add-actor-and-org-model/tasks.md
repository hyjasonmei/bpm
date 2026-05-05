## 1. Backend domain entities

- [ ] 1.1 Create `bpm-svc/src/Domain/Org/Principal.cs` (abstract base; Id, Type enum, DisplayName, CreatedAt; inherit AuditableEntity)
- [ ] 1.2 Create `bpm-svc/src/Domain/Org/User.cs` (Email unique, FullName, ManagerId self-FK, DepartmentId FK, IsActive); inherit Principal
- [ ] 1.3 Create `bpm-svc/src/Domain/Org/Department.cs` (Code unique, Name, ParentId self-FK, HeadUserId FK); inherit Principal
- [ ] 1.4 Create `bpm-svc/src/Domain/Org/Group.cs` (Code unique, Name, Description); inherit Principal
- [ ] 1.5 Create `bpm-svc/src/Domain/Org/GroupMember.cs` (composite key GroupId + PrincipalId; navigation refs)
- [ ] 1.6 Create `bpm-svc/src/Domain/Authz/Role.cs` (Code unique, Name, Scope enum, FlowCode nullable; check constraint enforcing scope/flow_code coherence)
- [ ] 1.7 Create `bpm-svc/src/Domain/Authz/Permission.cs` (Action, Resource)
- [ ] 1.8 Create `bpm-svc/src/Domain/Authz/RolePermission.cs` (RoleId + PermissionId composite)
- [ ] 1.9 Create `bpm-svc/src/Domain/Authz/RoleAssignment.cs` (Id, RoleId, PrincipalId, Scope enum, ScopeRef nullable)

## 2. Backend persistence (EF Core)

- [ ] 2.1 Add EF configurations under `bpm-svc/src/Persistence/Configurations/Org/` for Principal (TPT base), User, Department, Group, GroupMember
- [ ] 2.2 Add EF configurations under `bpm-svc/src/Persistence/Configurations/Authz/` for Role, Permission, RolePermission, RoleAssignment
- [ ] 2.3 Configure TPT explicitly (`modelBuilder.Entity<User>().ToTable("Users")` + base.UseTptMappingStrategy())
- [ ] 2.4 Add DbSets to `BpmDbContext`: Principals, Users, Departments, Groups, GroupMembers, Roles, Permissions, RolePermissions, RoleAssignments
- [ ] 2.5 Add indexes: User.Email unique, User.ManagerId, User.DepartmentId, Department.Code unique, Department.ParentId, Department.HeadUserId, Group.Code unique, GroupMember.PrincipalId (for reverse lookup), Role.Code unique, RoleAssignment.PrincipalId, RoleAssignment(RoleId, PrincipalId, Scope, ScopeRef) composite
- [ ] 2.6 Generate migration: `dotnet ef migrations add AddPrincipalAndOrgModel`
- [ ] 2.7 Apply migration locally and verify SQLite schema with `sqlite3 bpm-svc/src/Api/bpm.db .schema`

## 3. Org-chart query helper

- [ ] 3.1 Define `IOrgChartReader` interface in `bpm-svc/src/Application/Org/IOrgChartReader.cs`: GetUser, GetManager, GetDepartmentOf, GetDepartmentParent, GetDepartmentHead, ExpandGroup
- [ ] 3.2 Implement `OrgChartReader` in `bpm-svc/src/Application/Org/OrgChartReader.cs`; transitive group expansion uses BFS with HashSet<Guid> visited
- [ ] 3.3 Register `IOrgChartReader` in DI container (Api/Program.cs or DependencyInjection.cs)
- [ ] 3.4 Unit test ExpandGroup with: direct members, nested groups, cyclic groups (must return Cycle error not loop), empty group

## 4. ActorRef DSL types

- [ ] 4.1 Create discriminated union types in `bpm-svc/src/Domain/Spec/ActorRef.cs` — abstract `ActorRef` with derived `ExprActorRef`, `RoleActorRef`, `GroupActorRef`, `UserActorRef`, `ConditionalActorRef`, `CollectionActorRef`; optional `Fallback` property on base
- [ ] 4.2 Define `ResolutionContext`, `ResolutionResult` (Success/Failure), `ResolutionError` (with Kind enum: PathUnresolved, RoleEmpty, GroupEmpty, Cycle, ConditionalBranchEmpty, ValidationFailed) in `bpm-svc/src/Domain/Spec/Resolution.cs`
- [ ] 4.3 Add System.Text.Json JsonConverter for ActorRef polymorphic deserialization (reads `type` field, dispatches to derived type)
- [ ] 4.4 Define `ActorPathWhitelist` static class with the 9 allowed path strings

## 5. ActorRef validator

- [ ] 5.1 Create `ActorRefValidator` in `bpm-svc/src/Application/Spec/ActorRefValidator.cs`; checks: type is recognized, expr.path on whitelist, role.code non-empty, group identifier non-empty, conditional has condition+then+else, conditional condition.op in allowed set, conditional nesting depth ≤ 3, collection.actors non-empty, collection.min_approvals ≤ actors.length when mode=any, fallback chain depth ≤ 1
- [ ] 5.2 Optional "lint" pass that, when given an `IOrgChartReader`, verifies referenced role/group/user actually exists; emits warnings for `user` type usage
- [ ] 5.3 Wire validator into the spec.json import path (`SpecImportService` or wherever spec.json is parsed)
- [ ] 5.4 Unit tests for each validator branch (positive + negative cases per spec scenarios)

## 6. ActorResolver

- [ ] 6.1 Create `IActorResolver` interface in `bpm-svc/src/Application/Spec/IActorResolver.cs`
- [ ] 6.2 Implement `ActorResolver` in `bpm-svc/src/Application/Spec/ActorResolver.cs`; pattern-match on ActorRef subtype, dispatch to Resolve* methods
- [ ] 6.3 ResolveExpr: parse path segments, walk org graph using IOrgChartReader, maintain HashSet<Guid> visited for cycle detection
- [ ] 6.4 ResolveRole: query RoleAssignments where RoleId matches; expand each assignment's Principal (if Group → ExpandGroup, if Department → all users in dept, if User → just that user); include flow-scoped roles only when ctx.flow_code matches
- [ ] 6.5 ResolveGroup: delegate to IOrgChartReader.ExpandGroup
- [ ] 6.6 ResolveUser: trivial direct return
- [ ] 6.7 ResolveConditional: evaluate condition against ctx.form_data using a small switch on op (==, !=, >, >=, <, <=, in, not_in); recurse into chosen branch
- [ ] 6.8 ResolveCollection: recurse into each child; mode=all returns Failure if any child fails; mode=any unions all successful child resolutions
- [ ] 6.9 Fallback handling: on top-level Failure or empty Success, retry with ref.Fallback if present (one level only)
- [ ] 6.10 Register `IActorResolver` in DI; unit-test every spec scenario

## 7. Resolution audit

- [ ] 7.1 Create `ActorResolutionAudit` entity in `bpm-svc/src/Domain/Audit/ActorResolutionAudit.cs` (Timestamp, RequestId, ActorRefJson, ContextSummary, ResultKind, ResolvedUserIds JSON, ErrorKind, ErrorReason)
- [ ] 7.2 EF configuration + DbSet + migration `AddActorResolutionAudit` (or fold into the org-model migration)
- [ ] 7.3 In ActorResolver, capture top-level Resolve calls with a using-block writing one audit row at exit (Success or Failure)
- [ ] 7.4 Unit-test that one Resolve call producing nested recursion yields exactly one audit row

## 8. Auth — JWT bearer

- [ ] 8.1 Add `Microsoft.AspNetCore.Authentication.JwtBearer` package to `bpm-svc/src/Api/bpm-svc-Api.csproj`
- [ ] 8.2 Create `bpm-svc/src/Api/Auth/JwtOptions.cs` (Secret, Issuer, Audience, DefaultExpiryDev = 8h, DefaultExpiryProd = 1h)
- [ ] 8.3 Add JWT bearer to authentication pipeline in `Program.cs`; HS256 with `BPM_JWT_SECRET` env; assert ≥ 32 bytes at startup or fail fast
- [ ] 8.4 Add `[Authorize]` to existing controllers; add `[AllowAnonymous]` to /health, /swagger
- [ ] 8.5 Update CORS preflight handling so OPTIONS still returns 204 without auth
- [ ] 8.6 Deprecate `BPM_DEMO_TOKEN` middleware (mark obsolete, remove after JWT path is verified end-to-end)

## 9. Auth — dev-login + persona seeding

- [ ] 9.1 Create `bpm-svc/src/Api/Controllers/DevLoginController.cs`; only register when `BPM_AUTH_MODE=dev` (use IHostEnvironment + config check at app startup, conditionally `MapControllerRoute`)
- [ ] 9.2 Implement POST /api/dev/login: read persona_code from body, look up seed user via `PersonaMappingOptions` config, mint JWT with sub/persona_code/tenant_id/roles/exp claims, return 200 { token, user: {...} }
- [ ] 9.3 Add `appsettings.Development.json` "Personas" section mapping employee/manager/finance/it/hr/admin → seed user emails
- [ ] 9.4 Create `bpm-svc/src/Persistence/Seed/OrgFixture.cs`: idempotent seed (check User.Email uniqueness) creating ~10 users, 3 departments (2-level tree), 2 groups, system roles (admin/designer/viewer), RoleAssignments per persona
- [ ] 9.5 Add CLI subcommand: `dotnet run -- seed-org` invokes OrgFixture.Run; also auto-run on startup if `BPM_SEED_ON_STARTUP=true`
- [ ] 9.6 Unit + integration test: dev-login returns 200 + valid JWT for each persona; returns 400 for unknown; returns 404 in prod mode
- [ ] 9.7 Verify org chart wiring: `manager` persona is `employee` persona's manager so `submitter.manager` resolves correctly when employee submits

## 10. Spec docs + samples

- [ ] 10.1 Update `spec_schema.md`: add ActorRef section documenting all six types + path whitelist + condition operators + fallback semantics; mark old approver_id/approver_role fields as removed
- [ ] 10.2 Update `prompt_template_v1.md`: add ActorRef shape spec + path whitelist + 5 worked examples (simple expr / role lookup / conditional with form-field / collection with min_approvals / mixed conditional+collection)
- [ ] 10.3 Migrate `sample_specs/leave_v1.json` to use ActorRef everywhere it referred to an approver
- [ ] 10.4 Migrate `sample_specs/purchase_v1.json` to use ActorRef
- [ ] 10.5 Add a third sample `sample_specs/expense_with_threshold_v1.json` exercising conditional (amount > 50000) + collection (any 2 of 3 finance approvers)

## 11. Frontend types + API client

- [ ] 11.1 Create `bpm-ui/src/lib/actor-ref.ts`: TypeScript discriminated union types for ActorRef, ResolutionResult, the path whitelist constant
- [ ] 11.2 Create `bpm-ui/src/lib/actor-ref-validator.ts` mirroring backend validator (subset — what wizard needs for inline error display)
- [ ] 11.3 Update `bpm-ui/src/lib/apiFetch.ts`: read `localStorage.bpm_jwt`, attach as `Authorization: Bearer <jwt>`; on 401, clear token and broadcast event
- [ ] 11.4 Replace deprecated demo-bearer constant in apiFetch with the JWT path

## 12. Frontend — RoleSwitcher rewire

- [ ] 12.1 Update `bpm-ui/src/components/RoleSwitcher.tsx`: on persona select, POST to /api/dev/login with persona_code; store returned token in localStorage; update in-memory user state
- [ ] 12.2 Add error toast on dev-login failure (do NOT clear existing token)
- [ ] 12.3 Add a "logged in as" display next to the dropdown showing the actual seed user's full_name (from the dev-login response, not localStorage flag)
- [ ] 12.4 Bilingual labels (zh-TW + en) on dropdown items

## 13. Frontend — ActorRefEditor

- [ ] 13.1 Create `bpm-ui/src/components/wizard/ActorRefEditor.tsx`: type picker dropdown (6 options, bilingual labels), switch on `value.type` to render the right child
- [ ] 13.2 Create `bpm-ui/src/components/wizard/editors/ExprEditor.tsx`: path picker bound to whitelist constant
- [ ] 13.3 Create `bpm-ui/src/components/wizard/editors/RoleEditor.tsx`: role-code input (free text for now; optional autocomplete from /api/roles in follow-up)
- [ ] 13.4 Create `bpm-ui/src/components/wizard/editors/GroupEditor.tsx`: group identifier input
- [ ] 13.5 Create `bpm-ui/src/components/wizard/editors/UserEditor.tsx`: user-id input + visible "test only" warning
- [ ] 13.6 Create `bpm-ui/src/components/wizard/editors/ConditionalEditor.tsx`: condition builder + recursive ActorRefEditor for then/else; cap nesting at 3
- [ ] 13.7 Create `bpm-ui/src/components/wizard/editors/CollectionEditor.tsx`: mode toggle, min_approvals input (clamps to actors.length), add/remove actor controls
- [ ] 13.8 Switching `value.type` produces a sensible default for the new shape

## 14. Frontend — wire into wizard steps

- [ ] 14.1 Update `bpm-ui/src/screens/wizard/StepApprovers.tsx`: replace any plain-text approver inputs with `<ActorRefEditor>`
- [ ] 14.2 Update `bpm-ui/src/screens/wizard/StepDecisions.tsx`: same
- [ ] 14.3 Update `bpm-ui/src/screens/wizard/StepNotify.tsx`: same
- [ ] 14.4 Verify the wizard's spec.json output (Submit step) carries ActorRef objects, not strings, in all relevant fields
- [ ] 14.5 Smoke-test: pick each ActorRef type at least once in a manual wizard run and confirm the produced spec.json round-trips through the backend validator

## 15. End-to-end verification

- [ ] 15.1 Run `dotnet build bpm-svc.sln` and verify clean build
- [ ] 15.2 Run all backend unit tests
- [ ] 15.3 Boot bpm-svc with `BPM_AUTH_MODE=dev`, `BPM_JWT_SECRET=<32+ bytes>`, `BPM_SEED_ON_STARTUP=true`; verify /health returns 200 and shows aiBackend + new authMode field
- [ ] 15.4 POST /api/dev/login with each of the 6 personas, confirm JWT decodes with expected sub/roles
- [ ] 15.5 Boot bpm-ui (`npm run dev`), open wizard, switch personas via RoleSwitcher, verify JWT in localStorage and Authorization header on /api/spec calls (devtools network tab)
- [ ] 15.6 In the wizard, build a leave-request flow using each ActorRef type, Submit, verify the resulting spec.json on disk uses typed-discriminator form
- [ ] 15.7 Re-run the existing dogfood pipeline (`dogfood.command`) with one of the migrated sample specs and verify Claude Code can compile it without ActorRef-related errors
- [ ] 15.8 Manual spot-check: feed a deliberately invalid ActorRef (off-whitelist path) to /api/spec, verify it's rejected with the spec validator's error message

## 16. Docs + commit

- [ ] 16.1 Update `bpm-svc/CLAUDE.md` (or main `CLAUDE.md`) with: JWT auth setup notes (env vars), persona mapping, seed fixture command
- [ ] 16.2 Update `SETUP.md` to mention BPM_JWT_SECRET and BPM_AUTH_MODE
- [ ] 16.3 Commit in logical chunks (entities + persistence; resolver; auth + seed; spec docs + samples; frontend editor; frontend wire-up); no `--no-verify`
- [ ] 16.4 Push via GitKraken (do not attempt git push from Claude — see CLAUDE memory)
