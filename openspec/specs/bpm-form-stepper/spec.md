# bpm-form-stepper Specification

## Purpose
TBD - created by archiving change add-process-runtime. Update Purpose after archive.
## Requirements
### Requirement: Wizard-authored spec is the runtime input

The system SHALL clarify that the spec.json produced by the onboarding wizard (Steps 1-9: SOURCE, STRUCTURE, FORMS, DECISIONS, APPROVERS, NOTIFY, SLA, TEST, GO LIVE) is the input the ProcessRuntime consumes via `StartInstanceAsync`. Once an instance starts, the spec is captured in `ProcessInstance.SpecSnapshotJson` and subsequent edits do not affect that instance.

This is a documentation requirement — no UI change in this proposal — but it formalizes the contract: every field captured by the wizard (FlowGraph, UserTask.assignee, UserTask.fields, Decision.condition, Approval.approver, Notification.trigger/channel/recipients/template, NodeSLA) is consumed by the runtime exactly as specified.

#### Scenario: Wizard output drives runtime

- **GIVEN** the wizard exports a draft for LEAVE flow
- **AND** the spec is placed in `specs-incoming/<tenant>/LEAVE.json`
- **WHEN** a user posts `POST /api/processes` with `spec_code = "LEAVE"`
- **THEN** the runtime loads that spec, snapshots it into the new instance, and begins executing per the wizard-defined nodes / edges / forms / approvers / notifications / SLAs

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

