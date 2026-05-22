# flowcook-step2-admin-ui-skeleton

## Why

The legacy `bpm-admin-ui` has 9-step onboarding + Process Admin Console + Flow Library + Sandbox Mailbox, all built around the old single-service BPM model. The flowcook pivot reorganises admin around five top-level pages (`AI Kitchen / User & Role / Sandbox / Audit / Site Setting`) and removes Process Admin Console from admin (moved to `bpm-ui` in Step 5).

Step 2 re-skeletons the admin UI so Step 3 (AI Kitchen 11-step wizard) has a place to live, and the User & Role page can already display Principal data from Step 1's API. We do this in-place inside the existing `bpm-admin-ui/` (per the monorepo evolution strategy in `flowcook-architecture`).

## What Changes

### `bpm-admin-ui/` — in-place restructure

- Replace primary nav with five entries: AI Kitchen / User & Role / Sandbox / Audit / Site Setting
- Wire User & Role page to `bpm-admin-svc /api/principals` (Step 1 output)
- Stub out empty containers for AI Kitchen, Sandbox, Audit, Site Setting
- Preserve legacy onboarding wizard code path under a feature flag, hidden by default (Step 3 will rebuild it)

### Authentication UI

- Login page + cookie-based session integration with `bpm-admin-svc` auth endpoints
- "Switch persona" UI gated by the persona-switch allow list (Site Setting reads list, but management of the list itself is Step 2 of Site Setting design — not full implementation here)

### Process Admin Console retirement

- Mark Process Admin Console pages obsolete inside `bpm-admin-ui`
- Keep them temporarily reachable via a deprecation banner so the team can still inspect production instances while Step 5 migrates them to `bpm-ui`

## Out of Scope

- AI Kitchen wizard content (Step 3)
- Live cases / reports / 介入 — these stay in legacy Process Admin Console until Step 5 moves them to `bpm-ui`
- Site Setting full implementation — Step 2 lays the page but settings come online as later steps deliver their respective config keys
- syncer integration (Step 6) — User & Role uses `bpm-admin-svc` directly

## Design Notes

- The User & Role page is the first real user-facing surface in flowcook, deliberately simple: table of principals + filter by type + create / edit modals.
- Sandbox / Audit / Site Setting pages get placeholder layouts in Step 2; their controls come online in Steps 4, 6, and as needed.
- Deprecation banner on legacy pages is a one-line note inside `bpm-admin-ui/screens/...` so the team is reminded the page is going away.

## References

- `openspec/specs/flowcook-architecture` §admin five-page navigation
- `openspec/specs/flowcook-principal-model`
- `openspec/changes/flowcook-step1-admin-svc-skeleton`
