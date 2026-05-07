## ADDED Requirements

### Requirement: DynamicForm renders any FormField via the registry

The `<DynamicForm>` component SHALL accept a `userTaskSpec` (the userTask portion of a spec snapshot) and render its fields using the field-renderer registry. The registry maps `FieldType` to a React component. The default registry MUST cover all 11 supported field types: `text`, `textarea`, `number`, `date`, `daterange`, `select`, `multiselect`, `file`, `user_picker`, `derived`, `repeater`.

#### Scenario: Renders fields in declaration order

- **GIVEN** userTaskSpec.fields = `[{ id: 'a', type: 'text' }, { id: 'b', type: 'number' }, { id: 'c', type: 'select' }]`
- **WHEN** DynamicForm renders
- **THEN** the DOM order is a, b, c

#### Scenario: Unknown field type renders placeholder

- **GIVEN** userTaskSpec contains a field with `type: 'flux_capacitor'` (not in registry)
- **WHEN** DynamicForm renders
- **THEN** a placeholder shows "Unsupported field type: flux_capacitor" (does not throw / crash)

### Requirement: Live evaluation of conditional / validator / derived

The form SHALL re-evaluate every field's `conditional` (visibility), `validator` (per-field error), and `derivedFrom` (computed value) on each value change. Order:

1. Apply value change
2. Re-evaluate `conditional` for all fields → update `visible[]`
3. Re-evaluate `derivedFrom` for all derived fields → update `derived[]`
4. Re-evaluate `validator` for all dirty visible fields → update `errors[]`

A hidden field's validator SHALL NOT fire (its value is irrelevant when not shown).

#### Scenario: Conditional shows cert when leave_type = 病假

- **GIVEN** field `cert` has `conditional = "leave_type === '病假'"`
- **WHEN** the user changes `leave_type` to `'病假'`
- **THEN** the cert field becomes visible

#### Scenario: Derived total updates on input

- **GIVEN** field `line_total` has `derivedFrom = "quantity * unit_price"`
- **WHEN** the user enters `quantity = 3` and `unit_price = 100`
- **THEN** the derived field shows `300`

#### Scenario: Hidden field validator does not fire

- **GIVEN** the cert field is hidden (leave_type != 病假)
- **AND** cert.validator = `value != null`
- **WHEN** the form re-evaluates
- **THEN** no error appears for cert (its hidden value is ignored)

### Requirement: Repeater renders rows with sub-field controls

`<RepeaterField>` SHALL render a table with one column per sub-field and one row per current item. The user SHALL be able to add a row (creates an empty row with default values) and remove a row (with confirmation). Per-row derived fields evaluate using only that row's other sub-field values; top-level derived expressions (`sum(items.line_total)`) evaluate across all rows.

#### Scenario: Add row creates empty item

- **GIVEN** an empty repeater
- **WHEN** the user clicks "Add row"
- **THEN** a new row is added with default values per sub-field schema

#### Scenario: Per-row derived isolated to that row

- **GIVEN** sub-fields `[quantity, unit_price, line_total (derived = quantity * unit_price)]`
- **AND** row 1 has quantity=2, unit_price=50; row 2 has quantity=3, unit_price=100
- **WHEN** the form renders
- **THEN** row 1's line_total = 100; row 2's line_total = 300 (independent)

#### Scenario: Top-level aggregate spans rows

- **GIVEN** top-level field `total = derived "sum(items.line_total)"`
- **WHEN** rows have line_totals 100 and 300
- **THEN** the top-level total shows 400

### Requirement: Submit validates and produces patch

On submit, the form SHALL:

1. Run validators for all visible non-derived fields
2. If any error: prevent submit, focus the first errored field, show toast
3. Build a `formPatch` containing only fields whose value differs from `initialFormData`
4. POST `/api/tasks/{id}/submit` with the patch + decision (Approval kind)

#### Scenario: Validation blocks submit

- **GIVEN** field `days` is required and currently empty
- **WHEN** the user clicks Submit
- **THEN** the form shows the error on `days`, focuses the input, and does NOT call the API

#### Scenario: Successful submit redirects

- **WHEN** the form submits successfully
- **THEN** the user is redirected to either the next pending task in the same instance, or to `/`

### Requirement: Local-storage draft auto-save

The form SHALL auto-save the current values to local storage every 500 ms (debounced) under key `bpm_task_draft_{taskId}`. On mount, the form SHALL pre-fill from local storage if present (with a "Restore draft?" prompt). On successful submit, the draft entry SHALL be cleared.

#### Scenario: Draft persists across browser refresh

- **GIVEN** Wilson is filling a form and has entered partial data
- **WHEN** he reloads the page
- **THEN** the form prompts "Restore draft?" and on confirm pre-fills the entered values

#### Scenario: Submit clears draft

- **GIVEN** a draft exists in local storage
- **WHEN** the form submits successfully
- **THEN** the draft entry is removed from local storage
