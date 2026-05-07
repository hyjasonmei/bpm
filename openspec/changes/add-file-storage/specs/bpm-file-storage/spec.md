## ADDED Requirements

### Requirement: StoredFile records each upload with content hash

The system SHALL persist a `StoredFile` row per upload with `Id`, `TenantId`, `OwnerUserId`, `OriginalFileName`, `ContentType`, `SizeBytes`, `StorageKey`, `Sha256` (SHA-256 hex), `StorageBackend`, `Status`, timestamps. The `Sha256` MUST be computed during upload by streaming the bytes through a hash function — NOT after the binary is written. Two uploads of identical content SHALL produce identical Sha256 values, allowing the backend to dedupe storage by content-addressed paths.

#### Scenario: Two uploads of same file dedupe

- **WHEN** Wilson uploads `receipt.pdf` (content X) at T1
- **AND** Yang uploads a byte-identical `receipt.pdf` at T2
- **THEN** two `StoredFile` rows exist (different Ids, owners, timestamps)
- **AND** both rows have the same Sha256
- **AND** the backend storage location is shared (one binary on disk / S3)

### Requirement: Upload endpoint enforces 50 MB cap and content-type whitelist

`POST /api/files` SHALL reject:

- Files larger than 50 MB with HTTP 413
- Files whose detected MIME type (from magic bytes, not the client header) is not in the allowed whitelist with HTTP 415
- Requests with total body size exceeding 200 MB with HTTP 413

The whitelist for v1 includes: PDF, JPEG, PNG, HEIC, WEBP, DOCX, XLSX, CSV, plain text. Executables, archives, and unknown application/octet-stream MUST be rejected.

#### Scenario: 60 MB file rejected

- **WHEN** a user uploads a 60 MB PDF
- **THEN** response is 413 Payload Too Large with reason "file exceeds 50 MB"

#### Scenario: Disguised executable rejected

- **WHEN** a user uploads a file with extension `.pdf` but magic bytes match an executable
- **THEN** response is 415 with "MIME type does not match content"

### Requirement: Pending files become Attached on form submit

The runtime SHALL transition a `StoredFile` from `Status = Pending` to `Status = Attached` when the file's id appears in a submitted form patch's field values during `ProcessRuntime.SubmitTaskAsync`. The transition MUST be performed by `IFileStorageService.MarkAttachedAsync` and SHALL record `AttachedAt = now`. Files that remain `Pending` for over 24 hours SHALL be transitioned to `Orphaned` by the janitor, and their binaries deleted from the backend.

#### Scenario: Form submit attaches file

- **GIVEN** Wilson uploaded `receipt.pdf` (Status = Pending) and the file id is in a userTask form patch
- **WHEN** the runtime processes SubmitTask with that patch
- **THEN** the file's Status is `Attached`; AttachedAt is set

#### Scenario: Orphaned upload purged

- **GIVEN** Wilson uploaded a file 25 hours ago with Status = Pending; the form was never submitted
- **WHEN** the daily janitor runs
- **THEN** the file's Status is updated to `Orphaned`, the backend binary is deleted, and the next janitor cycle marks it `Deleted`

### Requirement: Read auth granted to instance participants

`OpenReadAsync` and `GetMetadataAsync` SHALL grant access to:

- The file's `OwnerUserId` (always)
- The instance initiator (when file is attached to a ProcessInstance)
- Any user who is or was an actual assignee of any task in the instance
- `tenant_admin` and `flow_admin` roles

Other users receive 403 (NOT 404 — the file existence is acknowledged to authorized parties only). For unauthenticated callers: 401.

#### Scenario: Owner can read

- **WHEN** Wilson reads his own uploaded file
- **THEN** 200 with the binary

#### Scenario: Assignee on the same instance can read

- **GIVEN** Wilson uploaded a receipt attached to his expense instance; Yang is the manager-approver of that instance
- **WHEN** Yang reads the file
- **THEN** 200

#### Scenario: Unrelated user blocked

- **WHEN** a different user (not owner, not on the instance, not admin) tries to read
- **THEN** 403

### Requirement: Local backend forbidden in production

The system SHALL fail startup when `BPM_FILE_STORAGE_BACKEND=local` and `BPM_AUTH_MODE=prod`. The error message MUST clearly direct the operator to choose `s3` or `azure-blob` and provide credentials.

#### Scenario: Prod with local backend fails fast

- **GIVEN** `BPM_AUTH_MODE=prod` and `BPM_FILE_STORAGE_BACKEND=local`
- **WHEN** the application starts
- **THEN** startup throws an exception with message containing "Local file storage is not safe for production"

#### Scenario: Dev with local backend succeeds

- **GIVEN** `BPM_AUTH_MODE=dev` and `BPM_FILE_STORAGE_BACKEND=local`
- **WHEN** the application starts
- **THEN** startup succeeds; LocalFileStorageBackend is registered

### Requirement: Soft-delete admin only; binary purge eventual

`DELETE /api/files/{id}` SHALL succeed only for admin roles. Soft-delete sets `Status = Deleted` immediately and the binary deletion is scheduled for the next janitor cycle. The metadata row persists for 90 days post-deletion (audit) before hard-delete.

#### Scenario: Non-admin delete blocked

- **WHEN** an employee calls DELETE /api/files/{id}
- **THEN** 403

#### Scenario: Admin soft-delete then hard-delete

- **GIVEN** an admin calls DELETE /api/files/{id}
- **WHEN** the next janitor cycle runs
- **THEN** the binary is removed from the backend
- **AND** 90 days later, the StoredFile row itself is hard-deleted
