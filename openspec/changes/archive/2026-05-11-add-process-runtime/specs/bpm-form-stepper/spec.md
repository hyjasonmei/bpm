## ADDED Requirements

### Requirement: Wizard-authored spec is the runtime input

The system SHALL clarify that the spec.json produced by the onboarding wizard (Steps 1-9: SOURCE, STRUCTURE, FORMS, DECISIONS, APPROVERS, NOTIFY, SLA, TEST, GO LIVE) is the input the ProcessRuntime consumes via `StartInstanceAsync`. Once an instance starts, the spec is captured in `ProcessInstance.SpecSnapshotJson` and subsequent edits do not affect that instance.

This is a documentation requirement — no UI change in this proposal — but it formalizes the contract: every field captured by the wizard (FlowGraph, UserTask.assignee, UserTask.fields, Decision.condition, Approval.approver, Notification.trigger/channel/recipients/template, NodeSLA) is consumed by the runtime exactly as specified.

#### Scenario: Wizard output drives runtime

- **GIVEN** the wizard exports a draft for LEAVE flow
- **AND** the spec is placed in `specs-incoming/<tenant>/LEAVE.json`
- **WHEN** a user posts `POST /api/processes` with `spec_code = "LEAVE"`
- **THEN** the runtime loads that spec, snapshots it into the new instance, and begins executing per the wizard-defined nodes / edges / forms / approvers / notifications / SLAs
