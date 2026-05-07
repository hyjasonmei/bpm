## Why

`FormFieldType = 'file'` is in the spec since v1.0 and several mock-up flows already render file inputs (病假證明 in LeaveForm, 報價單 in PURCHASE_PRESET, 收據 in expense flows). But the system has no upload endpoint, no blob storage, no virus scan, no retention policy. Spec authors can declare a file field; users have nowhere to upload to.

For the 9 mock-up flows, file fields are needed in:

- **LEAVE** — 醫師證明 / 公文 (病假, 公假)
- **GEE / GEV / APE / TEO** — 收據 / 發票 (multiple per row, via repeater)
- **HWP / ITPR** — 報價單 (PDF / image)
- **EXTOB** — 員工證件 / 合約

Without file storage, half the partner's flows can't run end-to-end.

## What Changes

### File storage backend (NEW capability `bpm-file-storage`)

**Entity** — `StoredFile`:

- `Id` (Guid) — used as the value stored in `FormData[fieldId]`
- `TenantId`, `OwnerUserId` (uploader)
- `OriginalFileName`, `ContentType` (MIME), `SizeBytes`
- `StorageKey` (string) — backend-specific identifier (S3 key / local path / Azure blob name)
- `Sha256` (hex string) — content hash for dedupe + integrity
- `StorageBackend` (enum): `Local` / `S3` / `AzureBlob`
- `Status` (enum): `Pending` (uploaded but not yet attached to a flow), `Attached` (referenced by a ProcessInstance), `Orphaned` (uploaded but never used; eligible for cleanup), `Deleted` (soft-deleted; binary purged)
- `UploadedAt`, `AttachedAt`, `LastAccessedAt`

**Service** — `IFileStorageService`:

- `Task<StoredFile> UploadAsync(UploadFileCommand cmd, CancellationToken ct)` — accepts stream, owner, content type, name; computes hash, persists binary, inserts row with `Status = Pending`
- `Task<Stream> OpenReadAsync(Guid fileId, Guid actorUserId, CancellationToken ct)` — auth-checks then opens read stream
- `Task<StoredFile> GetMetadataAsync(Guid fileId, ...)` — read-only metadata (no binary)
- `Task MarkAttachedAsync(Guid fileId, Guid processInstanceId, ...)` — flips `Status = Attached`, records the linking instance for retention
- `Task SoftDeleteAsync(Guid fileId, Guid actorUserId, ...)` — admin-only; sets `Status = Deleted` + scheduled binary purge

**Storage backends**:

- `LocalFileStorage` (dev / SQLite POC) — writes to `bpm-svc/data/files/<tenant_id>/<sha256[0..2]>/<sha256>.<ext>`; gitignored
- `S3FileStorage` (future prod) — uses `AWSSDK.S3` with `BPM_S3_BUCKET` + IAM credentials env
- `AzureBlobStorage` (future, customer-on-prem) — Azure SDK

Backend selected via `BPM_FILE_STORAGE_BACKEND=local|s3|azure-blob` env var. Local is default for dev; the LocalFileStorage path is permitted only when `BPM_AUTH_MODE=dev` (prod startup with `BPM_FILE_STORAGE_BACKEND=local` fails fast — too easy to accidentally lose customer data).

### Upload endpoint

`POST /api/files`:

- Multipart form data: file binary + JSON metadata `{ owner_user_id?, intended_field_id?, intended_instance_id? }`
- Auth: any authenticated user
- Limits: 50 MB per file; 200 MB per request total (bpm-svc.csproj `Kestrel:Limits` updated)
- Content-Type whitelist: PDF, common image types (jpg/png/heic/webp), Word/Excel (docx/xlsx), CSV, plain text. Reject executables, archives that could contain executables (zip/rar/7z), application/octet-stream
- Returns: `{ file_id, original_file_name, size_bytes, content_type, sha256, status: 'Pending' }`

`GET /api/files/{id}`:

- Returns metadata (no binary)
- Auth: file owner OR any user authorized to read the ProcessInstance the file is attached to (initiator, current/past assignee, admin)

`GET /api/files/{id}/content`:

- Streams the binary
- Same auth as metadata
- Sets `Content-Disposition: attachment; filename="<original>"` and `Cache-Control: private, max-age=300`

`DELETE /api/files/{id}`:

- Admin only — soft-delete (Status = Deleted, schedule binary purge)
- Returns 200 immediately; actual binary deletion happens via janitor

### Pending → Attached lifecycle

When a user uploads a file (e.g., during form-fill in a userTask), the upload returns `file_id` with `Status = Pending`. The form stores `field_value = file_id` in the form data.

When the userTask is *submitted* (`ProcessRuntime.SubmitTaskAsync`), the runtime walks the form patch for any field whose value matches a `StoredFile.Id` (where `OwnerUserId == actor`) and calls `MarkAttachedAsync(fileId, instanceId)`. This:

- Flips `Status = Pending → Attached`
- Records the linking instance for retention semantics
- Prevents the file from being garbage-collected

Files that are uploaded but never attached (e.g., user uploads then closes the browser) become `Orphaned` after 24 hours and are purged.

### Janitor: orphan cleanup

A `FileStorageJanitor` background service runs daily (00:30 UTC):

- For `Status = Pending` rows where `UploadedAt < now - 24h` → set `Status = Orphaned`
- For `Status = Orphaned` → delete binary from backend, set `Status = Deleted` (rows kept for audit; binary gone)
- For `Status = Deleted` rows older than 90 days → hard-delete row

### Frontend file upload component

`bpm-ui/src/components/forms/FileUploadField.tsx` (NEW):

- Drag-and-drop zone + button
- Shows progress bar (XHR with `progress` event)
- Supports both single-file and multi-file (multi mode = repeater of files)
- Validates client-side: type whitelist, size cap; rejects before upload
- On upload success, returns `file_id` to the parent form; the form persists `field_value = file_id`
- Visual: thumbnail preview for images, file icon + name for documents
- Click opens `GET /api/files/{id}/content` in new tab for download

`bpm-ui/src/lib/files.ts`:

- `uploadFile(file: File, opts) → Promise<StoredFileMeta>`
- `getFileUrl(id) → string` for inline display

### Wizard StepForms — file field affordance

`StepForms` already supports `type: 'file'` in the FieldType dropdown; this change adds:

- Multi-file toggle (single file vs array of files)
- Allowed extension picker (common presets + custom)
- Max size hint (default 10 MB; configurable per field)

### Sample specs

- `leave_v1.json` — `cert` field gains `accept: '.pdf,.jpg,.png'`, `maxSizeMb: 10`
- New `expense_employee_v1.json` (from prior proposals) — repeater with `receipt` file field per row
- New `hardware_purchase_v1.json` — `quote_file` field with `accept: '.pdf'`

### Out of scope (future changes)

- Virus scan integration (ClamAV / VirusTotal API) — defer; document hook point
- File preview rendering inside the form (PDF inline viewer) — viewer only opens external PDF tab
- Image rotation / resize on upload
- Versioning of files (replace with new version) — this change has 1-file-per-id; if user wants to replace, they upload a new file and update the field
- Attachment to email notifications (NotifyTemplate carrying attached file IDs to deliver as email attachments)
- E-signature capture / PDF signing
- Encrypted-at-rest storage (S3 SSE / Azure encryption) — leverage backend's default; no app-level encryption in v1
- Retention by tenant policy (some customers want 7-year retention, others 1) — uniform 90-day-after-soft-delete in v1

## Capabilities

### New Capabilities

- `bpm-file-storage` — StoredFile entity, IFileStorageService, three backend implementations (local/S3/Azure), upload/download/metadata/delete endpoints, content-type whitelist, 50 MB cap, Pending→Attached lifecycle, janitor.

### Modified Capabilities

- `bpm-form-stepper` — StepForms file field gains multi-file toggle, accepted extensions picker, max size hint.
- `bpm-process-runtime` — runtime walks submitted form data for file field IDs and calls `MarkAttachedAsync` to upgrade `Pending → Attached`; runtime tests cover this hook.

## Impact

- **bpm-svc/src/Domain/Entities/Files/StoredFile.cs**: new entity
- **bpm-svc/src/Domain/Entities/Files/FileStatus.cs**: enum
- **bpm-svc/src/Application/Files/IFileStorageService.cs**: interface
- **bpm-svc/src/Application/Files/FileStorageService.cs**: orchestration (auth, hash, lifecycle)
- **bpm-svc/src/Infrastructure/Files/LocalFileStorage.cs / S3FileStorage.cs / AzureBlobStorage.cs**: backends
- **bpm-svc/src/Infrastructure/Files/FileStorageJanitor.cs**: BackgroundService
- **bpm-svc/src/Api/Files/FilesController.cs**: 4 endpoints
- **bpm-svc/src/Persistence/Configurations/Files/StoredFileConfiguration.cs**: EF config + migration `AddFileStorage`
- **bpm-svc/src/Application/Process/Runtime/ProcessRuntime.cs**: extension to call `MarkAttachedAsync` during SubmitTask form patch processing
- **bpm-ui/src/components/forms/FileUploadField.tsx**: new
- **bpm-ui/src/lib/files.ts**: new
- **bpm-ui/src/screens/onboarding/steps/StepForms.tsx**: file field type expanded UI
- **bpm-ui/src/screens/forms/LeaveForm.tsx, GEEForm.tsx, etc.**: NOT modified (mocks stay; rendering of attached files is a future codegen concern, not part of demo)
- **NuGet additions**: AWSSDK.S3 (only loaded when `BPM_FILE_STORAGE_BACKEND=s3`), Azure.Storage.Blobs (likewise)
- **DB migration**: `AddFileStorage` (1 new table, indexes); no changes to existing
- **Disk usage**: local backend writes under `bpm-svc/data/files/`; gitignored; document in README
- **Demo guard**: 9 mock-up forms not modified; new FileUploadField is for future spec-driven forms (form-runtime-rendering change wires it)
