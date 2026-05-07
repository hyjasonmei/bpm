# Tasks

## 1. Domain + persistence

- [ ] 1.1 Create `EntraSyncConfiguration.cs` entity with encrypted ClientSecret
- [ ] 1.2 EF + migration `AddEntraSyncConfiguration`

## 2. Graph client

- [ ] 2.1 Add NuGet `Microsoft.Graph`
- [ ] 2.2 Create `EntraGraphClient.cs`: authenticate via `ClientSecretCredential`; call /users with $expand=manager paged; call /groups + /groups/{id}/members
- [ ] 2.3 Tests against mocked responses

## 3. Mapper

- [ ] 3.1 Create `EntraToBpmMapper.cs`: maps Graph User → input shape consumed by DiffEngine (same shape as CSV row)
- [ ] 3.2 Maps Graph Group → BPM Group input
- [ ] 3.3 Tests

## 4. Diff engine refactor

- [ ] 4.1 Refactor `DiffEngine` from `add-hr-sync-csv` to accept `IEnumerable<UserSyncRow>` (source-agnostic)
- [ ] 4.2 Both CSV path and Entra path produce `IEnumerable<UserSyncRow>` then feed DiffEngine
- [ ] 4.3 Re-run CSV tests to confirm no regression

## 5. Sync service

- [ ] 5.1 Create `IEntraSyncService.cs / EntraSyncService.cs`
- [ ] 5.2 RunSyncAsync: fetch via Graph, map, diff, optionally apply (transactional)
- [ ] 5.3 EnqueueOnDemandAsync: creates a sync run record + signals worker

## 6. Worker

- [ ] 6.1 Create `EntraSyncWorker.cs` BackgroundService
- [ ] 6.2 Loop: every 30 min, find configs whose LastSyncAt + SyncIntervalHours <= now; run sync
- [ ] 6.3 Per-config locking via row `IsLocked` field
- [ ] 6.4 Audit on each run

## 7. API endpoints

- [ ] 7.1 Create `EntraSyncController.cs`:
  - GET / POST / PUT / DELETE on /api/admin/entra-sync-config
  - POST /api/admin/entra-sync/{configId}/run?dryRun=true|false — manual trigger
  - GET /api/admin/entra-sync/{configId}/runs — list past runs

## 8. Frontend admin UI

- [ ] 8.1 EntraSyncConfigForm
- [ ] 8.2 RunHistoryList
- [ ] 8.3 "Run now" with dry-run toggle
- [ ] 8.4 Result viewer (re-uses CSV dry-run report viewer)

## 9. End-to-end verification

- [ ] 9.1 `dotnet build` clean
- [ ] 9.2 Apply migration
- [ ] 9.3 Configure a test Entra app registration; populate config
- [ ] 9.4 Run dry-run; verify diff against actual Entra users
- [ ] 9.5 Apply; verify Org tables match Entra
- [ ] 9.6 Modify a user in Entra; wait 6 hours (or trigger manually); verify next sync picks up the change
- [ ] 9.7 Disable a user in Entra (accountEnabled = false); next sync sets IsActive = false
- [ ] 9.8 **Demo guard**: 9 mock-up forms NOT modified

## 10. Commit

- [ ] 10.1 Commit in chunks
- [ ] 10.2 Push via GitKraken
