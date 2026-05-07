## ADDED Requirements

### Requirement: Wizard "Preview as user" mounts DynamicForm

Each userTask card in `StepForms` SHALL include a "Preview as user" button that opens a modal hosting `<DynamicForm>` rendered against the current draft's userTask spec. The user SHALL be able to interact with the form (fill fields, see conditional show/hide, see derived values, see validation errors). Submission within preview SHALL be a no-op (toasts "Preview only — not submitted") so authors can sanity-check without side effects.

#### Scenario: Preview shows live conditional

- **GIVEN** the wizard draft has cert field with `conditional = "leave_type === '病假'"`
- **WHEN** the user clicks Preview, then sets leave_type = 病假
- **THEN** the cert field appears in the preview without leaving the wizard

#### Scenario: Preview submit does not actually submit

- **WHEN** the user clicks Submit inside the preview dialog
- **THEN** a toast appears "Preview only — not submitted"; no API call is made
