## ADDED Requirements

### Requirement: IExpressionEvaluator evaluates CEL expressions safely

The system SHALL provide an `IExpressionEvaluator` service that evaluates CEL (Common Expression Language) expressions against an `ExpressionContext`. The service MUST support three operations:

- `EvaluateBoolean(expr, ctx)` — evaluate to a boolean for gateway / conditional / validator use
- `EvaluateValue(expr, ctx)` — evaluate to any CEL-typed value for derived field use
- `Validate(expr, shape)` — AST-validate without evaluation, returning a list of errors

Expressions MUST run in a sandbox: no I/O, no network access, no file system access, no reflection beyond CEL stdlib basics, no random number generation other than via `now()`. Execution time per expression MUST be bounded; expressions exceeding 100 ms wall time SHALL be aborted with a timeout error.

#### Scenario: Boolean expression evaluates

- **WHEN** the evaluator runs `"amount >= 50000"` with ctx form_data `{ amount: 80000 }`
- **THEN** the result is `true`

#### Scenario: Derived expression returns value

- **WHEN** the evaluator runs `"quantity * unit_price"` with ctx `{ quantity: 3, unit_price: 100 }`
- **THEN** the result is `300`

#### Scenario: Sandbox blocks unsafe operation

- **WHEN** an expression attempts to call a non-whitelisted function `httpGet(...)`
- **THEN** parse fails with "function 'httpGet' not in expression library"

#### Scenario: Timeout aborts expensive eval

- **WHEN** an expression deliberately constructed to be expensive runs past 100 ms
- **THEN** the evaluator throws `ExpressionTimeoutException`

### Requirement: ExpressionContext carries form_data, now, and helpers

The `ExpressionContext` SHALL provide expression-evaluating code with:

- `FormData` (JsonElement) — current form data accumulator (from `ProcessInstance.CurrentFormDataJson` at runtime, or sample data in dev / wizard preview)
- `NowUtc` (DateTime) — fixed timestamp for the evaluation; `now()` helper reads from this so tests are deterministic
- `InitiatorUserId` / `TenantId` — identity context
- `Helpers` — registered function library

#### Scenario: now() helper reads from context

- **GIVEN** ctx.NowUtc = `2026-05-08T10:00:00Z`
- **WHEN** the expression `"now()"` evaluates
- **THEN** the result is `2026-05-08T10:00:00Z` exactly (NOT the system clock)

### Requirement: Function library is whitelisted at registration

The system SHALL register a fixed set of CEL functions at startup. Spec authors SHALL NOT be able to add functions per-tenant. The library is the same across all tenants. Adding a new function requires a code change + redeploy.

The library at minimum SHALL include: `now`, `businessDaysBetween`, `sum`, `count`, `avg`, `min`, `max`, `dateAdd`, `dateDiff`, `length`, `contains`, `startsWith`, `endsWith`, `lower`, `upper`, `match`. Each function SHALL be deterministic, side-effect-free, and bounded in cost.

#### Scenario: Sum aggregates a numeric list

- **GIVEN** ctx form_data = `{ items: [{ amount: 100 }, { amount: 200 }, { amount: 300 }] }`
- **WHEN** the expression `"sum(items.amount)"` evaluates
- **THEN** the result is `600`

#### Scenario: BusinessDaysBetween excludes weekends

- **WHEN** `businessDaysBetween("2026-05-08", "2026-05-12")` evaluates (Fri to Tue)
- **THEN** the result is 3 (Fri, Mon, Tue; Sat-Sun excluded)

#### Scenario: Match has timeout

- **WHEN** an expression calls `match(input, "(a+)+b")` against a known catastrophic input
- **THEN** the regex aborts within 1 second with a timeout exception, NOT a 30-minute hang

### Requirement: Spec-load expression validation

The `SpecImportService` SHALL parse and validate every expression in a spec at import time. For each expression, the validator SHALL:

- Confirm the expression parses as valid CEL
- Confirm it returns the type expected by the field (Boolean for gateway/conditional/validator, Any for derived)
- Confirm it references only fields in scope (gateway: top-level form keys; userTask field expressions: sibling fields)
- Confirm it calls only registered library functions

Validation errors SHALL aggregate (not fail-first) and SHALL include enough location context (gateway id / userTask id / field id) for a wizard UI to highlight the broken expression.

#### Scenario: Multiple broken expressions all surface

- **GIVEN** a spec with 2 broken gateway conditions and 1 broken validator
- **WHEN** SpecImportService.ValidateExpressions runs
- **THEN** the result contains all 3 errors with their locations; the spec is rejected

#### Scenario: Gateway condition with unknown field rejected

- **GIVEN** a gateway condition `"days >= 7"` but no `days` field exists in any userTask
- **WHEN** validation runs
- **THEN** the error message is "gateway 'gateway_days' references unknown field 'days'"

#### Scenario: Field validator references sibling

- **GIVEN** a userTask with fields `[{ id: 'days', type: 'number' }, { id: 'reason', type: 'textarea', validator: 'days > 0' }]`
- **WHEN** validation runs
- **THEN** the validator passes (`reason.validator` may reference sibling `days`)

### Requirement: Validate-expression endpoint for wizard preview

The system SHALL expose `POST /api/specs/validate-expression` accepting `{ expression, shape, sample_context }` and returning `{ valid, errors?, evaluated_sample? }`. The endpoint runs `IExpressionEvaluator.Validate` and, if valid, also runs `EvaluateBoolean` / `EvaluateValue` against the sample_context for round-trip preview. The endpoint requires authentication but no special role.

#### Scenario: Valid expression evaluates sample

- **WHEN** POST `/api/specs/validate-expression` with `{ expression: "days >= 7", shape: "boolean", sample_context: { form_data: { days: 8 } } }`
- **THEN** response is `{ valid: true, evaluated_sample: true }`

#### Scenario: Invalid expression returns errors

- **WHEN** POST with `{ expression: "days >== 7", shape: "boolean" }`
- **THEN** response is `{ valid: false, errors: [{ message: "parse error: unexpected '==' at column 7", line: 1, column: 7 }] }`

### Requirement: bpm-cel-v1 subset enforced for spec author expressions

The system SHALL enforce a documented "bpm-cel-v1" subset for any expression appearing in a spec.json. The 9-stepper validator and the spec import validator SHALL both reject expressions using operators, literals, functions, identifiers, or macros outside this subset, with a clear error message naming the offending construct and the v1 alternative (or "defer to v1.5").

This subset is intentionally smaller than full CEL because:
1. Both backend (.NET) and frontend (JS) evaluators must agree bit-identically on every expression that appears in a customer flow; constraining the surface area is the only practical way to guarantee parity
2. Spec authors are not engineers — narrowing what they can write keeps onboarding cognitively manageable
3. Macros (filter / map / exists) require lambdas and are an outsized parity / implementation cost; ActorRef DSL covers most "find users matching X" needs without expressions

#### v1 surface

**Operators:** `==` `!=` `<` `<=` `>` `>=` `&&` `||` `!` `+` `-` `*` `/` `%` ternary `?:` membership `in` field access `.` index `[…]`. **Excluded:** bitwise `&` `|` `^` `>>` `<<`; slice syntax `a[1:3]`.

**Literals:** integer (`42`), decimal (`3.14`), string (`'hi'` / `"hi"`), boolean (`true` / `false`), null (`null`), list (`[1, 2, 3]`), duration (`duration("24h")` / `duration("3d12h")`), timestamp (`timestamp("2026-05-10T00:00:00Z")`). **Excluded:** map literal `{}`, bytes literal, type literal — defer to v1.5.

**Built-in functions (11):** `now()` / `today()` / `daysBetween(t1, t2)` / `businessDaysBetween(t1, t2)` / `sum(list)` / `count(list)` / `len(x)` / `match(s, regex)` / `lower(s)` / `upper(s)` / `contains(haystack, needle)`.

**Identifiers / context:** form-field ids (root scope of current form data), `submitter` (User object), path traversal limited by `ActorPathWhitelist`, `instance` (ProcessInstance metadata), `now` shorthand for `now()`.

**Macros:** ALL EXCLUDED in v1 — `filter` / `map` / `exists` / `exists_one` / `all` / closures / list comprehension. Defer to v1.5.

#### Edge case definitions (cross-runtime parity rules)

- Integer overflow: throw error at int64 bounds — DO NOT wrap
- Decimal precision: `decimal(18,2)` everywhere; JS uses `decimal.js`, .NET uses `decimal`
- Division by zero: throw error — DO NOT return Infinity / NaN
- String comparison: byte-wise UTF-8 — guarantees the same ordering on both runtimes
- Date comparison: internal arithmetic in UTC; presentation in tenant TZ
- Regex flavor: ECMAScript subset — no lookbehind, no named groups; .NET uses `RegexOptions.ECMAScript`
- Null propagation: `null.field` THROWS — DO NOT silently return null
- Boolean truthiness: STRICT — only literal `true` / `false` count as boolean; numeric 0, empty string, and null are NOT falsy

#### Scenario: Spec author uses macro — rejected

- **GIVEN** a spec with notification recipient filter `users.filter(u => u.dept == submitter.dept)`
- **WHEN** the spec is imported (or saved in the 9-stepper)
- **THEN** validator rejects with: "macro 'filter' not supported in bpm-cel-v1 (defer to v1.5); use ActorRef.functional_members instead"

#### Scenario: Spec author uses bitwise operator — rejected

- **WHEN** spec has `flags & 0x01 == 0x01`
- **THEN** validator rejects with: "operator '&' not supported in bpm-cel-v1"

#### Scenario: Division by zero throws

- **GIVEN** form data has `count: 0`
- **WHEN** evaluator runs `total / count`
- **THEN** evaluator throws `ExpressionEvaluationException` with message containing "division by zero"

#### Scenario: Both runtimes agree on UTF-8 ordering

- **GIVEN** strings `"abc"` and `"ab中"`
- **WHEN** the .NET evaluator and the JS evaluator each compare `"abc" < "ab中"`
- **THEN** both return `true` (because "中" UTF-8 bytes start with `e4` > "c"'s `63`)

### Requirement: Subset version is declared in spec metadata

Each spec.json SHALL declare `meta.celVersion: "bpm-cel-v1"` (or higher when subsequent versions ship). The validator MUST refuse to import a spec whose celVersion the current build doesn't support. This versioning is independent of the spec.json schema version (`meta.flowVersion`); CEL subset version evolution is not lock-stepped with spec schema evolution.

#### Scenario: Unknown celVersion rejected

- **GIVEN** a spec with `meta.celVersion: "bpm-cel-v9"`
- **WHEN** the spec is imported on a build that knows v1 only
- **THEN** the import fails with: "unknown CEL subset version 'bpm-cel-v9'; this build supports: bpm-cel-v1"
