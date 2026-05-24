# Tasks

## 1. Router skeleton

- [ ] 1.1 `npm install react-router-dom@^6`
- [ ] 1.2 Create `src/router.tsx` with the route map listed in the proposal
- [ ] 1.3 `App.tsx` becomes the AuthGate + `<RouterProvider router={…}>` root
- [ ] 1.4 Remove the `Screen` union, the `setScreen` prop drilling, and the `bpm_screen` localStorage key

## 2. AppLayout as route shell

- [ ] 2.1 `AppLayout` exports a layout element rendered as the root route
- [ ] 2.2 Sidebar links rewritten to `<NavLink to>` with active styling
- [ ] 2.3 Per-screen page chrome (back button, etc.) reuses `<Outlet />` context

## 3. Per-route adapters

- [ ] 3.1 `/apply/:code` route element: looks up form from `features/registry`, mounts in create mode
- [ ] 3.2 `/tasks/:taskId` route element: fetches task summary to learn `specCode`, mounts the form in task mode
- [ ] 3.3 `/cases/:instanceId` mounts `<CaseDetail>` reading `instanceId` from `useParams`
- [ ] 3.4 404 NotFound route falls back to home

## 4. Migrate callers

- [ ] 4.1 Home rows use `useNavigate()` for task / instance clicks
- [ ] 4.2 Search rows use `useNavigate()`
- [ ] 4.3 CreateIndex tiles use `<Link to="/apply/:code">`
- [ ] 4.4 Form `onSubmitted` callback resolves a target URL the caller can `navigate()` to (see `redirect-home-after-submit` change for the policy)

## 5. Verify

- [ ] 5.1 `npx tsc -p tsconfig.app.json --noEmit` clean
- [ ] 5.2 Manual: refresh on `/tasks/<id>` and the task form opens directly
- [ ] 5.3 Manual: refresh on `/cases/<id>` and CaseDetail opens directly
- [ ] 5.4 Manual: browser back / forward navigate between screens
- [ ] 5.5 Manual: copy `/apply/LEAVE` URL into another window → form loads
- [ ] 5.6 Manual: chef-cooked forms still render correctly (no prop-contract regression)
- [ ] 5.7 chrome-devtools screenshot per route on PR (Home / form / case detail)
