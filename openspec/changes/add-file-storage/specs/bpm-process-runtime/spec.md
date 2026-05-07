## ADDED Requirements

### Requirement: SubmitTask attaches files referenced in form patch

`ProcessRuntime.SubmitTaskAsync` SHALL inspect the submitted form patch JSON for any field value that resolves to a `StoredFile.Id` owned by the actor with `Status = Pending`. For each matched file id, the runtime SHALL call `IFileStorageService.MarkAttachedAsync(fileId, instanceId)` within the same DB transaction as the state mutation. Files NOT referenced in the patch retain their previous status.

#### Scenario: File field id triggers attach

- **GIVEN** Wilson uploaded `receipt.pdf` resulting in fileId R; Status = Pending
- **AND** he submits a userTask with form patch `{ receipt: R }`
- **WHEN** runtime processes SubmitTaskAsync
- **THEN** R's Status flips to Attached; AttachedAt is the submit timestamp

#### Scenario: Multi-file array attaches all

- **GIVEN** Wilson uploaded 3 receipt files with ids R1, R2, R3 (all Pending)
- **AND** he submits a userTask with form patch `{ receipts: [R1, R2, R3] }`
- **THEN** all three files transition to Attached

#### Scenario: Foreign file id rejected silently

- **GIVEN** Wilson submits a patch carrying a fileId owned by Yang
- **WHEN** runtime processes the patch
- **THEN** the Yang-owned file is NOT attached (ownership mismatch); the submit still succeeds (the field value is preserved as a stale id, surfaced to admin via the audit; this prevents accidental ownership transfer)
