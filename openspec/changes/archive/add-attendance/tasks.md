# Tasks

## 1. Backend — Domain

- [ ] 1.1 Create `bpm-svc/src/Domain/Entities/Attendance/PunchType.cs` enum (`In`, `Out`)
- [ ] 1.2 Create `bpm-svc/src/Domain/Entities/Attendance/PunchSource.cs` enum (`Manual`, `Correction`, `Auto`)
- [ ] 1.3 Create `bpm-svc/src/Domain/Entities/Attendance/AttendancePunch.cs` (inherits AuditableEntity)

## 2. Backend — Persistence

- [ ] 2.1 Create `bpm-svc/src/Persistence/Configurations/Attendance/AttendancePunchConfiguration.cs`
- [ ] 2.2 Add index `(TenantId, UserId, LocalDate)` and `(TenantId, UserId, PunchAt DESC)`
- [ ] 2.3 Add `DbSet<AttendancePunch> AttendancePunches` to `BpmDbContext`
- [ ] 2.4 Generate migration: `dotnet ef migrations add AddAttendance`
- [ ] 2.5 Apply migration locally; verify schema with `sqlite3 bpm.db .schema "AttendancePunches"`

## 3. Backend — Application

- [ ] 3.1 Create `bpm-svc/src/Application/Attendance/Dtos/PunchDto.cs`
- [ ] 3.2 Create `bpm-svc/src/Application/Attendance/Dtos/TodayStatusDto.cs` (status, punches, workHours, inProgress)
- [ ] 3.3 Create `bpm-svc/src/Application/Attendance/Dtos/DailySummaryDto.cs` (date, firstIn, lastOut, workHours, punchCount)
- [ ] 3.4 Create `bpm-svc/src/Application/Attendance/IAttendanceService.cs`
- [ ] 3.5 Create `bpm-svc/src/Application/Attendance/AttendanceService.cs`
- [ ] 3.6 Implement `CheckInAsync(userId)` — write punch with `PunchType.In`, `Source.Manual`
- [ ] 3.7 Implement `CheckOutAsync(userId)` — write punch with `PunchType.Out`, `Source.Manual`
- [ ] 3.8 Implement `ComputeWorkHoursForDay(IEnumerable<AttendancePunch>)` — pure function, in/out pairing per design §2
- [ ] 3.9 Implement `GetTodayAsync(userId)` — fetch today's punches, compute status + work hours
- [ ] 3.10 Implement `GetHistoryAsync(userId, days)` — fetch punches in range, group by LocalDate, compute daily summaries
- [ ] 3.11 Register `AttendanceService` in `Application/DependencyInjection.cs`

## 4. Backend — API

- [ ] 4.1 Create `bpm-svc/src/API/Controllers/AttendanceController.cs`
- [ ] 4.2 `POST /api/attendance/checkin` → calls `CheckInAsync(userContext.UserId)`, returns 201 + PunchDto
- [ ] 4.3 `POST /api/attendance/checkout` → calls `CheckOutAsync`
- [ ] 4.4 `GET /api/attendance/today` → returns TodayStatusDto
- [ ] 4.5 `GET /api/attendance/history?days=30` (default 30, max 90) → returns DailySummaryDto[]
- [ ] 4.6 All endpoints `[Authorize]`, never accept userId from request body/query

## 5. Backend — Tests

- [ ] 5.1 Unit test `ComputeWorkHoursForDay`: empty punches → 0
- [ ] 5.2 Unit test: single In + single Out → diff in hours
- [ ] 5.3 Unit test: In, Out, In, Out → sum of two segments
- [ ] 5.4 Unit test: trailing In with no Out → uses "now" as virtual close, marks inProgress
- [ ] 5.5 Unit test: consecutive Ins → previous In ignored
- [ ] 5.6 Unit test: consecutive Outs → second Out ignored
- [ ] 5.7 Unit test: only Outs no Ins → 0 hours, no exception
- [ ] 5.8 Integration test: checkin then checkout then GetToday → status = OffDuty, work hours > 0
- [ ] 5.9 Integration test: GetToday for user with no punches → status = NotCheckedIn

## 6. Frontend — types + api client

- [ ] 6.1 Create `bpm-ui/src/types/attendance.ts` mirroring backend DTOs
- [ ] 6.2 Create `bpm-ui/src/lib/api/attendance.ts` with `checkIn()`, `checkOut()`, `getToday()`, `getHistory(days)`
- [ ] 6.3 Wire base URL + auth header from existing api client convention

## 7. Frontend — Header nav

- [ ] 7.1 In `bpm-ui/src/components/AppLayout.tsx`, import `Clock` from `lucide-react`
- [ ] 7.2 Extend `Screen` union: `| { kind: 'attendance' }`
- [ ] 7.3 Add `<NavBtn icon={<Clock />} active={screen.kind==='attendance'} onClick={() => setScreen({ kind: 'attendance' })}>Attendance</NavBtn>` in right-side cluster, **before** the Notifications button

## 8. Frontend — Attendance screen

- [ ] 8.1 Create `bpm-ui/src/screens/Attendance.tsx` skeleton
- [ ] 8.2 Status card component: today's status (NotCheckedIn / OnDuty / OffDuty), accumulated work hours, most-recent in/out times
- [ ] 8.3 Primary action button: state-driven label (`Check in` / `Check out` / `Check in again`)
- [ ] 8.4 On click → call `checkIn()` or `checkOut()` per state, optimistic update, refetch on success
- [ ] 8.5 History table: date, first in, last out, work hours, punch count (last 30 days)
- [ ] 8.6 `Request Correction` button top-right (placeholder `alert`, real page is separate change)
- [ ] 8.7 Wire screen into `App.tsx` router based on `screen.kind === 'attendance'`

## 9. Frontend — verify

- [ ] 9.1 `npx tsc -p tsconfig.app.json --noEmit` clean
- [ ] 9.2 `npm run build` clean
- [ ] 9.3 Manual: open page → click Check in → status flips to OnDuty → Check out → status flips to OffDuty → row appears in history
- [ ] 9.4 Manual: refresh page → state persists from backend
- [ ] 9.5 Browser screenshot via chrome-devtools (fullPage) for dogfood-screenshots/

## 10. Documentation

- [ ] 10.1 Update CLAUDE.md if there's a feature list (or leave alone)
- [ ] 10.2 Add brief note to dogfood walkthrough if relevant
