## ADDED Requirements

### Requirement: Wizard expression inputs validated against CEL

The wizard's expression input fields (StepDecisions for gateway `condition`; StepForms for FormField `conditional` / `validator` / `derivedFrom`) SHALL provide live (debounced) validation feedback by calling `POST /api/specs/validate-expression`. Each field SHALL show:

- ✓ chip when valid
- ✗ chip with parse error message when invalid
- Loading indicator while the validate request is in flight

The submit / next-step button SHALL remain enabled even when an expression is invalid (the wizard does not block authoring), but the spec validator SHALL reject the spec at GO LIVE if any expression is invalid.

#### Scenario: Live ✓ chip on valid expression

- **WHEN** the user types `"days >= 7"` in a gateway condition input
- **AND** the validate endpoint returns `{ valid: true }`
- **THEN** the input shows a green ✓ chip

#### Scenario: Live ✗ chip on invalid expression

- **WHEN** the user types `"days >== 7"` (typo)
- **AND** the validate endpoint returns `{ valid: false, errors: [...] }`
- **THEN** the input shows a red ✗ chip with the parse error message tooltip

#### Scenario: Spec rejected at GO LIVE if invalid expression remains

- **GIVEN** a draft with an invalid `derivedFrom` expression
- **WHEN** the user clicks GO LIVE → submits the spec
- **THEN** the export validation fails with the broken expression's location reported back to the user
