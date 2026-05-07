# Design notes

## 1. Library evaluation

Candidates:

- **QuestPDF** — fluent C# API, modern, reasonable license (Community for one developer; Pro for teams)
- **PuppeteerSharp + headless Chrome** — render HTML to PDF; flexible but adds ~150 MB Chromium dependency
- **DinkToPdf** — wkhtmltopdf wrapper; reliable but unmaintained as of 2024
- **PdfSharp** — pure C#, low-level, hand-build pages

For SME scale (10s-100s of PDFs per day) and reasonable visual quality, QuestPDF or hand-built PdfSharp are the realistic options. Final pick at implementation; QuestPDF preferred for dev velocity if license fits.

## 2. Layout

Standard sections:

```
[ HEADER: tenant logo + name + report title + date ]

Case Information
| Case ID | <id> | Spec | LEAVE v3 |
| Initiator | Wilson | Started | 2026-05-08 |
| Status | Completed | Completed at | 2026-05-12 |

Form Data
[for each userTask submitted, render fields as label/value rows; repeater fields as table]

Approvals
| Step | Approver | Decision | Comment | Date |
| Manager | Yang | Approve | 同意 | 2026-05-09 |
| HR | Mary | Approve | OK | 2026-05-12 |

Timeline
[icon-prefixed event list with timestamps]

Comments
[chronological thread]

[ FOOTER: page x of y, generated at, signed-link disclaimer ]
```

## 3. Bilingual handling

User's locale determines field labels (FormField has `{ 'zh-TW': '...', en: '...' }` per label). For audit purposes, the export option may force bilingual rendering ("zh-TW + en" side-by-side); but v1 ships single-locale only.

## 4. Bulk export queuing

Bulk export runs async (could be 100+ instances → 30+ minutes).

- Admin POST to `/api/admin/pdf/bulk-export` returns job_id
- BulkExportWorker polls PdfExportJobs every 30s, picks up Queued
- Generates each PDF, packages into ZIP
- Stores ZIP via FileStorage (with metadata "PDF bulk export job N")
- Sets job Status = Completed; emits notification to admin
- Admin downloads via `GET /api/jobs/{id}/result` which redirects to the signed file URL

## 5. Signed URL for share

When admin requests a shareable URL: generate PDF, upload to FileStorage, return `/api/files/{id}/content?token=<HMAC>` where the token includes file_id + expiration; backend validates HMAC on every access.

The token is unguessable; expiration is 7 days. Sharing with external auditor / supplier is safe.

## 6. Image embedding for attachments

For each attached file in the instance:

- If image (PNG / JPG / WEBP): inline thumbnail (max 200 px wide) on the form-data section
- Else (PDF / Excel / CSV): list with file name + size + SHA-256 fingerprint

Full binary embedding is opt-in only; defaults off to keep PDF size <2 MB typical.

## 7. Open questions

- **Templating customization**: customer wants their corporate header. Defer; v1 ships static template using tenant branding fields.
- **Multi-language side-by-side**: defer.
- **Print-quality images**: thumbnails are rasterized at fixed dpi; if customer wants high-res, embed full image.
- **Generation perf at scale**: 100 PDFs in one job may take 15-30 min. Acceptable for nightly batch; if need real-time at scale, parallelize the worker (single-threaded for simplicity v1).
