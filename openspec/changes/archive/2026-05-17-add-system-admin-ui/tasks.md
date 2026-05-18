# Tasks

## 1. Admin shell

- [ ] 1.1 Create `bpm-ui/src/screens/admin/AdminShell.tsx` with sidebar + content area
- [ ] 1.2 Route registration `/admin/*` with auth gate (tenant_admin role)
- [ ] 1.3 Auth-guard wrapper component that redirects non-admins to `/`

## 2. Users / Departments / Groups

- [ ] 2.1 UsersList with search, table, pagination, deactivate/activate actions
- [ ] 2.2 UserDetail side-panel with edit form
- [ ] 2.3 NewUserDialog (manual creation)
- [ ] 2.4 DepartmentsTree using react-arborist; drag-drop reorganization with confirmation
- [ ] 2.5 DepartmentDetail editor (code, name, parent, head, function_tag, approval_limit)
- [ ] 2.6 GroupsList + GroupDetail with member editor (add/remove users / sub-groups)

## 3. Roles / Permissions / Assignments

- [ ] 3.1 RolesList with system + flow-scoped split
- [ ] 3.2 RoleDetail with permission matrix (multi-select grid)
- [ ] 3.3 RoleAssignments view with filter by principal / role
- [ ] 3.4 PermissionMatrix component (rows = roles, columns = (action, resource) tuples)

## 4. Delegation global view

- [ ] 4.1 AdminDelegationView with filters (granter, delegate, status)
- [ ] 4.2 Read-only access; no create / cancel on behalf

## 5. HR Sync UI

- [ ] 5.1 ImportRunList showing past runs with status chips
- [ ] 5.2 NewImportFlow with stepper: Upload → Map → Dry-run → Apply
- [ ] 5.3 MappingStep: table of CSV columns + dropdown of fields with auto-suggestions highlighted
- [ ] 5.4 DryRunStep: tabbed view (Inserts / Updates / Deactivations / Errors / Warnings); row-level details
- [ ] 5.5 ApplyConfirmation modal showing summary
- [ ] 5.6 ImportRunDetail for past runs (view of dry-run report + applied summary)

## 6. Calendars

- [ ] 6.1 CalendarsList; create / edit / archive
- [ ] 6.2 CalendarEditor with weekly windows form + year-grid for exceptions
- [ ] 6.3 YearGrid component (12 months, colored cells per exception type)
- [ ] 6.4 "Import 2026 holidays" button

## 7. Tenant configuration

- [ ] 7.1 Add backend endpoints `GET /api/tenant-config` + `PUT /api/tenant-config`
- [ ] 7.2 TenantConfigForm UI

## 8. System health dashboard

- [ ] 8.1 Add backend endpoint `GET /api/admin/health` aggregating worker statuses + counts
- [ ] 8.2 HealthDashboard with stat cards + worker status list + "Run worker now" buttons
- [ ] 8.3 Auto-refresh every 30s

## 9. Audit log viewer

- [ ] 9.1 Add backend `IAuditLogReader` service unifying TaskHistory + ActorResolutionAudits + NotificationDispatchAudits + role-assignment changes
- [ ] 9.2 Add `GET /api/admin/audit?event_type=&from=&to=&user=` endpoint
- [ ] 9.3 AuditLogViewer with filters + paginated table
- [ ] 9.4 CSV export button

## 10. Notifications dispatcher view

- [ ] 10.1 DispatchedNotifications: filter by status / channel / date; click → details
- [ ] 10.2 Retry button for Failed deliveries (admin only)

## 11. End-to-end verification

- [ ] 11.1 Boot stack; login as tenant_admin; visit `/admin`; verify sidebar + auth-gate
- [ ] 11.2 Navigate Users; verify search + edit
- [ ] 11.3 Navigate Departments; verify tree, drag-drop
- [ ] 11.4 Run an HR sync upload → mapping → dry-run → apply via UI; verify Org tables updated
- [ ] 11.5 Visit Health; verify counts + worker status
- [ ] 11.6 Visit Audit; filter by date; export CSV
- [ ] 11.7 Login as non-admin; visit `/admin`; verify redirect to `/`
- [ ] 11.8 **Demo guard**: 9 mock-up forms, Home, Search, Report, lib/workflow.ts NOT modified

## 12. Commit

- [ ] 12.1 Commit in chunks per major area
- [ ] 12.2 Push via GitKraken
