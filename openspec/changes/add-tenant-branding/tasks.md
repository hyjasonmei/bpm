# Tasks

## 1. Domain — TenantSettings extension

- [ ] 1.1 Add `BrandLogoBase64` (string?, large text), `BrandLogoMimeType` (string(50)?), `BrandSystemName` (string(50)?) to `TenantSettings.cs` — color swatch + text fallback **dropped per 2026-05-10** (only logo upload + system name; bundle ships preset asset for fallback)
- [ ] 1.2 Update `TenantSettingsConfiguration.cs` with explicit max-length constraints for the new columns
- [ ] 1.3 Generate migration `AddBranding`
- [ ] 1.4 Apply locally; verify schema

## 2. Domain — BrandingChange audit row (NEW)

- [ ] 2.1 Create `Domain/Entities/Sandbox/BrandingChange.cs` (ActorUserId, ChangedFieldsJson, OldValuesSnapshotJson, NewValuesSnapshotJson, CreatedAt)
- [ ] 2.2 Implement `IImpersonable` so impersonator id is stamped automatically
- [ ] 2.3 EF configuration; index (CreatedAt DESC)
- [ ] 2.4 DbSet in AppDbContext

## 3. Backend — IBrandingService

- [ ] 3.1 `Application/Branding/IBrandingService.cs` interface
- [ ] 3.2 `BrandingDto` record (logoDataUrl?, systemName?)
- [ ] 3.3 `UpdateBrandingRequest` record (logoBase64?, logoMimeType?, systemName?, removeLogo?)
- [ ] 3.4 `Persistence/Branding/BrandingService.cs` impl
- [ ] 3.5 `GetAsync()`: load TenantSettings; map to DTO. If LogoBase64 present, build `data:<mime>;base64,...` string for frontend convenience.
- [ ] 3.6 `UpdateAsync(req, actorUserId)`: validate (size ≤ 200KB, MIME whitelist, magic bytes match), apply changes, write BrandingChange audit row, save.
- [ ] 3.7 SVG safety: parse SVG XML, reject if contains `<script>`, any `on*` attribute, or `href`/`xlink:href` starting with `javascript:`. Implement as helper `SvgSanitizer.IsSafe(string xml) → (bool, string? reason)`.
- [ ] 3.8 Magic byte verification: PNG starts `89 50 4E 47`, JPEG starts `FF D8 FF`, WebP `RIFF...WEBP`, SVG `<?xml` or `<svg`.
- [ ] 3.9 ~~Color whitelist~~ — DROPPED (color swatch removed per 2026-05-10)

## 4. Backend — Branding API

- [ ] 4.1 `Api/Branding/BrandingController.cs`
- [ ] 4.2 `GET /api/branding` — `[AllowAnonymous]` → returns BrandingDto (200 always; empty fields for unset values)
- [ ] 4.3 `PUT /api/branding` — `[Authorize(Roles = "admin")]` → calls UpdateAsync, returns updated BrandingDto
- [ ] 4.4 Reject body > 300KB (rough size cap; base64 + JSON overhead + multi fields)
- [ ] 4.5 Map domain validation exceptions to 422 / 413 cleanly

## 5. Backend — Tests

- [ ] 5.1 Unit: `SvgSanitizer.IsSafe` with `<script>` → false
- [ ] 5.2 Unit: `SvgSanitizer.IsSafe` with `<image onerror=...>` → false
- [ ] 5.3 Unit: `SvgSanitizer.IsSafe` with clean SVG → true
- [ ] 5.4 Unit: magic byte check for fake PNG (e.g., text `<script>` claiming `image/png`) → reject
- [ ] 5.5 Unit: UpdateAsync writes BrandingChange row with diff JSON
- [ ] 5.6 Integration: PUT branding then GET returns the new logo data URL
- [ ] 5.7 Integration: oversized image (300KB) → 413
- [ ] 5.8 Integration: non-admin PUT → 403
- [ ] 5.9 Integration: GET works without auth (200, empty)

## 6. Frontend (admin) — branding api + types

- [ ] 6.1 `bpm-admin-ui/src/types/branding.ts`: `BrandingDto`, `UpdateBrandingRequest`
- [ ] 6.2 `bpm-admin-ui/src/lib/api/branding.ts`: `getBranding()`, `updateBranding(req)`, `removeLogo()` (calls update with `removeLogo: true`)

## 7. Frontend (admin) — Branding section in Site Settings

- [ ] 7.1 In `screens/SiteSettings.tsx`, add new `<SectionCard>` after Sandbox titled "Branding"
- [ ] 7.2 Layout: 2-column grid
  - Left: live preview (logo + system name) at scaled-down header replica; falls back to bundle preset asset when no upload
  - Right: form (upload, system name)
- [ ] 7.3 File input + drag-drop zone:
  - Accept `image/png, image/jpeg, image/svg+xml, image/webp`
  - Read as base64 via FileReader
  - Show preview + size + filename + Remove button
  - Reject > 200KB with toast
- [ ] 7.4 ~~Logo Text input~~ — DROPPED
- [ ] 7.5 ~~Color swatch grid~~ — DROPPED
- [ ] 7.6 System Name input: maxLength=50; live preview reflects
- [ ] 7.7 Action buttons:
  - **Save** — call updateBranding; show success toast; broadcast `bpm:branding-changed`
  - **Discard** — reset form to last-saved state
  - **Reset to defaults** — confirm dialog; call updateBranding with all fields cleared
- [ ] 7.8 Live preview re-renders on every form keystroke

## 8. Frontend (admin) — wire branding into AdminLayout

- [ ] 8.1 Add bundle preset asset `bpm-admin-ui/src/assets/bpm-default-logo.svg` (≤5KB, neutral mark + "BPM" text)
- [ ] 8.2 Move logo button into a `<BrandedLogo />` component
- [ ] 8.3 Component: on mount, read `sessionStorage.bpm_branding_cache`; if absent fetch `/api/branding`, set cache
- [ ] 8.4 Listen for `bpm:branding-changed` → re-read cache
- [ ] 8.5 Listen for `storage` event (cross-tab sync) on `bpm_branding_cache`
- [ ] 8.6 Render: if `logoDataUrl` → `<img src=... alt=systemName>`; else `<img src={bpmDefaultLogo} alt="BPM" />`
- [ ] 8.7 Render systemName text right of logo (use bundle default "BPM System" if null)

## 9. Frontend (employee) — branding api + types + wire-in

- [ ] 9.1 Mirror `lib/api/branding.ts` and `types/branding.ts` from admin UI (deduplication later via shared package)
- [ ] 9.2 Add bundle preset asset `bpm-ui/src/assets/bpm-default-logo.svg` (same as admin UI's)
- [ ] 9.3 Mirror `BrandedLogo` component into `bpm-ui/src/components/`
- [ ] 9.4 Replace the existing logo button in `AppLayout.tsx` with `<BrandedLogo />`

## 10. Frontend — common helpers

- [ ] 10.1 ~~`swatchToClass`~~ — DROPPED (no swatch palette)
- [ ] 10.2 ~~Tailwind safelist for dynamic classes~~ — DROPPED

## 11. Verify

- [ ] 11.1 Backend `dotnet build` clean
- [ ] 11.2 Both UI `tsc` + `npm run build` clean
- [ ] 11.3 Manual E2E:
  - Open admin UI → Site Settings → Branding section
  - Set logo text "ACME", color blue, system name "ACME 流程管理" → Save
  - Reload admin UI: logo box shows "ACME" in blue, system name "ACME 流程管理"
  - Open employee UI: same
  - Upload a real PNG: logo switches to image
  - Click "Reset to defaults": logo box returns to "BPM" red
- [ ] 11.4 Manual: try uploading an unsafe SVG (`<svg><script>alert(1)</script></svg>`) → toast "SVG contains disallowed elements"
- [ ] 11.5 Manual: non-admin loads UI → sees current branding (no toggle UI accessible)
- [ ] 11.6 Browser screenshot for `dogfood-screenshots/` showing both UIs with custom brand
