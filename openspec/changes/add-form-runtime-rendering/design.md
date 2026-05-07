# Design notes

## 1. Why a registry, not a switch

A registry (`Map<FieldType, FieldComponent>`) lets us:

- Easily add new field types in future
- Test renderers in isolation (each is a pure component given props)
- Override per-tenant (future feature)

A switch in DynamicForm's render loop would work for v1 but inflate the file as types are added.

## 2. Why ship a JS CEL subset, not run server-side eval

Live form interaction (showing/hiding fields, computing totals as user types) requires < 50 ms response. Round-tripping to the API for every keystroke is unacceptable.

We accept the cost: ship a JS CEL evaluator that mirrors the C# implementation's grammar + helper set. The two implementations must stay in sync. Mitigation: a shared test fixture file (JSON of inputs + expected outputs) run by both BE tests and FE tests.

For helpers needing data the FE doesn't have (`businessDaysBetween` needs the calendar), the JS impl falls back to async fetch via `/api/calendars/business-days?start=...&end=...&calendarId=...`. Acceptable as an exception — calendar lookups are rare in form interaction.

## 3. Form state shape

```typescript
type FormState = {
  values: { [fieldId: string]: any }       // current user-input values
  errors: { [fieldId: string]: string }    // current validation errors per field
  visible: { [fieldId: string]: boolean }  // current conditional visibility
  derived: { [fieldId: string]: any }      // current computed values for derived fields
  dirty: Set<string>                        // fields the user touched (for don't-show-error-until-blur UX)
}
```

On every input change:
1. Update `values[changedField] = newValue`
2. Re-evaluate every conditional → update `visible`
3. Re-evaluate every derived → update `derived`
4. Re-evaluate validators on dirty fields → update `errors`
5. Re-render

Order matters: conditional first (because a hidden field's validator should not fire), then derived, then validation.

## 4. Repeater rendering

`<RepeaterField>`:

- Renders a `<table>` (semantic) with header cells from each subField's label
- One row per item in the field's value array
- Each cell uses the appropriate sub-field renderer (recursive call into the registry)
- Add row: appends a new empty item with default values per sub-field
- Remove row: confirms then removes
- min/max items enforced

For `derivedFrom` inside a sub-field (e.g., `line_total = quantity * unit_price`), evaluation is per-row using only that row's other sub-fields as context.

For top-level derived expressions referencing repeater aggregates (`grand_total = sum(items.line_total)`), the evaluator reads the repeater field's array of rows.

## 5. Submit semantics

On submit:

1. Run validators across all visible non-derived fields
2. If any error: prevent submit, focus first errored field, show toast
3. Build `formPatch` from `values` (only fields different from initialFormData)
4. POST `/api/tasks/{id}/submit` with patch + decision (for Approval kind)
5. On success: redirect (next pending task or `/`)
6. On 409 (task already submitted by someone else): refresh, show "this task is already completed" notice

For Approval kind, the form is informational (read-only) plus a comment field plus Approve/Reject/Return buttons.

## 6. TaskExecution screen layout

```
┌─ Header: spec name · case ID · status badge · due date ─┐
│                                                         │
│  ┌─ Sidebar: stepper showing current node + history ─┐ │
│  │ [Started] → [Apply] → [Manager Approve ●] → [HR]  │ │
│  └──────────────────────────────────────────────────┘ │
│                                                         │
│  ┌─ Main: <DynamicForm> for the current task ────────┐ │
│  │   field1: ...                                       │ │
│  │   field2: ...                                       │ │
│  │   ...                                               │ │
│  │   ┌─ For Approval kind: comment + buttons ─┐       │ │
│  │   │ [Approve] [Reject] [Return] [Save draft] │       │ │
│  │   └────────────────────────────────────────┘       │ │
│  └──────────────────────────────────────────────────┘ │
└─────────────────────────────────────────────────────────┘
```

The sidebar reuses the existing Stepper component (already present in `bpm-ui/src/components/Stepper.tsx`).

## 7. Save draft semantics

A `Save draft` button stores the partial form state on the *task itself* — extending the Task entity? Or local-storage only?

Decision: local-storage only for v1. Drafts are per-task, per-browser. If user switches browser, draft is lost. Acceptable for SME; if a customer demands cross-device drafts, add a `Task.DraftFormDataJson` column later.

Local storage key: `bpm_task_draft_{taskId}`. Cleared on submit success.

## 8. Open questions

- **Translation**: field labels are bilingual (`{ 'zh-TW': ..., en: ... }`); render using user's preferred locale (defaults to zh-TW until i18n change ships)
- **Custom validators with API calls**: e.g., "this part number must exist in our SAP". Out of scope; would need an `apiValidator` field hook calling a registered endpoint
- **Performance**: large repeater (100+ rows) might lag; virtualize if measured
- **Accessibility**: focus management, keyboard navigation, ARIA labels — basic level shipped; full WCAG audit later
