# flowcook-wizard Specification

## Purpose

Define the AI Kitchen eleven-step wizard that customer admins (or flowcook internal) use to capture a flow spec. The wizard is the canonical authoring path for `wizard`-typed flows. Each completed run produces a single spec JSON that is the contract between admin (design-time) and chef + bpm (build/run-time).

## Requirements

### Requirement: Eleven canonical steps in order

The wizard SHALL present exactly the following steps in order:

| # | Step | Spec output |
|---|---|---|
| 1 | SOURCE | `meta`, `flow.nodes`, `flow.edges` (with BPMN preview replacing the old STRUCTURE step) |
| 2 | TRIGGER & ACCESS | `triggers[]`, `access` |
| 3 | VARIABLES | `variables[]` |
| 4 | FORMS | `userTasks[].fields[]` |
| 5 | DECISIONS | `decisions[].rule` (Cel) |
| 6 | APPROVERS | `approvalNodes[].rule` |
| 7 | NOTIFY | `notifications[]` |
| 8 | INTEGRATIONS | `integrations[]` |
| 9 | SLA | `sla`, `escalation` |
| 10 | TRANSLATION | `labels[locale]` |
| 11 | NOTES | `notes` |

After step 11 the wizard SHALL present a Submit button (not a separate step) that triggers the `draft → submitted` lifecycle transition defined in `flowcook-lifecycle`.

#### Scenario: Step order is fixed
- **WHEN** an admin opens any wizard run
- **THEN** the eleven steps appear in the order above
- **AND** the Submit button replaces the legacy "GO LIVE" step

### Requirement: Each step has its own validator gate

The wizard SHALL refuse to advance to step N+1 until step N's validator passes. Each step's validator SHALL be deterministic and idempotent.

#### Scenario: Cannot skip a placeholder step
- **WHEN** the user fills steps 1-3 but leaves step 4 empty
- **THEN** "Next" on step 4 is disabled until at least one userTask field is defined

### Requirement: Step 2 (TRIGGER & ACCESS) v1 is single-form

In v0/v1 the TRIGGER & ACCESS step SHALL accept exactly one trigger of type `form` per flow. The underlying schema SHALL use a `triggers[]` array to leave room for multiple triggers / additional types (`cron`, `webhook`, `mail`, `api`) in later versions, but the UI SHALL constrain to one form trigger.

#### Scenario: Wizard v1 hides cron / webhook options
- **WHEN** an admin reaches step 2
- **THEN** the UI offers only a single form-trigger configuration
- **AND** `triggers[]` is written with exactly one entry

### Requirement: Step 2 also captures flow-level access

The step SHALL capture three principal-reference fields stored in `access`:

- `launchable_by[]` — who may start a new instance
- `visible_to[]` — who may see this flow in the catalog
- `watcher[]` — optional; who may observe other people's instances

Instance-level permissions (the actor on a task seeing their own inbox, the admin role seeing everything) are derived from actor and system role at runtime and SHALL NOT be captured here.

#### Scenario: Access fields default to empty
- **WHEN** the admin first opens step 2
- **THEN** `launchable_by[]`, `visible_to[]`, and `watcher[]` are empty
- **AND** the validator requires `launchable_by[]` to be non-empty to advance

### Requirement: Step 3 (VARIABLES) declares flow-scoped variables

The VARIABLES step SHALL allow declaring named variables with `default_value`, `description`, and a `sensitive` flag. Variables are flow-scoped (not global). Subsequent steps MAY reference variables using `${var_name}` syntax in fields where the schema notes "supports variable reference."

#### Scenario: A sensitive variable masks its value in UI
- **WHEN** a variable has `sensitive: true`
- **THEN** the admin UI SHALL display the value masked (`****`)
- **AND** audit events for changes SHALL record only the variable name, not the value

### Requirement: Step 5 (DECISIONS) uses Cel expressions

Gateway rules SHALL be authored in Cel (Common Expression Language). The expression MAY reference flow form data and `${var}` variables.

#### Scenario: Cel expression references a variable
- **WHEN** the rule is `amount > ${MAX_AUTO_APPROVE}`
- **THEN** the runtime SHALL substitute `${MAX_AUTO_APPROVE}` with the current variable value before evaluation

### Requirement: Step 6 (APPROVERS) picks a Principal + role + inherit

The APPROVERS step SHALL allow selecting any principal (user / dept / group) plus a role, plus the `inherit_to_members` checkbox. The model SHALL match `flowcook-principal-model`.

#### Scenario: Approver = dept with inherit
- **WHEN** an approval node is set to "Engineering dept, role Approver, inherit=true"
- **THEN** at runtime any user with the inherited role in Engineering SHALL be eligible to act
- **AND** the first to claim the task assumes it

### Requirement: Step 7 (NOTIFY) carries pure-signal channels only

The NOTIFY step SHALL declare email / sms / webhook notifications meant as signals (e.g., "approved → ping #ops Slack"). Structured outbound-data integrations MUST be done in step 8, not here.

#### Scenario: Trying to use NOTIFY for ERP push
- **WHEN** an admin attempts to use NOTIFY to push line-item data to an ERP system
- **THEN** the design SHALL surface that this belongs to step 8 INTEGRATIONS instead

### Requirement: Step 8 (INTEGRATIONS) takes an OpenAPI spec

The INTEGRATIONS step SHALL accept the customer's external system as an uploaded OpenAPI (JSON or YAML) spec. The UI SHALL parse the spec, list endpoints, and let the customer choose endpoint(s), flow trigger node(s), field mapping, and auth.

#### Scenario: OpenAPI with multiple endpoints
- **WHEN** the customer uploads an OpenAPI spec listing 12 endpoints
- **THEN** the UI shows all 12 and lets the customer pick which to invoke from this flow
- **AND** records the choice in `integrations[]` with explicit `endpoint.operationId` reference

#### Scenario: Sensitive auth value
- **WHEN** auth requires a bearer token
- **THEN** the token is stored in a secret store
- **AND** the spec carries only a reference (`auth.config_ref = "secret://..."`)

### Requirement: Step 10 (TRANSLATION) supports AI fill of empty cells

The TRANSLATION step SHALL list every label (form, button, notification, error) and present a side-by-side table for zh (primary) and en (secondary). One-click AI fill SHALL fill only empty cells, never overwrite filled ones. The schema MUST be a `Record<locale, string>` shape so future N-language extension does not require migration.

#### Scenario: AI fill on a partly-filled table
- **WHEN** 80% of labels already have en translations and the admin clicks "AI fill"
- **THEN** only the empty 20% are filled
- **AND** existing filled values remain untouched

### Requirement: Step 11 (NOTES) is a single free-text textarea

NOTES SHALL be one textarea stored as `spec.notes` (single string). Future enhancement to per-step sticky sidebars is out of scope.

#### Scenario: chef reads NOTES
- **WHEN** chef begins cooking a flow
- **THEN** chef receives `spec.notes` as additional context in its system prompt

### Requirement: Submit button replaces the legacy GO LIVE step

The legacy "GO LIVE" step SHALL NOT exist as a wizard step. Instead the wizard footer SHALL show a Submit button on step 11. Clicking Submit triggers the `draft → submitted` lifecycle transition defined in `flowcook-lifecycle`.

#### Scenario: Submit only enabled when all validators pass
- **WHEN** the user reaches step 11 but earlier steps still fail validation
- **THEN** the Submit button is disabled, with a hint indicating which earlier step needs attention

### Requirement: Test step removed; Sandbox tab handles trial runs

The wizard SHALL NOT include a TEST step. Trial running of a draft / submitted flow SHALL be handled by the Sandbox feature (`flowcook-sandbox`) via the admin Sandbox tab.

#### Scenario: User wants to try a draft flow
- **WHEN** the user finishes step 11 but wants to test before submit
- **THEN** the wizard provides a link to open Sandbox with the current draft pre-loaded
- **AND** Sandbox runs the flow against bpm runtime under sandbox config
