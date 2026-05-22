# Tasks

## 1. JS CEL evaluator

- [ ] 1.1 Create `bpm-ui/src/lib/cel-eval.ts` implementing CEL subset (grammar mirroring backend)
- [ ] 1.2 Helpers: `now`, `sum`, `count`, `length`, `contains`, `startsWith`, `endsWith`, `lower`, `upper` (sync)
- [ ] 1.3 Async helpers: `businessDaysBetween` calls `/api/calendars/business-days` with caching
- [ ] 1.4 Tests using shared fixture file `bpm-ui/tests/fixtures/cel-cases.json` (also consumed by C# tests for cross-impl parity)
- [ ] 1.5 Document grammar in `bpm-ui/src/lib/cel-eval.md`

## 2. Field renderers

- [ ] 2.1 Create `bpm-ui/src/components/form-runtime/fields/TextField.tsx`
- [ ] 2.2 `TextareaField.tsx`
- [ ] 2.3 `NumberField.tsx` (numeric input, locale-aware decimal)
- [ ] 2.4 `DateField.tsx` (single date)
- [ ] 2.5 `DateRangeField.tsx`
- [ ] 2.6 `SelectField.tsx`, `MultiselectField.tsx`
- [ ] 2.7 `FileField.tsx` wrapping FileUploadField from `add-file-storage`
- [ ] 2.8 `UserPickerField.tsx` autocomplete on `/api/users`
- [ ] 2.9 `DerivedField.tsx` (read-only display)
- [ ] 2.10 `RepeaterField.tsx` (table with sub-field rendering recursively)
- [ ] 2.11 Each renderer accepts: `field` spec, `value`, `onChange`, `error`, `disabled`

## 3. DynamicForm orchestrator

- [ ] 3.1 Create `bpm-ui/src/components/form-runtime/DynamicForm.tsx`
- [ ] 3.2 Build initial state from spec + initialFormData
- [ ] 3.3 Re-evaluate visible / derived / errors on every change
- [ ] 3.4 Render visible fields in declaration order using registry
- [ ] 3.5 Submit: validate all visible non-derived fields; build patch; POST
- [ ] 3.6 Local-storage draft auto-save on each change (debounced 500ms)

## 4. Field-renderer registry

- [ ] 4.1 Create `bpm-ui/src/components/form-runtime/registry.ts`: `Map<FieldType, FieldComponent>`
- [ ] 4.2 Default registry registers all 11 renderers
- [ ] 4.3 `getRenderer(fieldType): FieldComponent | null`; null fallback to a generic "unsupported field type" placeholder

## 5. TaskExecution screen

- [ ] 5.1 Create `bpm-ui/src/screens/TaskExecution.tsx`
- [ ] 5.2 Route: `/tasks/:id`
- [ ] 5.3 Loads task + instance + userTask spec
- [ ] 5.4 Renders sidebar Stepper showing flow nodes + current
- [ ] 5.5 Mounts DynamicForm for the userTask
- [ ] 5.6 For Approval kind: comment textarea + 3 buttons
- [ ] 5.7 Submit handles success / 409 / 403
- [ ] 5.8 Add route registration to `AppLayout.tsx`

## 6. Wizard "Preview as user"

- [ ] 6.1 Update `bpm-ui/src/screens/onboarding/steps/StepForms.tsx`:
  - Each userTask card gets a "Preview as user" button
  - Opens a dialog with `<DynamicForm>` rendered against the current draft, sample form data
  - User can fill the preview to see what their flow user will see; preview submission is a no-op (toast "preview only")

## 7. Tests

- [ ] 7.1 Unit tests per renderer (input → output state)
- [ ] 7.2 Integration test on DynamicForm: spec with 5 fields, conditional, validator, derived → state evolves correctly with user input
- [ ] 7.3 Repeater test: add row, remove row, reorder, derived per-row, derived top-level aggregating across rows
- [ ] 7.4 CEL parity test: shared fixtures pass on both BE C# and FE TS
- [ ] 7.5 TaskExecution integration test: load task, submit, navigate

## 8. End-to-end verification

- [ ] 8.1 Boot stack; create a flow via wizard with 4-5 fields including conditional + derived; GO LIVE
- [ ] 8.2 Start an instance; navigate to `/tasks/{id}`; verify DynamicForm renders correctly
- [ ] 8.3 Fill fields; verify visibility / derived behavior matches spec
- [ ] 8.4 Submit; verify task completes, next task spawned
- [ ] 8.5 Test repeater field: add 3 expense items; verify total derived correct
- [ ] 8.6 **Demo guard**: 9 mock-up forms (`forms/*.tsx`), Home, Search, Report, lib/workflow.ts NOT modified

## 9. Commit

- [ ] 9.1 Commit in chunks (CEL JS; renderers; DynamicForm; TaskExecution; wizard preview; tests)
- [ ] 9.2 Push via GitKraken
