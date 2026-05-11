# Tasks

## 1. Library evaluation

- [ ] 1.1 Survey .NET CEL libraries (`cel-cs`, `Cel.NET`, others); test each on: license, last commit, AOT-friendliness, perf benchmark (eval 50-token expr × 10K iterations)
- [ ] 1.2 Decision: pick library OR commit to hand-rolled subset; document rationale in design.md
- [ ] 1.3 Add chosen NuGet (or vendored source) to `bpm-svc/src/Application/Application.csproj`

## 2. Backend evaluator

- [ ] 2.1 Create `bpm-svc/src/Application/Process/Expressions/CelExpressionEvaluator.cs` implementing `IExpressionEvaluator`
- [ ] 2.2 `EvaluateBoolean(expr, ctx)` — parse, bind, evaluate; throw `ExpressionEvaluationException` with line/col on parse error or runtime error
- [ ] 2.3 `EvaluateValue(expr, ctx)` — same but returns whatever CEL produced (typed to JsonElement for downstream use)
- [ ] 2.4 `Validate(expr, shape)` — AST-validate without evaluation; checks type, field references, function whitelist
- [ ] 2.5 Result caching: parse once per (spec snapshot, expression text); cache the AST keyed by `Hash(snapshotId, expr)`
- [ ] 2.6 Unit tests: 30+ scenarios covering each operator, each function, each error mode

## 3. ExpressionContext + helpers

- [ ] 3.1 Create `bpm-svc/src/Application/Process/Expressions/ExpressionContext.cs` record (FormData, NowUtc, InitiatorUserId, TenantId, Helpers)
- [ ] 3.2 Create `bpm-svc/src/Application/Process/Expressions/Helpers/` directory with one file per function:
  - `NowHelper.cs`, `BusinessDaysBetweenHelper.cs`, `SumHelper.cs`, `CountHelper.cs`, `AvgHelper.cs`, `MinHelper.cs`, `MaxHelper.cs`, `DateAddHelper.cs`, `DateDiffHelper.cs`, `MatchHelper.cs`, `LengthHelper.cs`, `ContainsHelper.cs`, etc.
- [ ] 3.3 Create `ExpressionLibrary.cs` static registration that returns `IReadOnlyDictionary<string, ICelFunction>`
- [ ] 3.4 Unit-test each helper: positive cases + edge cases (empty list, null, type mismatch)
- [ ] 3.5 `MatchHelper`: 1s timeout via `RegexOptions.NonBacktracking` (or `Timeout = TimeSpan.FromSeconds(1)`); reject pattern that fails compilation; integration test with a known catastrophic-regex input

## 4. Spec-load validation

- [x] 4.1 Created `bpm-svc/src/Application/Spec/SpecImportService.cs` (`ISpecImportService` + impl) wired into Program.cs's `/api/spec` endpoint. The post body is validated before the file write; failures return 400 `{ error: "spec_validation_failed", errors }`.
- [x] 4.2 Gateway: walks `decisions[].branches[].condition`; allowed scope = top-level form keys (union across userTasks).
- [x] 4.3 FormField `conditional`: validated Boolean; allowed = sibling field ids in same userTask.
- [x] 4.4 FormField `validator`: validated Boolean; allowed = siblings + `value`.
- [x] 4.5 FormField `derivedFrom`: validated Value (any-type); allowed = siblings.
- [x] 4.6 Aggregated into `SpecValidationResult(bool Valid, IReadOnlyList<SpecValidationError> Errors)`; each error carries `Location`/`Expression`/`Message`.
- [x] 4.7 Tests in `bpm-svc/tests/Bpm.Tests/Application/Spec/SpecImportServiceTests.cs` cover valid leave_v1, broken gateway, unknown sibling, validator `value` keyword, derivedFrom helper, empty body, malformed JSON.

  **Limitation (documented in service xmldoc):** `IExpressionEvaluator.Validate` does not take an allowed-set parameter and the Cel.NET 1.0.0 wrapper doesn't expose AST inspection cheaply. We drive scope-checking through the *evaluation* path: build a stub `ExpressionContext` whose `FormData` declares every allowed identifier (mapped to a Dyn-friendly empty map). Cel.NET's type-checker rejects references outside that scope as "undeclared reference to 'x'". Runtime overload errors against the placeholder values (e.g., `map._>=_[](int)`) are swallowed — only syntax / scope errors surface. A proper AST walker is the right long-term shape; deferred to v1.5 to avoid extending `IExpressionEvaluator`. Also fixed `leave_v1.json` `===` → `==` (JS-style operator was always invalid CEL).

## 5. Replace MinimalExpressionEvaluator

- [~] 5.1 Skipped — no `MinimalExpressionEvaluator` exists; `CelNetExpressionEvaluator` is the only impl since 2026-05-10 commit 67c4daa (PR-B confirmed).
- [~] 5.2 Skipped — DI already registers CelNetExpressionEvaluator unconditionally.
- [~] 5.3 Skipped — process-runtime fixtures already run under CelNet; PR-D verified.
- [~] 5.4 Skipped — nothing to mark obsolete.
- [~] 5.5 Skipped — no removal plan needed.

## 6. Validate-expression endpoint

- [x] 6.1 Created `bpm-svc/src/Api/Spec/SpecValidationController.cs` exposing `POST /api/specs/validate-expression`.
  - Body: `{ expression, shape: 'boolean'|'any', sample_context?: { form_data, now } }`
  - Returns: `{ valid, errors?, evaluatedSample? }`
  - Without sample context: parse-only via stub-context + parse/scope error filter.
  - With sample context: evaluates against constructed `ExpressionContext`; runtime exceptions return `{ valid: true, errors: [<runtime>], evaluatedSample: null }` so parse vs runtime are distinct signals to the wizard.
- [x] 6.2 `[Authorize]` on the controller — any authenticated user can call.
- [x] 6.3 Tests in `bpm-svc/tests/Bpm.Tests/Api/Spec/SpecValidationControllerTests.cs`: valid boolean, invalid expression, valid+sample (boolean + value), empty expression, `[Authorize]` reflection check (since the test class instantiates the controller directly without WebApplicationFactory).

## 7. Frontend wiring

- [ ] 7.1 Update `bpm-ui/src/lib/expressions.ts` (NEW or existing) — TS interface for expression validation, `validateExpression(expr, shape, sampleCtx)` API client
- [ ] 7.2 Update `bpm-ui/src/screens/onboarding/steps/StepForms.tsx`: each `conditional` / `validator` / `derivedFrom` input shows inline status (✓ valid / ✗ invalid with message) — debounced 500 ms, calls validate endpoint
- [ ] 7.3 Update `bpm-ui/src/screens/onboarding/steps/StepDecisions.tsx`: same for gateway `condition`
- [ ] 7.4 Bilingual error messages where the evaluator's message has a Chinese counterpart; fallback English

## 8. Sample specs upgrade

- [ ] 8.1 Update `sample_specs/leave_v1.json`: add `derivedFrom = "businessDaysBetween(date_range.start, date_range.end)"` on `days` field
- [ ] 8.2 Update `sample_specs/expense_with_threshold_v1.json`: gateway uses `sum(expense_items.amount) >= 50000`
- [ ] 8.3 Update `sample_specs/expense_employee_v1.json`: `total_amount` field becomes derived `sum(expense_items.amount)`
- [ ] 8.4 Update `sample_specs/hardware_purchase_v1.json`: `line_total` derived per-row works without change; add a top-level `grand_total` derived = `sum(hw_items.line_total)`
- [ ] 8.5 Run `openspec validate` and `dotnet test` to verify all samples parse and run

## 9. Prompt template upgrade

- [ ] 9.1 Update `prompt_template_v1.md`:
  - Section "Writing CEL expressions": grammar overview, available helpers, examples for each
  - Anti-pattern: do NOT emit `// TODO: define logic` placeholders; emit `unresolved` ActorRef instead, or omit the field entirely if optional
  - 6+ worked examples spanning gateway / conditional / validator / derived

## 10. End-to-end verification

- [x] 10.1 `dotnet build bpm-svc/bpm-svc.slnx` → 0 errors (8 NU1904 warnings are pre-existing, unrelated to this PR).
- [x] 10.2 `dotnet test bpm-svc/bpm-svc.slnx` → 64/64 passing (51 baseline + 13 new).
- [~] 10.3 Skipped per spec — covered instead by controller-level integration tests (10.4) following PR-C's pattern (direct controller instantiation with claims-set HttpContext).
- [x] 10.4 Broken-gateway test (`days >= banana` in `decisions[].branches[].condition`) asserts `Valid=false` + location `gateway:gateway_days/branch[0].condition` + message containing `banana`. See `SpecImportServiceTests.Broken_gateway_condition_surfaces_location_and_parse_message`.
- [~] 10.5 Skipped — UI test (front-end work belongs to PR-G).
- [~] 10.6 Skipped — UI test.
- [x] 10.7 Demo guard verified via `git status`: only `bpm-svc/src/Api/Program.cs`, `bpm-svc/src/Application/DependencyInjection.cs`, `sample_specs/leave_v1.json`, plus the new files under `bpm-svc/src/Api/Spec/`, `bpm-svc/src/Application/Spec/SpecImportService.cs`, and matching test directories. `Home.tsx`, `forms/*`, `Search.tsx`, `Report.tsx`, `lib/workflow.ts` untouched.

## 11. Commit

- [x] 11.1 Single commit (HEREDOC, `Co-Authored-By: Claude Opus 4.7 (1M context) <noreply@anthropic.com>`). Push deferred to Jason via GitKraken (per `feedback_git_push.md`).
- [ ] 11.2 Push via GitKraken
