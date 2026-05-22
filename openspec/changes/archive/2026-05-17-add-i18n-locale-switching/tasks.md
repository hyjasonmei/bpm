# Tasks

## 1. Schema

- [ ] 1.1 Migration `AddUserPreferredLocale` adds User.PreferredLocale column (nullable string)
- [ ] 1.2 Tenant config: add DefaultLocale field

## 2. Backend ILocaleResolver

- [ ] 2.1 Create `bpm-svc/src/Application/I18n/ILocaleResolver.cs`
- [ ] 2.2 Implementation: User.PreferredLocale → Tenant.DefaultLocale → "zh-TW"
- [ ] 2.3 `Pick(bilingualMap, locale)` helper

## 3. Notification dispatcher locale-aware rendering

- [ ] 3.1 Update NotificationDispatcher to resolve each recipient's locale
- [ ] 3.2 Render subject/body per recipient locale variant
- [ ] 3.3 Tests: dispatch to two users (different locales) → different rendered subjects per delivery

## 4. Frontend i18n

- [ ] 4.1 Create `bpm-ui/src/lib/i18n/messages/zh-TW.json` and `en.json` (initial seed: top 100 strings extracted)
- [ ] 4.2 Create `bpm-ui/src/lib/i18n/index.ts` with `t(key, params?)` + LocaleProvider
- [ ] 4.3 Migrate UI components: RoleSwitcher, AppLayout, common buttons / errors → use t()
- [ ] 4.4 Locale picker in RoleSwitcher dropdown calling PUT /api/me

## 5. Backend error message codes

- [ ] 5.1 Standardize error responses: `{ error_code, message }`
- [ ] 5.2 Map codes in messages/{locale}.json
- [ ] 5.3 Frontend translates errors via lookup

## 6. End-to-end verification

- [ ] 6.1 Apply migration
- [ ] 6.2 PUT /api/me with preferred_locale = 'en'; verify response + persistence
- [ ] 6.3 Login → UI in English
- [ ] 6.4 Notifications dispatched to this user use English template
- [ ] 6.5 **Demo guard**: 9 mock-up forms NOT modified

## 7. Commit

- [ ] 7.1 Commit in chunks
- [ ] 7.2 Push via GitKraken
