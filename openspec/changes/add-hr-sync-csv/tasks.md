# Tasks

## 1. Domain

- [ ] 1.1 Create `bpm-svc/src/Domain/Entities/OrgImport/ImportRunStatus.cs` enum
- [ ] 1.2 Create `OrgImportRun.cs` entity

## 2. Persistence

- [ ] 2.1 EF config; migration `AddOrgImportRun`
- [ ] 2.2 Indexes: `(TenantId, Status)`, `(InitiatedByUserId, AppliedAt DESC)`

## 3. CSV parsing + mapping

- [ ] 3.1 Add NuGet `CsvHelper` to Application.csproj
- [ ] 3.2 Create `bpm-svc/src/Application/OrgImport/Csv/CsvParser.cs`
- [ ] 3.3 Create `MappingSuggester.cs`: column-name → field guesses
- [ ] 3.4 Tests: various header formats (English, Chinese, mixed); BOM handling

## 4. Diff engine

- [ ] 4.1 Create `bpm-svc/src/Application/OrgImport/DiffEngine.cs`
- [ ] 4.2 For each row: lookup existing User by email; categorize as insert / update / no-op; collect changes
- [ ] 4.3 Department auto-detection: collect unique department_codes, mark unseen ones for insert
- [ ] 4.4 Optional deactivation of missing rows
- [ ] 4.5 Tests with small fixtures: 5 users, 3 departments → expected diff

## 5. Cycle detection

- [ ] 5.1 Create `CycleDetector.cs`
- [ ] 5.2 Build adjacency map from CSV + existing DB
- [ ] 5.3 DFS; report any back-edge as a cycle path
- [ ] 5.4 Tests: A→B→A; A→A self-ref; longer chain A→B→C→D→B; valid (no cycle)

## 6. Service

- [ ] 6.1 Create `IOrgImportService.cs`
- [ ] 6.2 Implement `OrgImportService.cs`:
  - Start: validate file is CSV by content type / extension; create run; auto-detect mapping; status = Uploaded
  - ConfigureMapping: validate mapping covers required fields; status = MappingConfigured
  - DryRun: parse CSV with mapping; run DiffEngine; run CycleDetector; persist DryRunReportJson; status = DryRunComplete
  - Apply: re-run dry-run, compare with stored; if mismatch, abort with concurrency error; otherwise transaction: insert / update users + departments; pipe titles through TitleNormalizer; populate AppliedSummaryJson; status = Applied
  - Cancel: status = Cancelled
- [ ] 6.3 Wire DI

## 7. API endpoints

- [ ] 7.1 Create `bpm-svc/src/Api/OrgImport/OrgImportsController.cs` with 6 endpoints (start / get / list / mapping / dry-run / apply / cancel)
- [ ] 7.2 Auth: tenant_admin
- [ ] 7.3 Integration tests for each (happy + error paths + cycle / mapping mismatch)

## 8. End-to-end verification

- [ ] 8.1 `dotnet build` clean
- [ ] 8.2 All tests pass
- [ ] 8.3 Apply migration; verify OrgImportRuns table
- [ ] 8.4 Test happy path: upload sample CSV (50 employees + 5 departments) → start → mapping → dry-run → review → apply → verify Org tables updated
- [ ] 8.5 Test re-import idempotent: upload same file → dry-run shows zero changes
- [ ] 8.6 Test partial update: change one user's title in CSV → dry-run shows 1 update; apply; verify
- [ ] 8.7 Test cycle: upload CSV with A→B, B→A; dry-run reports cycle; apply blocked
- [ ] 8.8 Test deactivation: upload partial CSV with deactivate_missing=true; verify only listed users active
- [ ] 8.9 **Demo guard**: 9 mock-up forms, Home, Search, Report, lib/workflow.ts NOT modified

## 9. Commit

- [ ] 9.1 Commit in chunks (entity + migration; CSV + mapping; diff + cycle; service; endpoints; verification)
- [ ] 9.2 Push via GitKraken
