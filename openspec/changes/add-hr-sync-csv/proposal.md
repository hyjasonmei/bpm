## Why

Today the Org model (User / Department / Group / Principal) is populated by `OrgFixture.cs` — a hand-edited C# file. For a real customer onboarding, we need:

1. **Importer**: customer uploads a CSV (or Excel) of employees + departments + manager_id → system inserts/updates the Org tables
2. **Field mapping**: spec_schema.md §2.8 already defines `integrations.fieldMappings` — the mapping is per-customer (employee id might be `sAMAccountName`, `empId`, `staff_no`, etc.)
3. **Dry-run**: see what would change before committing
4. **Idempotency**: re-uploading after HR updates produces minimal diff (insert / update / soft-delete)
5. **Title normalization**: pipe titles through the `TitleNormalizer` from `extend-actor-and-org-for-ai-routing` automatically

Without a sync workflow, every customer onboarding involves manual SQL or hand-edited fixtures. Doesn't scale.

This change ships the importer + dry-run + diff workflow. AD / Entra ID auto-sync is a separate change (`add-mcp-entra-sync`).

## What Changes

### HR sync capability (NEW `bpm-hr-sync`)

**Entity** — `OrgImportRun`:

- `Id`, `TenantId`, `InitiatedByUserId`, `Status` (enum: `Uploaded` / `MappingConfigured` / `DryRunComplete` / `Applied` / `Failed` / `Cancelled`)
- `SourceFileId` (FK StoredFile — the uploaded CSV)
- `MappingJson` — the field mapping the user configured for this import
- `DryRunReportJson` — diff summary
- `AppliedAt`, `AppliedSummaryJson` (counts: inserted / updated / soft-deleted)
- `Error` (when Failed)

**Service** `IOrgImportService`:

- `Task<OrgImportRun> StartImportAsync(StartImportCommand cmd)` — accepts a StoredFile id, creates an import run; auto-detects column headers; returns suggested mapping
- `Task<OrgImportRun> ConfigureMappingAsync(...)` — user/admin confirms the field mapping (employee_id_column, name_column, manager_email_column, department_code_column, title_column, is_active_column)
- `Task<DryRunReport> DryRunAsync(importRunId)` — parses CSV, simulates inserts/updates/soft-deletes against current Org tables; produces `{ inserts: [...], updates: [...], deactivations: [...], errors: [...] }` (no DB writes)
- `Task ApplyAsync(importRunId)` — runs the import for real; transactional; idempotent (re-running same import is no-op)
- `Task CancelAsync(importRunId)`

**Workflow**:

```
Admin uploads CSV → /api/files (returns fileId)
  ↓
Admin starts import → /api/org-imports body { fileId } → returns runId + suggested mapping
  ↓
Admin reviews / corrects mapping → /api/org-imports/{runId}/mapping body { mapping } 
  ↓
Admin requests dry-run → POST /api/org-imports/{runId}/dry-run → returns diff report
  ↓
Admin reviews diff → confirms → POST /api/org-imports/{runId}/apply
  ↓
Service inserts new Users, updates existing, soft-deletes those missing (IsActive = false; not hard-deleted)
```

### Field mapping configuration

The mapping defines which CSV column maps to which User / Department field:

```json
{
  "csv_to_user": {
    "user.email": "Email",          // the CSV column header
    "user.full_name": "Full Name",
    "user.title_raw": "Job Title",
    "user.manager_email": "Reports To",
    "user.department_code": "Dept",
    "user.is_active": "Active"
  },
  "csv_to_department": {
    "department.code": "DeptCode",
    "department.name": "DeptName",
    "department.parent_code": "ParentDept",
    "department.head_email": "DeptHead"
  }
}
```

The CSV may have all columns in one file (employees with department info inline) or two files (one for employees, one for departments). Single-file mode auto-derives departments from unique `department_code` values.

### Suggested mapping detection

On upload, the service inspects column headers and suggests common mappings:

- `email` / `Email` / `信箱` → `user.email`
- `name` / `Full Name` / `姓名` → `user.full_name`
- `manager` / `Reports To` / `主管` → `user.manager_email`
- `department` / `Dept` / `部門` → `user.department_code`
- `title` / `Job Title` / `職稱` → `user.title_raw`
- `active` / `IsActive` / `在職` → `user.is_active`

The user can override any suggestion.

### Idempotent diff semantics

For each parsed row:

1. Lookup existing User by email (the immutable key)
2. If not found → INSERT (counted in `inserts`)
3. If found AND any mapped field differs → UPDATE (counted in `updates`)
4. If found AND no field differs → no-op

After processing all CSV rows: rows in DB whose email is NOT in the CSV are *not auto-deactivated* by default (avoid mass deactivation if HR forgot a team). Admin can enable `?deactivate_missing=true` opt-in to soft-delete (set IsActive = false) on missing.

### Title normalization

The importer pipes `title_raw` through `TitleNormalizer.Normalize()` and persists `title_normalized`. Both are stored.

### Department auto-creation

If the CSV references a department code not yet in the DB, the importer creates a stub Department row (with `Name = code`, no parent, no function_tag). The admin then enriches via `/api/departments/{id}` (existing endpoints from System Admin UI later).

### Cycle / data integrity checks

- Manager loop: A reports to B reports to A → reject the import with error pointing to the cycle
- Self-reference: A reports to A → reject
- Department parent loop: same check
- Dangling manager email (manager email references a row not in the CSV nor in DB): warning (allow), but the user gets manager_id = null

### API endpoints

- `POST /api/org-imports` — start import; body: `{ fileId, twoFile?: { employees, departments } }`
- `GET /api/org-imports` — list past runs
- `GET /api/org-imports/{id}` — single run with current state
- `PUT /api/org-imports/{id}/mapping` — set / update mapping
- `POST /api/org-imports/{id}/dry-run` — produce diff
- `POST /api/org-imports/{id}/apply` — commit diff to DB
- `POST /api/org-imports/{id}/cancel`

Auth: all `tenant_admin` only.

### Frontend

UI to drive this lives in `add-system-admin-ui`. This proposal ships the API; admin UI consumes it.

### Out of scope (future changes)

- Excel `.xlsx` upload (CSV only in v1)
- Real-time AD / Entra ID sync (separate `add-mcp-entra-sync`)
- Scheduled re-import (cron)
- Per-row audit linking original CSV row to the resulting User row
- Undo apply (restore from a snapshot)
- Multi-file complex imports (e.g., separate users / departments / groups files)
- Email change as primary key migration helper (if customer's HR changes email format wholesale)
- Group / role import via CSV (only User + Department in v1)

## Capabilities

### New Capabilities

- `bpm-hr-sync` — OrgImportRun entity, IOrgImportService (start / configureMapping / dry-run / apply / cancel), CSV parsing + suggested mapping detection, idempotent diff, integrity checks (manager cycle, dept parent cycle), department auto-creation, title normalization integration.

### Modified Capabilities

- `bpm-org-model` — formalize that User.email is the immutable identity key for upsert semantics; document the soft-delete pattern (IsActive = false, never hard-delete via this importer).

## Impact

- **bpm-svc/src/Domain/Entities/OrgImport/OrgImportRun.cs**: new entity
- **bpm-svc/src/Domain/Entities/OrgImport/ImportRunStatus.cs**: enum
- **bpm-svc/src/Application/OrgImport/IOrgImportService.cs / OrgImportService.cs**: orchestration
- **bpm-svc/src/Application/OrgImport/Csv/CsvParser.cs**: header detection + row iteration
- **bpm-svc/src/Application/OrgImport/MappingSuggester.cs**: column-name → field guesser
- **bpm-svc/src/Application/OrgImport/DiffEngine.cs**: produces dry-run report
- **bpm-svc/src/Application/OrgImport/CycleDetector.cs**: validates no manager / dept loops
- **bpm-svc/src/Persistence/Configurations/OrgImport/**: EF config; migration `AddOrgImportRun`
- **bpm-svc/src/Api/OrgImport/OrgImportsController.cs**: 6 endpoints
- **NuGet**: `CsvHelper` (industry-standard, ~200 KB; MIT license)
- **No new frontend** — admin UI in `add-system-admin-ui`
- **DB migration**: 1 new table
- **Demo guard**: 9 mock-up forms NOT modified
