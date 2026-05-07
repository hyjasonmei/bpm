## Why

CSV import (`add-hr-sync-csv`) gets us onboarding-day setup. But going forward:

- Customer's HR system updates — new hires, transfers, terminations — need to flow into BPM without manual CSV re-uploads
- Org chart drifts daily; CSV cadence (weekly?) lags
- Manual upload is "another thing IT must remember to do"

Real solution: scheduled / triggered sync from a live identity source. Microsoft Entra ID (Azure AD) is the dominant choice for the SME segment we target — most customers already use it for Microsoft 365.

This change ships:

- Scheduled sync (every 6 hours) pulling User + Group + manager_id from Entra via Microsoft Graph
- On-demand sync trigger from admin UI
- Diff + apply pattern (same as CSV) — preview changes, then commit
- Title normalizer integration

The same plumbing extends to other providers (Google Workspace via Admin SDK, Okta) in future changes. v1 ships Entra only.

## What Changes

### MCP Entra sync capability (NEW `bpm-mcp-entra-sync`)

**Configuration** — `EntraSyncConfiguration`:

- `Id`, `TenantId`, `EntraTenantId` (Microsoft tenant guid)
- `ClientId`, `ClientSecret` (encrypted; or use managed identity if running in Azure)
- `SyncIntervalHours` (default 6; 0 = on-demand only)
- `UserFilter` — Graph $filter to scope which users to sync (e.g., `accountEnabled eq true`)
- `GroupFilter` — same for groups
- `EmailDomainFilter[]` — only sync users whose email matches; defense-in-depth
- `IncludeGroups` (bool) — also sync Entra groups → BPM Groups
- `IsActive`

**Service** `IEntraSyncService`:

- `RunSyncAsync(configId, syncMode)` — `syncMode = DryRun | Apply`
- `GetSyncRunsAsync(configId)` — list past runs
- `EnqueueOnDemandAsync(configId)` — admin trigger

**Worker** `EntraSyncWorker` (BackgroundService):

- Polls EntraSyncConfigurations table; picks rows where `LastSyncAt + SyncIntervalHours <= now`
- For each: runs `RunSyncAsync(configId, Apply)`
- Records EntraSyncRun row with diff + summary

### Sync run flow

```
EntraSyncWorker wakes →
  picks config →
  fetches Users + Groups via Microsoft Graph (paged) →
  diffs against current Org tables →
  produces inserts / updates / deactivations →
  applies changes in one transaction →
  records EntraSyncRun
```

### Diff semantics

Reuse the CSV diff engine pattern with adaptations:

- Email is identity key (same as CSV)
- Manager link via Graph's `manager` relationship → resolved to manager_email
- Department resolved via Graph's `department` field
- Title from `jobTitle` field
- IsActive from `accountEnabled`
- Title normalizer applied as in CSV

### Group sync

When `IncludeGroups = true`:

- Pull Entra groups matching GroupFilter
- For each: upsert into BPM `Groups` table (mapping Entra ObjectId → BPM Group code)
- Memberships: pull each group's members; sync to GroupMember rows

### On-demand sync

`POST /api/admin/entra-sync/{configId}/run?dryRun=true|false` — admin triggers manually; returns sync run id; UI polls for completion.

### Reuse OrgImport entities

Avoid table duplication: this change uses the same `OrgImportRun` entity schema (from `add-hr-sync-csv`) but with `Source = "EntraSync"` discriminator. The diff + apply path is shared; only the data fetch differs.

### Out of scope (future changes)

- Google Workspace integration
- Okta / OneLogin / generic SCIM
- Real-time event-based sync via Entra change notifications (push-based; defer to v2)
- User photo sync
- Org chart visual sync (sync all hierarchy changes vs just deltas)
- Conflict resolution UI for ambiguous matches (e.g., two users with same email — should never happen)
- Group membership webhooks (push from Entra when groups change)

## Capabilities

### New Capabilities

- `bpm-mcp-entra-sync` — EntraSyncConfiguration entity, IEntraSyncService, EntraSyncWorker (BackgroundService), Microsoft Graph client integration, scheduled + on-demand sync, diff reuse with `bpm-hr-sync` patterns, title normalization integration.

### Modified Capabilities

- `bpm-hr-sync` — formalize the diff + apply abstraction so both CSV and Entra paths share the engine.

## Impact

- **bpm-svc/src/Domain/Entities/EntraSync/EntraSyncConfiguration.cs**: new
- **bpm-svc/src/Application/EntraSync/IEntraSyncService.cs / EntraSyncService.cs**: orchestration
- **bpm-svc/src/Application/EntraSync/EntraGraphClient.cs**: thin wrapper around Microsoft.Graph SDK
- **bpm-svc/src/Application/EntraSync/EntraToBpmMapper.cs**: maps Graph User → BPM User shape
- **bpm-svc/src/Infrastructure/EntraSync/EntraSyncWorker.cs**: BackgroundService
- **bpm-svc/src/Application/OrgImport/DiffEngine.cs**: refactored to be source-agnostic (CSV + Entra both feed it)
- **bpm-svc/src/Persistence/Configurations/EntraSync/**: EF + migration `AddEntraSyncConfiguration`
- **bpm-svc/src/Api/EntraSync/EntraSyncController.cs**: 5 admin endpoints
- **bpm-ui/src/screens/admin/entra-sync/**: config form + run history + on-demand trigger
- **NuGet**: `Microsoft.Graph` (~5 MB, official SDK)
- **DB migration**: 1 new table
- **Demo guard**: 9 mock-up forms NOT modified
