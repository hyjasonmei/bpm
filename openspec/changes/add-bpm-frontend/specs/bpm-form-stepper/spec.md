## ADDED Requirements

### Requirement: Step config is single source of truth

Each form's workflow SHALL be defined once as a constant in `src/lib/workflow.ts` of shape `{ code, label, steps: Step[], ownerByStep: PersonaCode[] }`. The Stepper component, the BpmnView modal, and the form-action permission check SHALL all consume this same config; no component SHALL hardcode its own step list.

#### Scenario: Stepper and BpmnView agree
- **WHEN** a developer adds a new step to `LEAVE`'s config
- **THEN** both the chevron stepper at the top of the form and the BpmnView modal show the new step with no further code change

### Requirement: Chevron stepper

The system SHALL render a horizontal chevron stepper at the top of every form. Each step SHALL be one of three visual states: `done` (slate-400 text + green check icon), `current` (amber background + white text + rounded), `future` (slate-400 text). Steps SHALL be separated by chevron-right icons. The stepper SHALL match the prototype byte-for-byte at the structural level (font weights, paddings, colors).

#### Scenario: Three-state rendering
- **WHEN** a form has steps `[APPLY, APPROVE, CLOSE]` with `activeStep = 1`
- **THEN** APPLY renders with a green check, APPROVE renders amber-on-white, CLOSE renders slate-400

#### Scenario: Stepper handles 7 steps
- **WHEN** a form has 7 steps (e.g. HWP)
- **THEN** all 7 chevrons render in a single horizontal row, with horizontal scroll if the viewport is narrower than the rendered width

### Requirement: BpmnView modal

The system SHALL provide a `BpmnView` modal opened from a "View BPMN" button on every form. The modal SHALL render the same step list as a BPMN-flavored SVG diagram: a start circle, one rectangle per step, an end circle, connected by arrows. Active step SHALL be filled amber; completed steps SHALL be filled with a soft green tint and a check; future steps SHALL be hairline outlined. The diagram SHALL include role labels under each step (the persona that owns it).

#### Scenario: View BPMN button opens modal
- **WHEN** the user clicks "View BPMN" on any form
- **THEN** a centered modal opens with the SVG diagram and a close button

#### Scenario: Active step is amber-filled
- **WHEN** the form's `activeStep` is 2 (out of 5)
- **THEN** the modal's third rectangle is filled with the accent (amber) color, and steps 1–2 are filled green with check marks

#### Scenario: Role labels are shown
- **WHEN** the diagram renders for LEAVE
- **THEN** under "MANAGER APPROVE" the label "Manager / 主管" is shown
