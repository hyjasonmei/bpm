## ADDED Requirements

### Requirement: SlaCalculator delegates to IBusinessCalendar

The `SlaCalculator` SHALL use `IBusinessCalendar.AddBusinessDuration` to compute task DueAt when `nodeSla.businessHoursOnly = true`. The calendar resolution priority is:

1. `meta.calendarId` from the spec snapshot, when present
2. The tenant's default calendar (`IsDefault = true`)
3. The system "Taiwan Default" calendar (fallback when tenant has no default)

The placeholder calendar from `add-sla-timer-escalation` SHALL be removed entirely (replaced, not co-existing) once this change ships.

#### Scenario: Spec-specified calendar wins

- **GIVEN** spec snapshot has `meta.calendarId = C1` and tenant default = C2
- **WHEN** runtime computes DueAt for a task with `businessHoursOnly = true`
- **THEN** the calculator uses C1, not C2

#### Scenario: Tenant default used when spec omits

- **GIVEN** spec snapshot has no `meta.calendarId`; tenant default = C2
- **WHEN** runtime computes DueAt
- **THEN** the calculator uses C2

#### Scenario: System default used when tenant lacks default

- **GIVEN** a tenant has no calendars yet (initial onboarding); seed has not run
- **WHEN** runtime computes DueAt
- **THEN** the calculator falls back to the system "Taiwan Default" calendar

### Requirement: businessDaysBetween helper uses tenant calendar

The CEL helper `businessDaysBetween(start, end)` SHALL use the tenant's default calendar (or the spec-specified one when invoked within an instance with calendarId set). This means the helper's count of business days correctly excludes 春節 / 端午 / 國慶 holidays per the seeded Taiwan default.

#### Scenario: Helper excludes 春節 holidays

- **GIVEN** Taiwan default calendar with 2026-02-14 to 2026-02-22 春節 holidays seeded
- **WHEN** `businessDaysBetween("2026-02-12", "2026-02-25")` evaluates
- **THEN** the result is the correct business-day count (excludes weekends + 春節 block)
