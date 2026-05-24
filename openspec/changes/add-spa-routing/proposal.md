## Why

Jason reported 2026-05-24 that the bpm-ui URL never changes — every
screen sits at `/`, so users can't bookmark a case, can't open two
windows on different screens, can't share a deep link with a colleague,
and the browser back button doesn't navigate anywhere. The current
navigation uses a `Screen` discriminated union persisted to
`localStorage` (`bpm_screen`) and switched in `App.tsx`. That worked
for the early demo but is now actively harmful — every report Jason +
partner share has the same generic URL.

## What Changes

### Add `react-router-dom` (v6)

`bpm-ui/package.json` gains `react-router-dom@^6`. Pick the data-router
flavour (`createBrowserRouter` + `RouterProvider`) so we can hoist
data-loading (`useMyTasks`, `useMyInstances`, `useFormRuntime`) into
`loader` / `useLoaderData` later without another refactor.

### Route map

| Path | Screen | Notes |
|---|---|---|
| `/` | `<Home>` | inbox + recent cases |
| `/login` | `<Login>` | redirect target when JWT is missing |
| `/create` | `<CreateIndex>` | flow picker |
| `/apply/:code` | form (`mode=create`) | from CreateIndex / "Create" menu |
| `/tasks/:taskId` | form (`mode=task`) | from Home inbox / Search |
| `/cases/:instanceId` | `<CaseDetail>` | from Home rows / Search |
| `/search` | `<Search>` | global search |
| `/reports` | `<Report>` | reports (still demo-guarded today) |
| `/attendance` | `<Attendance>` | attendance demo |
| `/sandbox/mailbox` | `<SandboxMailbox>` | sandbox tools |

### `App.tsx` replaces the `Screen` switch

`App.tsx` becomes the router root: `<AuthGate>` wraps
`<RouterProvider router={…}>`. The `Screen` union and the
`bpm_screen` localStorage key are removed.

### `AppLayout` becomes the route shell

`AppLayout` is rendered as the root route's element and uses
`<Outlet />` for the per-route screen. Navigation buttons in the
sidebar use `<NavLink to=…>` instead of `setScreen({…})`.

### Form-mode dispatch becomes URL-driven

The current `screen.taskId ? 'task' : 'create'` logic moves into the
form-route element: `/apply/:code` mounts the form in create mode,
`/tasks/:taskId` mounts it in task mode (the route also fetches the
task to get its `specCode` and renders the matching form via the
`features/registry`).

### Back-compat for chef-cooked forms

The form-component prop contract (`persona`, `mode`, `taskId`,
`onSubmitted`) does NOT change. Chef-cooked forms keep working with no
edits — they get the same props, just sourced from `useParams` +
`useNavigate` instead of `setScreen` callbacks.

### Out of scope

- Server-side rendering (none today)
- 404 polish beyond a basic NotFound route
- Search-engine-friendly URLs (internal app, no SEO need)
- Route-level code splitting (separate perf change)

## Capabilities

### New

- `bpm-shell-ui` — adds the SPA routing contract for bpm-ui.

### Modified

- None at the API level (route map is purely client-side).

## Impact

- `bpm-ui/package.json` — `react-router-dom@^6`
- `bpm-ui/src/App.tsx` — replaced by router root
- `bpm-ui/src/router.tsx` — new (route map + element bindings)
- `bpm-ui/src/components/AppLayout.tsx` — uses `<Outlet />` + `<NavLink>`
- `bpm-ui/src/components/AppLayout.tsx` — `Screen` union removed
- `bpm-ui/src/screens/Home.tsx` — rows navigate via `useNavigate()`
- `bpm-ui/src/screens/Search.tsx` — rows navigate via `useNavigate()`
- `bpm-ui/src/screens/CreateIndex.tsx` — items use `<Link to>`
- `bpm-ui/src/screens/CaseDetail.tsx` — reads `instanceId` from `useParams`
- `bpm-ui/src/lib/apiFetch.ts` — `clearJwt` no longer needs to clear `bpm_screen`
- No backend changes
- No DB migration
- No chef-skill update (chef-cooked forms keep the same prop contract)
- bpm-admin-ui not affected (separate app)
