## ADDED Requirements

### Requirement: Bundle is a single zip with a manifest.json entry point

The system SHALL define a "spec bundle" as a single `.zip` file. Every bundle MUST contain a top-level `manifest.json` file. Loaders MUST read `manifest.json` first; any file in the zip whose path is not enumerated under `manifest.files[]` MUST be ignored (forward-compatibility for additive schema evolution).

The manifest MUST include:

- `bundleSchemaVersion` (int) — currently `1`
- `flowCode` (string)
- `flowVersion` (int)
- `exportedAt` (ISO-8601 UTC)
- `sourceInstanceId` (string) — identifier of the originating bpm-svc deployment
- `parent` (string|null) — sha256 of the parent bundle's `manifest.json`, or null for a root bundle
- `files` (array) — each entry: `{ path, sha256, sizeBytes, kind }`

#### Scenario: Loader reads manifest first

- **GIVEN** a bundle zip with `manifest.json` listing 6 files
- **WHEN** the parser opens it
- **THEN** the parser reads `manifest.json` first
- **AND** loads each listed file by path
- **AND** verifies each file's actual sha256 matches the manifest entry

#### Scenario: Unlisted file ignored

- **GIVEN** a bundle whose zip contains `policies/audit.json` but `manifest.files[]` does not list it
- **WHEN** the parser opens the bundle
- **THEN** parsing succeeds
- **AND** `policies/audit.json` is not loaded
- **AND** an Info-level log records "ignored unlisted file: policies/audit.json"

#### Scenario: Unknown schema version refused

- **GIVEN** a bundle with `bundleSchemaVersion = 99`
- **WHEN** the parser opens it
- **THEN** the parser throws `BundleParseException` with message containing "unknown bundle schema version 99"

### Requirement: Bundle MUST contain spec.json, bpmn.xml, spec.md, sample-org.json, and at least one test-case

The system SHALL refuse to build a bundle that does not include all of: `spec.json`, `bpmn.xml`, `spec.md`, `sample-org.json`, and at least one entry under `test-cases/`. These files are the floor for reproducibility — without them a clean target instance cannot render the same forms (`spec.json`, `bpmn.xml`), explain itself to a human (`spec.md`), resolve any ActorRef (`sample-org.json`), or be checked for behavioral equality (`test-cases/`).

Each entry under `test-cases/{id}.json` MAY include two optional arrays — `expectedNotifications[]` and `expectedWebhooks[]` — that the reproducibility runner asserts against rows in `SandboxCapturedMessages` after the case completes:

- `expectedNotifications[i].notificationId` (string|null) — when set, MUST equal `OriginatingNotificationId` on the captured row (exact match).
- `expectedNotifications[i].subjectContains` (string|null) — when set, MUST be a case-sensitive substring of the captured email's `Subject`.
- `expectedNotifications[i].recipientUserEmails` (string[]|null) — when set, every entry MUST appear (string-array containment, no org-graph resolution) in the captured row's `IntendedRecipientsJson` array.
- `expectedWebhooks[i].subscriptionId` (string|null) — when set, MUST equal `OriginatingWebhookSubscriptionId` (exact match).
- `expectedWebhooks[i].eventType` (string|null) — when set, MUST equal the captured `EventType` (exact match).
- `expectedWebhooks[i].payloadSchema` (object|null) — reserved for a future structural-diff implementation; loaders MUST accept and forward but SHOULD NOT fail when the field is present.

A case fails when any expected entry has no matching captured row. Bundles without these fields run unchanged (null = no assertion).

#### Scenario: Builder rejects empty test-cases

- **GIVEN** a build request with `testCases = []`
- **WHEN** `BundleBuilder.BuildAsync` is invoked
- **THEN** it throws `BundleBuildException`
- **AND** the exception's errors[] contains "test-cases: at least one test case is required"

#### Scenario: Builder rejects missing sample-org

- **GIVEN** a build request with `sampleOrg = null`
- **WHEN** `BundleBuilder.BuildAsync` is invoked
- **THEN** it throws `BundleBuildException`
- **AND** the exception's errors[] contains "sample-org: required"

#### Scenario: Notification assertion missing surfaces case failure

- **GIVEN** a bundle whose only test case sets `expectedNotifications: [{ subjectContains: "永遠不會出現的字串" }]`
- **WHEN** `BundleReproducibilityRunner.RunAsync` executes the case against the live runtime
- **THEN** the case's `NotificationAssertions[0].Passed` is false
- **AND** the case's `Status` is `Fail`
- **AND** the report's `OverallStatus` is `Fail`

### Requirement: Bundle file integrity is verified per-file via sha256

The system SHALL compute sha256 for every file written into the bundle and record it in `manifest.files[].sha256`. The parser SHALL recompute sha256 on each loaded file and reject any whose computed value differs from the manifest entry.

#### Scenario: Tampered file detected

- **GIVEN** a bundle that has been opened, `spec.json` modified, then re-zipped with the *original* manifest
- **WHEN** the parser loads the bundle
- **THEN** the parser throws `BundleParseException` with message containing "checksum mismatch for spec.json"

### Requirement: Cross-file consistency validated post-parse

The system SHALL run a `BundleValidator` after parsing that asserts cross-file consistency:

- Every `userTask.id` in `spec.json` has a matching `forms/{userTaskId}.json`
- Every `actorRef` referenced anywhere in `spec.json` resolves successfully against the union of `actors.json` and `sample-org.json`
- Every `node.id` referenced in any `test-cases/*.json` exists in `spec.json`
- Every `notification.id` referenced in `spec.json` triggers has a matching `notifications/{notificationId}.json`

Validation failures MUST be reported as a structured `BundleValidationResult` with one entry per violation, not as exceptions.

#### Scenario: ActorRef references a path that sample-org cannot resolve

- **GIVEN** spec.json uses `actorRef.expr "submitter.manager.manager"`
- **AND** sample-org.json contains users with at most one level of manager edge
- **WHEN** the validator runs
- **THEN** the result contains an error: "actorRef 'submitter.manager.manager' cannot be resolved against sample-org (max manager depth: 1)"

#### Scenario: Test case references nonexistent node

- **GIVEN** spec.json defines nodes `[start, task_apply, end]`
- **AND** test-cases/leave_happy.json expectedTrace contains `approval_manager`
- **WHEN** the validator runs
- **THEN** the result contains an error: "test case leave_happy: expectedTrace references unknown node 'approval_manager'"

### Requirement: Bundle size and asset limits

The system SHALL enforce a per-bundle size limit of 25 MB total uncompressed and a per-asset size limit of 5 MB. `BundleBuilder` MUST refuse to emit a bundle exceeding either limit.

#### Scenario: Asset over 5MB rejected

- **GIVEN** a build request with `assets[].source-diagram.png` of 6 MB
- **WHEN** `BundleBuilder.BuildAsync` runs
- **THEN** it throws `BundleBuildException` with message containing "asset size_exceeded source-diagram.png 6291456 > 5242880"

### Requirement: Parent pointer enables lineage

The system SHALL set `manifest.parent = null` for a freshly designed flow with no parent, and SHALL set `manifest.parent = sha256(parentManifest)` when a bundle is rebuilt after being imported as a 9-stepper draft. This forms a unidirectional lineage chain that the Flow Library can render as a version tree without storing diffs explicitly.

#### Scenario: Re-export from draft sets parent pointer

- **GIVEN** bundle X exists with `manifestChecksum = "abc123"`
- **WHEN** a user imports X as draft, edits one form field, and re-exports
- **THEN** the new bundle Y has `manifest.parent = "abc123"`

