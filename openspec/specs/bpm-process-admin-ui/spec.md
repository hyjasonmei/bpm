# bpm-process-admin-ui Specification

## Purpose
TBD - created by archiving change add-process-admin-ui. Update Purpose after archive.
## Requirements
### Requirement: Process Admin SPA route auth-gated to flow_admin

The system SHALL serve a Process Admin SPA at `/processes/*` requiring `flow_admin:<flow_code>` role for that specific flow OR `tenant_admin`. The role assignment system already supports per-flow admin scoping; this UI uses that directly. Non-authorized users SHALL be redirected to `/`.

#### Scenario: Flow admin reaches their flow

- **GIVEN** Wilson has `flow_admin:LEAVE` role
- **WHEN** Wilson navigates to /processes/definitions/LEAVE
- **THEN** the designer for LEAVE renders

#### Scenario: Flow admin blocked from other flow

- **WHEN** Wilson navigates to /processes/definitions/PURCHASE (without flow_admin:PURCHASE)
- **THEN** redirect to /

#### Scenario: Tenant admin sees all flows

- **WHEN** a tenant_admin navigates to /processes/definitions
- **THEN** all flow definitions across the tenant are listed

### Requirement: Designer combines BPMN canvas with property editors

The Designer screen SHALL render a three-pane layout: left tree (node list), center BPMN canvas (using `bpmn-js`), right detail panel that switches based on selected node type. The detail panel embeds the same editors used by the wizard:

- userTask → StepForms-style editor
- approval → StepApprovers-style editor (ActorRefEditor)
- gateway → StepDecisions-style editor (with CEL live validation)
- notify → StepNotify-style editor
- (start/end events → no editor)

Save creates a draft (status = Draft); Publish creates a new spec version. In-flight instances continue using their snapshotted version.

#### Scenario: Selecting a userTask shows form editor

- **WHEN** the admin clicks a userTask node on the canvas
- **THEN** the right pane mounts the form-fields editor for that userTask

#### Scenario: Save creates draft

- **WHEN** the admin saves edits without publishing
- **THEN** the spec's draft state is updated; in-flight instances are unaffected; published version unchanged

#### Scenario: Publish creates new version

- **WHEN** the admin clicks Publish
- **THEN** a new spec version is created (e.g., v3 → v4); active version becomes v4; in-flight instances continue with v3 (per snapshot semantics)

### Requirement: Simulator runs dry-mode runtime

The Simulator screen SHALL invoke `IProcessSimulator.SimulateAsync(spec, sampleFormData, sampleUsers)` which executes the same `IProcessRuntime` engine but in dry mode (DB writes rolled back; in-memory dispatcher / file storage). The result is a trace structure listing every state transition, gateway evaluation, recipient resolution, and computed DueAt.

#### Scenario: Simulate 8-day leave routes via VP

- **GIVEN** the LEAVE spec with `gateway_days.condition = "days >= 7" → VP`
- **WHEN** the admin runs a simulation with form `{ days: 8 }`
- **THEN** the trace shows: gateway evaluated true, VP node spawned, recipients resolved to current VP user

#### Scenario: Simulate produces no DB writes

- **WHEN** simulation completes
- **THEN** no NotificationDelivery rows persist; no Task rows persist; spec snapshot is unchanged

### Requirement: Live case detail supports admin intervention

The Live Case Detail screen SHALL surface admin actions for open tasks:

- Force reassign — pick a different user for an open task
- Force return — return current approval to a previous userTask node
- Force submit — admin acts as the assignee with a recorded reason
- Terminate instance — cancel the instance with reason

Each action MUST require a non-empty reason. The corresponding API endpoints write TaskHistory rows with `actor_role = 'admin'` payload field for audit clarity.

#### Scenario: Force reassign requires reason

- **WHEN** the admin clicks Force Reassign without entering a reason
- **THEN** the submit button is disabled until a reason is provided

#### Scenario: Admin action audit trail

- **GIVEN** an admin force-reassigns task T1 to user U2 with reason "原代理人請假"
- **WHEN** the action commits
- **THEN** a TaskHistory row exists with `EventType = TaskSpawned, ActorUserId = admin id, payload includes { actor_role: 'admin', reason: '原代理人請假', force_reassigned_from: 'original_user' }`

### Requirement: Reports show per-spec aggregate metrics

The Reports screen SHALL display per-spec aggregations:

- Total instances / completed / cancelled / running
- Breach rate over period (default 30 days)
- Cycle time histogram + p50 + p95
- Bottleneck node (highest average time-in-node)
- Per-assignee load (open tasks by user)

Data SHALL be cached server-side for 5 minutes per (spec, period). Cache invalidation on instance completion / cancellation events.

#### Scenario: Reports show breach rate

- **GIVEN** 100 LEAVE instances completed in last 30 days, 5 of which had SLA breaches
- **WHEN** the admin views Reports for LEAVE
- **THEN** the breach rate is 5%

#### Scenario: Cache hit on second view

- **GIVEN** the Reports page was loaded 2 minutes ago
- **WHEN** the admin reloads the page
- **THEN** the response is served from cache; `X-Cache: HIT` header

### Requirement: Demo screens unmodified

The change SHALL NOT modify `bpm-ui/src/screens/Home.tsx`, `bpm-ui/src/screens/forms/*.tsx`, `bpm-ui/src/screens/Search.tsx`, `bpm-ui/src/screens/Report.tsx`, or `bpm-ui/src/lib/workflow.ts`. Process Admin lives in `bpm-ui/src/screens/processes/*`.

#### Scenario: Mock-up forms unchanged

- **WHEN** the change ships
- **AND** an employee opens any mock-up flow
- **THEN** visuals are byte-identical

