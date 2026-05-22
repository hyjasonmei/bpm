> **Status: superseded 2026-05-22.** The flowcook MVP picks a
> different shape — chef writes a bespoke per-flow React component
> from the spec instead of a generic `<DynamicForm spec={...} />`
> runtime interpreting the spec at render time. See
> `archive/2026-05-22-flowcook-mvp-chef-bootstrap/proposal.md` for
> the new approach. The "AI engineer writes you a working app" sell
> is stronger than "AI engineer writes a JSON the renderer
> interprets", so this proposal is retired.

## Why

Today every flow has a hand-coded React form (`LeaveForm.tsx`, `GEEForm.tsx`, ...). Adding a new flow requires writing a new form component. This breaks the "spec is the source of truth" promise: when an admin edits the spec to add a field, no form picks it up automatically.

Real product needs **spec-driven dynamic form rendering**: read the userTask's `fields[]` from the spec snapshot, render appropriate input controls, validate on submit, attach files via the existing storage capability, evaluate `conditional` and `validator` and `derivedFrom` expressions live as the user types.

This change is rule-based (not AI). Given a `FormField`, it deterministically picks a renderer:

- `text` → `<input type="text">`
- `textarea` → `<textarea>`
- `number` → `<input type="number">`
- `date` / `daterange` → date picker
- `select` / `multiselect` → dropdown
- `file` → `FileUploadField` from `add-file-storage`
- `repeater` → table with add/remove rows; each row has nested fields per `subFields`
- `derived` → read-only computed display
- `user_picker` → autocomplete against `/api/users?active=true`

The 9 hand-coded mocks stay for the demo, but new flows use the dynamic renderer.

## What Changes

### Form runtime (NEW capability `bpm-form-runtime`)

**Component** `<DynamicForm>`:

- Props: `userTaskSpec` (the relevant userTask from instance.SpecSnapshotJson), `initialFormData`, `onSubmit(patch)`, `mode: 'fill' | 'review'`, `taskId`
- Renders fields in declaration order
- For each field, picks a renderer from a registry keyed by `field.type`
- Manages local state per field; submits a patch on form-submit
- Live evaluates `conditional` (show/hide), `validator` (per-field error), `derivedFrom` (computed value) using the JS-side CEL evaluator
- Handles repeater: `<RepeaterFieldEditor>` with sub-table; each row independently captured

**Field renderers** (one per FieldType in `bpm-ui/src/components/form-runtime/fields/`):

- `TextField`, `TextareaField`, `NumberField`, `DateField`, `DateRangeField`
- `SelectField`, `MultiselectField`
- `FileField` (uses `FileUploadField` from `add-file-storage`)
- `UserPickerField` (autocomplete `/api/users`)
- `DerivedField` (read-only)
- `RepeaterField` (uses `subFields` recursively)

**JS CEL evaluator**:

- `bpm-ui/src/lib/cel-eval.ts`: small CEL implementation for the same grammar subset documented in `add-cel-expressions`
- Same allowed functions: `now`, `businessDaysBetween` (calls API), `sum`, `count`, `length`, etc.
- Critical: uses the same grammar as the backend so behaviors match
- Async helpers (those needing API calls like `businessDaysBetween`) go through a request layer; pure helpers run sync for snappy UX

For now, ship a JS port of CEL (handle the subset our specs need); for fully matching grammar across BE/FE down the road, vendored shared parser would be ideal — out of scope for v1.

### Embedded "form rendering" use cases

The dynamic form is used in **three** contexts:

1. **TaskExecution screen** (NEW) — when a user opens a task from `/api/tasks/mine`, render the task's form for fill / submit. Replaces flow-specific hand-coded forms when migrated.
2. **Process Admin live monitoring** (later) — read-only review of past form submissions.
3. **Wizard "Preview"** — in StepForms, a "Preview as user" button opens a dialog showing the dynamic form rendered against the current spec. Catches issues before GO LIVE.

### Migration path for the 9 mock-up forms

NOT in scope. The 9 hand-coded forms remain as demo artifacts. They show fidelity for a customer presentation. New flows authored via the wizard immediately use the dynamic renderer.

When a customer eventually migrates from a hand-coded form to the dynamic one, that's a per-customer change (delete the file, the dynamic renderer takes over for that flow). v1 ships parallel: hand-coded for the 9 demo flows; dynamic for everything else.

### TaskExecution screen

`bpm-ui/src/screens/TaskExecution.tsx` (NEW):

- Route: `/tasks/{id}` (deep-linked from notification email or in-app inbox)
- Loads task via `getTask(id)`
- Loads instance form data
- Loads userTask spec from instance snapshot (includes the field schema)
- Renders `<DynamicForm>` with editing fields (or `mode='review'` if user is just viewing)
- Submit button triggers `submitTask(id, formPatch)` then redirects to next pending task or home
- For Approval kind: shows Approve / Reject / Return buttons + comment box

### Out of scope (future changes)

- Custom widget plugins (per-tenant custom field types)
- Form layout (sections, columns, tabs) — fields are rendered linearly in declaration order
- Branching forms (different fields based on a sibling field's value beyond `conditional` show/hide) — covered by spec-level userTask split
- Print-friendly view — a future PDF export change handles this
- Multi-step single-task forms (a userTask with stepped fields) — model as multiple userTasks
- AI-generated custom React components — the V8 isolate / Generative UI direction explicitly deferred per inovation_idea.md §3.6
- Image rotation / cropping in image fields
- "Review and revise" mode (form pre-filled with another instance's data)

## Capabilities

### New Capabilities

- `bpm-form-runtime` — `<DynamicForm>` component, field renderer registry, JS-side CEL evaluator (subset), TaskExecution screen, request-time field-validation hooks.

### Modified Capabilities

- `bpm-form-stepper` — wizard's StepForms gains a "Preview as user" button that opens `<DynamicForm>` rendered against current draft.
- `bpm-process-runtime` — no API change; this proposal is consumer of the existing task endpoints.

## Impact

- **bpm-ui/src/components/form-runtime/DynamicForm.tsx**: orchestrator
- **bpm-ui/src/components/form-runtime/fields/**: 11 field components
- **bpm-ui/src/lib/cel-eval.ts**: JS CEL evaluator (subset)
- **bpm-ui/src/lib/form-runtime.ts**: helpers (build initial state, apply derived values, validate, build patch)
- **bpm-ui/src/screens/TaskExecution.tsx**: new screen
- **bpm-ui/src/screens/onboarding/steps/StepForms.tsx**: "Preview as user" button + dialog mounting DynamicForm
- **bpm-ui/src/components/AppLayout.tsx**: route registration for `/tasks/{id}`
- **No backend changes** beyond endpoints already shipped in `add-process-runtime`
- **No NuGet changes**
- **NPM dependencies**: lightweight CEL JS lib (or hand-rolled subset using already-present TS); date picker (`react-day-picker` or use existing date inputs); we'll evaluate a JS CEL impl like `cel-js` if available
- **Demo guard**: 9 mock-up forms in `bpm-ui/src/screens/forms/*.tsx` remain untouched; new TaskExecution screen is for spec-driven flows
