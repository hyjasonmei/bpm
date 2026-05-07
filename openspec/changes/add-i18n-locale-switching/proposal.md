## Why

The system already supports bilingual content per spec (subject/body templates, field labels — both have `zh-TW` and optional `en`). But:

- User has no `preferred_locale` setting; everything defaults to zh-TW
- Notifications dispatched in user's locale would be more useful (English-speaking 外籍 employee at a Taiwanese factory shouldn't get all-Chinese emails)
- UI strings (RoleSwitcher, AppLayout, error toasts) are mixed zh-TW + en hardcoded
- Onboarding wizard hints / button labels not centrally translated

This change adds first-class locale support: per-user preference, dynamic UI translation, locale-aware notification rendering.

## What Changes

### i18n capability (NEW `bpm-i18n`)

**User extension**:

- `User.PreferredLocale` (string, nullable) — IETF tag (`zh-TW`, `en`, `ja`, etc.); defaults null = falls back to tenant default
- `Tenant.DefaultLocale` (in tenant config) — fallback when user has no preference
- System default: `zh-TW`

**Locale resolution priority**:

1. User.PreferredLocale (if set)
2. Tenant.DefaultLocale
3. System default (`zh-TW`)

**Service** `ILocaleResolver`:

- `Task<string> GetUserLocaleAsync(userId, ct)` — applies priority
- `string Pick(IDictionary<string, string> bilingual, string locale)` — picks the right value from a bilingual map; falls back to zh-TW if requested locale absent

### Notification rendering uses user locale

When the dispatcher picks subject/body for a notification, it picks based on the *target user's* preferred locale (each delivery row may have a different rendered subject/body if recipients have different locales). The Mustache renderer is invoked once per locale variant.

### Frontend i18n

`bpm-ui/src/lib/i18n/`:

- `messages/zh-TW.json` and `messages/en.json` (and `ja.json` for one extension)
- `t(key, params?)` lookup function
- React context `LocaleProvider` provides current locale via hook `useLocale()`
- Components use `t('login.button.signin')` instead of hardcoded "Sign in"

Initial migration: walk the codebase, replace hardcoded zh-TW / en strings with `t()` calls, populate the JSON files.

UI: a locale picker in the user menu (right side of AppLayout, in RoleSwitcher dropdown). Changes update User.PreferredLocale via API + reload.

### Backend strings

For backend error messages returned to UI (validation errors, etc.), the API SHOULD return both a code and a default zh-TW message. Frontend i18n maps code → translated message. Examples:

```json
{ "error_code": "validation.expression.parse_error", "message": "..." }
```

`messages/zh-TW.json`:
```json
{ "validation.expression.parse_error": "表達式解析錯誤：{detail}" }
```

### Out of scope (future changes)

- RTL languages (Arabic / Hebrew) — layout changes deferred
- Pluralization rules per locale (use simple format strings only)
- Number / date formatting per locale beyond Intl built-ins
- Translation crowdsourcing UI
- Auto-translate via API
- Per-tenant custom translations (overrides on top of bundled)
- Speech / voice locale support

## Capabilities

### New Capabilities

- `bpm-i18n` — User.PreferredLocale + Tenant.DefaultLocale settings, ILocaleResolver service, frontend `t()` + React context, JSON message files for zh-TW + en (initial; ja extensible), locale picker UI.

### Modified Capabilities

- `bpm-notification-engine` — dispatcher picks subject/body based on each recipient's resolved locale; rendering invoked per locale variant.

## Impact

- Migration adds `User.PreferredLocale` column
- TenantConfig table extended with DefaultLocale
- Backend `ILocaleResolver` service + DI
- Frontend i18n library — choose between `react-i18next` (feature-rich) or hand-rolled (simpler) — pick hand-rolled (no NPM bloat)
- Frontend strings extracted into messages/{locale}.json
- Locale picker added to RoleSwitcher dropdown
- Notification dispatcher: per-recipient locale-aware rendering loop
- Demo guard: 9 mock-up forms NOT modified (they remain mixed zh-TW + en hardcoded as demo artifacts)
