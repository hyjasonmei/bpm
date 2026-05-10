## ADDED Requirements

### Requirement: Tenant carries branding fields on settings

The system SHALL extend `TenantSettings` with three nullable branding fields:

- `BrandLogoBase64` (text) — base64-encoded image data
- `BrandLogoMimeType` (string, max 50) — `image/png`, `image/jpeg`, `image/svg+xml`, `image/webp`
- `BrandSystemName` (string, max 50) — text shown next to the logo

All fields default to null. With every field null, the UI SHALL render the **bundled default logo asset** (a neutral preset SVG shipped inside the UI bundle, no DB row required) plus the bundled default system name "BPM System". This keeps the experience non-blank while we (or the tenant) have not yet uploaded a custom logo.

#### Scenario: Defaults when nothing configured

- **GIVEN** a fresh database with no branding fields set
- **WHEN** any UI loads
- **THEN** the header shows the bundled default logo asset and "BPM System" — non-blank, looks intentional, not a placeholder

#### Scenario: System name override only

- **GIVEN** an admin sets `BrandSystemName = "ACME 流程管理"` but never uploads a logo
- **WHEN** any UI loads
- **THEN** the header shows the bundled default logo asset (still) and "ACME 流程管理" text

#### Scenario: Custom logo + name

- **GIVEN** an admin uploads `BrandLogoBase64` (an ACME PNG) and sets `BrandSystemName = "ACME"`
- **WHEN** any UI loads
- **THEN** the header shows the uploaded ACME logo and the "ACME" text

### Requirement: GET /api/branding is publicly accessible

The endpoint `GET /api/branding` SHALL be `[AllowAnonymous]` and return a `BrandingDto` with: `logoDataUrl?`, `systemName?`. Unset fields SHALL be null in the response. If `BrandLogoBase64` is set, the response includes a fully-constructed `logoDataUrl` field of the form `data:<mime>;base64,<base64>` for direct use in `<img src>`. The frontend uses null `logoDataUrl` as the signal to render the bundled default asset.

#### Scenario: Anonymous read

- **GIVEN** no JWT in the request
- **WHEN** caller hits `GET /api/branding`
- **THEN** the response is 200 with `{ logoDataUrl, systemName }` (each may be null)

#### Scenario: Image embedded as data URL

- **GIVEN** `BrandLogoBase64 = "iVBORw0KGgo..."`, `BrandLogoMimeType = "image/png"`
- **WHEN** caller hits `GET /api/branding`
- **THEN** the response includes `logoDataUrl: "data:image/png;base64,iVBORw0KGgo..."`

### Requirement: PUT /api/branding requires admin role

The endpoint `PUT /api/branding` SHALL be `[Authorize(Roles = "admin")]`. Non-admin callers MUST receive 403. The body accepts any subset of `{ logoBase64, logoMimeType, systemName, removeLogo }`. `removeLogo: true` SHALL clear both `BrandLogoBase64` and `BrandLogoMimeType` (the UI then falls back to the bundled default asset).

#### Scenario: Non-admin rejected

- **GIVEN** a JWT for an employee
- **WHEN** caller PUTs branding
- **THEN** the response is 403; no DB change

#### Scenario: Admin sets system name

- **GIVEN** admin posts `{ "systemName": "ACME 流程管理" }`
- **THEN** the response is 200 with the updated DTO and DB value matches

#### Scenario: Remove uploaded image

- **GIVEN** the tenant has `BrandLogoBase64` set
- **WHEN** admin PUTs `{ "removeLogo": true }`
- **THEN** both `BrandLogoBase64` and `BrandLogoMimeType` are cleared
- **AND** the UI falls back to the bundled default logo asset

### Requirement: Image upload size and MIME constraints

The system SHALL reject `PUT /api/branding` if the decoded `logoBase64` exceeds 200 KB. The MIME type MUST be one of `image/png`, `image/jpeg`, `image/svg+xml`, `image/webp`. The system SHALL also verify magic bytes match the declared MIME (e.g., PNG bytes `89 50 4E 47`); mismatched files MUST be rejected as 422.

#### Scenario: Oversized image rejected

- **WHEN** admin posts `logoBase64` decoding to 250 KB
- **THEN** the response is 413 with detail "logo too large (max 200KB)"

#### Scenario: Mismatched MIME rejected

- **GIVEN** admin posts a body with `logoMimeType: "image/png"` but content is actually plain text
- **THEN** the response is 422 with detail "logo bytes do not match declared MIME"

#### Scenario: Allowed MIMEs

- **WHEN** admin posts a valid PNG / JPEG / SVG (clean) / WebP
- **THEN** the upload is accepted

### Requirement: SVG sanitization blocks scripts and event handlers

When `logoMimeType = "image/svg+xml"`, the system SHALL parse the SVG XML and reject it (422) if it contains:

- A `<script>` element anywhere
- Any attribute starting with `on` (e.g., `onclick`, `onerror`, `onload`)
- A `href` or `xlink:href` attribute starting with `javascript:`

#### Scenario: SVG with script blocked

- **WHEN** admin uploads `<svg><script>alert(1)</script></svg>`
- **THEN** the response is 422 with detail "SVG contains disallowed elements: script"

#### Scenario: SVG with onerror blocked

- **WHEN** admin uploads `<svg><image onerror="x" href="..."/></svg>`
- **THEN** the response is 422 with detail "SVG contains disallowed elements: on* attribute"

#### Scenario: Clean SVG accepted

- **WHEN** admin uploads `<svg viewBox="0 0 24 24"><circle r="10" cx="12" cy="12"/></svg>`
- **THEN** the upload is accepted

### Requirement: Bundled default logo asset

The frontend bundle SHALL ship a default logo asset at `/assets/bpm-default-logo.svg` (or equivalent module import). When the API returns `logoDataUrl: null`, the layout component SHALL render this default asset. The asset is intentionally bundled with the UI (not stored in DB) so:

- Fresh installs immediately look professional, never blank
- Updating the default logo across all tenants is a UI deploy, not a DB migration
- Tenants with no upload show identical branding (no per-tenant random color choice diverging the look)

The default asset SHOULD be a neutral, geometric mark + the text "BPM" in a single restrained color — the goal is "looks intentional", not "looks like ACME's brand".

#### Scenario: Bundle includes default

- **GIVEN** the bpm-ui or bpm-admin-ui build output
- **WHEN** the bundle is inspected
- **THEN** `bpm-default-logo.svg` (or equivalent asset) is present and ≤ 5KB

#### Scenario: UI falls back to default

- **GIVEN** API responds with `{ logoDataUrl: null, systemName: null }`
- **WHEN** the layout component renders
- **THEN** the bundled default logo is shown next to the bundled default system name "BPM System"

### Requirement: Audit row written for every branding change

Every successful `PUT /api/branding` SHALL persist one `BrandingChange` row containing the actor user id, timestamp, and JSON snapshots of old + new values. Both the impersonator id (if applicable) and the actor user id SHALL be recorded (`BrandingChange` implements `IImpersonable`).

#### Scenario: Audit on system name change

- **GIVEN** branding currently has `BrandSystemName = null`
- **WHEN** admin changes it to "ACME 流程管理"
- **THEN** a BrandingChange row is written with old `{ systemName: null }`, new `{ systemName: "ACME 流程管理" }`, actor=admin's id

#### Scenario: Audit on logo upload

- **WHEN** admin uploads a 50KB PNG (with no other field changes)
- **THEN** a BrandingChange row is written; old/new contain logoMimeType + logoBase64 length (NOT the bytes themselves to keep audit table small) — implementation-defined snapshot strategy

### Requirement: Both UIs render configured branding

Both `bpm-ui` (employee) and `bpm-admin-ui` SHALL fetch `/api/branding` on mount, render the configured logo and system name, and refresh when notified of changes.

The render rule SHALL be:
1. If `logoDataUrl` is set → render `<img>` with that source
2. Else → render the bundled default logo asset
3. The system name text appears next to the logo (custom systemName if set, otherwise bundled default "BPM System")

#### Scenario: Employee UI reflects custom upload

- **GIVEN** branding is configured with an uploaded logo + `systemName: "ACME 流程管理"`
- **WHEN** an employee loads the app
- **THEN** the header shows the uploaded logo and the text "ACME 流程管理"

#### Scenario: Admin UI reflects same branding

- **WHEN** an admin loads the admin UI
- **THEN** the header shows the same logo and system name

#### Scenario: Both UIs fall back to default

- **GIVEN** no branding configured
- **WHEN** either UI loads
- **THEN** the header shows the bundled default logo + "BPM System"

### Requirement: Branding changes propagate without page reload

When admin saves a new branding from the Site Settings page, the layout component on the same tab SHALL update its rendered logo within 1 second (without a manual page reload). Other open tabs of the same browser SHALL update via the localStorage `storage` event.

#### Scenario: Same-tab update

- **GIVEN** admin is on the Site Settings page
- **WHEN** they upload a new logo and click Save
- **THEN** the BPM logo at the top of the same page (in the layout header) updates to the new logo without manual reload

#### Scenario: Cross-tab update

- **GIVEN** admin has Site Settings open in tab A and the employee app open in tab B
- **WHEN** they save a branding change in tab A
- **THEN** within 2 seconds, tab B's header logo updates to the new branding (via storage event)
