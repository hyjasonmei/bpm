## Why

When a user goes on leave, attends a multi-day off-site, or otherwise can't act on assigned tasks, their inbox piles up and the entire flow stalls. Real customers' first question after seeing the LEAVE demo will be: "what happens when the manager who needs to approve is *also* on leave?"

Today the system has no answer:

- No `Delegation` entity, no service, no API
- No UI for users to declare "王經理代理我從 5/10 到 5/15"
- The original spec.md flagged delegation as a first-class concept ("Apply delegation at task-creation time, not at resolution time. Both `original_assignee_id` and `actual_assignee_id` must be recorded"), but no implementation has shipped
- `RoleSwitcher` has no entry point for self-service delegation management

This change introduces a typed `Delegation` entity with a clear lifecycle (scheduled → active → expired/cancelled), a self-service API + UI that lets every user grant a delegate for a fixed window, and the `IDelegationService` contract that the future Process Runtime change will call at task-creation time to transform `original_assignee_id` → `actual_assignee_id`.

## What Changes

### Backend domain (NEW capability `bpm-delegation`)

**Entity** (in `Bpm.Domain.Entities.Delegation`):

`Delegation` — represents a granter's intent that any task assigned to them between `StartAt` and `EndAt` should be transformed to target the delegate user instead. Columns:

- `Id` (Guid)
- `TenantId` (Guid; for future multi-tenant)
- `GranterUserId` (Guid, FK → User) — who is delegating
- `DelegateUserId` (Guid, FK → User) — who receives the delegated tasks
- `StartAt` (DateTime UTC) — when delegation begins; MAY be in the future
- `EndAt` (DateTime UTC) — when delegation ends; required, MUST be > StartAt
- `Reason` (string, nullable, max 500) — optional note
- `Status` (enum) — `Scheduled` / `Active` / `Expired` / `Cancelled`
- `CancelledAt` (DateTime UTC?, nullable)
- `CreatedAt` / `LastModifiedAt` from `AuditableEntity`

`Status` is *derived* from time + cancellation, not stored authoritatively. A query helper `DelegationStatusOf(d, now)` computes:
- `Cancelled` if `CancelledAt` is set
- `Expired` if `now >= EndAt` (and not cancelled)
- `Scheduled` if `now < StartAt`
- `Active` if `StartAt <= now < EndAt`

We persist `Status` only as a denormalized cache (refreshed when read or by a daily janitor) for query efficiency; the source of truth is the time fields + `CancelledAt`.

### Backend service

`IDelegationService` (in `Bpm.Application.Delegation`):

- `Task<Guid> CreateAsync(CreateDelegationCommand cmd, CancellationToken ct)` — granter creates a new delegation for themselves
- `Task<IReadOnlyList<Delegation>> ListMineAsync(Guid granterUserId, DelegationStatusFilter filter, CancellationToken ct)` — granter lists own delegations (active / scheduled / expired / cancelled / all)
- `Task<Delegation?> GetActiveDelegateAsync(Guid granterUserId, DateTime atTime, CancellationToken ct)` — **the contract for future Task spawning** — given a user and a moment in time, return their currently active delegation (or null). Used by Process Runtime when assigning a task: if active delegation exists, transform `original_assignee_id` → `actual_assignee_id`.
- `Task CancelAsync(Guid delegationId, Guid actorUserId, CancellationToken ct)` — granter cancels their own delegation. Only the granter (`actorUserId == delegation.GranterUserId`) is permitted; other actors → `ForbiddenException`.

Validation rules (enforced inside `CreateAsync`):

- `GranterUserId != DelegateUserId` (cannot self-delegate) — reject with `ValidationException`
- `DelegateUserId` is an active User — reject otherwise
- `EndAt > StartAt + 1 hour` (minimum 1-hour window; sanity check) — reject otherwise
- `StartAt >= now - 5 minutes` (cannot back-date; small clock-skew tolerance) — reject otherwise
- No overlap with any other non-cancelled delegation owned by the same granter — reject with `ConflictException` listing the conflicting row's id and time window

Cycle detection: when creating delegation `A → B`, the service SHALL check whether B has an active or scheduled delegation pointing back to A (1-hop cycle). If yes, the service emits a *warning* in the response (`{ id, warnings: ["1-hop cycle detected: ..."] }`) but does NOT reject — the customer may have a legitimate reason (mutual coverage during a joint trip). At task-creation time the runtime applies only one hop (no transitive resolution) so there is no infinite loop risk.

### Backend persistence

EF configuration in `bpm-svc/src/Persistence/Configurations/Delegation/DelegationConfiguration.cs`. Migration `AddDelegation` creates the `Delegations` table.

Indexes:
- `(GranterUserId, EndAt DESC)` — for `ListMineAsync` (newest first)
- `(GranterUserId, StartAt, EndAt) WHERE CancelledAt IS NULL` — for active-delegation lookup
- `(DelegateUserId, StartAt, EndAt) WHERE CancelledAt IS NULL` — for "who am I currently delegating for" reverse lookup
- `(EndAt) WHERE Status IN ('Scheduled', 'Active')` — for the daily janitor that flips Active→Expired

A daily background job (`DelegationStatusRefreshJob`, `IHostedService`) wakes at 00:05 UTC, walks rows whose `Status != ComputedStatus`, updates the cache. Belt-and-suspenders — the live status query always re-computes from time + CancelledAt, so the cache being stale is at worst a UI lag, not a correctness issue.

### Backend API

`bpm-svc/src/Api/Delegation/DelegationController.cs`:

- `POST /api/delegations` — current user creates a delegation for themselves (granter = current user; cannot create for another user)
- `GET /api/delegations/mine?filter=active|scheduled|expired|cancelled|all` — list own delegations
- `GET /api/delegations/{id}` — fetch one (only granter can read)
- `PUT /api/delegations/{id}` — modify (only granter; only `EndAt` can be brought *earlier* and only `Reason` can be edited; cannot change DelegateUserId mid-delegation — must cancel and create new)
- `POST /api/delegations/{id}/cancel` — cancel (granter only); sets `CancelledAt = now`
- `GET /api/delegations/inbound?filter=active|scheduled|expired|all` — list delegations pointing at me as DelegateUserId (so a delegate can see "who's letting me cover for them")

There is **no** admin override endpoint — per design choice, only the granter can manage their own delegation. If admin intervention is needed, it goes through normal user-impersonation tooling (out of scope).

### Frontend — types + API client

`bpm-ui/src/lib/delegation.ts`:

- TypeScript types mirroring backend (`Delegation`, `DelegationStatus`, `DelegationStatusFilter`)
- API client functions: `createDelegation`, `listMineDelegations`, `cancelDelegation`, `updateDelegation`, `listInboundDelegations`
- Helper `getActiveDelegationForCurrentUser()` — used by RoleSwitcher to display the active row

### Frontend — RoleSwitcher integration (`bpm-ui-shell` modification)

The existing `RoleSwitcher` dropdown in `AppLayout` is extended with a new section above the persona picker:

```
┌─ RoleSwitcher dropdown ──────────────┐
│  ┌─ 代理人 / Delegation ────────┐    │
│  │ 目前代理人：王經理 (5/10-15)│    │
│  │ [管理代理人 →]              │    │
│  └─────────────────────────────┘    │
│  ────                                │
│  Switch demo persona                 │
│  → Employee (Wilson)                 │
│    Manager (Yang)                    │
│    Finance (Chen)                    │
│    ...                               │
└──────────────────────────────────────┘
```

When no active delegation: section shows "目前代理人：無" + "[設定代理人 →]" button.
When active: shows delegate name + window + cancel link + "管理代理人 →" for full UI.

### Frontend — DelegationManagementDialog

Clicking "管理代理人 →" opens a modal `DelegationManagementDialog` showing:

- **目前進行中** card (active delegation, if any) with cancel button + days-remaining counter
- **預定中** list (scheduled, future delegations) with cancel button + edit-end-time button
- **歷史** (expandable accordion, last 12 months of expired/cancelled rows for reference)
- **新增代理人** form (always visible at bottom):
  - Delegate user picker (autocomplete on User.full_name; excludes current user; excludes inactive users)
  - Start datetime picker (default: tomorrow 00:00 in user's timezone; cannot be past)
  - End datetime picker (default: start + 1 day; must be > start + 1 hour)
  - Reason textarea (optional, max 500 chars; placeholder "出差 / 休假 / 其他")
  - "建立代理" submit button — disabled until form is valid

Inline validation messages match backend rules (no self-delegate, no overlap, no past start, no end ≤ start + 1h).

### Frontend — inbound view

A small banner when the current user is *currently delegating for someone else*:

```
🔁 您目前代理 林副總（5/10 - 5/15）— 期間内的任務會自動指派給您
```

Shown at the top of `Home.tsx` *only* when `inbound.length > 0`. (Home.tsx is one of the demo screens — explicit micro-modification documented; no other layout change.)

### Integration with future Process Runtime

This change does NOT modify any task-spawning code (none exists yet). It exposes:

- `IDelegationService.GetActiveDelegateAsync(userId, atTime)` — the future Process Runtime change calls this when creating a `Task` row, with the resolved `original_assignee_id`. If a delegate exists at `now`, the runtime sets `actual_assignee_id = delegate.DelegateUserId`; otherwise `actual_assignee_id = original_assignee_id`. Both fields are persisted.
- `IDelegationService` contract is documented in spec deltas so the Process Runtime change has clear semantics to wire to.

Notification dispatcher: per the design decision, **delegation does NOT transform notification recipients**. Notifications are dispatched transparently to the resolver's output. Recipients of `current_approver` / `current_assignee` runtime types come from `NotificationContext` populated by the Process Runtime — which already carries `actual_assignee_id` (post-delegation) — so the delegate naturally receives the "you have a task" notification, while the granter does not. No change needed in `bpm-notification-engine`.

### Out of scope (future changes)

- Per-flow scoped delegation (e.g., "Wilson 代理我，但只在請假流程；採購還是我來")
- Delegate-side rejection (the delegate cannot decline a delegation; granter has unilateral authority)
- Multi-step delegation chains (A→B→C resolved transitively) — runtime applies one hop only
- System admin override (admin cannot create / cancel another user's delegation through this API; if needed, use user-impersonation tooling, out of scope)
- Delegation history retention policy (we keep all rows forever in v1; janitor can be added later)
- Notifications about delegation lifecycle events (per Jason's choice — granter checks their own dashboard; no email/in-app on delegation start/end/use)
- HR sync integration (e.g., automatic delegation when HR records a leave)
- Bulk operations (cancel multiple delegations at once)
- Calendar / iCal export of delegations

## Capabilities

### New Capabilities

- `bpm-delegation` — Delegation entity with derived Status; `IDelegationService` with create/list/get/cancel + the `GetActiveDelegateAsync` contract for future Process Runtime; REST API for self-service management; daily Status refresh job; cycle warning; overlap rejection; self-delegate prevention.

### Modified Capabilities

- `bpm-ui-shell` — `RoleSwitcher` dropdown gains a delegation section showing the user's active delegation; new `DelegationManagementDialog` reachable from "管理代理人 →"; inbound banner on `Home.tsx` when the current user is delegating for someone else.

## Impact

- **bpm-svc/src/Domain/Entities/Delegation/Delegation.cs**: new entity
- **bpm-svc/src/Domain/Entities/Delegation/DelegationStatus.cs**: enum
- **bpm-svc/src/Application/Delegation/IDelegationService.cs**: interface
- **bpm-svc/src/Application/Delegation/DelegationService.cs**: implementation with validation rules
- **bpm-svc/src/Application/Delegation/Commands/**: `CreateDelegationCommand`, `CancelDelegationCommand`, `UpdateDelegationCommand` records + validators
- **bpm-svc/src/Persistence/Configurations/Delegation/DelegationConfiguration.cs**: EF config
- **bpm-svc/src/Persistence/Migrations/AddDelegation**: additive migration
- **bpm-svc/src/Infrastructure/Delegation/DelegationStatusRefreshJob.cs**: daily janitor (IHostedService)
- **bpm-svc/src/Api/Delegation/DelegationController.cs**: 6 endpoints
- **bpm-ui/src/lib/delegation.ts**: types + API client
- **bpm-ui/src/components/RoleSwitcher.tsx**: section above persona picker showing active delegation + "管理代理人" entry; minimal addition, persona switching unchanged
- **bpm-ui/src/components/delegation/DelegationManagementDialog.tsx**: NEW — main UI
- **bpm-ui/src/components/delegation/DelegationList.tsx**: list rendering with status chips
- **bpm-ui/src/components/delegation/DelegationForm.tsx**: create form with validation
- **bpm-ui/src/components/delegation/InboundBanner.tsx**: banner shown on Home when user has inbound active delegation
- **bpm-ui/src/screens/Home.tsx**: ONE-LINE addition — `<InboundBanner />` mounted at top. Documented and small. Other demo screens (`forms/*`, `Search`, `Report`, `lib/workflow.ts`) NOT modified.
- **No DB schema change to existing tables**
- **No new dependencies** beyond what's already in bpm-svc.csproj and bpm-ui package.json

## Demo guard

This change touches `bpm-ui/src/components/RoleSwitcher.tsx` and adds a one-line mount of `<InboundBanner />` to `bpm-ui/src/screens/Home.tsx`. Both modifications are *additive* — when the user has zero delegations (the default for all demo personas), the RoleSwitcher dropdown shows "目前代理人：無 [設定代理人 →]" instead of being empty, and the InboundBanner renders nothing.

For the evening demo: if delegation rows are not seeded, the visuals are *nearly* identical (only the small "目前代理人：無" line is new in the dropdown — that's a feature surface, not a regression). The `forms/*.tsx`, `Search.tsx`, `Report.tsx`, `lib/workflow.ts` files are NOT modified.

If absolute byte-identical demo is required, gate the RoleSwitcher delegation section behind `import.meta.env.VITE_DELEGATION_ENABLED === 'true'`. Default off until you opt in. Mention this in tasks.md.
