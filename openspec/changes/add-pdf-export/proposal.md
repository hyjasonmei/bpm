## Why

ISO 9001 / IATF 16949 audit cycles require physical (or PDF) records of approval flows. Customers explicitly ask: "怎麼把這份請假單印出來給稽核？" Today the system has no PDF generation — admins screenshot or export raw JSON.

Plus: customers want to email a "完整 case 記錄" to a supplier, contractor, or external auditor. Plain JSON is not consumable for non-technical readers.

This change ships PDF export of completed (or in-flight) ProcessInstances with:

- Header (logo, customer name, case id, spec name, status)
- Form data section (each userTask's submitted fields with labels in user's locale)
- Approval section (each approval with approver, decision, comment, timestamp)
- Notification log section
- TaskHistory timeline
- Comments thread

## What Changes

### PDF generator (NEW capability `bpm-pdf-export`)

**Service** `IPdfExportService`:

- `Task<Stream> ExportInstanceAsync(Guid instanceId, ExportOptions, CancellationToken ct)` — produces PDF binary

**Implementation**: use `QuestPDF` (.NET PDF library, MIT-equivalent licence for community / one-developer use, or paid for commercial; we'll evaluate; alternatives: PuppeteerSharp + headless Chrome, or DinkToPdf; or hand-build via PdfSharp). Decision deferred to library evaluation phase.

The output is paginated, bilingual (zh-TW + en column when present), with section headings, tables for form fields, timeline for history.

**ExportOptions**:
- `IncludeFormData` (default true)
- `IncludeApprovals` (default true)
- `IncludeNotifications` (default false — usually noisy)
- `IncludeHistory` (default true)
- `IncludeComments` (default true; respects IsInternal — admin generates with all, employee gets non-internal only)
- `Locale` ('zh-TW' | 'en'; default user's preferred)
- `IncludeAttachedFiles` (default false; when true, embeds inline thumbnails for image files and a list-with-checksums for non-image files; full files NOT inlined as binary attachments — keeps PDF size sane)

### API endpoints

- `GET /api/processes/{id}/pdf` — generates and streams PDF; auth: instance reader; query params for ExportOptions
- `POST /api/processes/{id}/pdf/share` — generates PDF, uploads to file storage, returns a shareable URL with expiration (admin-only; for emailing to external party); URL is signed (HMAC) with 7-day expiration

### Bulk export

- `POST /api/admin/pdf/bulk-export` — admin-only; body `{ instance_ids: [...], options }`; queues a background job; returns job_id; job produces a ZIP of PDFs and stores in file storage; admin downloads via `GET /api/jobs/{id}/result`

### Branding

The PDF header uses tenant-config'd branding:
- Tenant name (from tenant config)
- Logo (uploaded as a tenant-level file via System Admin UI)
- Color scheme (default neutral)

### Out of scope (future changes)

- Visual template customization (custom CSS / layout)
- WYSIWYG template editor for the PDF
- Multi-language side-by-side (zh + en in same PDF)
- Digital signature on the PDF (e.g., gov-issued e-stamps)
- ZUGFeRD / Factur-X compliant invoice PDFs
- Inline embedding of large attachments (would balloon size)
- PDF/A archival format (use stdlib output for now)
- Automated print-to-printer integration

## Capabilities

### New Capabilities

- `bpm-pdf-export` — IPdfExportService, instance PDF generation, bulk export with queued background job, signed shareable URLs, branding from tenant config.

### Modified Capabilities

- None — consumes existing entities + APIs.

## Impact

- **bpm-svc/src/Domain/Entities/Pdf/PdfExportJob.cs**: tracks bulk export jobs (entity)
- **bpm-svc/src/Application/Pdf/IPdfExportService.cs / PdfExportService.cs**: generation
- **bpm-svc/src/Application/Pdf/PdfTemplate/**: helper classes / view models per section
- **bpm-svc/src/Infrastructure/Pdf/QuestPdfRenderer.cs** (or chosen lib): rendering
- **bpm-svc/src/Api/Pdf/PdfController.cs**: endpoints
- **bpm-svc/src/Infrastructure/Pdf/BulkExportWorker.cs**: BackgroundService for queued bulk jobs
- **bpm-ui/src/lib/pdf.ts**: client functions
- **bpm-ui/src/screens/processes/completed/CompletedCaseDetail.tsx**: "Export PDF" button
- **bpm-ui/src/screens/admin/imports/...**: bulk export tab in admin
- **NuGet**: chosen PDF library (~5-15 MB; license review)
- **DB migration**: 1 new table (PdfExportJobs)
- **Demo guard**: 9 mock-up forms NOT modified
