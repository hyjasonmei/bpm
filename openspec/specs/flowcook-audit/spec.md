# flowcook-audit Specification

## Purpose

Define the **Audit** feature of admin (page 4 of five) and the cross-service audit event pipeline. Audit is the immutable record of every meaningful action across admin, bpm, chef, and syncer. The page is read-only; the data arrives via syncer pulling from bpm.

## Requirements

### Requirement: Seven-column event schema

Every audit event SHALL conform to the following schema:

| Column | Meaning |
|---|---|
| `actor_user_id` | Who performed the action (null for system events) |
| `actor_principal_id` | Acting as which principal (the role/dept/group when via persona / inherit) |
| `action_type` | Enumerated label such as `created`, `updated`, `deleted`, `approved`, `rejected`, `login`, `login_fail`, `sync`, `persona_switch`, `flow_submitted`, `flow_cooking_started`, `flow_on_hold`, etc. |
| `target_type` + `target_id` | What was acted on (e.g., `process_instance/<guid>`, `spec/<guid>`, `principal/<guid>`, `config/<key>`) |
| `timestamp` | UTC ISO-8601 |
| `before` / `after` JSON | Pre / post snapshot for data-changing actions (nullable for non-state events) |
| `source_system` | One of `admin`, `bpm`, `chef`, `syncer` |
| `reason` | Optional comment string |

#### Scenario: Login event has no target snapshot
- **WHEN** a user logs in successfully
- **THEN** the event has `action_type = login`, no `before` / `after`, and `target_type = session/<id>`

#### Scenario: Soft-delete event captures the row state
- **WHEN** an admin soft-deletes a process instance
- **THEN** the event records `before = { ...full row pre-delete... }`, `after = { ...same with deleted_at = <time>... }`

### Requirement: Append-only / immutable

Audit rows SHALL never be updated, soft-deleted, or hard-deleted. Corrections SHALL be issued as new events with `action_type = correction` referencing the original event's id, never by altering the original.

#### Scenario: Attempt to update an audit row
- **WHEN** any code path attempts an UPDATE on the audit table
- **THEN** the operation SHALL be refused (enforced at DB constraint and / or repository layer)

#### Scenario: Correction of an earlier mis-attribution
- **WHEN** a previously-emitted audit event needs to record that the wrong actor was attributed
- **THEN** a new event with `action_type = correction`, `reason`, and reference to the original event id SHALL be appended

### Requirement: bpm writes locally; syncer pulls to admin in batch

bpm SHALL write audit events to its local DB synchronously. syncer SHALL pull pending events from bpm in batches every 5 minutes (configurable) and write them into admin's audit table.

#### Scenario: bpm continues writing while admin offline
- **WHEN** admin is unreachable
- **THEN** bpm continues writing audit events locally
- **AND** when admin comes back, syncer drains the backlog

#### Scenario: Default sync cadence
- **WHEN** the system is at default config
- **THEN** syncer polls bpm `/api/audit/since?cursor=...` every 5 minutes

### Requirement: At-least-once delivery with dedupe by event_id

Each audit event SHALL carry a globally unique `event_id` (generated at write time). syncer's delivery semantic SHALL be at-least-once. admin SHALL dedupe by `event_id` upon receiving.

#### Scenario: syncer retries after a network failure
- **WHEN** syncer's first batch attempt fails mid-flight
- **THEN** the next attempt may re-deliver some already-stored events
- **AND** admin's insert ignores duplicates by `event_id`

### Requirement: admin Audit page is read-only

The Audit page in admin UI SHALL only display events. It SHALL NOT expose any mutation action (no delete, no edit, no correction button). Corrections, when needed, are produced at the source system (bpm / chef / etc.).

#### Scenario: Trying to delete an audit row
- **WHEN** a user inspects the Audit page DOM
- **THEN** no delete control exists for any row

### Requirement: Default filters

In v0 the Audit page SHALL support at minimum filtering by:

- time range
- `action_type`
- `source_system`

Other filters (by actor, by target, by free-text search, export, retention controls) are deferred.

#### Scenario: Filter by action type
- **WHEN** a user selects `action_type = flow_on_hold`
- **THEN** the table shows only those events

### Requirement: All flowcook services emit audit

Each of `admin`, `bpm`, `chef`, and `syncer` SHALL emit audit events for their meaningful actions. The `source_system` field identifies which.

#### Scenario: chef emits an event on on-hold callback
- **WHEN** chef calls `POST /api/flows/{id}/on-hold`
- **THEN** the handler SHALL write an audit event with `source_system = chef`, `action_type = flow_on_hold`

#### Scenario: syncer emits its own runs
- **WHEN** syncer completes a successful pull-batch
- **THEN** it writes one event with `source_system = syncer`, `action_type = sync`, including counts in `reason` or `after` JSON
