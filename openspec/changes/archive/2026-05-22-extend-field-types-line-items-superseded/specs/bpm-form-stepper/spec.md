## ADDED Requirements

### Requirement: FormField supports repeater type

The `FormField.type` enum SHALL include the value `'repeater'`. A repeater field represents a variable-length array of structured rows; each row is shaped by the field's `subFields` schema. The validator MUST reject any FormField where `type = 'repeater'` but `subFields` is missing or empty, or where `subFields` is non-empty but `type != 'repeater'`.

#### Scenario: Valid repeater field

- **WHEN** a FormField has `{ id: "expense_items", type: "repeater", subFields: [{ id: "amount", type: "number", required: true }] }`
- **THEN** the validator accepts it

#### Scenario: Repeater without subFields rejected

- **WHEN** a FormField has `{ id: "x", type: "repeater" }` with no `subFields`
- **THEN** the validator rejects it with "repeater field requires non-empty subFields"

#### Scenario: Non-repeater with subFields rejected

- **WHEN** a FormField has `{ id: "x", type: "text", subFields: [...] }`
- **THEN** the validator rejects it with "subFields valid only for repeater type"

### Requirement: subFields cannot be repeaters

The `subFields` of a repeater FormField SHALL NOT contain another field with `type = 'repeater'`. Nesting depth is capped at 1 in v1.3. The validator MUST reject any nested repeater regardless of depth.

#### Scenario: Direct nested repeater rejected

- **WHEN** a FormField has subFields `[{ id: "inner", type: "repeater", subFields: [...] }]`
- **THEN** the validator rejects it with "nested repeaters not supported (v1.3)"

### Requirement: Repeater carries minItems / maxItems / rowSummary

A repeater FormField SHALL accept three optional configuration fields:

- `minItems` (integer, optional, ≥ 0, default 0) — minimum number of rows the user must enter
- `maxItems` (integer, optional, ≥ 1, default 100) — upper bound to prevent abuse
- `rowSummary` (string, optional) — Mustache-style template using sub-field IDs in `{{...}}`, used as the collapsed row label in the wizard and runtime UI

The validator MUST reject `minItems > maxItems` and MUST reject `rowSummary` placeholders that reference IDs not in `subFields`.

#### Scenario: Valid bounds

- **WHEN** a repeater has `{ minItems: 1, maxItems: 20 }`
- **THEN** the validator accepts it

#### Scenario: minItems exceeds maxItems

- **WHEN** a repeater has `{ minItems: 5, maxItems: 3 }`
- **THEN** the validator rejects it

#### Scenario: rowSummary references unknown field

- **WHEN** a repeater has subFields with id `[category, amount]` and `rowSummary = "{{vendor}} — {{amount}}"`
- **THEN** the validator rejects it with "rowSummary references unknown sub-field 'vendor'"

#### Scenario: Default bounds when omitted

- **WHEN** a repeater carries no `minItems` or `maxItems`
- **THEN** the parsed value carries `minItems = 0`, `maxItems = 100`

### Requirement: StepForms wizard supports repeater editing

`StepForms` SHALL include `'repeater'` in the field-type dropdown with the label "重複列". When a user selects this type, the wizard SHALL expand the field row inline to expose:

- numeric inputs for `minItems` / `maxItems`
- a `rowSummary` text input with autocomplete suggesting `{{<sub-field-id>}}` tokens
- an "編輯子欄位" affordance opening a recursive sub-field editor (same UI as top-level fields, but with the `repeater` type option disabled)

#### Scenario: Selecting repeater shows subField editor

- **WHEN** the user changes a field's type from `text` to `repeater`
- **THEN** the row expands to show subField editor, defaulting to one auto-generated subField (`{ id: "field_1", type: "text" }`)

#### Scenario: Cannot pick repeater inside subFields

- **WHEN** the user opens the type dropdown inside a sub-field editor
- **THEN** the `repeater` option is disabled with tooltip "v1.3 不支援巢狀 repeater"

### Requirement: Demo screens preserved

The mock-up flow screens (`bpm-ui/src/screens/forms/*.tsx`, `Home.tsx`, `Search.tsx`, `Report.tsx`, `lib/workflow.ts`) SHALL NOT be modified by this change. The 9 mock-up flows continue to render identically. This change only updates the wizard, the spec layer, and sample specs.

#### Scenario: Demo screens unchanged

- **WHEN** the change is applied
- **AND** a reviewer opens any of the 9 mock-up flows
- **THEN** the visuals are byte-identical to the pre-change state
