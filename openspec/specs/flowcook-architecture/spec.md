# flowcook-architecture Specification

## Purpose

Define the **flowcook** four-service architecture and its monorepo folder layout. flowcook pivots the legacy single-service BPM platform into four logical services that ship inside one monorepo: **admin** (design-time control plane), **bpm** (runtime workflow engine), **chef** (AI code-generation pipeline), and **syncer** (admin ↔ bpm bridge). The architecture preserves a key business commitment — **the customer's bpm side keeps working even if the admin contract lapses**.

## Requirements

### Requirement: Four logical services across six monorepo folders

The system SHALL be organized as one git monorepo with six top-level service folders. The four logical services map to folders as follows:

| Logical service | Folders |
|---|---|
| admin | `bpm-admin-svc/` (BE), `bpm-admin-ui/` (FE) |
| bpm | `bpm-svc/` (BE), `bpm-ui/` (FE) |
| chef | `chef/` |
| syncer | `syncer/` |

#### Scenario: New developer can map a feature to a folder
- **WHEN** a developer reads a feature description such as "audit log appears in admin"
- **THEN** they MUST locate the FE in `bpm-admin-ui/` and the BE in `bpm-admin-svc/` without further guidance

#### Scenario: chef cannot live inside admin or bpm folders
- **WHEN** a code generator service is added
- **THEN** it SHALL live in `chef/` and NOT under `bpm-admin-svc/` or `bpm-svc/`

### Requirement: Per-customer deployment (no multi-tenant)

The system SHALL be deployed once per customer organization. There SHALL be no shared admin or bpm instance across customers in v0.

#### Scenario: Two customers each get a dedicated stack
- **WHEN** the second customer signs up
- **THEN** they receive their own `bpm-admin-svc` + `bpm-admin-ui` + `bpm-svc` + `bpm-ui` + `chef` + `syncer` instances
- **AND** their data is physically isolated in separate databases

### Requirement: admin + chef are flowcook IP, bpm is customer-side

The system SHALL run `bpm-admin-svc`, `bpm-admin-ui`, `chef`, and `syncer` on infrastructure controlled by flowcook. The bpm-side (`bpm-svc`, `bpm-ui`) MAY be hosted by flowcook or self-hosted by the customer.

#### Scenario: Customer subscription lapses
- **WHEN** a customer's flowcook contract is not renewed
- **THEN** `bpm-admin-svc`, `bpm-admin-ui`, `chef`, and `syncer` connections to that customer SHALL stop
- **AND** the customer's `bpm-svc` + `bpm-ui` SHALL continue running with the last-synced state (specs, org data, variables)
- **AND** end users SHALL still be able to start, fill, and approve flows

### Requirement: bpm runtime is self-sufficient

The bpm service SHALL contain everything needed to execute existing flows without contacting admin, chef, or syncer:

- workflow runtime + state machine
- form rendering (DynamicForm)
- inbox / live cases / completed / reports / 人工介入
- persona / role / dept data (last synced)
- notification dispatch

#### Scenario: syncer is offline
- **WHEN** `syncer` is unreachable
- **THEN** `bpm-svc` SHALL keep accepting form submissions and progressing flows
- **AND** audit events SHALL accumulate locally for later sync

#### Scenario: admin is offline
- **WHEN** `bpm-admin-svc` is unreachable
- **THEN** end users on `bpm-ui` SHALL be unaffected
- **AND** customer admins lose only design-time features (no new flows can be cooked, no role changes pushed)

### Requirement: admin five-page navigation

The `bpm-admin-ui` SHALL present exactly five top-level pages:

1. **AI Kitchen** — flow management (wizard, CoPilot, Flow Library, chef console)
2. **User & Role** — Principal management
3. **Sandbox** — sandbox controls (mail intercept, clock freeze)
4. **Audit** — read-only audit log viewer
5. **Site Setting** — global config (admin self-config + bpm global behavior)

#### Scenario: Navigation contains exactly these five entries
- **WHEN** an admin opens the admin UI
- **THEN** the primary nav SHALL list AI Kitchen / User & Role / Sandbox / Audit / Site Setting
- **AND** no other top-level nav entries SHALL exist

### Requirement: Two flow types share one lifecycle

The system SHALL support two flow types: `wizard` (produced by AI Kitchen 11-step wizard, processed by chef) and `custom` (produced by consultant in plain text, processed by human engineers). Both types SHALL share the same lifecycle state machine defined in `flowcook-lifecycle`.

#### Scenario: A custom-typed flow uses the same states
- **WHEN** a consultant submits a plain-text spec
- **THEN** the flow record SHALL move through `draft → submitted → cooking → committed → approved`
- **AND** the `cooking` phase SHALL be performed by a human engineer instead of chef

### Requirement: chef and admin are flowcook-controlled

chef SHALL be invoked only by flowcook-operated services (admin or chef itself). Customers SHALL NOT have direct API access to chef.

#### Scenario: Customer admin cannot bypass admin to chef
- **WHEN** a customer admin attempts to invoke chef directly
- **THEN** the call SHALL be rejected; the only valid path is through `bpm-admin-svc` submit lifecycle transition
