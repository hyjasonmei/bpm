# flowcook-lifecycle Specification

## Purpose

Define the seven-state lifecycle of every flow design in flowcook, plus the transitions that move a flow from `draft` to `approved`. Applies uniformly to both `wizard` and `custom` flow types. State changes drive chef pickup, on-hold pauses, customer notifications, and audit history.

## Requirements

### Requirement: Seven lifecycle states

A flow design SHALL exist in exactly one of the following states at any moment:

| State | Meaning |
|---|---|
| `draft` | Being authored; not yet submitted |
| `submitted` | In chef queue, awaiting pickup |
| `cooking` | chef (or human engineer for `custom`) is generating implementation |
| `on hold` | chef is stuck and has appended a question to NOTES; waiting for user clarification |
| `committed` | chef has produced PR / bundle; pending merge / acceptance |
| `approved` | PR merged to main / bundle accepted; this version is live |
| `rejected` | Committed output was refused; flow returns to drafting |

#### Scenario: State is exactly one value
- **WHEN** querying any flow record
- **THEN** the `state` field SHALL be one of the seven values; never null, never multiple

### Requirement: Submit triggers `draft → submitted`

The admin UI SHALL expose a Submit action that transitions a flow from `draft` to `submitted` and enqueues it for chef.

#### Scenario: Customer admin submits a wizard flow
- **WHEN** the customer clicks Submit after completing the wizard
- **THEN** state changes `draft → submitted`
- **AND** an audit event `action_type = flow_submitted` is recorded

### Requirement: chef pickup triggers `submitted → cooking`

chef SHALL pull from per-customer serial queue. When chef accepts a flow, the state SHALL transition `submitted → cooking`.

#### Scenario: chef picks up next in queue
- **WHEN** chef polls the queue and finds a `submitted` flow
- **THEN** state changes `submitted → cooking`
- **AND** audit event `action_type = flow_cooking_started` is recorded

### Requirement: chef may transition to `on hold` when stuck

chef SHALL call `POST /api/flows/{id}/on-hold` with a question when the spec is ambiguous or missing critical information. The handler SHALL transition state `cooking → on hold` and append the question to the flow's `notes` field.

#### Scenario: chef raises a question
- **WHEN** chef encounters spec ambiguity during cooking
- **THEN** chef SHALL NOT guess
- **AND** chef SHALL POST to the on-hold endpoint
- **AND** the flow state becomes `on hold`
- **AND** the question is appended to `notes` with chef iteration tag

### Requirement: Resume from `on hold` goes through `submitted`

When a user resolves an on-hold question by editing `notes` and clicking Resume, the flow SHALL transition `on hold → submitted` (not directly to `cooking`). chef SHALL re-pick the flow from the queue.

#### Scenario: User resumes after answering
- **WHEN** a user finishes editing NOTES on an `on hold` flow and clicks Resume
- **THEN** state changes `on hold → submitted`
- **AND** chef re-picks the flow, transitions to `cooking`, and re-reads NOTES (including its prior question + user's answer)

### Requirement: Cancel returns the flow to `draft`

The system SHALL allow cancel from `submitted`, `cooking`, or `on hold` states. Cancel SHALL transition state back to `draft`. The system SHALL NOT introduce a separate `cancelled` state.

#### Scenario: Cancel from submitted (withdraw)
- **WHEN** user cancels a flow currently in `submitted`
- **THEN** state changes `submitted → draft`
- **AND** chef does not pick it up

#### Scenario: Cancel from cooking (interrupt)
- **WHEN** user cancels a flow currently in `cooking`
- **THEN** state changes `cooking → draft`
- **AND** chef SHALL clean up any partial output for this iteration

#### Scenario: Cancel from on hold
- **WHEN** user determines the on-hold question reveals a wizard-step error and clicks Cancel
- **THEN** state changes `on hold → draft`, the user re-edits earlier steps

### Requirement: chef commits a PR / bundle transitioning `cooking → committed`

When chef finishes generating implementation, the resulting PR / bundle SHALL transition the flow state to `committed`.

#### Scenario: chef pushes bundle (v0)
- **WHEN** chef completes and pushes bundle to bpm
- **THEN** state changes `cooking → committed`

#### Scenario: chef opens PR (tech-tier customer, v1+)
- **WHEN** chef opens a git PR for a tech-tier customer
- **THEN** state changes `cooking → committed`

### Requirement: Approval transitions `committed → approved`

When the PR is merged or the bundle is accepted, the state SHALL transition to `approved`. chef MAY detect this transition via polling or webhook.

#### Scenario: PR merge detected
- **WHEN** chef polls the git host and finds the committed PR merged to main
- **THEN** state changes `committed → approved`

### Requirement: Rejection transitions `committed → rejected → draft`

If a reviewer rejects the committed PR / bundle, the state SHALL transition `committed → rejected`. The user MAY then re-edit and resubmit, transitioning `rejected → draft`. The rejected record SHALL be preserved in audit history (not overwritten).

#### Scenario: PR rejected for review
- **WHEN** a reviewer rejects the PR
- **THEN** state changes `committed → rejected`
- **AND** audit retains the rejected record forever

### Requirement: Approved flows produce new versions for changes

The system SHALL support flow versioning via `lineage_id` (shared across versions of the same flow) and `version` (sequential integer). Editing an `approved` flow SHALL create a new draft record at `version + 1`, pre-filled with the prior approved spec. The prior approved record is frozen and remains queryable.

#### Scenario: User edits an approved flow
- **WHEN** a user opens an `approved` flow and clicks "Edit new version"
- **THEN** a new flow record is created with `state = draft`, `lineage_id = same`, `version = prior + 1`
- **AND** the new draft is pre-filled with the prior spec
- **AND** the prior `approved` record remains untouched and queryable

### Requirement: chef queue is per-customer serial (v0)

chef SHALL process at most one flow at a time per customer. Different customers SHALL be processed independently. Within a customer, multiple submitted flows SHALL queue and execute serially.

#### Scenario: Two flows submitted by same customer
- **WHEN** customer X submits flow A, then flow B before A is done
- **THEN** chef finishes A entirely before starting B

#### Scenario: Two customers submit concurrently
- **WHEN** customer X and customer Y each submit a flow
- **THEN** chef MAY process them in parallel (each on its own queue)

### Requirement: No iteration cap in v0

The system SHALL NOT enforce a numeric or cost cap on on-hold cycles in v0. Repeated on-hold ↔ resume loops are allowed.

#### Scenario: Multiple on-hold cycles
- **WHEN** chef enters `on hold` for the third time on the same flow
- **THEN** the transition is allowed
- **AND** no automatic escalation occurs (manual flowcook-internal escalation is out of v0 scope)

### Requirement: All state transitions emit audit events

Every state transition SHALL produce one audit event of type `flow_{transition}`. Audit events SHALL include `flow_id`, `from_state`, `to_state`, `actor_user_id`, and `timestamp`.

#### Scenario: Transition writes audit
- **WHEN** any state changes
- **THEN** exactly one audit event is appended to the admin audit table
- **AND** the event becomes part of the immutable history (per `flowcook-audit`)
