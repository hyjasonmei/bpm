# bpm-shell-ui (delta) — BPMN viewer

## ADDED Requirements

### Requirement: Form-side BPMN viewer uses bpmn-js

The "View BPMN" modal opened from `FormShell` SHALL render the spec's
BPMN graph using the `bpmn-js` library. Hand-rolled SVG flowcharts SHALL
NOT be used.

#### Scenario: Create mode renders the spec's BPMN

- **WHEN** the user opens a form in create mode and clicks "View BPMN"
- **THEN** the modal SHALL fetch the spec's BPMN XML via
  `GET /api/spec/{code}?include=bpmn`
- **AND** render it with `bpmn-js`'s NavigatedViewer (pan + zoom)

#### Scenario: Task mode highlights active + completed nodes

- **WHEN** the user opens a task-mode form and clicks "View BPMN"
- **THEN** the modal SHALL fetch the instance's snapshotted BPMN via
  `GET /api/processes/{id}/bpmn-xml`
- **AND** apply a `bpm-active` marker to the current task's `nodeId`
- **AND** apply `bpm-completed` markers to every node id present in
  `TaskHistory` with kind `TaskCompleted`

### Requirement: Lazy-loaded viewer

The `bpmn-js` library SHALL be dynamically imported so users who never
open "View BPMN" do not pay for the ~600 KB of viewer code in their
initial bundle.

## MODIFIED Requirements

### Requirement: ProcessInstance snapshot preserves the BPMN

`ProcessRuntime.StartInstanceAsync` SHALL copy `bundle.bpmnXml` into
`ProcessInstance.BpmnXml` so the viewer stays consistent across spec
edits — the same way `SpecSnapshot` already freezes the rest of the
spec at instance start.

#### Scenario: Spec edit does not retroactively change rendered diagrams

- **GIVEN** an instance was started against spec version N
- **AND** the wizard later publishes spec version N+1 with a different
  BPMN graph
- **WHEN** the user opens "View BPMN" on the older instance
- **THEN** the viewer SHALL render the graph from spec version N (the
  snapshot), not N+1
