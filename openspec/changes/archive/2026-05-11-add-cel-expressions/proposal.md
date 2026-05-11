## Why

The system has three distinct fields that hold *expressions* — small bits of logic written by spec authors and evaluated at runtime:

- **Gateway condition**: `decisions[].branches[].condition` — controls flow routing (`days >= 7`, `amount >= 50000`)
- **FormField conditional**: `userTasks[].fields[].conditional` — show/hide a field based on other fields (`leave_type === '病假'`)
- **FormField validator**: `userTasks[].fields[].validator` — per-field input validation (`value > 0 && value <= 30`)
- **FormField derived**: `userTasks[].fields[].derivedFrom` — computed values (`businessDaysBetween(date_range.start, date_range.end)`, `quantity * unit_price`)

Today these are stored as opaque strings. The wizard accepts and persists them; the runtime can't *do* anything with them. The `add-process-runtime` change ships a "minimal expression evaluator" supporting only `==`/`!=`/comparison/AND-OR/dotted-paths — enough to pass the existing sample specs, but explicitly a placeholder.

The product needs a real expression language. Real customer flows already need:

- `category === 'travel' && amount > 30000` (gateway)
- `cert` field shown only when `leave_type in ['病假', '公假']`
- `value > 0 && value <= 30` (validator on `days`)
- `sum(expense_items.amount)` (derived total of repeater rows — see `extend-field-types-line-items`)
- `businessDaysBetween(start, end)` (derived day count)
- `now() < deadline` (validation against current time)

Plus the existing spec_schema.md commits to **CEL (Common Expression Language)** as the chosen language. CEL is Google's open spec, has TS / .NET / Go / Java implementations, includes a battle-tested grammar for boolean logic, arithmetic, list ops, string ops, and a stdlib. Picking CEL avoids inventing a one-off DSL.

This change ships a CEL evaluator + the registration of the four call-sites (gateway / conditional / validator / derived), plus a controlled function library (built-in helpers like `businessDaysBetween`, `sum`, `count`, `now`) the spec author can use.

## What Changes

### CEL evaluator (NEW capability `bpm-cel-expressions`)

Backend:

- Add `cel-cs` (or `Cel.NET` / hand-implemented subset) NuGet — choose the most maintained .NET implementation. Fallback: implement a vetted subset directly using `Sprache` or `Pidgin` parser combinators if no library is mature enough for production.
- `IExpressionEvaluator` interface (replaces the placeholder from `add-process-runtime`):
  - `bool EvaluateBoolean(string expr, ExpressionContext ctx)` — for gateway / conditional / validator
  - `JsonElement EvaluateValue(string expr, ExpressionContext ctx)` — for derived (returns whatever type CEL evaluates to)
  - `IReadOnlyList<string> Validate(string expr, ExpressionShape shape)` — at spec-load time, AST-validate the expression returns the right type and references only allowed fields/functions
- `ExpressionContext` carries `form_data` (JsonElement), `now` (DateTime), and `helpers` (the registered function library)

Built-in function library (shipped):

- `now()` — current UTC datetime
- `businessDaysBetween(start, end)` — counts weekdays only; integration with `add-calendar-and-business-hours` later for holiday awareness
- `sum(arr.field)`, `count(arr)`, `avg(arr.field)`, `min(arr.field)`, `max(arr.field)` — repeater aggregates
- `dateAdd(date, n, unit)` — `unit ∈ {"days", "hours", "weeks", "months"}`
- `dateDiff(start, end, unit)` — same units
- `length(s)` / `contains(s, sub)` / `startsWith(s, prefix)` / `endsWith(s, suffix)` / `lower(s)` / `upper(s)` — string helpers
- `match(s, pattern)` — regex (anchored, no DoS-prone patterns; CEL spec already constrains)

The library MUST NOT include:
- File system access
- Network calls
- Random / non-deterministic functions other than `now()`
- Reflection / type introspection beyond CEL stdlib basics

### Spec validation at load time

`SpecImportService` (or wherever specs land) SHALL parse every expression at import time and validate:

- It's syntactically a valid CEL expression
- It references only fields that exist in scope (e.g., a userTask field's `conditional` can only reference other fields in the same userTask)
- It returns the expected type: `boolean` for conditional/validator/gateway; `any` for derived
- It does NOT call functions outside the registered library

Validation errors are returned as part of the spec import response — broken expressions are caught before any instance starts.

### Replace placeholder evaluator in ProcessRuntime

The `add-process-runtime` proposal shipped `MinimalExpressionEvaluator` as a placeholder. This change:

1. Implements `CelExpressionEvaluator` against the chosen library
2. Replaces the DI registration in `Application/DependencyInjection.cs`
3. Removes `MinimalExpressionEvaluator` (mark obsolete, delete after one release of co-existence in case of regressions; document in design.md §6)
4. Re-runs all existing test fixtures against the new evaluator — they SHALL continue passing because CEL is a strict superset of the minimal subset

### Form runtime use of CEL (deferred dependency)

The `add-form-runtime-rendering` change (later in queue) will use CEL for live form rendering:

- Field conditional → JS-side CEL evaluator (port of the same library, same grammar) for show/hide
- Field validator → JS-side CEL evaluator for input validation
- Field derived → JS-side CEL evaluator for computed display

This proposal documents the contract; the form rendering proposal implements the JS port.

For now (until form rendering lands), the wizard's StepForms accepts these expressions as strings and the spec validator (server-side) checks correctness. At runtime, the gateway / approval / submission paths use the C# evaluator.

### Sample specs

Update the in-repo sample specs to use real CEL expressions where the placeholder versions hand-waved:

- `purchase_v1.json` — `gateway_after_finance.condition = "amount >= 100000"` (real CEL, was already simple enough)
- `expense_with_threshold_v1.json` — refines `total > 50000` to `sum(expense_items.amount) > 50000` once `extend-field-types-line-items` lands, demonstrating aggregate
- `leave_v1.json` — adds a derived `days` field `derivedFrom = "businessDaysBetween(date_range.start, date_range.end)"`
- `expense_employee_v1.json` (from prior proposals) — adds `total_amount` derived = `"sum(expense_items.amount)"`

### Out of scope (future changes)

- Visual expression builder (drag-and-drop AST UI) — wizard keeps the textarea + autocomplete
- Custom function registration per tenant — only the bundled library is exposed
- Stored procedures / database functions — CEL is pure-function, no side-effects
- Macros / expression composition (DRY for repeated subexpressions) — out for now
- Type-strict expression parameters with full inference — basic shape validation is enough; if a customer needs richer types, switch to a typed dialect like CEL+Annotations
- I18n of error messages from the evaluator — English only in v1
- Performance: caching parsed AST per spec snapshot — easy optimization, not required for correctness; defer

## Capabilities

### New Capabilities

- `bpm-cel-expressions` — `IExpressionEvaluator` interface + CEL implementation, expression library, spec-load validation, ExpressionContext shape, function whitelist.

### Modified Capabilities

- `bpm-process-runtime` — replace `MinimalExpressionEvaluator` placeholder with the CEL implementation; existing tests continue passing.
- `bpm-form-stepper` — clarify that the wizard's expression fields (FormField.conditional / validator / derivedFrom; Decision.condition) are CEL strings; describe spec-load validation surfacing errors back to the wizard.

## Impact

- **bpm-svc/src/Application/Process/Expressions/IExpressionEvaluator.cs**: keep interface; replace minimal impl with CEL-backed
- **bpm-svc/src/Application/Process/Expressions/CelExpressionEvaluator.cs**: NEW — wraps chosen CEL library
- **bpm-svc/src/Application/Process/Expressions/ExpressionLibrary.cs**: NEW — registers the bundled function set
- **bpm-svc/src/Application/Process/Expressions/ExpressionContext.cs**: refactored to carry typed access to form_data, now, helpers
- **bpm-svc/src/Application/Spec/SpecImportService.cs**: extend to walk every expression in a loaded spec, validate via `IExpressionEvaluator.Validate`
- **bpm-ui/src/lib/expressions.ts**: TypeScript stub (interface only — JS-side implementation lands in the form runtime change). Documents the CEL grammar subset relevant to the wizard's expression input fields
- **bpm-ui/src/screens/onboarding/steps/StepForms.tsx**: gain inline expression validation (server round-trip); placeholder expressions like `// TODO` start showing a clear "expression invalid" warning rather than passing silently
- **NuGet additions**: `cel-cs` (or chosen library) ~1-2 MB
- **DB migration**: NONE — expressions are in spec snapshot text
- **Demo guard**: `forms/*`, `Home`, `Search`, `Report`, `lib/workflow.ts` not modified
