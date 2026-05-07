# Design notes

## 1. Why hand-rolled i18n vs react-i18next

react-i18next is feature-rich (pluralization, nested namespaces, lazy loading) but adds ~50 KB. For our needs (~500 strings, bilingual mostly, occasional 3rd locale), a hand-rolled `t(key, params?)` over a JSON object is sufficient (~5 KB).

If demand grows (10+ locales, RTL support, complex pluralization), swap to react-i18next via the existing `t()` interface — minimal surface change.

## 2. Locale fallback chain

When a key is missing in user's locale: fall back to zh-TW (system default). When a bilingual map (subject/body) lacks the user's locale: same fallback.

Surfacing missing translations: dev mode logs warnings. Prod silently uses fallback.

## 3. User locale change flow

- User opens RoleSwitcher → picks "English"
- API call PUT /api/me { preferred_locale: 'en' }
- Frontend updates LocaleProvider's locale; re-renders UI in en
- Backend stores preference; subsequent notifications targeting this user use 'en'

For new users (JIT-provisioned): default to tenant default; no preference set.

## 4. Server-side locale for emails

Notification rendering: dispatcher fetches each recipient's User.PreferredLocale (with fallback). Subject/body are bilingual maps in the notification spec. Renderer picks the right variant per recipient.

If a recipient has zh-TW preferred but the notification spec only has 'en' subject, fall back to 'en' (don't fail).

## 5. Date / number / currency formatting

Use `Intl.DateTimeFormat`, `Intl.NumberFormat` (browser built-ins). Pass user's locale. No third-party library needed.

For Mustache template values, the renderer can pre-format dates per recipient locale: `{{leave.start | date('MMM dd')}}` — extension to the renderer for v2; v1 uses raw ISO strings.

## 6. Open questions

- **Mixed-locale UI**: an English-speaking user looking at zh-TW field labels (because the spec author only authored Chinese labels). Acceptable; show with "(no English label)" placeholder optional.
- **Tenant override for system messages**: a tenant wants "歡迎" instead of bundled "你好". Defer; per-tenant overrides are a future feature.
- **Locale-specific styling**: e.g., Chinese typography spacing. Defer.
