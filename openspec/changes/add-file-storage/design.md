# Design notes

## 1. Why a separate `StoredFile` table rather than blob columns

Blob columns (BLOB in SQLite, VARBINARY(MAX) in SQL Server) work for tiny files but:

- Bloat row size, slow down SELECTs that don't need the binary
- Can't easily migrate to S3 / Azure later
- Make backups expensive

Separate table + storage backend abstraction = flexible. Local filesystem for POC; S3 for prod when we scale.

## 2. Why content hash before storage

Computing SHA-256 on upload allows:

- **Dedupe** — two identical receipts uploaded twice (common in expense flows when user re-uploads) reuse the same backend object
- **Integrity** — the hash is recorded; a future backup-restore can verify
- **Idempotency** — uploading the same file twice produces the same `Sha256` value, can short-circuit re-upload

The cost is one streaming hash computation during upload (~20 MB/s on commodity disk). Accept the cost for the audit and dedupe benefits.

For dedupe, the storage backend uses content-addressed paths: `<tenant>/<sha[0..2]>/<sha>.<ext>`. If the file already exists, skip re-write. The `StoredFile` row is still inserted (one logical file per upload) but the physical bytes are shared.

## 3. Pending → Attached lifecycle

Why split? Because users upload files speculatively:

- They click "Choose file" → upload begins immediately
- They never submit the form (close browser, change mind)
- Without lifecycle, those files stick forever — orphaned data + GDPR liability

With lifecycle:

- Upload → `Pending` (ephemeral)
- Form submit → `Attached` (durable)
- Daily janitor purges `Pending > 24h`

The `MarkAttachedAsync` hook fires during `ProcessRuntime.SubmitTaskAsync` after the form patch is applied. This change extends the runtime to call it.

## 4. Content-type whitelist — security

Permitted MIME types (and matching extensions enforced):

| Category | MIME | Extensions |
|---|---|---|
| PDF | application/pdf | .pdf |
| Images | image/jpeg, image/png, image/heic, image/webp | .jpg, .png, .heic, .webp |
| Word | application/vnd.openxmlformats-officedocument.wordprocessingml.document | .docx |
| Excel | application/vnd.openxmlformats-officedocument.spreadsheetml.sheet | .xlsx |
| CSV | text/csv | .csv |
| Plain text | text/plain | .txt |

Rejected: any executable, archive (.zip, .rar, .7z, .tar.gz), application/octet-stream (catch-all unknown), HTML (XSS vector if served back un-sanitized).

The whitelist is enforced at upload time. The MIME is detected from the file's magic bytes (using `Mime-Detective` or hand-rolled signature check) — NOT from the client-supplied Content-Type header (which is forgeable).

If a customer eventually needs Word .doc (legacy 2003) or other formats, extend the whitelist explicitly with risk review. No `application/octet-stream` ever.

## 5. Authorization model

Files are owned by the uploader. Read access is granted to:

- The owner (always)
- Users who can read the ProcessInstance the file is attached to:
  - The instance initiator
  - The current or past assignee of any task in that instance
  - tenant_admin, flow_admin

When a file is `Pending` (not yet attached to an instance), only the owner can read it.

The auth check happens in `IFileStorageService.OpenReadAsync` — the controller delegates without making its own decisions, so a future reuse (e.g., file referenced from a notification email) inherits the same auth rules.

## 6. Local backend safety in production

`BPM_FILE_STORAGE_BACKEND=local` MUST fail startup when `BPM_AUTH_MODE=prod`. Local disk is acceptable for dev because losing data in dev is a nuisance; in production it's a data loss event.

The startup check raises a clear error: `"Local file storage is not safe for production. Set BPM_FILE_STORAGE_BACKEND=s3 (or azure-blob) and provide credentials."`

## 7. Why janitor scheduling at 00:30 UTC

The notification dispatch worker runs continuously. The delegation refresh job runs at 00:05. The file storage janitor runs at 00:30 — a quiet hour, separated from other periodic tasks. If the bpm-svc instance is down at 00:30, next-day catch-up.

Production deployments may run multiple instances; the janitor uses a `JanitorLock` table row pattern (single instance acquires lock, others skip the cycle) to avoid duplicate work. Defer multi-instance handling until we run multiple instances in prod.

## 8. Soft-delete vs hard-delete

- Soft-delete: `Status = Deleted`, binary purged, row kept (with original_file_name for audit "what was this referenced as?")
- Hard-delete: row removed entirely, after 90 days

The 90-day window aligns with typical SME audit windows. Customers with longer retention requirements ask explicitly; we make per-tenant policy a future enhancement.

## 9. File size 50 MB rationale

Receipts and quotes are typically < 5 MB. Engineering specs / contracts can hit 20 MB. A 50 MB ceiling covers all standard SME flows and forces noisy outliers (videos, full project archives) into a separate channel. If a customer needs > 50 MB, we discuss design; chunked upload is a different feature.

## 10. Open questions

- **CDN**: should authenticated GET responses go through a CDN with signed URLs (S3 presigned, Azure SAS)? Yes, eventually — saves bpm-svc bandwidth. v1 streams through the API; v2 returns signed URLs from `OpenReadAsync` when the backend supports it.
- **Virus scan**: defer. Hook point: `IFileScanner` interface, default `NoOpScanner`, swap to ClamAV later.
- **Pseudonymization for support**: when an engineer opens a customer file to debug, audit the access. Defer — log only `OpenReadAsync` calls with actor + file_id for now.
- **File-attached-to-multiple-instances** (e.g., a shared template referenced by many flows): not in v1. Each `MarkAttachedAsync` records the *first* attachment; subsequent attachments to other instances are recorded in a separate audit (not a separate StoredFile row).
