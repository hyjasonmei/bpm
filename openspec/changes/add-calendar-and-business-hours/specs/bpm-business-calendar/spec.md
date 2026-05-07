## ADDED Requirements

### Requirement: BusinessCalendar entity supports per-tenant working hours

The system SHALL persist a `BusinessCalendar` entity per tenant carrying `Name`, `Timezone` (IANA format), `IsDefault` flag, `WorkingDaysJson` (per-weekday windows), and `Status`. Each tenant SHALL have exactly one calendar with `IsDefault = true`. Multiple non-default calendars MAY coexist for tenants with multi-region or multi-shift operations.

#### Scenario: Tenant default lookup

- **GIVEN** tenant Acme has 3 BusinessCalendars; only "Acme HQ" has IsDefault = true
- **WHEN** the system resolves a calendar by `tenantId = Acme, calendarId = null`
- **THEN** "Acme HQ" is returned

#### Scenario: Multi-window day support

- **GIVEN** a calendar with `WorkingDaysJson` for Monday = `[{ start: '09:00', end: '12:30' }, { start: '13:30', end: '18:00' }]`
- **WHEN** `IsBusinessHour(2026-05-11 12:45 Asia/Taipei)` is queried
- **THEN** the result is `false` (lunch break)

#### Scenario: Different timezone

- **GIVEN** a calendar with Timezone = "Asia/Taipei" and Mon 09:00-18:00
- **WHEN** `IsBusinessHour(2026-05-11 02:00 UTC)` is queried (= 10:00 Asia/Taipei)
- **THEN** the result is `true`

### Requirement: CalendarException supports holidays and compensatory work days

The system SHALL persist `CalendarException` entries that override the regular calendar for specific dates. Three types are supported:

- `Holiday` — date is non-working despite the regular weekday's windows
- `WorkDay` — date is working despite the regular weekday being non-working (e.g., Saturday 春節 補班)
- `SpecialHours` — date has custom windows (e.g., half-day on holiday eve)

The validator MUST reject combinations where the same date has both `Holiday` and `WorkDay` exceptions on the same calendar.

#### Scenario: Holiday overrides regular weekday

- **GIVEN** a calendar with Wednesday 09:00-18:00 working hours
- **AND** a `Holiday` exception for 2026-04-04 (Wednesday)
- **WHEN** `IsBusinessHour(2026-04-04 14:00)` is queried
- **THEN** the result is `false`

#### Scenario: WorkDay activates a weekend

- **GIVEN** a calendar with Saturday non-working
- **AND** a `WorkDay` exception for 2026-02-08 (Saturday) with default windows
- **WHEN** `IsBusinessHour(2026-02-08 10:00)` is queried
- **THEN** the result is `true`

#### Scenario: SpecialHours overrides regular

- **GIVEN** a calendar with Friday 09:00-18:00
- **AND** a `SpecialHours` exception for 2026-06-19 with windows `[{ start: '09:00', end: '12:00' }]`
- **WHEN** `IsBusinessHour(2026-06-19 14:00)` is queried
- **THEN** the result is `false` (afternoon not in custom windows)

### Requirement: AddBusinessDuration walks across windows / exceptions

`IBusinessCalendar.AddBusinessDuration(start, duration, calendarId)` SHALL return the timestamp at which `duration` of business hours has elapsed, walking forward across windows, weekends, and exceptions. The result MUST NOT include any non-business time in its accounting.

#### Scenario: 8-hour SLA spans weekend

- **GIVEN** Friday 14:00 spawn time, Mon-Fri 09:00-18:00 calendar
- **WHEN** `AddBusinessDuration(start, 8h)` runs
- **THEN** the result is Tuesday 13:00 (4h Friday afternoon + 4h Monday morning... wait Fri has 4h left → 4h done; Saturday/Sunday skipped; remaining 4h Monday morning → ends Monday 13:00 — verify exact semantics in test)

#### Scenario: SLA wraps a holiday block

- **GIVEN** Tue 春節 starts; an 8h SLA spawned 2026-02-12 (the day before 春節 start)
- **WHEN** `AddBusinessDuration` runs
- **THEN** DueAt skips all the 春節 days and lands the appropriate work hour after the block

#### Scenario: 24×7 calendar (no exclusions)

- **GIVEN** a calendar with all weekdays full 24h windows AND no exceptions
- **WHEN** `AddBusinessDuration(start, 8h)` runs
- **THEN** the result is `start + 8h` exactly (calendar effectively passive)

### Requirement: CountBusinessDays for CEL helper

The system SHALL expose `IBusinessCalendar.CountBusinessDays(start, end, calendarId)` returning the integer number of business days in the half-open interval `[start, end)`. This is the implementation behind the CEL helper `businessDaysBetween` introduced in `add-cel-expressions`.

#### Scenario: Counting excludes weekends and holidays

- **GIVEN** Taiwan default calendar with 2026-04-04 to 2026-04-06 holidays
- **WHEN** `CountBusinessDays(2026-04-03, 2026-04-08)` is queried
- **THEN** the result is 1 (only 2026-04-07 Tuesday counts; 4-3 not counted as half-open from 4-3; 4-4/5/6 holidays; 4-7 Tuesday)

### Requirement: Holiday import endpoint

The system SHALL expose `POST /api/calendars/{id}/import-holidays?source=tw-government&year=YYYY` (admin only) that imports a year's holidays from a packaged source. The `tw-government` source SHALL be a hand-curated JSON shipped with the bpm-svc binary, updated annually. The import operation MUST be idempotent — re-running it for the same year does not duplicate exceptions.

#### Scenario: Import 2026 idempotent

- **WHEN** an admin calls `import-holidays?source=tw-government&year=2026` twice
- **THEN** the second call inserts zero new rows; existing rows remain

#### Scenario: Import unknown source rejected

- **WHEN** an admin calls `import-holidays?source=foo&year=2026`
- **THEN** the response is 400 with error "unknown source"

### Requirement: CRUD endpoints for tenant admins

The system SHALL expose CRUD endpoints under `/api/calendars` accessible only to `tenant_admin` role. The validator SHALL ensure:

- At least one working window exists across the week (not all weekdays empty)
- Timezone is a valid IANA TZ string
- Default calendar uniqueness per tenant (only one IsDefault = true)
- Exception dates within the calendar's tenant scope
- No duplicate exception per (calendarId, date)

#### Scenario: Create calendar

- **WHEN** an admin POSTs a new calendar with valid windows + Asia/Taipei timezone
- **THEN** 201 Created; the new calendar appears in subsequent GET /api/calendars

#### Scenario: All-empty windows rejected

- **WHEN** an admin POSTs a calendar where every weekday has empty windows
- **THEN** the response is 400 with "at least one working window required"

#### Scenario: Non-admin denied

- **WHEN** a regular user calls POST /api/calendars
- **THEN** response is 403
