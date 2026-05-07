## ADDED Requirements

### Requirement: StepForms file field offers extension and size constraints

When a FormField has `type = 'file'`, the wizard's StepForms editor SHALL expose three additional controls:

- **Multi-file toggle** — sets `field.multiple = true|false`; when true, the field's value is an array of file ids
- **Allowed extensions** — preset list (PDF only / images only / documents / custom comma-separated); persisted as `field.accept` (e.g., `".pdf,.jpg,.png"`)
- **Max size (MB)** — numeric input; default 10; persisted as `field.maxSizeMb`

These controls inform both the runtime upload validation (server enforces) and the future form-runtime renderer's client-side checks.

#### Scenario: Single PDF restriction

- **WHEN** the wizard user creates a `quote_file` field with `accept = ".pdf"` and `maxSizeMb = 20`
- **THEN** the spec.json carries those values; the upload endpoint rejects non-PDF and >20MB submissions for that field

#### Scenario: Multi-file repeater pairing

- **WHEN** the wizard user toggles multi-file on a field inside a repeater (e.g., `expense_items.receipts`)
- **THEN** the field value is an array of file ids per repeater row
