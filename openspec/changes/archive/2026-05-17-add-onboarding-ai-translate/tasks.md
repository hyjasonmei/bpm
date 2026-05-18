# Tasks

## 1. Spec schema — define LocalizableString

- [ ] 1.1 Update `spec_schema.md` to introduce `LocalizableString` type
- [ ] 1.2 Document migration v1 → v2 in `spec_schema.md` (specSchemaVersion field)
- [ ] 1.3 Update `meta.primaryLocale` and `meta.targetLocales` fields in schema
- [ ] 1.4 Update `sample_specs/*.json` to v2 form (or note they auto-migrate at load)

## 2. Backend — Spec models

- [ ] 2.1 In `bpm-svc/src/Domain/Spec/SpecModels.cs` (or wherever Spec record lives), add `LocalizableString` record
- [ ] 2.2 Update `Meta` to include `PrimaryLocale`, `TargetLocales`, `SpecSchemaVersion`
- [ ] 2.3 Update node `Label`, field `Label`/`Placeholder`/`Hint`, option `Label`, notification `Subject`/`Body`, gateway branch `Label` to use `LocalizableString`
- [ ] 2.4 Custom JSON converter for backwards-compat: if input is plain string, treat as `{ default: <string>, i18n: null }`

## 3. Backend — SpecLoader v1 → v2 migrator

- [ ] 3.1 In `SpecLoader` (or where spec.json gets parsed), detect `meta.specSchemaVersion < 2` (default 1 if absent)
- [ ] 3.2 Write `MigrateV1ToV2(JsonNode root)` that:
  - For each known LocalizableString location, transform `{ label_zh, label_en }` or `{ "zh-TW": x, "en": y }` → `{ default: x, i18n: { en: y } }`
  - Set `meta.primaryLocale = "zh-TW"`, `meta.targetLocales = []`, `meta.specSchemaVersion = 2`
- [ ] 3.3 Unit tests: migrate sample specs from `sample_specs/` and assert v2 shape

## 4. Backend — LocaleResolver.Pick(LocalizableString, locale)

- [ ] 4.1 Extend or replace existing `Pick(IDictionary<string,string>, locale)` method with new signature `Pick(LocalizableString, locale, primaryLocale)`
- [ ] 4.2 Logic: if locale == primaryLocale return default; if i18n[locale] exists return that; else return default + log warning
- [ ] 4.3 Update notification dispatcher / form renderer / wizard preview to use new Pick

## 5. Backend — SpecValidator extension

- [ ] 5.1 Add validator rule: every LocalizableString MUST have entries in `i18n` for every locale in `meta.targetLocales`
- [ ] 5.2 Returns structured errors `{ path: "forms.LEAVE.fields.days.label", missing: ["en", "ja"] }`
- [ ] 5.3 Toggle: validator runs in Translate step (warn) and GoLive step (block)

## 6. Backend — AI translate endpoint

- [ ] 6.1 `Api/Spec/SpecTranslateController.cs` (NEW)
- [ ] 6.2 `POST /api/spec-translate` body: `{ primaryLocale, targetLocales, context: {industry, flowName, flowCode, domainHint}, items: [{path, value}] }`
- [ ] 6.3 Build system prompt with the context block + glossary-style examples (呈核 / 會簽 / 副本 / 加簽)
- [ ] 6.4 Call `IAiBackend.ChatAsync` with structured output schema (translations array)
- [ ] 6.5 Return `{ translations: [{path, <locale>: "...", confidence: {<locale>: high|medium|low}}, ...] }`
- [ ] 6.6 `[Authorize(Roles = "admin,designer")]`
- [ ] 6.7 Rate limit middleware: 10 req/min/tenant (use existing rate limiter or simple in-memory dict)

## 7. Frontend — Wizard step #9 Translate

- [ ] 7.1 Create `bpm-admin-ui/src/screens/onboarding/steps/StepTranslate.tsx`
- [ ] 7.2 Insert into wizard step list between `StepTest` (step 8) and `StepGoLive` (now step 10) — 10 steps total
- [ ] 7.3 UI top: target locales chip selector with `+ Add locale` and `× remove`
- [ ] 7.4 Table:
  - Column 1: path + default value (read-only)
  - Column 2..N: each target locale, AI-filled, ✏️ inline editable
  - Color-code: green=high confidence, amber=medium/low, gray=user-edited
- [ ] 7.5 Top-right `🪄 AI translate all` button → POST /api/spec-translate, fill in empty cells; preserve user-edited cells
- [ ] 7.6 Top-right `🔁 Re-translate low-confidence only` button → POST with only low-confidence items
- [ ] 7.7 Bottom: status counter "X of Y translated · Z low confidence"
- [ ] 7.8 Continue button enabled iff every cell filled

## 8. Frontend — wire translations into draft spec

- [ ] 8.1 `bpm-admin-ui/src/lib/onboarding.ts` — extend `DraftSpec` to carry `meta.primaryLocale`, `meta.targetLocales`, and convert label fields to LocalizableString
- [ ] 8.2 Add helper `setTranslation(draft, path, locale, value)` that mutates the right LocalizableString
- [ ] 8.3 Add `walkLocalizableStrings(draft) → Iterable<{path, ls: LocalizableString}>` for iteration in StepTranslate
- [ ] 8.4 Update other steps (Forms, Notify) to populate `default` rather than direct string

## 9. Frontend — API client

- [ ] 9.1 `bpm-admin-ui/src/lib/api/specTranslate.ts`
- [ ] 9.2 `translateBatch(req): Promise<TranslateResponse>` typed call

## 10. Tests

- [ ] 10.1 Backend unit: SpecLoader migrates v1 sample → v2
- [ ] 10.2 Backend unit: validator catches missing locale
- [ ] 10.3 Backend integration: POST /api/spec-translate returns translations; rate limit triggers after 10
- [ ] 10.4 Frontend snapshot: StepTranslate renders correctly with mock draft
- [ ] 10.5 E2E: complete onboarding with `targetLocales=["en"]`, AI fills, customer edits one cell, GoLive submits with i18n bundles in spec.json

## 11. Documentation

- [ ] 11.1 Update `prompt_template_v1.md` (used by Claude Code deploy pipeline) to handle v2 spec shape
- [ ] 11.2 Update `spec_schema.md` with worked examples
- [ ] 11.3 Note in `prompt.md` (onboarding wizard prompt) that translate is AI-assisted

## 12. Out of scope (link to future changes)

- [ ] 12.1 Document: `add-locale-glossary` (per-tenant terminology dict feeding into translate prompt)
- [ ] 12.2 Document: `add-locales-admin-ui` (post-launch translation completeness dashboard, repair missing locales after spec is live)
