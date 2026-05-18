## ADDED Requirements

### Requirement: Spec schema uses LocalizableString for all human-facing text

The system SHALL define a `LocalizableString` JSON type with the following shape:

```json
{ "default": "<string>", "i18n": { "<locale>": "<string>", ... } }
```

Where `default` is REQUIRED and `i18n` is OPTIONAL. The following spec elements SHALL use `LocalizableString`:

- `meta.flowName`
- `nodes[].label`
- `forms[].fields[].label` / `placeholder` / `hint`
- `forms[].fields[].options[].label`
- `notifications[].subject` / `body`
- `gateways[].branches[].label`

Identifier fields (`meta.flowCode`, node `id`, role codes, group codes, expression strings) SHALL remain plain strings — they are NOT user-facing display text.

#### Scenario: Field label stored as LocalizableString

- **GIVEN** a wizard customer fills `Days` field label as "請假天數" with target locale `en`
- **WHEN** the spec is finalized and POSTed to `/api/spec`
- **THEN** the spec.json contains `forms[*].fields[*].label = { "default": "請假天數", "i18n": { "en": "Leave Days" } }`

#### Scenario: Identifier fields stay plain strings

- **WHEN** a wizard customer sets `meta.flowCode = "LEAVE"`
- **THEN** the spec.json contains `meta.flowCode: "LEAVE"` (plain string, NOT a LocalizableString)

### Requirement: meta.primaryLocale and meta.targetLocales are mandatory

The spec.meta SHALL include:

- `primaryLocale` (string, IETF BCP 47 tag, e.g., `zh-TW`) — language of every `default` value
- `targetLocales` (string[], may be empty) — locales that MUST have entries in every LocalizableString's `i18n`

If `targetLocales` is empty, no translations are required and the wizard's Translate step SHALL be skippable.

#### Scenario: Customer with only zh-TW skips translate

- **GIVEN** wizard customer sets `targetLocales = []`
- **WHEN** they reach Step 9 (Translate)
- **THEN** the step shows "No target locales selected — proceed to GoLive" and the Continue button is enabled without any translation entries

#### Scenario: Customer with target locales must complete translations

- **GIVEN** `targetLocales = ["en", "ja"]` and one field label has `i18n.en` filled but `i18n.ja` missing
- **WHEN** the customer attempts to leave Step 9
- **THEN** the wizard blocks Continue and shows "ja missing for `forms.LEAVE.fields.days.label`"

### Requirement: Wizard adds Step 9 — Translate, between Test and GoLive

The onboarding wizard SHALL include a new step `Translate` positioned as step 9 of 10 (before `GoLive`, after `Test`). The step SHALL render a table with one row per `LocalizableString` in the draft spec and one column per target locale.

#### Scenario: Step layout

- **GIVEN** customer on the wizard
- **WHEN** they progress past Test
- **THEN** they enter Step 9 (Translate); the bottom step indicator shows 9 of 10 with `Translate` highlighted

#### Scenario: Skipping when targetLocales empty

- **GIVEN** `targetLocales = []`
- **WHEN** customer enters Step 9
- **THEN** the page shows the "No target locales selected" notice and Continue immediately advances to GoLive

### Requirement: AI batch translation via POST /api/spec-translate

The system SHALL provide `POST /api/spec-translate` accepting:

```json
{
  "primaryLocale": "<locale>",
  "targetLocales": ["<locale>", ...],
  "context": { "industry": "<string>", "flowName": "<string>", "flowCode": "<string>", "domainHint": "<string>" },
  "items": [{ "path": "<dot-path>", "value": "<source string>" }, ...]
}
```

And returning:

```json
{
  "translations": [
    { "path": "<dot-path>",
      "<locale>": "<translation>",
      "confidence": { "<locale>": "high|medium|low" } },
    ...
  ]
}
```

The endpoint SHALL be `[Authorize(Roles="admin,designer")]` and rate-limited to 10 requests per minute per tenant.

#### Scenario: Successful batch translate

- **GIVEN** a designer is authenticated
- **WHEN** they POST 30 items with targetLocales `["en"]`
- **THEN** the response contains 30 translation entries, each with an `en` key and a `confidence.en` value

#### Scenario: Non-designer rejected

- **GIVEN** an employee user is authenticated (no admin/designer role)
- **WHEN** they POST `/api/spec-translate`
- **THEN** the response is 403

#### Scenario: Rate limit triggered

- **GIVEN** a tenant has called the endpoint 10 times in the past minute
- **WHEN** an 11th call arrives
- **THEN** the response is 429 with header `Retry-After`

### Requirement: Translation context informs AI quality

The AI translate prompt SHALL include the `context` block (industry, flowName, flowCode, domainHint) and a small glossary of Taiwan-SME BPM domain terms (呈核 = "submit for approval", 會簽 = "joint approval", 副本 = "carbon copy", 加簽 = "additional approval"). The system prompt SHALL instruct the AI to maintain consistency with these definitions.

#### Scenario: Domain term translates correctly

- **GIVEN** a node label is "呈核給副總"
- **WHEN** the customer requests `en` translation with industry=manufacturing
- **THEN** the AI returns "Submit for VP approval" (or similar matching glossary), NOT a literal mistranslation like "Process VP approval"

(This requirement is best-effort given AI quality variability; the test gauges *that the prompt includes the context*, not that any specific output must come back. Confidence flag is the customer's review hook.)

### Requirement: Confidence flag exposed to customer

For each translation, the AI SHALL return a `confidence` value of `high`, `medium`, or `low` per target locale. The wizard UI SHALL color-code rows: `high` = green, `medium`/`low` = amber, user-edited = gray. The confidence flag SHALL NOT be persisted in spec.json (it's a wizard-only review aid).

#### Scenario: Low confidence rendered amber

- **GIVEN** AI returns `confidence.en = "low"` for `forms.LEAVE.fields.days.label`
- **WHEN** the wizard renders the table
- **THEN** the en cell has amber background

#### Scenario: User edit overrides confidence color

- **GIVEN** an AI-translated cell with confidence `low` (amber)
- **WHEN** the customer clicks ✏️ and edits the value
- **THEN** the cell turns gray (user-edited) and re-running "AI translate all" does NOT overwrite this cell

### Requirement: SpecLoader auto-migrates v1 specs at load

When `SpecLoader` reads a spec.json with `meta.specSchemaVersion < 2` (or absent), it SHALL transform legacy `{ label_zh, label_en }` and `{ "zh-TW", "en" }` shapes into LocalizableString in memory before deserialization. The on-disk file SHALL NOT be rewritten by the loader. ProcessInstance.SpecSnapshotJson written before this change MAY remain v1 on disk; the runtime treats them as v2 via the loader.

#### Scenario: v1 spec loaded and migrated

- **GIVEN** a sample spec.json with `forms[*].fields[*].label_zh = "請假天數"`, `label_en = "Leave Days"`, no `specSchemaVersion`
- **WHEN** SpecLoader reads it
- **THEN** the in-memory Spec has `forms[*].fields[*].label = { default: "請假天數", i18n: { en: "Leave Days" } }`
- **AND** `meta.primaryLocale = "zh-TW"`, `meta.targetLocales = []`, `meta.specSchemaVersion = 2`
- **AND** the on-disk file is unchanged

#### Scenario: v2 spec loaded as-is

- **GIVEN** spec.json already at v2 with explicit LocalizableStrings
- **WHEN** SpecLoader reads it
- **THEN** no migration runs; deserialization is direct

### Requirement: LocaleResolver.Pick returns the right text

The `ILocaleResolver.Pick(LocalizableString s, string locale, string primaryLocale)` method SHALL return:

1. `s.Default` if `locale == primaryLocale`
2. `s.I18n[locale]` if `s.I18n` has the key
3. `s.Default` (with a warning log) otherwise

#### Scenario: Primary locale returns default

- **GIVEN** `s = { default: "請假天數", i18n: { en: "Leave Days" } }`, `primaryLocale = "zh-TW"`
- **WHEN** Pick(s, "zh-TW", "zh-TW") is called
- **THEN** it returns "請假天數"

#### Scenario: Target locale returns translation

- **WHEN** Pick(s, "en", "zh-TW") is called
- **THEN** it returns "Leave Days"

#### Scenario: Missing locale falls back to default

- **WHEN** Pick(s, "ja", "zh-TW") is called (no i18n.ja)
- **THEN** it returns "請假天數" (the default)
- **AND** a warning is logged with the missing locale and path
