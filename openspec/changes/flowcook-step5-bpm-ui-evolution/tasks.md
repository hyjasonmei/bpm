# Tasks

## 1. Live Cases page in `bpm-ui`

- [ ] 1.1 Route + nav for Live Cases
- [ ] 1.2 Server-paginated table of running instances
- [ ] 1.3 Filter by flow / actor / state
- [ ] 1.4 Detail panel with task history timeline
- [ ] 1.5 Auth gate: visible to admin-class principals only

## 2. Completed cases page

- [ ] 2.1 Same as Live Cases but for completed / closed instances
- [ ] 2.2 Reuse table component

## 3. Reports page

- [ ] 3.1 Move existing reports backend from bpm-admin-svc (if Step 4 left some) and bpm-svc
- [ ] 3.2 Implement existing 5 report widgets (kept in-memory percentile)

## 4. Notifications page

- [ ] 4.1 Notification dispatch log view
- [ ] 4.2 Sandbox-redirected vs real send distinction

## 5. 介入 (admin intervention) page

- [ ] 5.1 Move legacy 4 intervention endpoints' UI to bpm-ui
- [ ] 5.2 Persona-switch allow-list gated

## 6. Soft-delete UI

- [ ] 6.1 "Delete" button in Live Cases / Completed / Tasks rows
- [ ] 6.2 Visible only to allow-listed admins
- [ ] 6.3 Confirm modal with reason input
- [ ] 6.4 Wire to bpm soft-delete API from Step 4

## 7. DynamicForm component

- [ ] 7.1 `<DynamicForm spec={userTask}>` reading fields[] and rendering
- [ ] 7.2 Field renderer registry (text / textarea / number / date / select / multiselect / file / user_picker / repeater / derived)
- [ ] 7.3 Validator runs on each input (Cel via existing JS evaluator if available; else simple rule engine)
- [ ] 7.4 onSubmit returns patch + dirty diff

## 8. Migrate 11 hand-coded forms

- [ ] 8.1 LEAVE
- [ ] 8.2 PURCHASE
- [ ] 8.3 TRAVEL
- [ ] 8.4 GEE (公告)
- [ ] 8.5 RESIGN
- [ ] 8.6 DEPTX
- [ ] 8.7 (remaining 5 per current code base)
- [ ] 8.8 Verify ProcessRuntime end-to-end runs each

## 9. Verify SandboxBanner shows new states

- [ ] 9.1 Show redirect target email
- [ ] 9.2 Show frozen clock value when enabled
- [ ] 9.3 Update style to match (minor)

## 10. Delete legacy Process Admin Console from `bpm-admin-ui`

- [ ] 10.1 Remove `screens/admin/*` files
- [ ] 10.2 Remove `LEGACY_ADMIN_UI_VISIBLE` flag plumbing
- [ ] 10.3 Update README
