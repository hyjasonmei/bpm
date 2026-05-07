# Design notes

## 1. Why Status is derived, not stored

`Delegation.Status` is a function of three facts: `StartAt`, `EndAt`, `CancelledAt` (and the current time). Storing it authoritatively means we'd need a job to flip Active→Expired the moment an EndAt passes, or queries return stale data. Both approaches add complexity for a value that's deterministically computable.

Solution: `Status` is computed on read via `DelegationStatusOf(d, now)`. The column exists in the DB as a denormalized cache (refreshed daily by `DelegationStatusRefreshJob` at 00:05 UTC) so SELECT queries can index by status without a function call. Live status queries (the `GetActiveDelegateAsync` lookup) re-compute on the fly using `WHERE StartAt <= @now AND EndAt > @now AND CancelledAt IS NULL` — no reliance on the cached column.

If the daily job ever fails or the cache lags, the worst case is the UI showing "Active" for one extra day on a row that should be "Expired" — never an authorization or task-routing bug, since the live path always re-computes.

## 2. Why one active delegation per user

Per Jason: 1a. A user can have at most one active delegation at any moment. This is enforced by the overlap rejection rule: when creating a delegation `(start, end)`, we reject if any non-cancelled row owned by the same granter has a time window overlapping `[start, end)`. Includes both Active and Scheduled rows.

Implication for UX: to "change my delegate", granter cancels current → creates new. The dialog makes this two clicks.

Edge case: what if the new delegation starts during the old one's end-of-day? We use half-open intervals `[start, end)`. A delegation ending at `2026-05-15T18:00:00Z` and another starting at `2026-05-15T18:00:00Z` do NOT overlap. Documented in `IsOverlapping(a, b)`.

## 3. Why `EndAt > StartAt + 1 hour`

Two reasons:
- Sanity: a 30-second delegation is a UI mistake, not an intent
- Backstop: the daily refresh job runs at 00:05; if a delegation has an end time at 00:04 the next day, the cache might briefly show "Active" after it's expired. A 1-hour minimum + live recomputation defuses this.

Easy to relax later if a real use case demands it.

## 4. Why future-start is allowed (3a)

People plan time off. "Next Monday I'll be on a workshop, my colleague Wilson covers" is the canonical case. Forcing `StartAt = now` would make every delegation a last-minute action.

The list endpoint distinguishes:
- `Active` — `StartAt <= now < EndAt`
- `Scheduled` — `now < StartAt`
- `Expired` — `now >= EndAt`
- `Cancelled` — `CancelledAt is set`

UI shows Scheduled with a light-blue chip + "從 X 開始 / Starts X" relative time.

## 5. Cycle handling — warn, do not block (1-hop only)

Two users covering each other on a joint trip is realistic:
- Wilson 代理 Yang from 5/10-15
- Yang 代理 Wilson from 5/10-15

If a task lands for Wilson during this window, runtime applies one hop: actual_assignee = Yang. If we then asked "is there a delegation for Yang?" — yes, pointing at Wilson — and applied recursively, we'd loop. Hence: **runtime applies exactly one hop**. Documented in spec deltas.

Cycle detection at create time is *informational*: if a 1-hop cycle exists, the response includes a warning string. The UI surfaces it as a yellow callout but the action proceeds. Customer is in charge.

(N-hop cycles aren't checked because the constraint of one delegation per granter at a time bounds the search to depth 2 — A → B and B → C is not a "cycle" but a multi-hop chain, which we still apply only the first hop of.)

## 6. Why notifications do NOT follow delegation (4a)

Choice: delegation transforms the *task assignment*, not the *notification recipient resolution*.

Why:
- The Process Runtime populates `NotificationContext.CurrentAssigneeUserId` with the post-delegation `actual_assignee_id`. The notification engine resolves `current_assignee` recipient from that context — so the delegate naturally gets the in-app + email, the granter does not. No engine change.
- For role-based recipients (`{ type: 'actor', inner: { type: 'role', code: 'HR' } }`): the resolver returns the HR set as-is. If a member is on delegated leave, they still receive the notification. This is intentional — they may want to see their delegated load even on vacation.
- Per Jason 5b: no system notifications about delegation lifecycle events. Granter checks the dashboard.

If a customer ever asks for "notify granter when their delegate accepts a task on their behalf", that's a future feature gated on a per-delegation `notify_granter_on_use` flag.

## 7. Why no admin override (7a)

If an admin needs to cancel another user's delegation, the use case is "the user is sick / unreachable and their delegate is too". That's organizational chaos not modeled here. Customer should escalate via the people-side, not via an admin button.

Operationally, admins can use user-impersonation (sign in as the granter) to manage the delegation through the normal UI. That tooling exists (or will) at the auth layer; not this proposal's concern.

## 8. UI surface decisions

**Why RoleSwitcher dropdown (6a)**: every persona switch already shows the current user's identity context. Delegation is identity-adjacent ("who I'm letting cover for me right now") so co-locating in the same dropdown is clean. No new top-bar real estate consumed.

**Why dialog instead of dedicated page**: delegation management is infrequent (a user might create 2-3 delegations per year). A modal dialog is enough — full screen would feel over-engineered. If a customer needs a permanent settings page later, `Notifications.tsx` precedent shows it's easy to lift.

**Why InboundBanner on Home only**: a delegate covering for someone else needs a clear visual reminder that some of the work in their inbox isn't normally theirs. Home is the first screen post-login. Other screens don't need it (they show case-level details where the delegation is already encoded in the task assignee).

## 9. Operations considerations

**Stale cache**: handled in §1.

**Granter deactivated**: if `User.IsActive = false` for the granter, what happens to their active delegations? The runtime's task-spawning logic already won't assign tasks to inactive users, so delegation never fires. The UI shows the delegation as Active still (data is data). On the rare case the user reactivates, delegation resumes if not yet expired.

**Delegate deactivated mid-window**: the runtime task spawning, when it hits an inactive `actual_assignee_id`, falls back to `original_assignee_id`. Delegate's transition is silent at the data layer; the granter sees their tasks land on themselves again. Document this edge case in tasks.md test list.

**Timezone**: store all times UTC. The frontend renders in `Intl.DateTimeFormat(navigator.language, { timeZone: ... })` — defaults to user's browser TZ for display, server stores UTC for queries.

## 10. Open questions

- **Delegation discovery**: should the granter see "Wilson is currently delegating for Yang" when picking a delegate (so they know Wilson is already loaded)? Probably yes — add a "Wilson is currently covering for 2 others" footnote in the picker. Defer to follow-up polish.
- **Bulk cancel**: for a user that goes on long leave then changes plans, "cancel all my future delegations" might be useful. Defer.
- **Soft expiry tolerance**: when a delegation ends at noon but the delegate is still mid-task, do we yank the task back to granter? Right now: yes (next task spawn at 12:01 goes to granter). Existing in-flight tasks stay with delegate. Document.
- **Audit / who-changed**: do we need a `DelegationChangeLog` table tracking edits and cancellations? Probably yes once a customer asks about compliance (ISO 9001 etc.). Defer; current `LastModifiedAt` is the only audit.
- **Per-flow scoping**: Jason picked global (4a) for v1, but partner's customers may eventually want "副總請假 → 代理人不能代採購（金額過大）". Defer; would add `Scope: Global | { type: 'flow_codes', codes: [...] } | { type: 'amount_under', limit: ... }`.
