# Delegation (代理人) — Design

**Date:** 2026-06-04
**Status:** Approved (開發者, Telegram) — Option A (full: delegate can actually act).
**Owner:** lead (bpm-svc + bpm-ui shared platform) + chef convention update.

## Goal

A bpm-ui user (going on leave) sets a delegate + date range; while active, the
delegate sees the delegator's pending tasks **and can act on them** (approve /
reject / etc.). Self-service from bpm-ui; honored by the live Model-B flows.

## UI (bpm-ui)

- A **代理人** button immediately left of the AccountMenu in the header. Opens a
  small popover: pick delegate (type account email, resolved against users) +
  start date + end date → Save. Shows the current delegation; allows clear.
- A "你正在代理 X" hint when the viewer is currently acting for someone (so they
  know why extra tasks appear).

## Storage — single source

Reuse the existing **`Admin_Delegations`** table (admin already owns the
Delegation entity + DelegationsController; the seeder seeds one). Do **not**
open a second table — that would split "admin-set" from "self-set" and the
runtime would have to read both.

- bpm-svc adds a `SharedDelegation` mirror (mapped to `Admin_Delegations`,
  `ExcludeFromMigrations`) — read for runtime/inbox, **write** for self-service.
- POC deviation noted: bpm-svc writing an admin-owned table breaks the strict
  "SharedX is read-only" contract. Justified here: delegation is a self-service
  process concern, single-source is worth it, and bpm-ui does not call admin-svc
  directly. Flagged for the eventual auth-bridge cleanup.

Fields (existing): `Id, DelegatorPrincipalId, DelegateToUserId, StartAt, EndAt,
Active, Reason, CreatedAt, UpdatedAt`. "My delegation" = the caller's single
active row; PUT upserts it. Active ⇔ `Active && StartAt <= now <= EndAt`.
v1 is single-hop (no transitive A→B→C); self-delegation rejected.

## Runtime — three integration points

1. **Read seam** — replace `StubDelegationService` with a real
   `DelegationService`: `GetActiveDelegateAsync(principalUserId, now)` → the
   active in-range `DelegateToUserId`, and `GetActiveDelegatorsAsync(delegateUserId,
   now)` → the user ids this person currently acts for.
2. **Inbox visibility** — `InboxController` (one shared place): when building a
   user's pending list, also fetch each active delegator's pending rows
   (`provider.GetPendingAsync(delegatorId)`), merged + tagged "代理 X". Per-flow
   providers are unchanged.
3. **Decision authorization** — the cross-cutting piece. Today each flow
   hand-rolls `if (c.ManagerUserId != actorUserId) throw Forbidden`. Add a shared
   `IActorAuthorizer.CanActAsync(requiredUserId, callerUserId, ct)` =
   `caller == required` OR `GetActiveDelegate(required) == caller`. Retrofit every
   flow's per-step assignee check to call it. Submitter-only checks (withdraw /
   resubmit) stay strict (not delegated).

## Frontend — decision buttons for the delegate

Detail components gate buttons on `currentAssigneeUserId === viewerUserId`. Add a
shared hook `useDelegatedFor()` → the user ids the viewer can act for (from
`GET /api/delegation/acting-for`). Each flow's detail changes its assignee test
to `assignee === viewer || delegatedFor.includes(assignee)`. One shared hook +
one small edit per flow.

## API (bpm-svc, `/api/delegation`)

| Route | Verb | Purpose |
|---|---|---|
| `/api/delegation/mine` | GET | caller's current delegation (or null) |
| `/api/delegation/mine` | PUT | upsert caller's delegation {delegateUserId, startAt, endAt} |
| `/api/delegation/mine` | DELETE | clear caller's delegation |
| `/api/delegation/acting-for` | GET | user ids the caller currently acts for |
| `/api/delegation/users` | GET | active users for the delegate picker |

These are bearer-authed (bpm-ui sends the JWT; `RequireUserId()` is the
delegator/delegate) — unlike the admin console endpoints, this is end-user
self-service in the bpm app.

## Chef convention update

- `chef/skill/conventions.md` + `SKILL.md`: decision authorization MUST go
  through `IActorAuthorizer.CanActAsync(requiredUserId, caller)` instead of a raw
  `if (c.XUserId != caller) throw`. Case-detail "can act" MUST use the shared
  delegation-aware check (`useDelegatedFor`). So future cooked flows are
  delegation-aware by construction.

## Touch list

**bpm-svc**
- `Persistence/SharedIdentity/SharedDelegation.cs` + DbSet (writable mirror)
- `Application/Delegation/IDelegationService.cs` (extend) + `Persistence/Delegation/DelegationService.cs` (replace stub)
- `Application/Common/Authorization/IActorAuthorizer.cs` + impl
- `Api/Delegation/DelegationController.cs` (self-service)
- `Api/Inbox/InboxController.cs` (delegator fan-in)
- `Application/Features/<CODE>/V1/*Service.cs` ×10 — auth check via IActorAuthorizer
- DI registration; no migration (Admin_Delegations already exists)

**bpm-ui**
- `components/DelegationButton.tsx` + popover; mount left of AccountMenu in `AppLayout`
- `lib/api/delegation.ts` + `lib/useDelegatedFor.ts` hook
- `features/<CODE>/V1/*CaseDetail.tsx` ×10 — assignee test uses delegatedFor

**chef**
- `chef/skill/conventions.md` + `SKILL.md` — authorization convention

## Build order

1. Shared backend: SharedDelegation, DelegationService, IActorAuthorizer, DelegationController.
2. Reference retrofit on **APE** (service auth + detail UI) — verify E2E.
3. Fan out the other 9 flows (service + detail) following the APE pattern.
4. InboxController fan-in.
5. bpm-ui delegation button/popover + useDelegatedFor.
6. chef convention update.

## Verification

- Alice sets Bob as delegate for a range covering now. Submit an APE whose
  approver is Alice → Bob's inbox shows it tagged "代理 Alice"; Bob opens it and
  Approve/Reject buttons appear; Bob approves → case advances, audit/log shows
  Bob acted. Outside the range, Bob sees nothing and is forbidden. Self-delegation
  rejected. Submitter-only actions remain non-delegable.
- All apps tsc clean; one flow proven E2E before fan-out.
