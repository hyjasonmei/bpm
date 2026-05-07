# Design notes

## 1. Why per-tenant calendar instead of system-wide

A tenant could have:

- Manufacturing site in Taiwan + sales office in Vietnam (different holidays)
- HQ on regular schedule + 24-hour customer support team (no business-hours filter)
- Production line that operates 24×7×365 (calendar irrelevant)

Per-tenant calendar with a Taiwan-default seed lets simple cases ride defaults while complex cases customize.

## 2. Multi-window weekday support

`WorkingDaysJson` allows each weekday to have multiple windows:

```json
{
  "day": "Mon",
  "windows": [
    { "start": "09:00", "end": "12:30" },
    { "start": "13:30", "end": "18:00" }
  ]
}
```

Why? Lunch breaks. SMEs in Taiwan typically have a 12:00-13:30 lunch break that *should not* count against SLA. Without windowed days we have to lie ("call lunch a 1.5h gap that doesn't count" — works but kludgy in code).

Implementation: `IsBusinessHour(instant)` returns true only when instant falls within a window. `AddBusinessDuration` walks window-by-window.

## 3. CalendarException types

| Type | What it does |
|---|---|
| `Holiday` | Mark this date as no work despite the regular day's windows (e.g., Wednesday 2026-04-04 兒童節 — child's day, even though Wednesdays are work days) |
| `WorkDay` | Mark this date as a work day despite the regular day being non-work (e.g., Saturday 2026-02-08 春節補班 — compensatory work day) |
| `SpecialHours` | Custom windows for this date (e.g., half-day on 端午前一天) |

Composition rule: a date with both `Holiday` and `WorkDay` exceptions is invalid (validator rejects); a `SpecialHours` overrides regular windows for that date.

## 4. Taiwan default calendar — what we ship

For 2026, hand-curate the holidays from the Taiwan government's published list:

- 2026-01-01 元旦
- 2026-02-14 至 2026-02-22 春節 (8 days, includes 補班 nuances)
- 2026-02-28 和平紀念日
- 2026-04-03 至 2026-04-06 兒童節+清明節
- 2026-05-01 勞動節
- 2026-06-19 端午節
- 2026-09-25 教師節 (sometimes a national day, sometimes not — check current year)
- 2026-10-09 至 2026-10-11 國慶連假
- 補班 days that turn weekends into work days

Note: dates above are illustrative; correct list is determined at implementation time from the official 2026 calendar. The seed script reads from a packaged JSON file `holidays_tw_2026.json` so updates to the list are JSON-only.

## 5. Timezone handling

The calendar's `Timezone` is the tenant's reference TZ. SLA computations:

1. Convert `spawnTime` from UTC to the calendar's TZ
2. Apply the calendar (windows, exceptions)
3. Convert the result back to UTC for storage in `Task.DueAt`

This way DueAt is stored as UTC (consistent with all other timestamps) but feels right to humans in their TZ.

Edge case: DST. Taiwan doesn't observe DST so it's a non-issue. For a customer in Australia with DST, the calendar computation must handle the spring-forward / fall-back gap; .NET's TimeZoneInfo handles this. Defer Australia-specific testing until a customer asks.

## 6. Performance

`AddBusinessDuration` for a 24h SLA might walk 4-5 windows worst case (across 3 days). Each window check is a constant-time comparison + exception lookup. Total < 100 µs.

Caching: per-request, cache the resolved calendar object (rows + exceptions for ±1 year around now) in a request-scoped service. Avoid hitting DB per SLA computation.

## 7. Why store windows as JSON, not separate rows

We considered:
- Option A: `BusinessCalendarWindow` table (one row per (calendar, day, window_index))
- Option B: `WorkingDaysJson` column on calendar

Option B wins because:
- Windows are read together; never queried independently
- JSON column is one row roundtrip vs N
- Schema changes (add a "shift" indicator field) are JSON-only

Option A would be required if we wanted to query "every Wednesday window across all calendars" — which we don't.

## 8. Exception year denormalization

`CalendarException.Year` is denormalized from `Date.Year` for fast filtering. Pattern: "give me all 2026 exceptions for this calendar" is a common query (admin UI, year-end planning, holiday-load forecasting). Index on `(BusinessCalendarId, Year)`.

## 9. Open questions

- **What about per-user holidays** (e.g., 王經理's birthday party day)? Not in scope — that's a personal calendar concern, not BPM. Defer.
- **Half-day support**: `SpecialHours` covers it. UI to make it ergonomic comes with admin.
- **Recurrence patterns**: not yet — every exception is a single date. If a customer needs "every first Friday off", we add later.
- **Cross-calendar inheritance**: e.g., "Acme Plant calendar inherits from Taiwan Default plus these 5 plant-specific shutdowns". Future feature; for now, customer copies and edits.
- **Validation**: what if a customer creates a calendar where every weekday is non-working? SLAs would never advance. Validator: at least one window across the week.
