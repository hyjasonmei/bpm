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

- [ ] 4.1 Extend `bpm-svc/src/Application/Spec/SpecImportService.cs` with a `ValidateExpressions(SpecSnapshot snap)` pass after the existing schema validation
- [ ] 4.2 For each Gateway: validate `condition` is Boolean; allowed fields = top-level form keys
- [ ] 4.3 For each FormField with `conditional`: validate Boolean; allowed = sibling fields in same userTask
- [ ] 4.4 For each FormField with `validator`: validate Boolean; allowed = sibling fields + `value` (the field's own value)
- [ ] 4.5 For each FormField with `derivedFrom`: validate Any-type; allowed = sibling fields
- [ ] 4.6 Aggregate failures into `SpecValidationResult`; surface to API consumers
- [ ] 4.7 Test: spec with one broken expression → response includes the location (gateway id / userTask id / field id) + the parse error

## 5. Replace MinimalExpressionEvaluator

- [ ] 5.1 Add env var `BPM_EXPRESSION_BACKEND` accepting `cel` (default after soak) or `minimal`
- [ ] 5.2 Wire DI: `if (backend == "cel") services.AddScoped<IExpressionEvaluator, CelExpressionEvaluator>();`
- [ ] 5.3 Run all `add-process-runtime` test fixtures under `cel` mode; verify pass
- [ ] 5.4 Mark `MinimalExpressionEvaluator` `[Obsolete("Replaced by CelExpressionEvaluator; remove next release")]`
- [ ] 5.5 Document removal plan in `bpm-svc/CLAUDE.md`

## 6. Validate-expression endpoint

- [ ] 6.1 Add `POST /api/specs/validate-expression` endpoint:
  - Body: `{ expression: string, shape: 'boolean' | 'any', sample_context: { form_data, now } }`
  - Returns `{ valid: bool, errors?: [...], evaluated_sample?: any }`
- [ ] 6.2 Auth: any authenticated user (so wizard can call it during authoring)
- [ ] 6.3 Tests: valid expression returns 200 with sample result; invalid returns 200 with errors

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

- [ ] 10.1 `dotnet build` clean
- [ ] 10.2 `dotnet test` all pass
- [ ] 10.3 Boot service with `BPM_EXPRESSION_BACKEND=cel`; submit a spec with 5 different expression types; verify all evaluate
- [ ] 10.4 Submit a deliberately broken spec (`days >= banana`); verify validation error includes location + parse message
- [ ] 10.5 Boot bpm-ui (`npm run dev`); in StepForms, type a valid CEL expression in `conditional` field; verify ✓ chip; type invalid; verify ✗ message
- [ ] 10.6 Run an end-to-end LEAVE process; verify gateway evaluates correctly under CEL
- [ ] 10.7 **Demo guard**: `forms/*`, `Home`, `Search`, `Report`, `lib/workflow.ts` not modified

## 11. Commit

- [ ] 11.1 Commit in chunks (library + evaluator; helpers; spec validation; endpoint; frontend wiring; samples + prompts; verification)
- [ ] 11.2 Push via GitKraken
