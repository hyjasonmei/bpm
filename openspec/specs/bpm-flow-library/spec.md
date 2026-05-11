# bpm-flow-library Specification

## Purpose
TBD - created by archiving change add-spec-bundle-and-flow-library. Update Purpose after archive.
## Requirements
### Requirement: Flow Library is a top-level admin screen

The system SHALL add a `Flow Library` entry to `bpm-admin-ui`'s admin navigation, positioned between `Onboarding` and `Site Settings`. Access SHALL require admin role (the same gate that protects existing admin screens via `isAdmin` in `lib/jwt.ts`).

#### Scenario: Non-admin sees no library nav

- **GIVEN** a user authenticated with persona `manager` (not admin)
- **WHEN** they load `bpm-admin-ui`
- **THEN** the existing `NoPermission` screen renders
- **AND** the Flow Library nav entry is not exposed

### Requirement: Library lists every bundle for the current tenant

The list view SHALL fetch from `GET /api/admin/flow-library` and render one row per bundle showing: `flowCode`, `flowVersion`, `status` (Draft / Pending / Installed / InstalledUnverified / Failed / SoftDeleted), `exportedAt`, `lastReproCheckAt`, and `lastReproResult` (Pass/Fail badge or em-dash if never run). Soft-deleted bundles SHALL be hidden by default with a "Show deleted (n)" toggle.

#### Scenario: Two versions of the same flowCode

- **GIVEN** the tenant has bundles `LEAVE v1 (Installed)` and `LEAVE v2 (InstalledUnverified)`
- **WHEN** the user opens Flow Library
- **THEN** both rows render
- **AND** v2 is sorted above v1 (descending by flowVersion)

### Requirement: Bundle detail view exposes every file

A bundle detail view (route reachable by clicking a row) SHALL display tabs for each file kind in the bundle: Manifest, spec.json, bpmn.xml (rendered via the existing `BpmnDiagram` component), spec.md (rendered as markdown), forms (one sub-tab per `forms/*.json`), notifications, sla, actors, sample-org, test-cases (one sub-tab per case, showing inputs and expectedTrace), assets (image previews if any).

Tabs for file kinds not present in the bundle SHALL be hidden (not shown as empty/disabled).

#### Scenario: Bundle without assets

- **GIVEN** a bundle whose manifest does not include any `Asset`-kind file
- **WHEN** the user opens detail view
- **THEN** the Assets tab is not rendered

### Requirement: Import accepts mode = install or draft

The import dialog SHALL accept a drag-dropped `.zip` and require the user to choose between two modes:

- `Install for runtime` — POST to `/api/admin/flow-library/import?mode=install`. Triggers parse → validate → load into scratch tenant → run reproducibility. Bundle status transitions to `Pending` synchronously, then `Installed` (all test-cases pass) or `Failed` (any fail) once the runner completes.
- `Open in 9-stepper as draft` — POST to `/api/admin/flow-library/import?mode=draft`. The server returns a hydration payload (DraftSpec + sample-org + test-cases); the UI navigates to Onboarding with `?bundle={id}` and the Onboarding screen rehydrates state from it. No `SpecBundle` row is persisted in this mode (the user can re-export later, which creates a new row with `parent` pointing at the source).

#### Scenario: Install of valid bundle

- **GIVEN** a well-formed `LEAVE_v1.zip` whose test-cases all pass on a clean instance
- **WHEN** the user drops it and chooses `Install for runtime`
- **THEN** a new SpecBundle row is created with `Status = Pending`
- **AND** the reproducibility runner executes
- **AND** within 30 seconds the row transitions to `Status = Installed`
- **AND** the list view auto-refreshes to show the new row

#### Scenario: Install of bundle whose test-cases fail

- **GIVEN** a `LEAVE_v1.zip` whose `test-cases/happy.json` expects trace `[start, task_apply, approval_manager, end]` but the spec actually routes through `[start, task_apply, end]` (no manager approval node defined)
- **WHEN** the user installs it
- **THEN** the runner records a Fail
- **AND** the row transitions to `Status = Failed`
- **AND** clicking the row shows the diff in the Test Cases tab

#### Scenario: Open as draft hydrates Onboarding

- **GIVEN** a bundle X exists with one userTask whose `formCode` is `LEAVE_FORM`
- **WHEN** the user imports X as draft
- **THEN** the Onboarding screen opens
- **AND** `DraftSpec.userTasks` contains the same userTask
- **AND** the URL contains `?bundle={X.id}`
- **AND** the user lands on the first step whose validator currently fails (or `go_live` if all pass)

### Requirement: Export streams the original zip bytes

`GET /api/admin/flow-library/{id}/export` SHALL stream the persisted `ZipBlob` with `Content-Disposition: attachment; filename="{flowCode}_v{flowVersion}.zip"` and `Content-Type: application/zip`. The bytes returned MUST be byte-identical to the bytes originally accepted on import or generated on build (no re-zip on export).

#### Scenario: Round-trip preserves checksum

- **GIVEN** bundle X was imported with manifest checksum `abc123`
- **WHEN** the user exports X and re-imports the downloaded zip on another instance
- **THEN** the new bundle's manifest checksum is also `abc123`

### Requirement: Repro check can be re-run on demand

`POST /api/admin/flow-library/{id}/repro-check` SHALL re-run `BundleReproducibilityRunner` against an already-installed bundle and update `LastReproCheckAt` and `LastReproCheckResultJson`. The bundle's `Status` SHALL transition to `Failed` if the re-run fails, even if the bundle was previously `Installed`.

This exists because the underlying runtime semantics may change (a future code change might introduce a regression that breaks an installed bundle); the on-demand check lets operators verify that previously-known-good bundles still reproduce.

#### Scenario: Regression detected by re-run

- **GIVEN** bundle X is `Installed` with `LastReproResult = Pass`
- **AND** a code change breaks how `submitter.manager` is resolved
- **WHEN** an admin clicks Repro Check on X
- **THEN** the runner reports Fail
- **AND** the bundle transitions to `Status = Failed`

### Requirement: Delete is soft-delete

`DELETE /api/admin/flow-library/{id}` SHALL set `Status = SoftDeleted` rather than removing the row. Soft-deleted bundles SHALL NOT appear in the default list, SHALL NOT be loadable for runtime install, and SHALL NOT be selectable as a draft import source. They MAY be revealed via the "Show deleted (n)" toggle.

#### Scenario: Soft-deleted bundle hidden by default

- **GIVEN** bundle X exists with `Status = SoftDeleted`
- **WHEN** the admin loads Flow Library
- **THEN** X does not appear in the row list
- **AND** the toggle "Show deleted (1)" is shown
- **AND** clicking the toggle reveals X with a strikethrough style

