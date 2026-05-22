## ADDED Requirements

### Requirement: Bundles load into an isolated scratch tenant

The system SHALL provide an `IBundleRuntimeLoader` that, given a `ParsedBundle`, creates a freshly-named scratch tenant `repro-{flowCode}-{checksumShort}`, seeds the bundle's `sample-org.json` users / groups / departments under that tenant id, registers the bundle's `spec.json` with the runtime spec store under the scratch tenant scope, and returns a disposable `LoadedBundleHandle`.

The loader MUST NOT touch any non-scratch tenant data. The naming convention `repro-*` is the canonical safety check: any cleanup operation MUST refuse to delete a tenant whose name does not start with `repro-`.

#### Scenario: Loader creates scratch tenant

- **GIVEN** a parsed bundle with `flowCode = "LEAVE"` and `manifestChecksum = "abc123def..."`
- **WHEN** `BundleRuntimeLoader.LoadAsync` runs
- **THEN** a new tenant exists named `repro-LEAVE-abc123de` (8-char prefix of checksum)
- **AND** `sample-org.json`'s users are inserted under that tenant
- **AND** `spec.json` is registered with `flowCode=LEAVE` under that tenant
- **AND** the returned handle's `ScratchTenantId` matches the new tenant

#### Scenario: Cleanup refuses non-scratch tenants

- **GIVEN** a programming error passes a tenant id whose name is `acme-prod` to the cleanup hook
- **WHEN** cleanup is invoked
- **THEN** it throws `InvalidOperationException` with message containing "refuses to delete non-scratch tenant 'acme-prod'"

#### Scenario: Handle dispose cleans up

- **GIVEN** a `LoadedBundleHandle` with scratch tenant `repro-LEAVE-abc123de`
- **WHEN** the handle is disposed
- **THEN** the scratch tenant and all its users / groups / departments / process instances are deleted
- **AND** all SpecBundle rows for that scratch tenant are unaffected (they live under the real tenant)

### Requirement: Reproducibility runner asserts node-trace equality per test case

`IBundleReproducibilityRunner.RunAsync` SHALL execute every entry in the bundle's `test-cases/*.json` against a `LoadedBundleHandle` by:

1. Calling `IProcessRuntime.StartInstanceAsync` with the test case's input form data
2. Driving the resulting `ProcessInstance` to completion by submitting form data per userTask in order
3. Collecting the actual node trace from `TaskHistory` after completion
4. Comparing the actual trace to the test case's `expectedTrace`, ignoring timestamps and assignee user ids (compare role/path resolution shape, not specific guids since sample-org users are placeholders)

The runner SHALL produce a `ReproReport` with one `CaseResult` per test case and an `OverallStatus` that is `Pass` only if every case passes.

#### Scenario: All cases pass

- **GIVEN** a bundle with two test-cases, both whose actual trace equals their expected trace
- **WHEN** `RunAsync` executes
- **THEN** `OverallStatus = Pass`
- **AND** every CaseResult has `Status = Pass`
- **AND** every CaseResult has `Diff = null`

#### Scenario: One case has trace divergence

- **GIVEN** a bundle whose test-case `happy.json` expects `[start, task_apply, approval_manager, end]`
- **AND** the actual run produces `[start, task_apply, end]` (no manager node spawned)
- **WHEN** `RunAsync` executes
- **THEN** `OverallStatus = Fail`
- **AND** the CaseResult for happy.json has `Status = Fail`
- **AND** `Diff` contains "expected approval_manager between task_apply and end; actual went straight to end"

#### Scenario: Trace equality ignores timestamps

- **GIVEN** an expected trace and an actual trace identical in node order but differing in TaskHistory timestamps
- **WHEN** the runner compares
- **THEN** the comparison treats them as equal

#### Scenario: Trace equality ignores assignee user ids

- **GIVEN** expected trace records `approval_manager → assignee Wilson@123` (from the source instance's sample-org)
- **AND** actual trace records `approval_manager → assignee Wilson@456` (different guid in target instance)
- **WHEN** the runner compares
- **THEN** the comparison treats them as equal because both resolve via `submitter.manager` against the same logical sample-org seed

### Requirement: Repro outcome gates Installed status

The Flow Library import endpoint with `mode=install` SHALL set the new SpecBundle row's `Status = Pending`, run the reproducibility runner, then set `Status = Installed` if `ReproReport.OverallStatus = Pass` or `Status = Failed` otherwise. The `LastReproCheckResultJson` column SHALL persist the full `ReproReport` for the detail view to render.

A bundle MUST NOT be `Installed` without a passing reproducibility report attached. The only state where Installed exists without a successful repro report is `InstalledUnverified`, used exclusively when `add-process-runtime` is not available in the current build (the runner is stubbed and returns "runtime not available").

#### Scenario: Failing repro blocks Installed

- **GIVEN** an import in mode=install whose runner returns OverallStatus = Fail
- **WHEN** the import completes
- **THEN** the SpecBundle row has `Status = Failed`
- **AND** `Status = Installed` is never reached

#### Scenario: Runtime unavailable produces InstalledUnverified

- **GIVEN** the build does not include `add-process-runtime` (runtime API absent)
- **WHEN** an import in mode=install runs
- **THEN** the runner returns "runtime not available"
- **AND** the SpecBundle row has `Status = InstalledUnverified`
- **AND** the Flow Library list view shows a warning badge for that row

### Requirement: End-to-end cross-instance reproducibility

The system SHALL pass an integration test where bundle X is built on instance A's database, exported, imported into a separate clean instance B's database, and the bundled test-cases produce the same `ProcessInstance.SpecSnapshotJson` and node trace on B as on A. This is the product-level acceptance criterion of this change and SHALL run as a CI suite.

#### Scenario: Two-instance round trip

- **GIVEN** instance A designs LEAVE via the 9-stepper and exports `LEAVE_v1.zip`
- **AND** instance B is a freshly initialized bpm-svc + bpm-admin-ui with an empty database
- **WHEN** instance B imports `LEAVE_v1.zip` in mode=install
- **AND** the reproducibility runner executes
- **THEN** every test case produces identical `ProcessInstance.SpecSnapshotJson` to instance A
- **AND** every test case produces identical node trace to instance A
- **AND** the bundle is marked `Installed` on instance B
