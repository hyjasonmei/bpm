## ADDED Requirements

### Requirement: Runtime uses CelExpressionEvaluator for gateway evaluation

The `ProcessRuntime` SHALL evaluate gateway `condition` expressions via `IExpressionEvaluator` whose default registered implementation is `CelExpressionEvaluator`. The minimal placeholder evaluator from `add-process-runtime` SHALL be replaced behind the `BPM_EXPRESSION_BACKEND=cel` env var (default after soak period) and removed entirely in a follow-up change.

#### Scenario: Gateway uses CEL syntax

- **GIVEN** spec gateway has condition `"days >= 7 && leave_type != '公假'"`
- **AND** instance.CurrentFormDataJson = `{ days: 8, leave_type: '事假' }`
- **WHEN** runtime evaluates the gateway via `IExpressionEvaluator.EvaluateBoolean`
- **THEN** the result is `true`; the corresponding edge is taken

#### Scenario: All sample specs continue passing under CEL backend

- **GIVEN** `BPM_EXPRESSION_BACKEND=cel`
- **WHEN** the LEAVE / purchase / expense_with_threshold sample specs run end-to-end
- **THEN** every gateway evaluation produces the same routing decisions as it did under the minimal evaluator
