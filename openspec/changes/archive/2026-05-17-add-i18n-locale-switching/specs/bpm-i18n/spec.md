## ADDED Requirements

### Requirement: User.PreferredLocale stores per-user locale preference

The system SHALL extend `User` with a nullable `PreferredLocale` column (IETF tag string, e.g., `"zh-TW"`, `"en"`, `"ja"`). When set, this is the user's chosen locale for UI + notifications. When null, the tenant's default locale (or system default `"zh-TW"`) applies.

#### Scenario: User updates preference

- **WHEN** PUT /api/me with `{ preferred_locale: "en" }`
- **THEN** User.PreferredLocale = "en"; subsequent locale resolution returns "en"

### Requirement: ILocaleResolver applies fallback chain

The `ILocaleResolver.GetUserLocaleAsync(userId)` SHALL apply the priority: User.PreferredLocale → Tenant.DefaultLocale → "zh-TW". The result is always a valid IETF tag string.

#### Scenario: User preference wins

- **GIVEN** User has PreferredLocale = "ja"; Tenant default = "zh-TW"
- **WHEN** ILocaleResolver.GetUserLocaleAsync runs for that user
- **THEN** returns "ja"

#### Scenario: Tenant default used when user preference null

- **GIVEN** User.PreferredLocale = null; Tenant default = "en"
- **WHEN** resolver runs
- **THEN** returns "en"

#### Scenario: System default fallback

- **GIVEN** User.PreferredLocale = null; Tenant default = null
- **WHEN** resolver runs
- **THEN** returns "zh-TW"

### Requirement: Frontend t() function with fallback

The frontend SHALL provide a `t(key, params?)` function that:

- Looks up the translation in the current locale's message file
- Falls back to "zh-TW" if the key is missing in the active locale
- Substitutes `{name}` placeholders with values from params
- In dev mode, console.warn for missing keys

#### Scenario: Translation lookup with params

- **GIVEN** messages.zh-TW = `{ "greeting": "你好 {name}" }`
- **WHEN** `t("greeting", { name: "Wilson" })` runs in zh-TW locale
- **THEN** returns "你好 Wilson"

#### Scenario: Missing key falls back

- **GIVEN** messages.en lacks key "rare.error"; messages.zh-TW has it = "極少出錯"
- **WHEN** t("rare.error") runs in en locale
- **THEN** returns "極少出錯" (zh-TW fallback)

### Requirement: Locale picker in RoleSwitcher

The RoleSwitcher dropdown SHALL include a locale picker showing supported locales (zh-TW + en in v1). Selecting one calls PUT /api/me to persist the preference and reloads the LocaleProvider state.

#### Scenario: Picker switches UI language

- **GIVEN** UI is in zh-TW
- **WHEN** the user picks "English" in the locale picker
- **THEN** the API is called; UI re-renders with en messages; preference is persisted across reloads
