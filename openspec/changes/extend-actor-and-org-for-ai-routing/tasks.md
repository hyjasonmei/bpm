# Tasks

## 1. Domain — org model extensions

- [ ] 1.1 Extend `bpm-svc/src/Domain/Entities/Org/Department.cs` with `FunctionTag` (string?), `ApprovalLimit` (decimal?)
- [ ] 1.2 Extend `bpm-svc/src/Domain/Entities/Org/User.cs` with `TitleRaw` (string?), `TitleNormalized` (string?), `ApprovalLimit` (decimal?), `IsDepartmentHead` (bool, denormalized), `IsExecutive` (bool, denormalized), `Attributes` (string? containing JSON)
- [ ] 1.3 Create `bpm-svc/src/Domain/Org/FunctionTagWhitelist.cs` static class with the 8 allowed values + `IsValid(string)` helper
- [ ] 1.4 Create `bpm-svc/src/Domain/Org/TitleNormalizer.cs` with pure-function `Normalize(string raw) -> string` (prefix stripping + CN/EN unification table)
- [ ] 1.5 Unit-test `TitleNormalizer` with: "資深副總" → "vp", "Vice President" → "vp", "代理經理" → "manager", "處長" → "director", "Senior Director" → "director", and an unknown title returning lowercased trimmed input

## 2. Persistence — migration + EF config

- [ ] 2.1 Update `bpm-svc/src/Persistence/Configurations/Org/UserConfiguration.cs` — add the new columns; index `TitleNormalized`, `ApprovalLimit`
- [ ] 2.2 Update `bpm-svc/src/Persistence/Configurations/Org/DepartmentConfiguration.cs` — add `FunctionTag`, `ApprovalLimit`; index `FunctionTag`
- [ ] 2.3 Generate migration: `dotnet ef migrations add ExtendOrgAndActorTypes -p src/Persistence -s src/Api`
- [ ] 2.4 Apply migration locally; verify schema with `sqlite3 src/Api/bpm.db .schema "Users"` and `.schema "Departments"`
- [ ] 2.5 Update `bpm-svc/src/Persistence/Seed/OrgFixture.cs`:
  - assign `function_tag` to seeded depts (財務部 → finance, 資訊部 → it, etc.)
  - assign `title_raw` to all seed users; run `TitleNormalizer.Normalize` to populate `title_normalized`
  - assign `approval_limit` to managers / VPs / executives in the fixture
  - flip `is_department_head` / `is_executive` based on title + dept head FK
- [ ] 2.6 Add CLI subcommand: `dotnet run -- normalize-titles` — re-runs `TitleNormalizer` over all User rows (idempotent, no-op when titles unchanged)

## 3. Domain — ActorRef new types

- [ ] 3.1 Add to `bpm-svc/src/Domain/Spec/ActorRef.cs`: `FunctionalHeadActorRef(string FunctionTag)`, `ByAmountActorRef(string AmountField, string From, string Strategy, bool IncludeSelf = false)`, `TitleMatchActorRef(IReadOnlyList<string> Patterns, string Scope)`, `UnresolvedActorRef(string Intent, string Reason, string? SuggestedClarification = null)`
- [ ] 3.2 Add optional metadata to base `ActorRef` record: `string? Intent`, `double? Confidence`, `bool? NeedsReview`, `bool? SkipIfInitiator`
- [ ] 3.3 Update `bpm-svc/src/Domain/Spec/ActorRefJsonConverter.cs` — extend the `type` switch with the four new strings; deserialize metadata fields on the base
- [ ] 3.4 Round-trip serialization tests: each new type round-trips through `JsonSerializer.Serialize/Deserialize` losslessly, including metadata fields
- [ ] 3.5 Add to `bpm-svc/src/Domain/Spec/Resolution.cs` `ErrorKind` enum: `AmountExceedsAllAuthorities`, `FunctionTagNotMapped`, `TitleNoMatch`, `UnresolvedAiNode`

## 4. Application — validator extensions

- [ ] 4.1 Extend `bpm-svc/src/Application/Spec/ActorRefValidator.cs`:
  - `functional_head`: `function_tag` non-empty AND in `FunctionTagWhitelist`
  - `by_amount`: `amount_field` non-empty, `from` ∈ {"submitter","current_approver"}, `strategy` ∈ {"manager_chain","department_tree"}, `include_self` is bool
  - `title_match`: `patterns` non-empty list of strings, `scope` ∈ {"company","same_department"}
  - `unresolved`: `intent` non-empty, `reason` non-empty
  - metadata: `confidence` ∈ [0.0, 1.0] when present; `needs_review` defaults true for unresolved (validator sets it if missing); `skip_if_initiator` is bool
- [ ] 4.2 Validator tests: positive case for each new type; off-whitelist `function_tag` rejected; out-of-range `confidence` rejected; `unresolved` without `intent` rejected
- [ ] 4.3 Lint-pass extensions (when given `IOrgChartReader`):
  - `functional_head`: warn if no Department has the matching `function_tag` (might still be valid pre-onboarding)
  - `title_match`: warn if no User matches any pattern
  - `by_amount`: warn if no User has `approval_limit >= 0` set anywhere (means resolver will always fail)

## 5. Application — IOrgChartReader extensions

- [ ] 5.1 Add to `bpm-svc/src/Application/Org/IOrgChartReader.cs`:
  - `Department? GetDepartmentByFunctionTag(string functionTag)`
  - `IReadOnlyList<User> FindUsersByTitlePattern(IReadOnlyList<string> patterns, Guid? scopedDepartmentId)`
  - `IReadOnlyList<User> WalkManagerChain(Guid startUserId, int maxLevels)`
  - `IReadOnlyList<Department> WalkDepartmentTreeUpward(Guid startDeptId, int maxLevels)`
- [ ] 5.2 Implement in `bpm-svc/src/Persistence/Org/OrgChartReader.cs`:
  - `GetDepartmentByFunctionTag`: single-row query, indexed lookup
  - `FindUsersByTitlePattern`: SQL `LIKE` over `title_normalized`, OR-joined patterns; if scoped, add `department_id = X`
  - `WalkManagerChain`: iterative loop with cycle detection (HashSet)
  - `WalkDepartmentTreeUpward`: same, on `parent_id`
- [ ] 5.3 Unit test each new method with seeded org fixture

## 6. Application — ActorResolver extensions

- [ ] 6.1 Add `ResolveFunctionalHead` to `bpm-svc/src/Application/Spec/ActorResolver.cs`:
  - Look up dept by tag; if missing → `Failure(FunctionTagNotMapped, "no department tagged X")`
  - If dept has `head_user_id` → return that user; else → empty + structured "department head unset" reason
- [ ] 6.2 Add `ResolveByAmount`:
  - Read `ctx.form_data[amount_field]`, parse as decimal; missing/non-numeric → `Failure(ValidationFailed, ...)`
  - Resolve start user/dept based on `from` + `strategy`
  - Iterate upward, at each step check `candidate.approval_limit >= amount`; first match returns
  - Cap at 10 levels; no match → `Failure(AmountExceedsAllAuthorities, ...)`
- [ ] 6.3 Add `ResolveTitleMatch`:
  - Get `ctx.submitter.department_id` if `scope = "same_department"`
  - Call `IOrgChartReader.FindUsersByTitlePattern(patterns, scope-dept-id-or-null)`
  - Empty → `Failure(TitleNoMatch, "no users match patterns X")`
- [ ] 6.4 Add `ResolveUnresolved`: always returns `Failure(UnresolvedAiNode, ref.Reason)` regardless of context
- [ ] 6.5 Add post-filter in `ActorResolver.Resolve` wrapper: when `ref.SkipIfInitiator ?? true`, drop `ctx.initiator_user_id` from successful results
- [ ] 6.6 Wire each new resolver into the top-level dispatch switch in `ActorResolver`

## 7. Application — resolution audit

- [ ] 7.1 No schema change — `ActorResolutionAudits` already has `ActorRefJson`, `ResultKind`, `ErrorKind`, `ErrorReason` columns covering the new failure modes
- [ ] 7.2 Verify that resolving a top-level `unresolved` node writes exactly one audit row with `ResultKind = Failure`, `ErrorKind = UnresolvedAiNode`
- [ ] 7.3 Verify that `by_amount` failure includes the `amount` value in the audit reason text (for triage)

## 8. Resolver tests

- [ ] 8.1 `functional_head` happy path — finance dept tagged, head set, returns head user
- [ ] 8.2 `functional_head` fallback — function_tag not present, fallback to `role:cfo` works
- [ ] 8.3 `by_amount` manager-chain — submitter has manager whose `approval_limit = 30000`, manager's manager `= 200000`; amount = 50000 → returns manager's manager
- [ ] 8.4 `by_amount` department-tree — same idea but walks dept heads
- [ ] 8.5 `by_amount` no qualifying authority — amount = 999999 with no one having that limit → AmountExceedsAllAuthorities
- [ ] 8.6 `by_amount` skip_if_initiator interplay — initiator has highest approval_limit; with skip_if_initiator=true, walks past them
- [ ] 8.7 `title_match` company scope — multiple 副總 across depts, returns all
- [ ] 8.8 `title_match` same_department scope — returns only the same-dept matches
- [ ] 8.9 `title_match` no matches → TitleNoMatch failure
- [ ] 8.10 `unresolved` always fails with UnresolvedAiNode + carries reason text

## 9. Spec docs + samples

- [ ] 9.1 Update `spec_schema.md` §2.10 — append the four new ActorRef types with the `worked examples` style; document metadata fields
- [ ] 9.2 Add `spec_schema.md` §2.11 — function_tag whitelist table
- [ ] 9.3 Update `prompt_template_v1.md`:
  - new section: "When you are uncertain — emit `unresolved`"
  - 4 worked examples covering each new type
  - explicit guidance: "do not fabricate a role/expr if confidence < 0.7; emit unresolved instead"
- [ ] 9.4 Migrate `sample_specs/expense_with_threshold_v1.json` — replace `conditional` amount routing with `by_amount`
- [ ] 9.5 Create `sample_specs/it_request_v1.json` — IT support flow demonstrating `functional_head` + one `unresolved` placeholder

## 10. Frontend — TypeScript types + validator

- [ ] 10.1 Update `bpm-ui/src/lib/actor-ref.ts` (or equivalent) — add the four new variants to the discriminated union; metadata fields on the base
- [ ] 10.2 Update frontend validator (subset mirror of backend) — same rules as task 4.1
- [ ] 10.3 Round-trip test: build each variant in TypeScript → JSON-serialize → POST to a backend echo endpoint → assert backend's parsed shape equals the input

## 11. Frontend — ActorRefEditor extensions

- [ ] 11.1 Update `bpm-ui/src/components/wizard/ActorRefEditor.tsx`: add the four new picker options to the type dropdown with bilingual labels (部門功能主管 / 金額簽核 / 職稱比對 / 待釐清)
- [ ] 11.2 Create `bpm-ui/src/components/wizard/editors/FunctionalHeadEditor.tsx` — function_tag select bound to whitelist; show dept name preview when selected (lookup `/api/org/function-tags`)
- [ ] 11.3 Create `bpm-ui/src/components/wizard/editors/ByAmountEditor.tsx` — amount-field picker (filters parent form's number fields), from/strategy/include_self toggles
- [ ] 11.4 Create `bpm-ui/src/components/wizard/editors/TitleMatchEditor.tsx` — pattern tag-input (Enter to add), scope toggle
- [ ] 11.5 Create `bpm-ui/src/components/wizard/editors/UnresolvedCard.tsx` — yellow card UI; intent + reason (read-only) + "release as" button that converts to a concrete ActorRef via the type picker
- [ ] 11.6 Update `ActorRefEditor` switch on `value.type` to render the right editor for the new types
- [ ] 11.7 Show metadata as a small read-only annotation row at the bottom of every actor card when `intent` / `confidence` / `needs_review` is set (italic, gray)

## 12. Frontend — supporting endpoints

- [ ] 12.1 New API: `GET /api/org/function-tags` — returns `[{ tag: "finance", department: { id, name } | null }, ...]` so the editor can show "Tag 已對應到 財務部" or "Tag 尚未對應任何部門"
- [ ] 12.2 New API: `GET /api/org/title-suggestions?q=<query>` — autocomplete against `title_normalized`; returns top 10 matches; used by `TitleMatchEditor` for tag-input suggestions

## 13. End-to-end verification

- [ ] 13.1 `dotnet build bpm-svc.slnx` clean
- [ ] 13.2 All backend unit tests pass (`dotnet test`)
- [ ] 13.3 Apply migration on a fresh `bpm.db` and verify `Users` / `Departments` schema includes new columns
- [ ] 13.4 Boot bpm-svc, hit `/api/org/function-tags`, verify it returns the seeded mapping
- [ ] 13.5 Boot bpm-ui (`npm run dev` in bpm-ui — remember `tsc -p tsconfig.app.json` for type-check); pick each new ActorRef type in the wizard, save, verify the spec.json roundtrip preserves the typed shape
- [ ] 13.6 Run dogfood pipeline (`dogfood.command`) with `expense_with_threshold_v1.json` (now using `by_amount`) and verify the resolver picks the correct approver across the org fixture
- [ ] 13.7 Manual: feed a deliberately invalid spec (`function_tag = "foo"`) and verify the validator rejects with the whitelist error
- [ ] 13.8 Manual: feed an `unresolved` node and verify the resolver returns `UnresolvedAiNode` failure + audit row written

## 14. Docs + commit

- [ ] 14.1 Update `bpm-svc/CLAUDE.md` (or main `CLAUDE.md`) — document `function_tag`, `title_normalized`, `approval_limit` fields and how seed fixture populates them
- [ ] 14.2 Note in `SETUP.md`: running `dotnet run -- normalize-titles` after a manual title import
- [ ] 14.3 Commit in chunks (org schema + migration; ActorRef types + validator; resolver; spec docs + samples; frontend editors; verification); no `--no-verify`
- [ ] 14.4 Push via GitKraken (Claude does not push to BPM repo)
