# Tasks

## 1. Domain

- [ ] 1.1 Create `bpm-svc/src/Domain/Entities/Calendar/CalendarStatus.cs` enum (Active, Archived)
- [ ] 1.2 Create `bpm-svc/src/Domain/Entities/Calendar/CalendarExceptionType.cs` enum (Holiday, WorkDay, SpecialHours)
- [ ] 1.3 Create `BusinessCalendar.cs` (inherits AuditableEntity); columns Id, TenantId, Name, Timezone, IsDefault, WorkingDaysJson, Status
- [ ] 1.4 Create `CalendarException.cs`; columns Id, BusinessCalendarId (FK), Date, Type, WindowsJson (only for SpecialHours), Description, Year (denormalized)

## 2. Persistence

- [ ] 2.1 EF configurations under `bpm-svc/src/Persistence/Configurations/Calendar/`
- [ ] 2.2 Indexes: `(TenantId, IsDefault) WHERE IsDefault = true` (for default lookup); `(BusinessCalendarId, Year)`; `(BusinessCalendarId, Date)`
- [ ] 2.3 Generate migration `AddBusinessCalendar`; apply locally; verify schema

## 3. Service

- [ ] 3.1 Create `IBusinessCalendar.cs` interface in Application/Calendar
- [ ] 3.2 Implement `BusinessCalendarService.cs`:
  - `LoadCalendarAsync(calendarId)` — loads calendar + exceptions for ±1 year; caches per request
  - `IsBusinessHour(instant, calendarId)` — converts instant to TZ, checks against windows + exceptions
  - `NextBusinessHour(after, calendarId)` — walk forward to next window
  - `AddBusinessDuration(start, duration, calendarId)` — accumulate duration across windows
  - `CountBusinessDays(start, end, calendarId)` — count business days
- [ ] 3.3 Wire into `IBusinessCalendar` registration in Application/DependencyInjection (replaces minimal placeholder from add-sla-timer-escalation)
- [ ] 3.4 Unit tests:
  - Single-window day with no exceptions
  - Multi-window day (lunch break)
  - Exception: Holiday on a regular weekday
  - Exception: WorkDay on a Saturday
  - Exception: SpecialHours overrides regular
  - Multi-day duration that wraps across weekend + holiday
  - Timezone correctness (server in UTC; calendar in Asia/Taipei; DueAt converted properly)

## 4. Holiday import

- [ ] 4.1 Create `bpm-svc/src/Application/Calendar/HolidayImportService.cs`
- [ ] 4.2 Bundle `bpm-svc/src/Application/Calendar/holidays_tw_2026.json` with hand-curated Taiwan 2026 holidays + 補班 days
- [ ] 4.3 `ImportTaiwanGovernmentHolidays(calendarId, year)` — reads the JSON, inserts CalendarException rows; idempotent (skip dates that already have an exception of matching type)
- [ ] 4.4 API endpoint `POST /api/calendars/{id}/import-holidays?source=tw-government&year=2026` — admin only

## 5. CRUD endpoints

- [ ] 5.1 Create `bpm-svc/src/Api/Calendar/CalendarsController.cs`:
  - `GET /api/calendars` — list tenant's
  - `GET /api/calendars/{id}` — single + exceptions
  - `POST /api/calendars` — create
  - `PUT /api/calendars/{id}` — edit working windows
  - `DELETE /api/calendars/{id}` — archive (no hard delete)
  - `POST /api/calendars/{id}/exceptions` — add exception
  - `DELETE /api/calendars/{id}/exceptions/{exceptionId}` — remove
- [ ] 5.2 Auth: tenant_admin role only
- [ ] 5.3 Validation: at least one window across the week; no duplicate exception per date; valid timezone string
- [ ] 5.4 Integration tests

## 6. Seeding

- [ ] 6.1 Create `bpm-svc/src/Persistence/Seed/CalendarFixture.cs`
- [ ] 6.2 On first run: create "Taiwan Default" calendar (TenantId = null for system-wide; mirror per-tenant via `IsDefault = true` per tenant when first tenant uses it); load 2026 holidays
- [ ] 6.3 Each tenant on creation gets a copy of "Taiwan Default" as its initial default (cloned; can be edited per-tenant after)
- [ ] 6.4 Verify seed loads cleanly

## 7. Spec linkage

- [ ] 7.1 Update `spec_schema.md` §2.1 to add optional `meta.calendarId` (Guid string)
- [ ] 7.2 Update `bpm-ui/src/lib/onboarding.ts` SpecMeta type to include `calendarId?`
- [ ] 7.3 Wizard StepSource (or a new admin-side spec settings tab) shows which calendar this spec uses; default = tenant default; admin can pick another
- [ ] 7.4 Process snapshot includes calendarId if set; runtime resolves the right calendar at SLA computation time

## 8. SLA timer integration

- [ ] 8.1 Refactor `add-sla-timer-escalation`'s `SlaCalculator` to delegate to `IBusinessCalendar` instead of the placeholder
- [ ] 8.2 Pass `meta.calendarId` from the spec snapshot down to `ComputeDueAt`; default to tenant default
- [ ] 8.3 Update CEL `BusinessDaysBetweenHelper` to read calendar from request context

## 9. Frontend types

- [ ] 9.1 Create `bpm-ui/src/lib/calendar.ts`: TypeScript types + API client (no UI yet — that's `add-system-admin-ui`)
- [ ] 9.2 Document the API in `bpm-svc/CLAUDE.md`

## 10. End-to-end verification

- [ ] 10.1 `dotnet build` clean
- [ ] 10.2 All tests pass
- [ ] 10.3 Apply migration; verify BusinessCalendars + CalendarExceptions tables; verify seed loaded
- [ ] 10.4 GET /api/calendars; verify default calendar appears
- [ ] 10.5 Spawn a task with `meta.calendarId` and SLA `8h, businessHoursOnly: true`; verify DueAt skips holidays in the seeded data
- [ ] 10.6 Test: SLA spanning 春節 weekend correctly extends DueAt past the holiday block
- [ ] 10.7 CEL `businessDaysBetween("2026-02-13", "2026-02-23")` returns the correct count given seeded 春節 holidays
- [ ] 10.8 **Demo guard**: 9 mock-up forms, Home, Search, Report, lib/workflow.ts not modified

## 11. Commit

- [ ] 11.1 Commit in chunks (entities + migration; service; holiday import; CRUD endpoints; SLA integration; seed; verification)
- [ ] 11.2 Push via GitKraken
