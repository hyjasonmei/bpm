# Tasks

## 1. Library selection

- [ ] 1.1 Evaluate QuestPDF / PuppeteerSharp / PdfSharp on: license, perf, output quality, AOT compat
- [ ] 1.2 Pick library; add NuGet
- [ ] 1.3 Document choice in design.md §1 update

## 2. PDF service

- [ ] 2.1 Create `IPdfExportService.cs`
- [ ] 2.2 Implement `PdfExportService.cs`:
  - Loads instance + spec snapshot + form data + tasks + history + comments
  - Builds view models per section
  - Renders via library
  - Returns Stream
- [ ] 2.3 Implement section renderers: Header, CaseInfo, FormData (with repeater table), Approvals, Timeline, Comments, Footer
- [ ] 2.4 Tests with snapshot fixtures

## 3. Single-instance export endpoint

- [ ] 3.1 `GET /api/processes/{id}/pdf` with query params for ExportOptions
- [ ] 3.2 Auth: instance reader (initiator / assignee / admin)
- [ ] 3.3 Streams PDF response with `Content-Type: application/pdf, Content-Disposition: attachment`

## 4. Shareable URL

- [ ] 4.1 `POST /api/processes/{id}/pdf/share` admin only; generates PDF, uploads to FileStorage, returns signed URL
- [ ] 4.2 HMAC token validation in FilesController for `?token=` query param
- [ ] 4.3 7-day expiration

## 5. Bulk export

- [ ] 5.1 `PdfExportJob` entity (Id, TenantId, RequestedByUserId, InstanceIds (JSON), Status, ResultFileId, RequestedAt, CompletedAt)
- [ ] 5.2 Migration
- [ ] 5.3 `POST /api/admin/pdf/bulk-export` body `{ instance_ids, options }` enqueues
- [ ] 5.4 `BulkExportWorker` BackgroundService picks up Queued, generates each PDF, ZIPs, stores in FileStorage
- [ ] 5.5 `GET /api/jobs/{id}` returns job state; `GET /api/jobs/{id}/result` redirects to signed file URL when Completed
- [ ] 5.6 Notification to admin on Completed

## 6. Branding

- [ ] 6.1 Tenant config: Logo (file upload via System Admin UI), display_name, primary_color
- [ ] 6.2 PDF template reads from tenant config; falls back to neutral defaults

## 7. Frontend

- [ ] 7.1 `bpm-ui/src/lib/pdf.ts` client functions
- [ ] 7.2 "Export PDF" button on CompletedCaseDetail
- [ ] 7.3 Bulk export tab in System Admin or Process Admin (TBD)
- [ ] 7.4 Job progress UI

## 8. End-to-end verification

- [ ] 8.1 `dotnet build` clean
- [ ] 8.2 Boot stack; complete a LEAVE instance; export PDF; verify content (header, form data, approval row, timeline)
- [ ] 8.3 Generate share link; access via signed URL externally; verify works for 7 days; expires after
- [ ] 8.4 Bulk export 5 instances; verify ZIP contains 5 PDFs
- [ ] 8.5 Verify bilingual fields render in user's locale
- [ ] 8.6 Verify file attachments shown as thumbnails for images, list otherwise
- [ ] 8.7 **Demo guard**: 9 mock-up forms NOT modified

## 9. Commit

- [ ] 9.1 Commit in chunks (lib + service; endpoints + share; bulk worker; branding; frontend; verification)
- [ ] 9.2 Push via GitKraken
