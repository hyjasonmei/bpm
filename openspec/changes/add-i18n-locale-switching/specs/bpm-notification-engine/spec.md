## ADDED Requirements

### Requirement: Notification dispatcher renders per-recipient locale

The dispatcher SHALL resolve each recipient's locale via `ILocaleResolver.GetUserLocaleAsync` and render the notification's subject + body using that locale's variant from the bilingual maps. If the locale's variant is missing, the renderer falls back to the zh-TW variant.

When a single dispatch produces multiple deliveries (one per recipient), each delivery SHALL carry its rendered subject/body in the recipient's locale — different deliveries may have different rendered text.

#### Scenario: Two recipients different locales

- **GIVEN** notification with subject = `{ 'zh-TW': '請假單', 'en': 'Leave Request' }`, body bilingual
- **AND** recipient A has PreferredLocale = "zh-TW"; recipient B = "en"
- **WHEN** dispatch runs
- **THEN** A's NotificationDelivery row has subject "請假單"; B's row has "Leave Request"

#### Scenario: Locale variant missing falls back

- **GIVEN** notification only has zh-TW subject; recipient B has PreferredLocale = "en"
- **WHEN** dispatch runs for B
- **THEN** B's delivery uses the zh-TW subject (graceful fallback)
