## ADDED Requirements

### Requirement: EntraSyncConfiguration per tenant

The system SHALL persist `EntraSyncConfiguration` per tenant carrying EntraTenantId, ClientId, encrypted ClientSecret, SyncIntervalHours (default 6), UserFilter, GroupFilter, EmailDomainFilter[], IncludeGroups, IsActive. ClientSecret SHALL be encrypted at rest via `IDataProtector`.

#### Scenario: Configure Entra sync

- **WHEN** an admin POSTs config with valid Entra tenant id + client id + secret
- **THEN** row inserted; secret encrypted; subsequent reads show last 4 chars only

### Requirement: Scheduled sync via background worker

The system SHALL run an `EntraSyncWorker` BackgroundService that, every 30 minutes, finds active EntraSyncConfigurations where `LastSyncAt + SyncIntervalHours <= now` and runs `RunSyncAsync(configId, Apply)` for each.

#### Scenario: 6-hour-interval triggers

- **GIVEN** config with SyncIntervalHours = 6 and LastSyncAt = 6 hours ago
- **WHEN** the worker checks
- **THEN** sync is triggered for that config

#### Scenario: 0 interval = manual only

- **GIVEN** config with SyncIntervalHours = 0
- **WHEN** the worker checks
- **THEN** sync is NOT triggered automatically; only on-demand admin trigger fires it

### Requirement: Sync uses Microsoft Graph with eager manager expansion

The Graph fetch SHALL eagerly expand the manager relationship via `$expand=manager` to avoid per-user round trips. Pagination SHALL be handled per Graph SDK conventions. The User select clause SHALL fetch at minimum: id, mail, displayName, jobTitle, department, accountEnabled.

#### Scenario: Eager fetch with manager

- **WHEN** the sync fetches users
- **THEN** the Graph request includes `$select=...&$expand=manager($select=id,mail)` so manager link is returned in the same response

### Requirement: Entra sync feeds the diff engine

The sync path SHALL produce a sequence of `UserSyncRow` records (mapped from Graph User responses) and feed them into the same `DiffEngine` used by CSV import (`add-hr-sync-csv`). Diff produces inserts / updates / deactivations. Apply commits the changes within a transaction.

#### Scenario: Diff identifies new user

- **GIVEN** Entra has a new user not present in BPM
- **WHEN** sync runs in DryRun mode
- **THEN** the report shows that user as an insert candidate

#### Scenario: accountEnabled = false soft-deactivates

- **GIVEN** an Entra user with accountEnabled = false
- **WHEN** sync runs Apply mode
- **THEN** the corresponding BPM User has IsActive = false; row not deleted

### Requirement: Group sync optional and idempotent

When `IncludeGroups = true`, sync SHALL upsert Entra Groups into BPM Groups (mapping Entra ObjectId → BPM Group code via Entra mailNickname or slugified displayName). Group memberships SHALL be replaced wholesale per group (not incrementally diffed at member level — simpler).

#### Scenario: Group upserted

- **WHEN** Entra returns group "Engineering" not yet in BPM
- **AND** IncludeGroups = true
- **THEN** a BPM Group row is created with code derived from mailNickname; ObjectId stored in attributes

#### Scenario: Group membership replaced

- **GIVEN** BPM Group "Engineering" has 3 GroupMember rows
- **AND** Entra now reports 5 members
- **WHEN** sync applies
- **THEN** the BPM table has exactly 5 GroupMember rows for that group; the previous 3 are deleted; new 5 inserted

### Requirement: Per-config locking prevents concurrent syncs

Each `EntraSyncConfiguration` SHALL carry an `IsLocked` flag. Setting it on sync start; clearing on completion. Concurrent sync attempts (scheduled overlapping with on-demand) check the flag and skip if locked.

#### Scenario: Concurrent sync blocked

- **GIVEN** a sync is currently running for config C1; IsLocked = true
- **WHEN** an admin clicks "Run now"
- **THEN** the request returns 409 Conflict with "sync already in progress"

### Requirement: On-demand sync API

`POST /api/admin/entra-sync/{configId}/run?dryRun=true|false` SHALL trigger a sync immediately. With `dryRun=true`, no DB writes; only diff report returned. With `dryRun=false`, transaction commits.

#### Scenario: Admin triggers dry run

- **WHEN** an admin POSTs with dryRun=true
- **THEN** Graph fetched; diff computed; no DB writes; report returned in response body
