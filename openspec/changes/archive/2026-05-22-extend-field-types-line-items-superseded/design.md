# Design notes

## 1. Why a single `repeater` type, not separate `table` / `list` / `grid`

We considered three alternatives:

- `table` with explicit columns (database-flavored)
- `list` with item template (UI-flavored)
- `grid` with row/col schema (spreadsheet-flavored)

All three describe the same underlying shape: an array of objects. Naming differences would invite the wizard to render three slightly different editors and the AI to fabricate the wrong one. **One concept, one name** — `repeater` (borrowed from React community vocabulary, reasonably language-neutral) covers expense lines, purchase items, and any future "row table" need.

If a customer needs spreadsheet-style affordances (cell formulas, freeze panes), that's a separate UI concern handled by an opt-in renderer flag, not a separate field type.

## 2. Why depth-1 cap on nested repeaters

Real flows occasionally seem to need nested repeaters ("each purchase item has a list of approvers", "each expense has a list of receipts"). But:

- Each surfaced "nested repeater" use case maps cleanly to a flat repeater + a join key. "Expense → receipts" becomes "expense_items[i].receipts: file (multi-file upload)" — same data, simpler shape.
- True n-deep nesting is a tree shape that isn't BPM's domain — it's a CMS or doc-builder concern.
- Codegen complexity grows quadratically with depth (form, validator, controller, migration); cap-at-1 keeps the codegen contract tractable.

We can lift the cap later if a customer flow can't be modeled flat. Document escape hatch in the prompt template.

## 3. derived fields inside repeater rows

A row's total (`quantity * unit_price`) is a per-row derived. Easy:

```jsonc
{
  "id": "line_total",
  "type": "derived",
  "derivedFrom": "quantity * unit_price"
}
```

Lives in subFields. Resolves at form-render time using the row's other sub-fields.

A flow's grand-total (sum of `line_total` across rows) is harder — it needs aggregate vocabulary:

```jsonc
{
  "id": "grand_total",
  "type": "derived",
  "derivedFrom": "sum(expense_items.line_total)"
}
```

This requires the derived expression evaluator to understand `sum()`, `count()`, `avg()`, `min()`, `max()`. **Deferred** — this change documents the syntax in `prompt_template_v1.md` but does not implement the evaluator. Sample specs can write the expression; runtime evaluation lands in a follow-up.

In the interim, customers needing grand totals can add a regular `number` field that the assignee fills manually (or copy-paste from the table). Acceptable for Phase A POC; feels janky and we'll fix it.

## 4. Validation inside rows

Each subField validates the same way a top-level field does. Future cross-cell validation ("row's amount ≤ category's per-row cap") needs an expression that references *this row's* sibling values. Tentative syntax:

```jsonc
{
  "id": "amount",
  "type": "number",
  "validator": "value <= category_limits[this.category]"
}
```

— deferred to whenever a customer flow demands it. The current change ships only single-field validators (`value > 0 && value <= 999999`). The `this.<other-field>` syntax is reserved for future use.

## 5. UX choices for the wizard

When a user picks `type = 'repeater'`, the field row expands inline:

```
┌─── Field: expense_items [Repeater] ─── ─ ───┐
│ Label (zh-TW): 費用明細                       │
│ Required: ☑                                  │
│ Min items: 1   Max items: 20                 │
│ Row summary: {{category}} — {{amount}}       │
│                                              │
│ ┌─ Sub-fields ──────────────────────────┐   │
│ │  + Add sub-field                       │   │
│ │  ─────                                 │   │
│ │  category : select [edit]              │   │
│ │  amount   : number [edit]              │   │
│ │  description : textarea [edit]         │   │
│ │  receipt  : file [edit]                │   │
│ └────────────────────────────────────────┘   │
└──────────────────────────────────────────────┘
```

The sub-fields editor reuses the same component as the parent — only the type dropdown excludes `repeater`. This keeps mental load low for the user.

## 6. What about choosing column widths / display order?

For Phase A: subFields in declaration order, equal-width columns, all visible. No customization.

If a customer wants narrower columns / hidden-by-default fields, that's a UI annotation we add to the FormField shape later (`displayWidth`, `hiddenByDefault`) — not a v1.3 concern. Document in the prompt template that the agent should default to "all columns visible, equal width."

## 7. Data shape on the wire

Submitted form values for a repeater field arrive as:

```json
{
  "expense_items": [
    { "category": "餐費",   "amount": 350,  "description": "客戶餐敘", "receipt": "file_id_1" },
    { "category": "計程車", "amount": 280,  "description": "回程",     "receipt": "file_id_2" }
  ]
}
```

— no extra wrapper, no `__type__` discriminator. The form's spec is sufficient for the renderer to know how to parse this.

## 8. Why Phase A doesn't ship the rendering

`StepForms` (wizard) editing a repeater is straightforward — bind controls to the spec.

But `FormShell` / `LeaveForm` etc. *rendering* a repeater on the user-facing screen requires:

- A new `<RepeaterField>` component
- Add-row / remove-row buttons
- Proper React key handling
- Validation rollup
- Serialization to the submission payload

That's 1-2 weeks of Phase B agent work (or our hand-coding) — and it's wrapped up with the broader Phase B codegen pipeline. This change scopes only the spec layer so the AI / wizard / validator know how to *describe* repeaters. Rendering arrives with the codegen pipeline.

For the demo: the existing hand-coded `GEEForm.tsx` already renders an expense-line table — this change does not touch it. The spec describes the shape that the codegen *would* produce; the demo continues to show hand-coded fidelity.

## 9. Open questions

- Should the wizard let the user *re-order* sub-fields after creation? Probably yes — drag handle, just like top-level fields. Add as a polish item.
- Should `rowSummary` support more than simple `{{field}}` templating (e.g., conditional formatting)? Probably not for v1.3 — start with literal substitution.
- How do we handle a repeater inside a `conditional` form section (only show the repeater when X is selected)? Already handled by the existing `conditional` field property — it composes naturally. Confirm with a test.
- File upload inside a repeater (each row has its own receipt file): does the file upload component need to know it's inside a repeater? Probably yes — temp-file IDs need to be scoped per-row to avoid cross-row collision. Document this as an implementation note for the codegen.
