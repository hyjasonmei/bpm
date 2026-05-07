# Tasks

## 1. Domain entity

- [ ] 1.1 Create `bpm-svc/src/Domain/Entities/Delegation/DelegationStatus.cs` enum: `Scheduled`, `Active`, `Expired`, `Cancelled`
- [ ] 1.2 Create `bpm-svc/src/Domain/Entities/Delegation/Delegation.cs` (inherits `AuditableEntity`) with: Id, TenantId, GranterUserId, DelegateUserId, StartAt (UTC), EndAt (UTC), Reason (nullable, max 500), Status (denormalized cache), CancelledAt (nullable)
- [ ] 1.3 Create static helper `DelegationStatusOf(Delegation d, DateTime nowUtc) → DelegationStatus` (pure function); unit-test with all four status branches
- [ ] 1.4 Create static helper `IsOverlapping(Delegation a, Delegation b)` using half-open `[start, end)` semantics; unit-test boundary cases (adjacent windows do NOT overlap)

## 2. Persistence

- [ ] 2.1 Create `bpm-svc/src/Persistence/Configurations/Delegation/DelegationConfiguration.cs`:
  - Table name `Delegations`
  - GranterUserId / DelegateUserId both required FK to `Users` with `Restrict` delete behavior (don't let a User be deleted while delegations reference it)
  - Reason max length 500
  - Indexes: `(GranterUserId, EndAt DESC)`, filtered `(GranterUserId, StartAt, EndAt) WHERE CancelledAt IS NULL`, filtered `(DelegateUserId, StartAt, EndAt) WHERE CancelledAt IS NULL`, filtered `(EndAt) WHERE Status IN (Scheduled, Active)`
  - Status as string (3-15 chars) — readable when inspecting SQLite via `.dump`
- [ ] 2.2 Add `DbSet<Delegation> Delegations` to `BpmDbContext`
- [ ] 2.3 Generate migration: `dotnet ef migrations add AddDelegation -p src/Persistence -s src/Api`
- [ ] 2.4 Apply locally; verify with `sqlite3 bpm.db .schema "Delegations"` and `.indices "Delegations"`

## 3. Application — service layer

- [ ] 3.1 Create `bpm-svc/src/Application/Delegation/IDelegationService.cs` with: `CreateAsync`, `GetAsync`, `ListMineAsync`, `ListInboundAsync`, `GetActiveDelegateAsync`, `UpdateAsync`, `CancelAsync`
- [ ] 3.2 Create command records in `bpm-svc/src/Application/Delegation/Commands/`:
  - `CreateDelegationCommand(Guid GranterUserId, Guid DelegateUserId, DateTime StartAt, DateTime EndAt, string? Reason)`
  - `UpdateDelegationCommand(Guid DelegationId, Guid ActorUserId, DateTime? NewEndAt, string? NewReason)` — only EndAt-earlier and Reason are mutable
  - `CancelDelegationCommand(Guid DelegationId, Guid ActorUserId)`
- [ ] 3.3 FluentValidation validators for each command:
  - `CreateDelegationValidator`: GranterUserId != DelegateUserId; EndAt > StartAt + 1 hour; StartAt >= now - 5 minutes
  - `UpdateDelegationValidator`: NewEndAt (if set) >= now AND <= existing EndAt AND > StartAt + 1 hour
- [ ] 3.4 Implement `DelegationService.CreateAsync`:
  - Run command validator
  - Look up DelegateUser; reject if inactive or not found (`NotFoundException`)
  - Query for overlapping non-cancelled rows owned by GranterUserId; reject with `ConflictException` listing conflicting id + time window if any
  - Cycle detection: query whether DelegateUser has an active or scheduled delegation pointing back at GranterUser; if yes, set `warnings = ["1-hop cycle detected: ..."]` in response
  - Compute initial Status (Scheduled if StartAt > now, else Active)
  - Insert; return `(NewId, Warnings)`
- [ ] 3.5 Implement `DelegationService.GetActiveDelegateAsync(granterUserId, atTime)`:
  - SQL: `SELECT * FROM Delegations WHERE GranterUserId = @granter AND StartAt <= @at AND EndAt > @at AND CancelledAt IS NULL LIMIT 1`
  - Returns the row or null
  - This is the contract for future Process Runtime; document its semantics in XML doc comments
- [ ] 3.6 Implement `DelegationService.CancelAsync`:
  - Load delegation; if not found → `NotFoundException`
  - If `delegation.GranterUserId != actorUserId` → `ForbiddenException`
  - If `CancelledAt != null` → idempotent no-op (return success)
  - Set `CancelledAt = now`, status cache → `Cancelled`
- [ ] 3.7 Implement `ListMineAsync` with status filter (Active / Scheduled / Expired / Cancelled / All); order by `EndAt DESC`
- [ ] 3.8 Implement `ListInboundAsync(delegateUserId, filter)` — same shape but filters by DelegateUserId
- [ ] 3.9 Wire DI in `Application/DependencyInjection.cs`
- [ ] 3.10 Unit-test each method: positive paths, validation failures, overlap rejection, cycle warning, cancel-twice idempotency, GetActiveDelegate correctness across boundaries (nowUtc == StartAt edge, nowUtc == EndAt - 1ns edge)

## 4. API — endpoints

- [ ] 4.1 Create `bpm-svc/src/Api/Delegation/DelegationController.cs` with `[Authorize]` and a base route `[Route("api/delegations")]`
- [ ] 4.2 `POST /api/delegations` — body `{ delegate_user_id, start_at, end_at, reason? }`; granter is the current user (read from `ICurrentUser`); returns `201 { id, warnings? }`; rejects with 400 (validation), 404 (delegate not found), 409 (overlap)
- [ ] 4.3 `GET /api/delegations/mine?filter=active|scheduled|expired|cancelled|all` — returns list scoped to current user; default filter = `active`
- [ ] 4.4 `GET /api/delegations/inbound?filter=active|scheduled|expired|all` — returns list where DelegateUserId = current user
- [ ] 4.5 `GET /api/delegations/{id}` — returns row if granter or delegate; 404 otherwise
- [ ] 4.6 `PUT /api/delegations/{id}` — granter only; body `{ end_at?, reason? }`
- [ ] 4.7 `POST /api/delegations/{id}/cancel` — granter only
- [ ] 4.8 Integration tests against TestServer:
  - Create flows (happy + validation-fail + overlap + self-delegate + cycle)
  - List filtering
  - Cancel + permission rejection (other user attempting to cancel returns 403)
  - PUT can shorten EndAt but not extend
  - Inbound view returns rows pointing at the caller

## 5. Background worker

- [ ] 5.1 Create `bpm-svc/src/Infrastructure/Delegation/DelegationStatusRefreshJob.cs` as `BackgroundService`
- [ ] 5.2 Loop: wake daily at 00:05 UTC (or every hour for tests when `BPM_DELEGATION_REFRESH_FAST=true`)
- [ ] 5.3 Query rows where cached `Status` differs from `DelegationStatusOf(d, now)`; bulk-update
- [ ] 5.4 Log "Refreshed N delegation status rows"
- [ ] 5.5 Register hosted service in `Api/Program.cs`; gated on `BPM_DELEGATION_WORKER=on` (default `on` in dev/prod, `off` in tests)

## 6. Frontend — types + API client

- [ ] 6.1 Create `bpm-ui/src/lib/delegation.ts`:
  - TypeScript types: `Delegation`, `DelegationStatus`, `DelegationStatusFilter`, `CreateDelegationInput`, `UpdateDelegationInput`
  - API client: `createDelegation`, `cancelDelegation`, `updateDelegation`, `listMineDelegations`, `listInboundDelegations`, `getActiveDelegationForCurrentUser`
- [ ] 6.2 Add a small zustand store / context `useDelegationContext()` that polls `getActiveDelegationForCurrentUser` on mount and exposes `{ active, refresh, isLoading }`
- [ ] 6.3 Polling cadence: re-fetch every 5 minutes (delegation state changes infrequently); also on user-action (create/cancel/update) trigger immediate re-fetch

## 7. Frontend — RoleSwitcher delegation section

- [ ] 7.1 Update `bpm-ui/src/components/RoleSwitcher.tsx`:
  - Above the persona picker section, render a `DelegationSummary` block
  - When no active delegation: shows "目前代理人：無" + "[設定代理人 →]" button (opens DelegationManagementDialog)
  - When active: shows "目前代理人：{delegate.name}（{startAt} - {endAt}）" + "[管理代理人 →]" button
  - When scheduled (future): shows "代理人：{delegate.name}（從 {startAt} 開始）" with light-blue chip
- [ ] 7.2 Gate behind `import.meta.env.VITE_DELEGATION_ENABLED !== 'false'` — default ON; set to `'false'` in `.env` if a demo absolutely cannot show the new section
- [ ] 7.3 Bilingual labels (zh-TW + en); follow existing RoleSwitcher i18n pattern

## 8. Frontend — DelegationManagementDialog

- [ ] 8.1 Create `bpm-ui/src/components/delegation/DelegationManagementDialog.tsx`:
  - Modal wrapper using existing dialog primitives in `components/ui`
  - Tabs / sections: 進行中 (Active) / 預定中 (Scheduled) / 歷史 (History, expandable accordion, last 12 months)
  - Bottom: 新增代理人 form
- [ ] 8.2 Create `bpm-ui/src/components/delegation/DelegationList.tsx`:
  - Renders delegation rows with status chip (Active/Scheduled/Expired/Cancelled)
  - Per-row actions based on status: Active → Cancel + Edit-end; Scheduled → Cancel + Edit-end; Expired/Cancelled → none
  - Days-remaining counter for Active; "Starts in X days" for Scheduled
- [ ] 8.3 Create `bpm-ui/src/components/delegation/DelegationForm.tsx`:
  - User picker (autocomplete on `User.full_name`); query `/api/users?active=true&exclude=current_user`
  - Start datetime picker (default tomorrow 00:00 user-local; min = now)
  - End datetime picker (default start + 1 day; min = start + 1 hour)
  - Reason textarea (optional, max 500 chars; counter)
  - Inline validation; submit disabled until valid
  - Submit calls `createDelegation`; on success closes dialog, refreshes context, toasts "代理人已設定"
  - On 409 conflict: show error inline citing conflicting delegation's window
  - On success with cycle warning: show yellow toast "提醒：偵測到雙方互相代理"

## 9. Frontend — InboundBanner

- [ ] 9.1 Create `bpm-ui/src/components/delegation/InboundBanner.tsx`:
  - Reads `useDelegationContext().inbound` (active rows where DelegateUserId = current user)
  - Renders nothing when inbound is empty
  - Renders a slim banner: "🔁 您目前代理 {granter.name}（{startAt} - {endAt}）— 期間内的任務會自動指派給您"
  - For multiple inbound: "您目前代理 N 位同事 — 點此展開"
- [ ] 9.2 Mount `<InboundBanner />` at the top of `bpm-ui/src/screens/Home.tsx` (one-line addition; documented as the only change to a demo screen)

## 10. Frontend — useDelegationContext refresh on action

- [ ] 10.1 After `createDelegation` / `cancelDelegation` / `updateDelegation`, invoke `refresh()`
- [ ] 10.2 RoleSwitcher and InboundBanner both consume the same context — they re-render together when the active row changes

## 11. Sample seed data

- [ ] 11.1 Update `bpm-svc/src/Persistence/Seed/OrgFixture.cs` to add 2-3 sample Delegations (e.g., manager Yang delegates to senior Wilson from 2026-05-10 to 2026-05-15, marked Active for the seed time anchor)
- [ ] 11.2 Verify the seed loads cleanly and the wizard / RoleSwitcher reflects it

## 12. Documentation

- [ ] 12.1 Update `bpm-svc/CLAUDE.md` with delegation entity overview + the `IDelegationService.GetActiveDelegateAsync` contract + how Process Runtime should call it at task creation
- [ ] 12.2 Add a section to `SETUP.md` on the daily refresh job env var and how to test-fast
- [ ] 12.3 Update `spec_schema.md` — add a brief mention that delegation transforms `original_assignee_id` to `actual_assignee_id` at task-creation time; not a spec.json field but worth documenting for reviewers

## 13. End-to-end verification

- [ ] 13.1 `dotnet build bpm-svc.slnx` clean
- [ ] 13.2 All backend tests pass (`dotnet test`)
- [ ] 13.3 Apply migration on fresh `bpm.db`; verify Delegations table + indexes
- [ ] 13.4 Boot bpm-svc with seed loaded; verify `GET /api/delegations/mine` (as the seeded granter) returns the seed row
- [ ] 13.5 `tsc -p tsconfig.app.json` (bpm-ui) clean
- [ ] 13.6 `npm run dev`; login as the seeded granter; click RoleSwitcher → see "目前代理人：Wilson" + management entry
- [ ] 13.7 Click 管理代理人 → cancel; verify badge updates immediately; refresh page, persists
- [ ] 13.8 Login as the delegate (Wilson); verify Home shows InboundBanner: "您目前代理 Yang"
- [ ] 13.9 Create a new delegation: pick delegate, today + 2 hours window; verify it appears as Active
- [ ] 13.10 Try create overlapping delegation; verify 409 inline error
- [ ] 13.11 Try self-delegate (pick yourself in delegate picker); verify the picker filters you out (UI prevention) and the API also rejects (defense in depth)
- [ ] 13.12 Set system clock fwd by 1 hour (via test endpoint or DB tweak), verify status chip flips to Expired on next refresh
- [ ] 13.13 **Demo guard**: confirm `forms/*.tsx`, `Search.tsx`, `Report.tsx`, `lib/workflow.ts` were NOT modified; the only demo-screen change is one line in `Home.tsx` mounting InboundBanner (which renders nothing when no inbound delegation)

## 14. Commit

- [ ] 14.1 Commit in chunks (entity + persistence; service + commands + tests; api + integration tests; refresh worker; frontend types + context; RoleSwitcher integration; DelegationManagementDialog; InboundBanner + Home wiring; seed + verification)
- [ ] 14.2 Push via GitKraken (Claude does not push to BPM repo)
