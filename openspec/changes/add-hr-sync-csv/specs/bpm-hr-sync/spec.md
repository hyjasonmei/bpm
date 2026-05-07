## ADDED Requirements

### Requirement: OrgImportRun tracks each import lifecycle

The system SHALL persist an `OrgImportRun` entity per import attempt. The run tracks status transitions: `Uploaded` → `MappingConfigured` → `DryRunComplete` → `Applied` (or `Failed` / `Cancelled` at any point). Each run carries: `SourceFileId` (the StoredFile carrying the CSV), `MappingJson` (column → field map), `DryRunReportJson` (computed diff), `AppliedSummaryJson` (counts after apply), `InitiatedByUserId`, timestamps.

#### Scenario: Run progresses through statuses

- **WHEN** an admin uploads a CSV → run created at `Uploaded`
- **AND** sets mapping → status `MappingConfigured`
- **AND** triggers dry-run → status `DryRunComplete` with report
- **AND** applies → status `Applied` with summary

#### Scenario: Apply without dry-run rejected

- **WHEN** an admin tries to call apply on a run still at status `MappingConfigured`
- **THEN** the response is 400 with "must run dry-run before apply"

### Requirement: Mapping suggester proposes likely column-to-field mappings

The system SHALL parse CSV headers and propose a default mapping based on common column names. Recognized patterns include English (`Email`, `Full Name`, `Reports To`, `Department`, `Job Title`, `Active`) and Chinese (`信箱`, `姓名`, `主管`, `部門`, `職稱`, `在職`) variants. The admin SHALL be able to override any suggestion.

#### Scenario: English headers auto-mapped

- **GIVEN** a CSV with headers `["Email", "Full Name", "Reports To", "Department", "Job Title"]`
- **WHEN** the import starts
- **THEN** the suggested mapping covers all five fields

#### Scenario: Chinese headers auto-mapped

- **GIVEN** a CSV with headers `["信箱", "姓名", "主管信箱", "部門代碼", "職稱"]`
- **WHEN** the import starts
- **THEN** the suggested mapping resolves to (email, full_name, manager_email, department_code, title_raw)

#### Scenario: Unknown header left unmapped

- **GIVEN** a CSV with header `"員工星座"` (zodiac sign)
- **WHEN** the import starts
- **THEN** that column is not auto-mapped; admin can leave it unmapped or push into User.attributes

### Requirement: Dry-run produces structured diff without DB writes

The dry-run operation SHALL parse the CSV using the configured mapping and produce a `DryRunReport` with:

- `imports_csv_rows`: count
- `inserts`: list of new Users to create
- `updates`: list of changed Users with field-level diffs
- `deactivations`: list of Users missing from CSV (only when `deactivate_missing` flag set)
- `department_inserts`: auto-created stub Departments
- `errors`: row-level errors (manager not found, etc.) — these block apply
- `warnings`: non-blocking (e.g., dangling manager email)

NO database writes occur during dry-run.

#### Scenario: Dry-run reports updates only

- **GIVEN** existing user wilson@x.com with title_raw = "VP"
- **WHEN** dry-run runs against a CSV showing wilson with title_raw = "Senior VP"
- **THEN** the report has 1 update with `changes: { title_raw: ["VP", "Senior VP"] }`

#### Scenario: Dry-run preserves DB state

- **WHEN** dry-run completes
- **THEN** the Users table is byte-identical to before; no rows mutated

### Requirement: Apply is transactional and idempotent

The apply operation SHALL perform all DB writes within a single transaction. Re-applying the same import (same CSV, same mapping, no other changes in DB) SHALL produce zero new diff (idempotent). Apply MUST detect concurrent writes by re-running dry-run and comparing against the stored report; mismatch aborts apply.

#### Scenario: Apply commits all changes atomically

- **GIVEN** dry-run reports 5 inserts and 2 updates
- **WHEN** apply runs
- **THEN** all 7 rows are written in a single transaction; AppliedSummaryJson reflects 5 + 2

#### Scenario: Idempotent re-apply

- **GIVEN** an apply just completed
- **WHEN** the admin re-uploads the same CSV → starts a new import → dry-run
- **THEN** the new dry-run shows zero changes

#### Scenario: Concurrent change abort

- **GIVEN** dry-run was completed at T0
- **AND** another admin manually edits a User between T0 and apply
- **WHEN** apply runs
- **THEN** the apply re-runs dry-run, detects the divergence, aborts with `ConcurrentChangeException`

### Requirement: Cycle detection blocks import

The diff engine SHALL detect manager cycles (A reports to B reports to A) and department parent cycles (Dept A's parent is Dept B; Dept B's parent is Dept A). Detected cycles SHALL be added to the report's `errors` list with the cycle path. Apply MUST be blocked when errors are non-empty.

#### Scenario: Manager cycle blocks import

- **GIVEN** a CSV with rows wilson reports to yang AND yang reports to wilson
- **WHEN** dry-run runs
- **THEN** the report has an error "manager cycle detected: wilson@x.com → yang@x.com → wilson@x.com"
- **AND** apply is blocked

#### Scenario: Self-reference blocks

- **GIVEN** wilson reports to wilson
- **WHEN** dry-run runs
- **THEN** the report has error "self-reference: wilson@x.com is own manager"

### Requirement: Department auto-creation as stubs

If the CSV references a department code with no existing Department row, the importer SHALL create a stub Department with `Name = department_code`, no parent, no function_tag, no head. The admin enriches via separate endpoints.

#### Scenario: Stub department on first reference

- **GIVEN** no Department with code "PROD" exists
- **WHEN** apply processes a User row with department_code = "PROD"
- **THEN** a Department row is inserted with `Code = "PROD", Name = "PROD"`; the User row's department_id points to it

### Requirement: Title normalization applied during apply

For every User row inserted or updated, the apply phase SHALL pipe `title_raw` through `TitleNormalizer.Normalize()` and persist `title_normalized`. The behavior matches the standalone `normalize-titles` CLI command described in `extend-actor-and-org-for-ai-routing`.

#### Scenario: Title normalized on apply

- **GIVEN** a CSV row with `title_raw = "資深副總"`
- **WHEN** apply commits the row
- **THEN** the persisted User has `title_normalized = "vp"` (per the normalizer's rules)

### Requirement: Default behavior does not deactivate missing rows

By default, Users present in the DB but absent from the CSV SHALL NOT be deactivated. Admin MUST opt in via `?deactivate_missing=true` query parameter on apply. The dry-run report MUST surface deactivation candidates only when this flag is set, so the admin sees the consequences before apply.

#### Scenario: Default partial CSV preserves others

- **GIVEN** existing 87 users in DB
- **AND** CSV has 10 rows
- **WHEN** apply runs WITHOUT deactivate_missing flag
- **THEN** the 10 users are processed (insert / update); the other 77 remain unchanged

#### Scenario: Opt-in deactivation marks IsActive=false

- **GIVEN** the same 87 users; CSV has 10
- **WHEN** apply runs WITH `deactivate_missing=true`
- **THEN** the 77 missing users have `IsActive = false`; their rows are NOT deleted; ActorResolver excludes them
