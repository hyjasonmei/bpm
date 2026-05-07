## Why

`add-sla-timer-escalation` shipped with a hardcoded business-hours model: Mon-Fri 09:00-18:00. That works for a generic Taiwanese SME but breaks when:

- Customer celebrates 春節 (Chinese New Year, variable dates each year, week-long shutdown)
- Customer celebrates 端午 / 中秋 / 國慶 / 補班日 (Taiwan-specific compensatory work days)
- Customer's office hours are 08:30-17:30 (manufacturing) or 10:00-19:00 (service industry)
- Customer has multi-shift operations (24h manufacturing line; SLAs should NOT pause)
- Customer has multiple offices in different timezones

Without a real calendar, `businessHoursOnly = true` is misleading. SLAs miss real breaches (treating Sunday as a workday) or fire false breaches (treating 春節 as work days).

This change introduces `IBusinessCalendar` with per-tenant configuration: working hours per weekday, holiday list, special compensatory work days. The SLA timer / `businessDaysBetween` CEL function plug into it cleanly.

## What Changes

### Calendar capability (NEW `bpm-business-calendar`)

**Entity** — `BusinessCalendar`:

- `Id`, `TenantId`
- `Name` — e.g., "Taiwan Default", "Acme HQ", "Acme Tainan Plant"
- `Timezone` — IANA name (e.g., `"Asia/Taipei"`); SLA computations convert to this TZ
- `IsDefault` (bool) — exactly one per tenant; used when nothing else specified
- `WorkingDaysJson` — `[{ day: "Mon", windows: [{ start: "09:00", end: "12:30" }, { start: "13:30", end: "18:00" }] }, ...]`; allows multi-window days (lunch break)
- `Status` (enum): `Active` / `Archived`
- Audit fields

**Entity** — `CalendarException`:

- `Id`, `BusinessCalendarId` (FK)
- `Date` (date)
- `Type` (enum): `Holiday` (no work) / `WorkDay` (compensatory work day overriding the regular non-work weekday) / `SpecialHours` (custom windows for that day)
- `WindowsJson` — only for `SpecialHours` type
- `Description` (e.g., "春節除夕")
- `Year` (denormalized for fast filter)

Pre-seeded: a "Taiwan Default" calendar for tenant null (system-wide template), with 2026 holidays imported (e.g., from Taiwan government open data CSV, manually curated for v1).

**Service** `IBusinessCalendar`:

- `Task<DateTime> AddBusinessDuration(DateTime start, TimeSpan duration, Guid? calendarId, CancellationToken ct)` — returns end time after consuming `duration` of business hours
- `Task<int> CountBusinessDays(DateTime start, DateTime end, Guid? calendarId, CancellationToken ct)` — for the CEL `businessDaysBetween` helper
- `Task<bool> IsBusinessHour(DateTime instant, Guid? calendarId, CancellationToken ct)`
- `Task<DateTime> NextBusinessHour(DateTime after, Guid? calendarId, CancellationToken ct)` — useful for SLA reset

When `calendarId` is null, the service uses the tenant's default calendar (or the system "Taiwan Default" if no tenant default).

### Holiday import workflow

`POST /api/calendars/{id}/import-holidays?source=tw-government&year=2026` — imports a year's holidays from a known source. v1 supports `tw-government` (CSV file packaged with the bpm-svc deployment, hand-curated yearly). Future: scrape from gov source on schedule.

### CRUD admin endpoints

- `GET /api/calendars` — list tenant's calendars
- `POST /api/calendars` — create new calendar
- `PUT /api/calendars/{id}` — edit working windows
- `POST /api/calendars/{id}/exceptions` — add a holiday or compensatory day
- `DELETE /api/calendars/{id}/exceptions/{date}`

Auth: tenant_admin only.

### Spec linkage

A spec MAY reference a calendar id under `meta.calendarId` (extension to spec_schema.md). When present, all SLA computations for that flow use that calendar. When absent, the tenant default is used. This allows e.g., a Taiwan factory flow vs a Vietnam factory flow on the same tenant.

### Frontend — calendar admin (in `add-system-admin-ui` change)

This proposal exposes the API + service. UI to manage calendars lands in `add-system-admin-ui`. For now: calendars are seeded and edited via SQL/JSON fixture.

### Replace minimal calendar in SLA timer

`add-sla-timer-escalation` shipped with `IBusinessCalendar` interface + a hardcoded default. This change:

1. Provides the database-backed implementation of `IBusinessCalendar`
2. Tenant default discovery via `GET /api/calendars/default-for-tenant`
3. Existing spec specs (without explicit calendar id) automatically pick up the seeded "Taiwan Default" calendar — same Mon-Fri default but with real holidays applied

### Out of scope (future changes)

- Calendar admin UI (in `add-system-admin-ui`)
- Schedule-driven holiday import (cron pull from gov source)
- Multi-region calendar federation (e.g., a global flow that uses different timezones per assignee)
- Calendar diff / version history
- Customer-imported ICS files
- Recurring exception patterns (e.g., "first Friday of every month")
- Holiday name i18n
- "What if?" calendar simulation (preview SLA dates with proposed exception)

## Capabilities

### New Capabilities

- `bpm-business-calendar` — BusinessCalendar + CalendarException entities, IBusinessCalendar service, AddBusinessDuration / CountBusinessDays / IsBusinessHour / NextBusinessHour, holiday import endpoint, CRUD endpoints, Taiwan-default seed.

### Modified Capabilities

- `bpm-sla-timer` — replace placeholder calendar with IBusinessCalendar; SLA computations honor per-spec `calendarId`; CEL `businessDaysBetween` helper uses the tenant default calendar.

## Impact

- **bpm-svc/src/Domain/Entities/Calendar/BusinessCalendar.cs**: new entity
- **bpm-svc/src/Domain/Entities/Calendar/CalendarException.cs**: new entity
- **bpm-svc/src/Domain/Entities/Calendar/CalendarExceptionType.cs**: enum
- **bpm-svc/src/Domain/Entities/Calendar/CalendarStatus.cs**: enum
- **bpm-svc/src/Application/Calendar/IBusinessCalendar.cs**: interface
- **bpm-svc/src/Application/Calendar/BusinessCalendarService.cs**: implementation
- **bpm-svc/src/Application/Calendar/HolidayImportService.cs**: hand-curated imports
- **bpm-svc/src/Persistence/Configurations/Calendar/**: EF configs
- **bpm-svc/src/Persistence/Migrations/AddBusinessCalendar**: 2 new tables, indexes
- **bpm-svc/src/Persistence/Seed/CalendarFixture.cs**: seeds "Taiwan Default" with 2026 holidays
- **bpm-svc/src/Api/Calendar/CalendarsController.cs**: CRUD + import endpoints
- **bpm-svc/src/Application/Sla/SlaCalculator.cs**: replace minimal calendar reference with IBusinessCalendar
- **bpm-svc/src/Application/Process/Expressions/Helpers/BusinessDaysBetweenHelper.cs**: pull calendar from request-scoped context
- **spec_schema.md** §2.1 (meta): add optional `calendarId`
- **bpm-ui/src/lib/calendar.ts**: types + API client (anticipating admin UI)
- **DB migration**: 2 new tables; data seed for default calendar + 2026 holidays
- **No NuGet additions** (System.TimeZoneInfo + DateOnly suffice)
- **Demo guard**: 9 mock-up forms, Home, Search, Report, lib/workflow.ts not modified
