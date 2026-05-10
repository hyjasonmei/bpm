## ADDED Requirements

### Requirement: Delegation entity carries granter, delegate, and window

The system SHALL persist a `Delegation` entity with: `Id`, `TenantId`, `GranterUserId`, `DelegateUserId`, `StartAt` (UTC), `EndAt` (UTC), `Reason` (nullable, max 500 chars), `Status`, `CancelledAt` (nullable), `CreatedAt`, `LastModifiedAt`. `GranterUserId` and `DelegateUserId` SHALL be foreign keys to `User`. The persisted `Status` is a denormalized cache; the authoritative status is computed from `StartAt`, `EndAt`, `CancelledAt`, and the current time via `DelegationStatusOf(d, nowUtc)`.

#### Scenario: Active delegation persisted

- **WHEN** a granter creates a delegation with StartAt = now, EndAt = now + 24h, DelegateUserId set
- **THEN** a Delegation row is inserted with `Status = Active`, `CancelledAt = null`

#### Scenario: Status computed authoritatively from times

- **GIVEN** a delegation with StartAt = T-1d, EndAt = T+1d, CancelledAt = null
- **WHEN** the system computes status at time T
- **THEN** the result is `Active`

#### Scenario: Cancelled status overrides time

- **GIVEN** a delegation with StartAt = T-1d, EndAt = T+1d, CancelledAt = T (set)
- **WHEN** the system computes status at time T+1h
- **THEN** the result is `Cancelled` (not Active despite still being inside the window)

### Requirement: One active or scheduled delegation per granter

The system SHALL reject any `CreateDelegation` call whose `(StartAt, EndAt)` window overlaps an existing non-cancelled delegation owned by the same `GranterUserId`. Overlap uses half-open `[start, end)` semantics — adjacent windows (one ending at exactly `t`, the next starting at exactly `t`) do NOT overlap. The rejection MUST surface as a 409 Conflict error with the conflicting row's id and time window.

#### Scenario: Overlapping window rejected

- **GIVEN** Wilson has an active delegation `[2026-05-10 09:00 UTC, 2026-05-15 17:00 UTC)` (not cancelled)
- **WHEN** Wilson tries to create another delegation `[2026-05-12 00:00 UTC, 2026-05-13 00:00 UTC)`
- **THEN** the request returns 409 with the existing row's id and window in the error body

#### Scenario: Adjacent window accepted

- **GIVEN** Wilson has a delegation ending at `2026-05-15 17:00 UTC`
- **WHEN** Wilson creates another starting at `2026-05-15 17:00 UTC` ending at `2026-05-16 17:00 UTC`
- **THEN** the request succeeds (windows don't overlap)

#### Scenario: Cancelled rows don't count as overlap

- **GIVEN** Wilson has a cancelled delegation `[2026-05-10, 2026-05-15)`
- **WHEN** Wilson creates a new delegation `[2026-05-12, 2026-05-14)`
- **THEN** the new request succeeds

### Requirement: Self-delegation rejected

The system SHALL reject any `CreateDelegation` where `GranterUserId == DelegateUserId`. The wizard / dialog UI SHALL also pre-filter the current user from the delegate picker (defense in depth — server is authoritative).

#### Scenario: Self-delegate rejected by API

- **WHEN** Wilson submits a CreateDelegation with `GranterUserId = Wilson, DelegateUserId = Wilson`
- **THEN** the request returns 400 with error "cannot delegate to self"

### Requirement: End must be at least 1 hour after start; start cannot be in the past

`CreateDelegation` SHALL reject requests where:

- `EndAt <= StartAt + 1 hour` — too short
- `StartAt < now - 5 minutes` — start in the past (5-min skew tolerance)

`UpdateDelegation` SHALL allow modifying `EndAt` in either direction (shorten OR extend). The new `EndAt` must still satisfy `EndAt > StartAt + 1 hour` and `EndAt > now`. Changing direction freely is intentional: the granter is the owner of the window and may rescind earlier or extend later as their absence plans change.

#### Scenario: 30-minute window rejected

- **WHEN** a granter submits StartAt = now, EndAt = now + 30 min
- **THEN** the request returns 400 with "duration must be at least 1 hour"

#### Scenario: Past start rejected

- **WHEN** a granter submits StartAt = now - 1 hour
- **THEN** the request returns 400 with "start cannot be in the past"

#### Scenario: Future scheduled start accepted

- **WHEN** a granter submits StartAt = now + 7 days, EndAt = now + 14 days
- **THEN** the delegation is created with Status = Scheduled

#### Scenario: Update shortens EndAt

- **GIVEN** an active delegation EndAt = now + 5 days
- **WHEN** the granter sends `PUT /api/delegations/{id}` with `end_at = now + 2 days`
- **THEN** the delegation's EndAt is updated to now + 2 days

#### Scenario: Update extends EndAt

- **GIVEN** the same delegation with EndAt = now + 5 days
- **WHEN** the granter sends `end_at = now + 10 days`
- **THEN** the delegation's EndAt is updated to now + 10 days
- **AND** the request returns 200 OK

#### Scenario: Update past EndAt rejected

- **GIVEN** an active delegation EndAt = now + 5 days
- **WHEN** the granter sends `end_at = now - 1 hour`
- **THEN** the request returns 400 with "EndAt must be in the future"

### Requirement: Cycle detection rejects creation

The system SHALL detect 1-hop cycles at create time: if creating delegation `A → B`, the system MUST query whether B has an existing non-cancelled delegation pointing back at A. If yes, the create SHALL be rejected with 409 Conflict and an error message identifying the conflicting reverse-direction delegation. Allowing cycles even with runtime safeguards (1-hop only) creates audit / mental-model confusion that outweighs the convenience; rejecting at write time keeps the delegation graph acyclic by construction.

#### Scenario: Cycle rejected at create

- **GIVEN** Yang has an active delegation pointing at Wilson `(Yang → Wilson)`
- **WHEN** Wilson attempts to create a delegation pointing at Yang `(Wilson → Yang)`
- **THEN** the response is 409 Conflict with body referencing the conflicting delegation `Yang → Wilson` (id and time window)
- **AND** no new delegation row is persisted

#### Scenario: Reverse delegation allowed after original cancelled

- **GIVEN** Yang's delegation to Wilson is `Cancelled`
- **WHEN** Wilson creates `(Wilson → Yang)`
- **THEN** the response is 201 Created — cycles only consider non-cancelled rows

#### Scenario: Non-cyclic creation succeeds normally

- **GIVEN** Yang has no delegation
- **WHEN** Wilson creates `(Wilson → Yang)`
- **THEN** the response is 201 Created

### Requirement: Only granter can cancel their own delegation

`POST /api/delegations/{id}/cancel` SHALL succeed only when the calling user is the delegation's `GranterUserId`. Any other actor — including the delegate — receives 403 Forbidden. The endpoint SHALL be idempotent: cancelling an already-cancelled delegation returns 200 OK without state change.

#### Scenario: Granter cancels successfully

- **GIVEN** Wilson's active delegation
- **WHEN** Wilson calls `POST /api/delegations/{id}/cancel`
- **THEN** response 200; `CancelledAt = now`; subsequent GET shows Status = Cancelled

#### Scenario: Delegate cannot cancel

- **GIVEN** Wilson's active delegation pointing at Yang
- **WHEN** Yang (the delegate) calls cancel
- **THEN** response 403 Forbidden

#### Scenario: Idempotent cancel

- **WHEN** Wilson cancels the same delegation twice
- **THEN** the second call returns 200; `CancelledAt` unchanged from the first call

### Requirement: GetActiveDelegateAsync is the contract for task creation

The system SHALL expose `IDelegationService.GetActiveDelegateAsync(granterUserId, atTime) → Delegation?` returning the unique active row at the requested time, or null. The query SHALL filter by `StartAt <= atTime AND EndAt > atTime AND CancelledAt IS NULL`. Future Process Runtime code creating a `Task` row MUST call this method with the resolved `original_assignee_id`; if a delegation is returned, the runtime MUST set `actual_assignee_id = delegation.DelegateUserId` and persist both fields. If no delegation is returned, `actual_assignee_id = original_assignee_id`.

#### Scenario: Query returns active row

- **GIVEN** Wilson has an active delegation `[T-1h, T+1h)` pointing at Yang
- **WHEN** `GetActiveDelegateAsync(Wilson, T)` is called
- **THEN** the result is the row pointing at Yang

#### Scenario: Query returns null when scheduled but not yet started

- **GIVEN** Wilson's delegation is `[T+1h, T+5h)` (still Scheduled)
- **WHEN** `GetActiveDelegateAsync(Wilson, T)` is called
- **THEN** the result is null

#### Scenario: Query returns null when expired

- **GIVEN** Wilson's delegation was `[T-5h, T-1h)` (Expired)
- **WHEN** `GetActiveDelegateAsync(Wilson, T)` is called
- **THEN** the result is null

#### Scenario: Query returns null when cancelled

- **GIVEN** Wilson's delegation `[T-1h, T+1h)` was cancelled at T-30min
- **WHEN** `GetActiveDelegateAsync(Wilson, T)` is called
- **THEN** the result is null

### Requirement: Runtime applies one delegation hop only

When the future Process Runtime spawns a Task, it SHALL look up `GetActiveDelegateAsync(original_assignee, now)` and apply at most one transformation. It SHALL NOT recursively look up the delegate's own delegation (which would create infinite loops in cycles). Recursion is explicitly forbidden by this requirement.

#### Scenario: Single hop applied

- **GIVEN** Wilson has delegation pointing at Yang; Yang has delegation pointing at Chen
- **WHEN** the runtime spawns a Task assigned to Wilson at the current time
- **THEN** `actual_assignee_id = Yang` (one hop only); the task is NOT routed further to Chen

#### Scenario: Cycle does not loop

- **GIVEN** Wilson → Yang and Yang → Wilson (mutual delegation)
- **WHEN** the runtime spawns a Task assigned to Wilson
- **THEN** `actual_assignee_id = Yang` and the runtime stops; no recursion

### Requirement: Inactive granter or delegate handled gracefully

When `GetActiveDelegateAsync` returns a delegation whose `DelegateUserId` resolves to an inactive User, the runtime SHALL fall back to `actual_assignee_id = original_assignee_id`. The delegation row remains in the DB unchanged (data is data); the UI may surface a warning. When the *granter* becomes inactive, no special handling is needed — the runtime never assigns tasks to inactive users in the first place, so delegation never fires for them.

#### Scenario: Inactive delegate triggers fallback

- **GIVEN** Wilson's active delegation points at Yang
- **AND** Yang's IsActive = false
- **WHEN** the runtime spawns a Task assigned to Wilson
- **THEN** `actual_assignee_id = Wilson` (fallback to original); audit row records the fallback reason

### Requirement: ListMine and ListInbound endpoints

The system SHALL expose:

- `GET /api/delegations/mine?filter=active|scheduled|expired|cancelled|all` — returns delegations where `GranterUserId = current user`
- `GET /api/delegations/inbound?filter=active|scheduled|expired|all` — returns delegations where `DelegateUserId = current user`

Both SHALL return rows in `EndAt DESC` order. Default filter is `active` for both endpoints.

#### Scenario: Filter by active

- **GIVEN** Wilson has 1 Active, 1 Scheduled, 2 Expired, 1 Cancelled
- **WHEN** Wilson calls `GET /api/delegations/mine?filter=active`
- **THEN** the response contains exactly the 1 Active row

#### Scenario: Inbound view from delegate's perspective

- **GIVEN** Yang receives delegations from Wilson and Chen, both currently active
- **WHEN** Yang calls `GET /api/delegations/inbound?filter=active`
- **THEN** the response contains both rows

### Requirement: Daily status refresh job updates the cache

The system SHALL run a daily background job (`DelegationStatusRefreshJob`) at 00:05 UTC that updates the cached `Status` column for any row where the cache differs from `DelegationStatusOf(d, now)`. This is a maintenance job for index efficiency only — live queries (`GetActiveDelegateAsync`) MUST NOT depend on the cache; they re-compute from `StartAt` / `EndAt` / `CancelledAt`.

#### Scenario: Job flips Active to Expired after EndAt passes

- **GIVEN** an Active delegation whose EndAt is yesterday (the job ran today at 00:05)
- **WHEN** the refresh job runs
- **THEN** the row's cached Status is updated to `Expired`

#### Scenario: Live query is correct even with stale cache

- **GIVEN** the cache hasn't been refreshed (stale)
- **AND** a delegation's EndAt has passed
- **WHEN** `GetActiveDelegateAsync` is called
- **THEN** the live query correctly returns null (because the SQL filter `EndAt > now` excludes it), regardless of cached `Status` value
