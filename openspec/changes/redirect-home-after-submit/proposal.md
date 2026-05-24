## Why

Jason reported 2026-05-24 two related bugs:

- **"當點下 create leave 出現 pending task, 這應該出現在首頁啊？"**
  After submitting a Create form, the success state (instance id + the
  first pending task) is shown *inside the form* and the user is
  stranded on the form route. They expected to land back on Home with
  the new task visible in the inbox.
- **"建立 ticket 沒出現在 home"** — even if you do navigate back to Home
  manually, the new instance / pending task doesn't show up. Home polls
  `useMyTasks` / `useMyInstances` every 30 s, but the post-submit
  navigation doesn't trigger a refresh, so users wait up to 30 s and
  assume the system swallowed their submit.

Both bugs come from the same gap: `onSubmitted` in `useFormRuntime` only
fires a toast and calls back into the form. It doesn't navigate, and it
doesn't invalidate the Home queries.

## What Changes

### Post-submit redirect policy

After a successful Create-mode submit, the runtime hook SHALL navigate
to:

- `/` (Home) when the spec's "first task" is assigned to **someone other
  than the submitter** — the common case (request goes off to manager).
- `/tasks/:firstTaskId` when the spec's first task is assigned to the
  submitter themselves — rare but valid (self-review steps, attendance
  punch acknowledgement, etc.).

After a successful Task-mode submit / approve / reject / return, the
runtime hook SHALL navigate to `/` (Home).

Override hook: a form can pass `onSubmitted` to opt into a custom
destination, but the default is the policy above. (Used today by chef
features; will stay used by future Phase-2 `<DynamicForm>`.)

### Home refresh signal

A small `bpm:tasks-invalidate` `CustomEvent` SHALL be dispatched on
window every time `submitCreate / submitUserTask / approve / reject /
returnTask` resolves. `useMyTasks` and `useMyInstances` listen for it
and re-fetch immediately (in addition to the existing 30 s poll). So
when the redirect lands on Home, the inbox is already up to date — no
30 s lag.

### Optimistic flash

Optional polish: on Home, while the re-fetch is in flight, render a
single-row "Your submission is being recorded…" placeholder with the
new instance id. Resolves to the real row on fetch. (Skip if this is
out of scope; add as a follow-up.)

### Out of scope

- Server push (SSE / websocket) — polling + event-bus is enough
- Cross-tab refresh (BroadcastChannel) — single-tab scope for POC
- Toast persistence across navigation (the current toast already
  survives because Home renders the same `<FlowToast>`)

## Capabilities

### Modified

- `bpm-shell-ui` — post-submit redirect policy + tasks-invalidate event
- `bpm-form-stepper` — `useFormRuntime` hook contract gains the
  navigation step

## Impact

- `bpm-ui/src/hooks/useFormRuntime.tsx` — after success, dispatch
  `bpm:tasks-invalidate` + invoke navigation policy (caller passes a
  `navigate` function or the hook uses `useNavigate()` directly)
- `bpm-ui/src/hooks/useMyTasks.ts` — listen for `bpm:tasks-invalidate`
- `bpm-ui/src/hooks/useMyInstances.ts` — listen for `bpm:tasks-invalidate`
- No backend changes
- No DB migration
- **Depends on:** `add-spa-routing` (needs `useNavigate()` to exist).
  If routing lands first, this change is trivial; if not, this change
  bootstraps `react-router-dom` alongside it.
- No chef-skill update (the form `onSubmitted` prop contract is
  preserved; default behaviour just becomes useful)
