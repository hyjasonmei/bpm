## ADDED Requirements

### Requirement: Punches are stored as immutable events, not daily records

The system SHALL persist each check-in or check-out as a single `AttendancePunch` row containing `PunchType` (`In` / `Out`), `PunchAt` (UTC), `LocalDate` (tenant-timezone date), `UserId`, `TenantId`, and `Source`. Daily aggregations (work hours, first-in, last-out) SHALL be computed at read time from the punch stream, NOT stored as denormalized columns. Punches once written MUST NOT be edited; corrections are represented as new punches with `Source = Correction` (defined for future use).

#### Scenario: Single check-in writes one punch row

- **GIVEN** Wilson has no punches today
- **WHEN** Wilson calls `POST /api/attendance/checkin` at 09:02 local time
- **THEN** one `AttendancePunch` row is written with `PunchType=In`, `Source=Manual`, `LocalDate` = today (tenant TZ), `PunchAt` = 09:02 UTC-equivalent

#### Scenario: Multiple check-ins same day are all kept

- **GIVEN** Wilson has check-in at 09:00, check-out at 12:00, check-in at 13:00, check-out at 18:00
- **WHEN** the system records each punch
- **THEN** four separate `AttendancePunch` rows exist for the same `LocalDate`
- **AND** none of them are merged or deduplicated

### Requirement: Today status is derived from the last punch of the day

The system SHALL compute today's status as one of three values based on the most recent punch in the user's tenant-local current day:

- `NotCheckedIn` — no punches today
- `OnDuty` — last punch is `In`
- `OffDuty` — last punch is `Out`

#### Scenario: No punches → NotCheckedIn

- **GIVEN** Wilson has no punches today
- **WHEN** he calls `GET /api/attendance/today`
- **THEN** the response has `status = NotCheckedIn`, `workHours = 0`, `inProgress = false`

#### Scenario: Last punch is In → OnDuty

- **GIVEN** Wilson punched In at 09:00 today, no later punches
- **WHEN** he calls `GET /api/attendance/today`
- **THEN** the response has `status = OnDuty`, `inProgress = true`

#### Scenario: Last punch is Out → OffDuty

- **GIVEN** Wilson punched In at 09:00 and Out at 18:00 today
- **WHEN** he calls `GET /api/attendance/today`
- **THEN** the response has `status = OffDuty`, `inProgress = false`

### Requirement: Work hours are computed by pairing In/Out punches in time order

The system SHALL compute today's accumulated work hours by sorting punches ascending by `PunchAt` and pairing each `In` with the next `Out`. If a trailing `In` has no following `Out`, the system SHALL use the current time as the virtual close and mark `inProgress = true`. Consecutive `In` punches: the earlier one is dropped (no segment counted). Consecutive `Out` punches: the later one is dropped (no segment counted). Lone `Out` (no preceding `In`): contributes 0 hours.

#### Scenario: One in, one out

- **GIVEN** punches: In@09:00, Out@17:00
- **WHEN** computing today
- **THEN** workHours = 8.0, inProgress = false

#### Scenario: Two segments

- **GIVEN** punches: In@09:00, Out@12:00, In@13:00, Out@18:00
- **WHEN** computing today
- **THEN** workHours = 8.0 (3 + 5), inProgress = false

#### Scenario: Trailing In counts up to now

- **GIVEN** punches: In@09:00 (and current time is 11:00)
- **WHEN** computing today
- **THEN** workHours = 2.0, inProgress = true

#### Scenario: Consecutive Ins drop the earlier one

- **GIVEN** punches: In@09:00, In@10:00, Out@18:00
- **WHEN** computing today
- **THEN** workHours = 8.0 (only 10:00–18:00 counted), inProgress = false

#### Scenario: Lone Out contributes nothing

- **GIVEN** punches: Out@18:00 (only)
- **WHEN** computing today
- **THEN** workHours = 0, status = OffDuty, no exception thrown

### Requirement: Endpoints operate only on the authenticated user

The system SHALL derive `UserId` solely from the authentication context for all attendance endpoints. The endpoints MUST NOT accept `userId` via body, query, or route parameter. There SHALL be no endpoint in this capability that returns another user's punch data.

#### Scenario: Endpoint cannot be coerced to read another user

- **GIVEN** Wilson is authenticated
- **WHEN** he calls `POST /api/attendance/checkin` with body `{ "userId": "<Yang's id>" }`
- **THEN** the punch is written for Wilson, NOT Yang
- **AND** the body field is silently ignored (no separate "spoofing" status code)

### Requirement: History returns daily summaries computed from punches

The system SHALL provide `GET /api/attendance/history?days=N` (default 30, max 90) that returns one `DailySummary` per local date in the range that has at least one punch. Each summary contains `date`, `firstIn` (earliest In punch time, or null), `lastOut` (latest Out punch time, or null), `workHours` (computed via the same pairing rule), and `punchCount`. Days with no punches are omitted from the response.

#### Scenario: Day with full in/out

- **GIVEN** Wilson punched In@09:00 Out@18:00 on 2026-05-07
- **WHEN** he calls `GET /api/attendance/history?days=7`
- **THEN** the 2026-05-07 entry has `firstIn = 09:00`, `lastOut = 18:00`, `workHours = 9.0`, `punchCount = 2`

#### Scenario: Day with no punches omitted

- **GIVEN** Wilson has zero punches on 2026-05-06 (a weekend)
- **WHEN** he calls `GET /api/attendance/history?days=7`
- **THEN** the response array does not contain a 2026-05-06 entry

#### Scenario: Days param clamped at 90

- **GIVEN** Wilson calls `GET /api/attendance/history?days=365`
- **WHEN** the controller validates the param
- **THEN** the value is clamped to 90 and only the last 90 days returned (no error)

### Requirement: Cross-midnight shifts are not handled

The system SHALL treat each `LocalDate` as an independent day for status, work-hours, and history computations. A punch In on day N with no Out before midnight does NOT carry over to day N+1: on day N the trailing-In rule applies (virtual close at midnight is NOT inserted; if the user is still asking on day N+1 with no day-N+1 punches, status is `NotCheckedIn`). This MVP explicitly excludes night-shift workers.

#### Scenario: Midnight does not auto-close the previous day

- **GIVEN** Wilson punched In@22:00 on 2026-05-07 and never punched Out
- **WHEN** at 02:00 on 2026-05-08 he calls `GET /api/attendance/today`
- **THEN** the response has `status = NotCheckedIn` (today = 2026-05-08, no punches yet)
- **AND** 2026-05-07's history entry has `firstIn = 22:00`, `lastOut = null`, `workHours` computed only against day-N events (likely 2.0 if computed-against-now-clipped-to-midnight, or 0 if strict — implementation MAY choose; spec only requires no carry-over)
