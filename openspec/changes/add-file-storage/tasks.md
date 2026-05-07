# Tasks

## 1. Domain entity

- [ ] 1.1 Create `bpm-svc/src/Domain/Entities/Files/FileStatus.cs` enum (Pending, Attached, Orphaned, Deleted)
- [ ] 1.2 Create `bpm-svc/src/Domain/Entities/Files/StorageBackend.cs` enum (Local, S3, AzureBlob)
- [ ] 1.3 Create `bpm-svc/src/Domain/Entities/Files/StoredFile.cs` (inherits AuditableEntity); columns per proposal

## 2. Persistence

- [ ] 2.1 Create `bpm-svc/src/Persistence/Configurations/Files/StoredFileConfiguration.cs`; index `(OwnerUserId, Status, UploadedAt DESC)`, `(Sha256)` (for dedupe lookup), `(Status, UploadedAt) WHERE Status='Pending'` (janitor poll)
- [ ] 2.2 Add DbSet<StoredFile> to BpmDbContext
- [ ] 2.3 Generate migration `AddFileStorage`; apply locally; verify schema

## 3. Storage backends

- [ ] 3.1 Create `IFileStorageBackend` interface: `WriteAsync(stream, key, ct)`, `OpenReadAsync(key, ct)`, `DeleteAsync(key, ct)`, `ExistsAsync(key, ct)`
- [ ] 3.2 Implement `LocalFileStorageBackend.cs`:
  - Root path from config `Files:Local:RootPath` (default `bpm-svc/data/files`)
  - File path: `{root}/{tenantId}/{sha256[0..2]}/{sha256}.{ext}`
  - Atomic write via temp file + rename
- [ ] 3.3 Implement `S3FileStorageBackend.cs` (only loaded when `BPM_FILE_STORAGE_BACKEND=s3`):
  - Reads `BPM_S3_BUCKET`, IAM credentials from env / IMDSv2
  - Object key: `{tenantId}/{sha256[0..2]}/{sha256}.{ext}`
  - Server-side encryption: SSE-S3 default
- [ ] 3.4 Implement `AzureBlobStorageBackend.cs` (only loaded when `BPM_FILE_STORAGE_BACKEND=azure-blob`):
  - Reads `BPM_AZURE_STORAGE_CONNECTION` env
  - Container per tenant
- [ ] 3.5 Add NuGet `AWSSDK.S3` to Infrastructure.csproj (only when needed) — use conditional package reference
- [ ] 3.6 Add NuGet `Azure.Storage.Blobs` similarly
- [ ] 3.7 DI registration in `Infrastructure/DependencyInjection.cs`: switch on `BPM_FILE_STORAGE_BACKEND`; fail fast on `local` + `BPM_AUTH_MODE=prod`

## 4. Service layer

- [ ] 4.1 Create `bpm-svc/src/Application/Files/IFileStorageService.cs`:
  - `UploadAsync(UploadFileCommand) → StoredFile`
  - `OpenReadAsync(fileId, actorUserId) → Stream`
  - `GetMetadataAsync(fileId, actorUserId) → StoredFile`
  - `MarkAttachedAsync(fileId, processInstanceId)`
  - `SoftDeleteAsync(fileId, actorUserId)`
- [ ] 4.2 Implement `FileStorageService.cs`:
  - Upload: stream to backend with simultaneous SHA-256 hashing; check dedupe via `Sha256` index; write metadata row
  - OpenRead: auth check (owner OR instance reader OR admin); call backend
  - MarkAttached: idempotent; record AttachedAt timestamp
  - SoftDelete: admin-only; status flip + scheduled binary purge
- [ ] 4.3 Implement `IMimeTypeDetector` using `Mime-Detective` NuGet (or hand-rolled signature checker for the 7 whitelisted types)
- [ ] 4.4 Wire DI in `Application/DependencyInjection.cs`

## 5. API endpoints

- [ ] 5.1 Create `bpm-svc/src/Api/Files/FilesController.cs`:
  - `POST /api/files` — multipart upload; auth: any logged-in user; 50 MB cap; whitelist check; returns metadata
  - `GET /api/files/{id}` — metadata only; auth: owner / instance reader / admin
  - `GET /api/files/{id}/content` — stream binary; same auth
  - `DELETE /api/files/{id}` — admin only; soft-delete
- [ ] 5.2 Update `bpm-svc/src/Api/Program.cs` Kestrel limits: `MaxRequestBodySize = 200 MB`, `MaxRequestHeaderTotalSize = 32 KB`
- [ ] 5.3 Integration tests for each endpoint
  - Successful upload; assert dedupe (upload same file twice → one backend object, two rows)
  - Reject .exe (not in whitelist)
  - Reject 60 MB file (over cap)
  - Reject Content-Type mismatch (PDF magic in a .png-named file)
  - Read by non-owner non-reader → 403
  - Soft-delete by non-admin → 403

## 6. Janitor

- [ ] 6.1 Create `bpm-svc/src/Infrastructure/Files/FileStorageJanitor.cs` (BackgroundService)
- [ ] 6.2 Daily at 00:30 UTC:
  - Pending older than 24h → Orphaned
  - Orphaned → delete from backend, set Deleted (binary gone, row stays)
  - Deleted older than 90 days → hard-delete row
- [ ] 6.3 Single-instance lock via `JanitorLocks` table row (skip for now — only run one instance in dev/prod for v1)
- [ ] 6.4 Register hosted service; gated on `BPM_FILE_JANITOR=on` (default on; off in tests)

## 7. ProcessRuntime hook

- [ ] 7.1 Extend `ProcessRuntime.SubmitTaskAsync`: after applying the form patch, walk the patch JSON for any field whose value matches a `StoredFile.Id` (where Status = Pending and OwnerUserId = actor)
- [ ] 7.2 For each matched fileId: call `IFileStorageService.MarkAttachedAsync(fileId, instanceId)`
- [ ] 7.3 Test: submit a form patch carrying a Pending file id; verify after submit Status = Attached

## 8. Frontend — file upload component

- [ ] 8.1 Create `bpm-ui/src/lib/files.ts`: `uploadFile(file, opts)`, `getFileUrl(id)`, type definitions
- [ ] 8.2 Create `bpm-ui/src/components/forms/FileUploadField.tsx`:
  - Drag-and-drop zone via HTML5 `dragover` / `drop` events
  - Click-to-select fallback
  - Multi-file support (when `multiple={true}`)
  - Client-side validation: type, size; reject before upload
  - XHR with progress callback for upload progress bar
  - On success: thumbnail (image) or file-icon + name (other); click to download
  - Bilingual labels
- [ ] 8.3 Create `bpm-ui/src/components/forms/FilePreview.tsx`: small reusable card

## 9. Wizard StepForms — file field

- [ ] 9.1 Update `StepForms.tsx`: when field type = file, show extra controls:
  - Multi-file toggle
  - Allowed extensions multi-select (presets: PDF only, images only, documents only, custom CSV)
  - Max size (MB) numeric input
- [ ] 9.2 Persist into FormField shape: extend the type with `accept?: string` (CSV of extensions), `maxSizeMb?: number`, `multiple?: boolean`

## 10. Sample specs

- [ ] 10.1 Update `sample_specs/leave_v1.json` cert field: `accept: '.pdf,.jpg,.png'`, `maxSizeMb: 10`
- [ ] 10.2 Add `sample_specs/expense_employee_v1.json` if not yet present (from prior proposals): with repeater `expense_items` containing `receipt: file`
- [ ] 10.3 Add `sample_specs/hardware_purchase_v1.json` with `quote_file` (required, PDF only)

## 11. End-to-end verification

- [ ] 11.1 `dotnet build` clean
- [ ] 11.2 All backend tests pass
- [ ] 11.3 Apply migration; verify StoredFiles table
- [ ] 11.4 Boot bpm-svc with `BPM_FILE_STORAGE_BACKEND=local`; upload a PDF via curl; verify response has file_id; verify file exists at `bpm-svc/data/files/<tenant>/<hash>/<hash>.pdf`
- [ ] 11.5 Upload same PDF twice; verify dedupe (one backend file, two metadata rows)
- [ ] 11.6 Try to upload an .exe; verify 400 with whitelist error
- [ ] 11.7 Verify GET /content streams the file correctly + Content-Disposition header
- [ ] 11.8 Boot bpm-ui; manually mount FileUploadField in a test page; verify drag-drop works
- [ ] 11.9 Run a full ProcessInstance with a userTask submitting a file field; verify Status flips Pending → Attached after submit
- [ ] 11.10 Test janitor: insert a 25-hour-old Pending row; trigger janitor manually (`POST /api/admin/run-file-janitor` dev endpoint or wait); verify status flipped to Orphaned then Deleted with binary gone
- [ ] 11.11 **Demo guard**: `forms/*` mock-up flows NOT modified; `Home`, `Search`, `Report`, `lib/workflow.ts` not modified

## 12. Commit

- [ ] 12.1 Commit in chunks (entity + migration; backends; service + API; janitor; frontend component; wizard; samples; verification)
- [ ] 12.2 Push via GitKraken
