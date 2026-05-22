# flowcook-syncer Specification

## Purpose

Define the **syncer** service — the bridge between admin (design-time / control plane) and bpm (runtime / customer side). syncer is bidirectional: it pushes design-time artifacts down to bpm and pulls operational telemetry back up to admin. The bridge MUST degrade gracefully so that an offline admin never blocks bpm runtime.

## Requirements

### Requirement: syncer is the only mutator between admin and bpm

admin SHALL NOT call bpm endpoints directly (except for sandbox config and reset / debug paths). bpm SHALL NOT call admin endpoints directly. All routine data movement SHALL flow through syncer.

#### Scenario: admin updates a principal
- **WHEN** a customer admin edits a user's display name
- **THEN** admin writes to its own DB
- **AND** syncer subsequently picks up the change and pushes it to bpm

### Requirement: Push channel — admin → bpm

syncer SHALL push the following classes of data from admin to bpm:

1. **Principal / Role / Delegation** — org graph changes
2. **Spec bundle** — when a flow becomes `committed`
3. **Variable values** — flow-scoped `${var}` value updates
4. **Site Setting (bpm-affecting subset)** — defaults that the bpm runtime needs (notification sender, timezone, default language, branding)

#### Scenario: New delegation pushed
- **WHEN** an admin creates a Delegation `alice → bob` for next week
- **THEN** within one sync interval, bpm's local Delegation table contains the row

### Requirement: Pull channel — bpm → admin

syncer SHALL pull the following classes of data from bpm to admin:

1. **Audit log** (per `flowcook-audit`)
2. **Process data** including sandbox status (e.g., counts of running instances, sandbox usage stats)

The audit pull cadence SHALL default to every 5 minutes (configurable).

#### Scenario: Audit appears on admin shortly after action
- **WHEN** a user on bpm performs an approval at time T
- **THEN** the corresponding audit event appears on admin's Audit page no later than T + 5 minutes

### Requirement: Org data sourcing supports customer IdPs (TODO)

In production, org data (users / depts / groups) SHALL be sourced from the customer's identity system (Entra ID, AD, HR / HRIS export). syncer SHALL contain per-data-source adapters. **For v0, this is captured as a TODO**; the dev / demo environment uses Seed CLI to populate principal data instead of integrating with any IdP.

#### Scenario: v0 demo without IdP
- **WHEN** the team runs a v0 demo
- **THEN** `SeedCli --org` populates admin with mock principals
- **AND** syncer pushes them to bpm
- **AND** no customer IdP is involved

#### Scenario: Future Entra integration (out of v0 scope)
- **WHEN** a real customer onboarding occurs
- **THEN** the syncer adapter for Entra ID pulls the customer's directory and maps it onto the flowcook principal schema before pushing to bpm

### Requirement: At-least-once delivery with dedupe

syncer SHALL guarantee at-least-once delivery for both push and pull. Receivers SHALL dedupe by `event_id` (audit) or by natural key + version (org / variables / bundles).

#### Scenario: syncer retries on network failure
- **WHEN** a sync run fails partway
- **THEN** the next run re-sends pending items
- **AND** receivers tolerate duplicates without effect

### Requirement: Graceful degradation when admin unreachable

When admin is offline, bpm SHALL continue serving runtime traffic. Audit events SHALL queue locally; previously-synced principals / specs / variables continue to apply.

#### Scenario: admin offline for an hour
- **WHEN** admin is unreachable for one hour
- **THEN** bpm keeps processing flows using last-synced data
- **AND** when admin returns, syncer drains the audit backlog and applies any new pushes

### Requirement: Customer contract lapse stops sync

When a customer's flowcook contract ends, syncer SHALL cease talking to that customer's bpm. The customer's bpm continues operating standalone on last-synced data.

#### Scenario: Contract terminated
- **WHEN** flowcook revokes a customer's syncer credentials
- **THEN** syncer stops pushing / pulling for that customer
- **AND** customer bpm continues running existing flows (no new specs, no role updates)

### Requirement: v0 authentication uses shared secret

syncer ↔ admin and syncer ↔ bpm authentication SHALL use a shared secret per customer in v0. Upgrading to mTLS or OAuth client credentials is deferred.

#### Scenario: Secret rotation
- **WHEN** flowcook rotates a customer's syncer secret
- **THEN** both admin and bpm are reconfigured before the next sync run

### Requirement: Conflict policy v0 — admin-wins on org, bpm-wins on process

When the same row is changed in both places (rare in v0 because admin is the org owner and bpm is the process owner), the system SHALL apply:

- For **Principal / Role / Delegation**: admin's version wins (because admin is the design-time source)
- For **process instances / tasks / history**: bpm's version wins (because bpm is the runtime source); admin never edits these

#### Scenario: Same principal touched on both sides
- **WHEN** a principal row has different `updated_at` on admin and bpm
- **THEN** admin's version overwrites bpm's on next sync
- **AND** an audit event records the conflict resolution

### Requirement: Variable value sync is fast-path

When a customer admin updates a flow's `${var}` value in admin, syncer SHALL push the new value to bpm within the same 5-minute window without requiring a chef re-cook.

#### Scenario: ERP URL changes
- **WHEN** admin updates `${ERP_URL}` from staging to prod
- **THEN** syncer pushes the new value to bpm's tenant variable table
- **AND** subsequent integration calls from bpm runtime use the new URL — no chef invocation needed
