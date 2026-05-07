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
