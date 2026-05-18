# Tasks

## 1. Spec schema docs

- [ ] 1.1 Update `spec_schema.md` §2.3 (UserTask): replace the `permissions` block with `assignee: ActorRef` + `viewers: ViewerRef[]`; mark v1.2
- [ ] 1.2 Add §2.10 sub-section on `ViewerRef` discriminated union with the four variants (`self`, `submitter`, `current_assignee`, `<ActorRef>`)
- [ ] 1.3 Add a new sub-section "UserTask assignee migration cheat-sheet" with the v1.0 → v1.2 string-to-typed mapping
- [ ] 1.4 Update §6 (review checklist for Review Agent): add bullet "every userTask carries assignee resolving to non-empty candidates given the seed fixture"

## 2. Domain types

- [ ] 2.1 Add `Bpm.Domain.Spec.FunctionalMembersActorRef` record to `bpm-svc/src/Domain/Spec/ActorRef.cs` with `string FunctionTag`, `bool IncludeSubtree = false`, `bool ActiveOnly = true`
- [ ] 2.2 Add `Bpm.Domain.Spec.ViewerRef` discriminated union (abstract record + 4 derived: `SelfViewer`, `SubmitterViewer`, `CurrentAssigneeViewer`, `ActorViewer(ActorRef Inner)`) to `bpm-svc/src/Domain/Spec/ViewerRef.cs`
- [ ] 2.3 Add JSON converters for `FunctionalMembersActorRef` and `ViewerRef` (mirror `ActorRefJsonConverter` style, polymorphic on `type`)
- [ ] 2.4 Add `FunctionalMembersEmpty` to `ResolutionError.Kind` enum in `Resolution.cs`
- [ ] 2.5 Add `Bpm.Domain.Spec.UserTaskSpec` record: `string Id, string FormCode, IReadOnlyList<FormFieldSpec> Fields, ActorRef Assignee, IReadOnlyList<ViewerRef> Viewers`

## 3. Validators

- [ ] 3.1 Extend `ActorRefValidator` to handle `functional_members`: function_tag in `FunctionTagWhitelist`, `include_subtree` is bool, `active_only` is bool
- [ ] 3.2 Create `bpm-svc/src/Application/Spec/ViewerRefValidator.cs`: each viewer is one of the 4 types; `ActorViewer.Inner` recurses into `ActorRefValidator`
- [ ] 3.3 Create `bpm-svc/src/Application/Spec/UserTaskSpecValidator.cs`: `Id` non-empty, `FormCode` non-empty, `Assignee` valid (delegates), `Viewers` element-wise valid (delegates)
- [ ] 3.4 Wire validator into `SpecImportService` so userTasks rejected at import time when the assignee is malformed

## 4. Resolver — functional_members

- [ ] 4.1 Add `ResolveFunctionalMembers` method to `bpm-svc/src/Application/Spec/ActorResolver.cs`:
  - Look up Department by tag via `IOrgChartReader.GetDepartmentByFunctionTag`
  - Tag missing → `Failure(FunctionTagNotMapped, ...)`
  - Query `Users` where `department_id = dept.id` and `is_active = true` (when `active_only`)
  - When `include_subtree = true`, BFS down `Department.parent_id` reverse direction (children of dept), accumulating users; cap depth at 5
  - Empty set → `Failure(FunctionalMembersEmpty, ...)`
- [ ] 4.2 Add `IOrgChartReader.GetDepartmentChildren(Guid deptId)` if not already present (returns direct children)
- [ ] 4.3 Implement transitive subtree expansion in `OrgChartReader.WalkDepartmentTreeDownward(Guid root, int maxDepth)`
- [ ] 4.4 Unit tests: flat dept (3 active + 1 inactive users) → returns 3 active; nested dept tree with `include_subtree = true` → returns descendants; tag missing → FunctionTagNotMapped

## 5. Spec importer — legacy migration

- [ ] 5.1 Create `bpm-svc/src/Application/Spec/SpecImportService.cs` (if not present); add `MigrateUserTaskPermissions(UserTask raw)` method
- [ ] 5.2 Mapping: `'self'` → `{ type: 'expr', path: 'submitter', skip_if_initiator: false }`; `'role:X'` → `{ type: 'role', code: 'X' }`; `'group:X'` → `{ type: 'group', id: 'X' }`
- [ ] 5.3 Default missing `assignee` to `{ type: 'expr', path: 'submitter', skip_if_initiator: false }` so absent permissions still validate
- [ ] 5.4 Viewers migration: `'self'` → `{ type: 'self' }`; `'manager'` → `{ type: 'expr', path: 'submitter.manager', skip_if_initiator: false }` wrapped as `ActorViewer`; `'role:X'` → `ActorViewer({ type: 'role', code: 'X' })`; `'all'` → omit (default-allow read)
- [ ] 5.5 Unit tests for each legacy string variant + idempotency (re-running on already-typed input is no-op)

## 6. Frontend — types

- [ ] 6.1 Update `bpm-ui/src/lib/onboarding.ts`:
  - Update `UserTask` type: drop `permissions`, add `assignee: ActorRef`, `viewers: ViewerRef[]`
  - Add `ViewerRef` TypeScript discriminated union mirroring backend
  - Update `LEAVE_PRESET` and `PURCHASE_PRESET` userTasks to use the new shape (e.g., `task_apply.assignee = { type: 'expr', path: 'submitter', skip_if_initiator: false }`)
  - Update `EMPTY_DRAFT` so newly added userTask nodes auto-default to assignee = expr:submitter
- [ ] 6.2 Add `bpm-ui/src/lib/viewer-ref.ts` with type definitions + a small validator mirroring backend
- [ ] 6.3 Add `functional_members` to `actor-ref.ts` discriminated union
- [ ] 6.4 Update wizard's spec exporter to write `assignee` / `viewers` (not `permissions`) when serializing draft to JSON

## 7. Frontend — wizard

- [ ] 7.1 Update `bpm-ui/src/screens/onboarding/steps/StepForms.tsx`:
  - Above each userTask's field editor, render a "誰來填" panel that hosts an `<ActorRefEditor value={userTask.assignee} ...>`
  - Below the field editor, render a "誰可看 / Viewers" panel with a multi-select for `self` / `submitter` / `current_assignee` plus an "add viewer" button that opens a nested `ActorRefEditor`
  - Default new userTask nodes to assignee = expr:submitter so no broken state
- [ ] 7.2 Update `bpm-ui/src/components/wizard/ActorRefEditor.tsx`: add `functional_members` to the type picker dropdown (label: 部門功能成員)
- [ ] 7.3 Create `bpm-ui/src/components/wizard/editors/FunctionalMembersEditor.tsx`: function_tag dropdown + include_subtree toggle + active_only toggle (default checked)
- [ ] 7.4 Add `bpm-ui/src/components/wizard/editors/ViewerListEditor.tsx`: chips for the runtime types + "add" opens an inline `ActorRefEditor`
- [ ] 7.5 Smoke test: pick each `assignee` type at least once across a manual wizard run; verify the exported draft.json carries the typed shape

## 8. Sample specs

- [ ] 8.1 Update `sample_specs/leave_v1.json` userTasks: `task_apply.assignee = expr:submitter`, `task_hr_archive.assignee = functional_members:hr`; viewers `[self, current_assignee]`
- [ ] 8.2 Update `sample_specs/purchase_v1.json`: `task_request.assignee = expr:submitter`, `task_purchase_exec.assignee = functional_members:procurement`
- [ ] 8.3 Update `sample_specs/expense_with_threshold_v1.json` (from previous proposal): finance userTasks use `functional_members:finance`
- [ ] 8.4 Update `sample_specs/it_request_v1.json` (from previous proposal): IT spec → `functional_members:it`; quote → `functional_members:procurement`; PO → `functional_members:finance`
- [ ] 8.5 Add `sample_specs/expense_employee_v1.json` modeling GEE: 5 userTasks (apply / approve / confirm / fin_review / close); confirm + fin_review = functional_members:finance
- [ ] 8.6 Add `sample_specs/travel_request_v1.json` modeling TRQ: notify_admin = functional_members:general_affairs

## 9. Prompt template

- [ ] 9.1 Update `prompt_template_v1.md`:
  - New section "UserTask assignee — choosing the right resolver" with the decision matrix from design.md §3
  - 4 worked examples: `expr:submitter` / `functional_members:hr` / `role:custom_role` / `collection` for parallel co-fill
  - Updated review-agent checklist: every userTask MUST carry assignee; assignee MUST resolve to ≥1 user against the seed fixture

## 10. End-to-end verification

- [ ] 10.1 `dotnet build bpm-svc.slnx` clean
- [ ] 10.2 All backend unit tests pass
- [ ] 10.3 Apply migration `extend-actor-and-org-for-ai-routing` first (function_tag column required); then this change is purely code (no migration)
- [ ] 10.4 Boot bpm-ui (`npm run dev`); type-check with `tsc -p tsconfig.app.json`
- [ ] 10.5 Open onboarding wizard, walk a sample flow:
  - Step 3 (Forms): pick assignee = `functional_members:finance` for a finance userTask; verify dropdown populated from `/api/org/function-tags`
  - Verify viewers section accepts mixing `self` + an ActorRef
  - Export draft → inspect JSON has `assignee` (not `permissions`)
- [ ] 10.6 Manual: feed a legacy spec with `permissions.submitter = 'role:HR'` to import endpoint; verify it migrates to `assignee = { type: 'role', code: 'HR' }`
- [ ] 10.7 Coverage check: run wizard for each of the 9 mock-up flows, picking the right assignee for every non-self userTask, verify spec.json round-trips clean and validator passes
- [ ] 10.8 **Demo guard**: verify `bpm-ui/src/screens/Home.tsx`, `forms/*`, `Search`, `Report`, `lib/workflow.ts` were NOT modified — these power the evening demo

## 11. Docs + commit

- [ ] 11.1 Update `bpm-svc/CLAUDE.md` with the new userTask assignee semantics and the v1.2 schema bump note
- [ ] 11.2 Update `SETUP.md` if any new env or seed step required (probably none)
- [ ] 11.3 Commit in chunks (schema docs + spec types; resolver + validator; importer migration; frontend types + wizard; samples + prompts; verification)
- [ ] 11.4 Push via GitKraken (Claude does not push to BPM repo)
