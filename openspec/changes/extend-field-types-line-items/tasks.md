# Tasks

## 1. Spec schema docs

- [ ] 1.1 Update `spec_schema.md` §2.3: add `'repeater'` to `FieldType`; add `subFields`, `minItems`, `maxItems`, `rowSummary` properties to `FormField`
- [ ] 1.2 Add §2.3.1 sub-section "Repeater fields" with: cardinality rules (subFields required when repeater), nesting cap (no repeaters in subFields), worked example
- [ ] 1.3 Add a complete worked example to §3 (full sample spec) showing a repeater with derived row total
- [ ] 1.4 Update §4 (schema evolution): bump to v1.3 (after the v1.2 from the userTask change)
- [ ] 1.5 Update §6 (review checklist): add bullet "every repeater has subFields, none of which are themselves repeaters"

## 2. Backend domain types

- [ ] 2.1 Add to `bpm-svc/src/Domain/Spec/FormFieldSpec.cs`: `IReadOnlyList<FormFieldSpec>? SubFields`, `int? MinItems`, `int? MaxItems`, `string? RowSummary`
- [ ] 2.2 Update `FormFieldSpec.Type` accepted values to include `"repeater"` (assuming current type is a string column not enum; if enum, add the variant)
- [ ] 2.3 Round-trip serialization tests: a FormField with `type = "repeater"` and 4 subFields round-trips through `JsonSerializer` losslessly

## 3. Backend validation

- [ ] 3.1 Create or extend `bpm-svc/src/Application/Spec/FormFieldValidator.cs`:
  - When `type = "repeater"`: `subFields` non-null and non-empty
  - Each subField recurses through `FormFieldValidator` with a "no nested repeaters" flag
  - When `type != "repeater"`: `subFields` MUST be null/absent (reject if present)
  - `minItems` ≥ 0; `maxItems` ≥ 1; `minItems ≤ maxItems` if both present
  - `rowSummary` template references only valid sub-field IDs (parse `{{...}}` placeholders)
- [ ] 3.2 Tests: depth-2 repeater rejected; non-repeater with subFields rejected; valid repeater with derived sub-field accepted; minItems > maxItems rejected; rowSummary referencing unknown field rejected

## 4. Frontend — types

- [ ] 4.1 Update `bpm-ui/src/lib/onboarding.ts`:
  - Extend `FieldType` union to include `'repeater'`
  - Extend `FormField` interface with optional `subFields`, `minItems`, `maxItems`, `rowSummary`
  - Update `FormFieldValidator` (frontend mirror) with the same rules as backend
- [ ] 4.2 Add a TypeScript helper `isRepeaterField(f: FormField): f is FormField & { subFields: FormField[] }` for editor rendering
- [ ] 4.3 Update sample preset (`PURCHASE_PRESET`): change `items` field from textarea to repeater with subFields `[item_name (text), quantity (number), unit_price (number), line_total (derived = quantity * unit_price)]`

## 5. Frontend — wizard

- [ ] 5.1 Update `bpm-ui/src/screens/onboarding/steps/StepForms.tsx`:
  - Extend the type dropdown to include `repeater` (label: 重複列)
  - When a row's `type = 'repeater'`, render a collapsible "子欄位" section inline showing `minItems`/`maxItems`/`rowSummary` inputs and the subFields list
  - subFields list reuses the existing field-row component recursively, with the type dropdown's `repeater` option disabled (greyed) and a tooltip "v1.3 不支援巢狀 repeater"
- [ ] 5.2 Add an `AddSubFieldButton` that creates a new subField with default type=text, label="(unnamed)"
- [ ] 5.3 Add `rowSummary` input with field-id autocomplete (suggest tokens from `subFields[i].id`)
- [ ] 5.4 Verify: the existing field drag-reorder handle (if any) works for sub-fields too; if it doesn't, file as polish but don't block this change

## 6. Sample specs

- [ ] 6.1 Update `sample_specs/purchase_v1.json`: replace `items` textarea with a repeater carrying real subFields; verify the spec validates
- [ ] 6.2 Create `sample_specs/expense_employee_v1.json`: GEE flow with 5 userTasks (apply / approve / confirm / fin_review / close). Apply form has a repeater `expense_items` with `[category (select), amount (number), description (textarea), receipt (file)]` and a top-level number field `total_amount` (Phase A: filled manually; Phase B: derived via `sum(expense_items.amount)`)
- [ ] 6.3 Create `sample_specs/hardware_purchase_v1.json`: HWP flow with apply form's `hw_items` repeater carrying `[spec (textarea), model (text), quantity (number), unit_price (number), line_total (derived = quantity * unit_price)]`
- [ ] 6.4 Verify each new sample passes `FormFieldValidator` cleanly

## 7. Prompt template

- [ ] 7.1 Update `prompt_template_v1.md`:
  - New section: "When the user describes line items / 多筆品項 / a table — emit a `repeater` field"
  - 3 worked examples: simple receipts list / item rows with derived totals / file-upload-per-row
  - Explicit rule: never use `textarea` for structured row data; always emit `repeater` with typed subFields
  - Form-rendering note for codegen: a `repeater` field maps to a `<table>` element with add-row / remove-row buttons; sub-field types map to standard form controls per existing FieldType conventions

## 8. Coverage check vs the 9 mock-up flows

- [ ] 8.1 LEAVE — no repeater needed (single application form)
- [ ] 8.2 GEE / GEV / APE / TEO — apply form has `expense_items` repeater (4 sub-fields); confirm + fin_review forms can show the repeater read-only (no extra spec needed; viewer renders same shape)
- [ ] 8.3 HWP / ITPR — apply form has `hw_items` (HWP) / `software_items` (ITPR) repeater
- [ ] 8.4 TRQ — no repeater needed (single travel request)
- [ ] 8.5 EXTOB — no repeater needed
- [ ] 8.6 Confirm: 5/9 flows now express line items via repeater; the other 4 don't need it; total coverage = full

## 9. End-to-end verification

- [ ] 9.1 `dotnet build bpm-svc.slnx` clean
- [ ] 9.2 All backend unit tests pass
- [ ] 9.3 Boot bpm-ui (`npm run dev`); type-check with `tsc -p tsconfig.app.json`
- [ ] 9.4 In the wizard, build a flow with a repeater field, set 3 sub-fields, save draft, reload — verify the draft round-trips
- [ ] 9.5 Export the draft, manually edit JSON to add a nested repeater, re-import — verify the validator rejects it with "nested repeaters not supported"
- [ ] 9.6 Manual: load `sample_specs/expense_employee_v1.json` into the wizard via import path (or copy into draft localStorage), verify the StepForms renders the repeater editor with the right subFields
- [ ] 9.7 **Demo guard**: `bpm-ui/src/screens/forms/*`, `Home.tsx`, `Search.tsx`, `Report.tsx`, `lib/workflow.ts` are NOT modified — this is a wizard / spec / sample-only change

## 10. Docs + commit

- [ ] 10.1 Update `bpm-svc/CLAUDE.md` with repeater field summary + v1.3 schema bump note
- [ ] 10.2 Commit in chunks (schema docs; backend types + validator; frontend types + validator; wizard editor; samples + prompt; verification)
- [ ] 10.3 Push via GitKraken (Claude does not push to BPM repo)
