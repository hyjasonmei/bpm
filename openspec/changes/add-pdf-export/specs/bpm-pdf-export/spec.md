## ADDED Requirements

### Requirement: Single-instance PDF export endpoint

The system SHALL expose `GET /api/processes/{id}/pdf` that generates and streams a PDF report of the ProcessInstance. Auth: instance reader (initiator / past or current assignee / admin). The PDF SHALL include sections: Header (tenant branding + case ID + spec name), Case Info, Form Data, Approvals, Timeline, Comments. Internal-only comments SHALL be excluded for non-admin requesters.

#### Scenario: Initiator exports own case

- **WHEN** Wilson calls `GET /api/processes/{id}/pdf` for his own LEAVE instance
- **THEN** the response is application/pdf streaming a PDF containing Wilson's submission, the manager's approval, the HR archive

#### Scenario: Internal comments excluded for non-admin

- **GIVEN** the instance has 3 internal comments (from admin)
- **WHEN** Wilson exports the PDF
- **THEN** the Comments section omits the internal comments

#### Scenario: Admin sees all

- **WHEN** an admin exports the same case
- **THEN** the Comments section includes the internal comments (marked with 🔒 chip)

### Requirement: Shareable signed URL with expiration

The system SHALL expose `POST /api/processes/{id}/pdf/share` (admin-only) that generates the PDF, uploads to file storage, and returns a signed URL with `?token=<HMAC>` valid for 7 days. The token MUST be HMAC-validated on every access by the files controller; expired tokens return 410 Gone.

#### Scenario: Share link works for external

- **WHEN** an admin generates a share link for a case
- **AND** an external user (not authenticated) opens the link
- **THEN** the PDF is accessible without login

#### Scenario: Expired link rejected

- **GIVEN** a share link generated 8 days ago
- **WHEN** anyone accesses it
- **THEN** the response is 410 Gone with message "share link expired"

### Requirement: Bulk export queues a background job

`POST /api/admin/pdf/bulk-export` SHALL accept up to 100 instance ids per request, create a `PdfExportJob` row with Status = Queued, and return the job id. The `BulkExportWorker` BackgroundService SHALL pick up Queued jobs, generate one PDF per instance, package as ZIP, upload to file storage, and set Status = Completed. On completion, an in-app notification SHALL be sent to the requesting admin with a link to download.

#### Scenario: 50-instance bulk export

- **WHEN** an admin requests bulk export of 50 instances
- **THEN** a job is created Status = Queued
- **AND** within 30 minutes, the worker completes the job; admin receives a notification

#### Scenario: Cap at 100 per request

- **WHEN** an admin requests 150 instances
- **THEN** the response is 400 with message "max 100 instances per bulk export"

### Requirement: Branding from tenant config

The PDF header SHALL render tenant branding sourced from tenant configuration: tenant display_name, optional logo (uploaded as a tenant-level file), and a default neutral color scheme. When tenant config has no logo, a text-only header is rendered.

#### Scenario: Tenant logo on header

- **GIVEN** tenant Acme has uploaded a logo file
- **WHEN** any PDF is generated for Acme
- **THEN** the header shows the logo image

#### Scenario: No logo gracefully degrades

- **GIVEN** tenant has not uploaded a logo
- **WHEN** PDF is generated
- **THEN** the header shows tenant display_name as text only; no broken-image artifact

### Requirement: Repeater fields render as tables in PDF

When the form data contains a repeater field (per `extend-field-types-line-items`), the PDF Form Data section SHALL render that field as a `<table>` with one column per sub-field and one row per item. Per-row derived sub-fields are computed and shown.

#### Scenario: Expense items table

- **GIVEN** a GEE instance with `expense_items = [{ category: '餐費', amount: 350 }, { category: '計程車', amount: 280 }]`
- **WHEN** the PDF renders
- **THEN** the Form Data section includes a table with two rows showing the line items
