> **Status: superseded 2026-05-22.** Repeater support landed in a
> different shape: instead of adding `'repeater'` to `FieldType` with
> nested `subFields`, the Tier 2 form-layout work (Phase B commit
> `7e7986f`) introduced `FormRepeater` as a `LayoutChild` kind with
> its own `itemFields[]` + `itemLayout[]` namespace and optional
> `totals[]` CEL aggregations. The new shape is documented in
> `openspec/specs/flowcook-wizard/spec.md` under "Step 2 (FORMS)
> carries a Tier 1 + Tier 2 layout tree". Keep this proposal archived
> as the design rationale; do not implement.

## Why

The current `FormField.type` enum is `text | textarea | number | date | daterange | select | multiselect | file | user_picker | derived` — flat scalars. Real flows need to capture **a variable-length list of structured rows**: each expense line has its own category / amount / description / receipt; each purchase order item has its own SKU / quantity / unit price / spec.

5 of the 9 mock-up flows the partner brought require this:

| Flow | Repeating row content |
|---|---|
| GEE (員工費用) | category, amount, description, receipt file |
| GEV (廠商費用) | category, vendor, amount, description, invoice file |
| APE (預支費用) | category, amount, description, expected expense date |
| HWP (硬體採購) | item spec, model, quantity, unit price, link |
| ITPR (IT 採購) | software/service name, edition, license count, vendor |

Today the wizard's `PURCHASE_PRESET` works around this with a single `textarea` field named `items` containing `"A4 影印紙 x 50 包\n原子筆 x 100 支"`. That:

- Loses structure — the generated C# entity can only store the freetext blob, not query individual rows
- Breaks cross-row computation (e.g., total = sum of `amount` across rows; today derived can't reach into a freetext list)
- Defeats validation (each row's "amount" should be a number; freetext can't enforce)
- Fails the spec → code pipeline contract: Claude Code generating the form has nothing structured to bind a `<table>` UI against

This change adds a `repeater` FieldType — a field whose value is an array of objects, each shaped by a nested `subFields` schema. The wizard's `StepForms` gains an "add nested field" affordance; the form runtime renders a table with add/remove row buttons.

## What Changes

### Spec schema (`spec_schema.md` §2.3)

Extend `FieldType` and `FormField`:

```typescript
type FieldType =
  | 'text' | 'textarea' | 'number' | 'date' | 'daterange'
  | 'select' | 'multiselect' | 'file' | 'user_picker' | 'derived'
  | 'repeater'   // NEW — array of objects shaped by subFields

type FormField = {
  id: string
  label: { 'zh-TW': string; 'en'?: string }
  type: FieldType
  options?: { value: string; label: string }[]
  required: boolean
  conditional?: string
  validator?: string
  default?: any
  hint?: { 'zh-TW': string; 'en'?: string }
  derivedFrom?: string

  // NEW — only valid when type = 'repeater'
  subFields?: FormField[]      // schema for each row; recursive (capped at 1 level deep — no nested repeaters in v1.3)
  minItems?: number            // optional, default 0
  maxItems?: number            // optional, default 100
  rowSummary?: string          // optional template like "{{category}} — {{amount}} TWD" for the collapsed row label
}
```

Validator rules:

- `subFields` SHALL be present and non-empty when `type = 'repeater'`; ignored otherwise.
- A repeater MAY NOT contain another repeater in its `subFields` (v1.3 caps depth at 1 — mirrors conditional's nesting cap).
- Each `subFields[i]` is a normal `FormField` and validates recursively (sans the no-nested-repeater rule).
- `minItems` ≥ 0; `maxItems` ≥ 1; `minItems ≤ maxItems`.
- `derived` fields inside `subFields` MAY reference sibling sub-fields by id (`derivedFrom: "quantity * unitPrice"`).
- A top-level `derived` field MAY reference a repeater's aggregate via the syntax `sum(items.amount)` / `count(items)` / `avg(items.unitPrice)` (deferred — see §6 of design.md).

### Backend domain (`bpm-svc`)

This change defines the *spec field type* but does not yet generate runtime form storage. Concretely:

- `Bpm.Domain.Spec.FormFieldSpec` record gains `IReadOnlyList<FormFieldSpec>? SubFields`, `int? MinItems`, `int? MaxItems`, `string? RowSummary`
- `FormFieldSpec.Type` accepts the new `"repeater"` value
- `Bpm.Application.Spec.FormFieldValidator` adds the rules above
- The C# spec → form generator (Phase B agent territory) will know to produce table UI when it sees `type = 'repeater'`; that's documented in `prompt_template_v1.md` but not implemented in this change

Persistence-wise this change is read-only against existing tables. Spec JSON in `specs-incoming/` carries the new shape.

### Wizard UI (`bpm-form-stepper`)

`StepForms` field editor gains repeater support:

- Type dropdown adds "重複列 (Repeater)" option
- When `type = 'repeater'` is selected, the row expands inline to show:
  - `minItems` / `maxItems` numeric inputs
  - `rowSummary` template input (with field-id autocomplete)
  - "編輯子欄位" button → opens nested mini-editor showing the subFields list, with the same field editor recursively (depth-capped at 1)
- The mini-editor disallows adding another repeater inside (greyed-out option with tooltip "v1.3 不支援巢狀 repeater")

### Sample specs

Update `purchase_v1.json` to use a real repeater for `items` (replacing the workaround textarea). Add new samples for the GEE / HWP shapes:

- `expense_employee_v1.json`: GEE flow with a repeater field `expense_items` carrying `{ category: select, amount: number, description: textarea, receipt: file }`
- `hardware_purchase_v1.json`: HWP flow with a repeater `hw_items` carrying `{ spec: textarea, model: text, quantity: number, unit_price: number, link: text, line_total: derived = quantity * unit_price }`

### Form rendering (deferred)

The actual `FormShell.tsx` / runtime form components for repeater rendering are out of scope here — they belong to the Phase B agent generation contract. This change only ensures:

- The spec describes repeaters precisely enough that an agent can generate a working form
- The wizard captures repeater shapes correctly
- Sample specs exercise the new shape

The 9 mock-up form components in `bpm-ui/src/screens/forms/*` SHALL NOT be modified — they continue to power the evening demo. (`GEEForm.tsx` already hand-rolls a table for expense items; this change does not refactor it. The spec describes the shape that *would* generate that table.)

### Out of scope (future changes)

- Nested repeaters (depth > 1)
- Cross-row aggregates in derived (`sum`, `count`, `avg`) — documented in design.md but implementation deferred
- Form rendering at runtime (Phase B codegen)
- Validation expressions inside repeater rows (e.g., "row's amount must be ≤ category's limit")
- Repeater UX features like drag-reorder, paste-from-CSV — captured in design.md as future work

## Capabilities

### Modified Capabilities

- `bpm-form-stepper`: FieldType union extended with `repeater`; FormField shape extended with `subFields` / `minItems` / `maxItems` / `rowSummary`; wizard editor renders the recursive sub-field UI; spec_schema.md doc updated.

### New Capabilities

None — purely an extension of `bpm-form-stepper`. (FormField belongs naturally to the form-stepper capability since the wizard's StepForms is the only place that authors them today; if a separate `bpm-form-spec` capability is created later, split during refactor.)

## Impact

- **spec_schema.md**: §2.3 (UserTask / FormField) extended with `repeater` type; new sub-section "Repeater fields" documenting subFields / minItems / maxItems / rowSummary
- **bpm-ui/src/lib/onboarding.ts**: `FieldType` adds `'repeater'`; `FormField` adds `subFields?` / `minItems?` / `maxItems?` / `rowSummary?`; validator updated
- **bpm-ui/src/screens/onboarding/steps/StepForms.tsx**: type dropdown adds option; per-row inline expansion shows subField mini-editor when repeater
- **bpm-svc/src/Domain/Spec/FormFieldSpec.cs**: new optional properties
- **bpm-svc/src/Application/Spec/FormFieldValidator.cs**: rules for repeater shape; depth-1 cap; per-subField recursive validation
- **sample_specs/purchase_v1.json**: items field upgraded to repeater
- **sample_specs/expense_employee_v1.json**: NEW (GEE shape)
- **sample_specs/hardware_purchase_v1.json**: NEW (HWP shape)
- **prompt_template_v1.md**: new section on repeater rendering — the agent must generate `<table>` UI with row-add/row-remove controls when seeing `type = 'repeater'`
- **No DB migration**
- **No breaking change to running 9-flow demo** — `bpm-ui/src/screens/forms/*.tsx`, `Home.tsx`, `Search.tsx`, `Report.tsx`, `lib/workflow.ts` not modified
- **No new dependencies**
