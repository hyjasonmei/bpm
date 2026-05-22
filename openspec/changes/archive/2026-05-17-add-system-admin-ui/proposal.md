## Why

The system has accumulated APIs across many proposals — Org CRUD, Roles & Permissions, Delegation, HR Sync, Calendar, File Storage, Notifications, SLA dashboards. Without a UI, customers and our own ops team need direct DB access (or curl) to manage everything. That's not a product.

This change ships the **System Admin UI** — an authenticated admin SPA route at `/admin/*` covering:

- Users CRUD (search, view, edit, deactivate)
- Departments CRUD with tree visualization
- Groups CRUD with member management
- Roles & Permissions management
- Delegation global view (admin can see all delegations across users)
- HR Sync workflow UI (upload → map → dry-run → apply)
- Calendars + holidays editor
- Tenant configuration (timezone, language, default calendar, notification settings)
- System health dashboard (running instances, stuck tasks, queue health)
- Audit log viewer

UI for *flows* (BPMN designer / process monitoring) lives in the separate `add-process-admin-ui` proposal — that's a different audience (business users) with different concerns.

## What Changes

### Admin shell (NEW capability `bpm-system-admin-ui`)

Route prefix: `/admin/*` (separate from the existing `/` employee-side app)

Auth: requires `tenant_admin` role. RoleSwitcher persona switching disabled inside `/admin/*`.

Sidebar navigation:
- Org → Users / Departments / Groups
- Authz → Roles / Permissions / Role Assignments
- Delegation
- HR Sync
- Calendars
- Tenant Config
- System Health
- Audit Log
- Notifications (dispatcher view, not user inbox)

### Users management

`/admin/users`:
- Search bar (name / email / department / role)
- Table with email / name / dept / title / manager / is_active / delegation status
- Click row → side panel with full details + edit
- Bulk actions: deactivate / activate (admin-only)
- "+ New user" button (manual creation alternative to HR sync)

`/admin/users/{id}`:
- All fields editable (email read-only after creation; manager picker; department picker)
- Display: roles assigned, currently delegating to / from
- Activity tab: recent tasks, recent submissions, current open delegation

### Departments management

`/admin/departments`:
- Tree visualization (expandable nodes via D3 or a lightweight tree component)
- Each node shows code / name / function_tag / head / member count
- Drag-and-drop reorganization with confirmation
- "+ New department" with parent selection
- Delete blocked if dept has members or sub-depts (must reassign first)

### Groups management

`/admin/groups`:
- List view: name / member count / created date
- Click → membership editor (add/remove users / sub-groups)
- Cycle detection on group nesting (uses existing org-chart helper)

### Roles & Permissions

`/admin/roles`:
- List system-scoped + flow-scoped roles
- Click → permission matrix (add / remove permissions)
- Role assignment view: which principals (users / depts / groups) hold the role at which scope (tenant / flow / step)

`/admin/permissions`:
- Read-only registry of built-in permissions (action × resource)
- Show which roles include each permission

`/admin/role-assignments`:
- Search / filter by principal or role
- Bulk assign / revoke

### Delegation global view

`/admin/delegation`:
- Admin can see all active / scheduled / expired / cancelled delegations across the tenant
- Filter by granter or delegate
- Cannot create or cancel on behalf of others (per design choice from `add-delegation`); read-only access only

### HR Sync workflow

`/admin/org-imports`:
- "+ New import" button → upload CSV
- Step-by-step UI: Upload → Mapping → Dry-run preview → Apply
- Mapping step: column-to-field picker with auto-suggestions visualized
- Dry-run step: tabbed view (Inserts / Updates / Deactivations / Errors / Warnings) with row-level detail
- Apply confirmation modal with summary count
- Past runs list

### Calendars

`/admin/calendars`:
- List tenant calendars; create / edit / archive
- Calendar editor: weekly windows + exceptions (holiday / work day / special hours)
- Year view (12-month grid) with holidays shaded; click date to add/remove exception
- "Import 2026 holidays" button calling the holiday import endpoint

### Tenant configuration

`/admin/tenant-config`:
- Tenant name / display name
- Default timezone
- Default calendar pick
- Default language (zh-TW / en)
- Notification email backend choice
- File storage backend choice (read-only display — operations changes only via env)
- Email domains for SSO (forward look at `add-sso-oidc`)

### System health dashboard

`/admin/health`:
- Counts: running ProcessInstances / open Tasks / overdue Tasks / queued Notifications
- SLA breach rate (last 30 days)
- File storage usage
- Background worker status (last run time + status per worker: NotificationDispatchWorker, FileStorageJanitor, SlaTimerJob, DelegationStatusRefreshJob, etc.)
- Quick action: "Run worker now" buttons for ops emergencies

### Audit log viewer

`/admin/audit`:
- TaskHistory + Comment + Delegation + ImportRun + role assignment changes — all readable here
- Filter by event type, user, date range
- Export selected rows as CSV (tenant-admin only)

### Notifications dispatcher view

`/admin/notifications/dispatched`:
- All NotificationDelivery rows (admin-cross-user)
- Filter by status (queued / sent / failed / read)
- Click → details + retry button for failed
- Different from the user's `/notifications` inbox — this is dispatcher-side ops

### Out of scope (future changes)

- BPMN designer / form designer / process simulator / live process monitoring → `add-process-admin-ui`
- Multi-tenant control panel (per Jason: multi-tenancy not in scope)
- Customer self-service signup
- Billing / subscription management
- White-label / custom theme
- Mobile-optimized admin (desktop only in v1)
- Inline help / tutorial overlays

## Capabilities

### New Capabilities

- `bpm-system-admin-ui` — `/admin/*` SPA routes; admin shell with sidebar; users / departments / groups / roles / permissions / role-assignments / delegation global / HR sync / calendars / tenant config / system health / audit log / notifications dispatch — comprehensive admin views consuming existing APIs.

### Modified Capabilities

- None — this proposal only ships UI consuming previously-shipped APIs. If a UI need surfaces a missing endpoint, that gap is documented and addressed in the relevant proposal's gap list (e.g., a "tenant config" GET endpoint may need to be added to the org-model capability).

## Impact

- **bpm-ui/src/screens/admin/AdminShell.tsx**: new
- **bpm-ui/src/screens/admin/users/**: UsersList, UserDetail, NewUserDialog
- **bpm-ui/src/screens/admin/departments/**: DepartmentsTree, DepartmentDetail
- **bpm-ui/src/screens/admin/groups/**: GroupsList, GroupDetail with membership editor
- **bpm-ui/src/screens/admin/roles/**: RolesList, RoleDetail, PermissionMatrix
- **bpm-ui/src/screens/admin/delegation/**: AdminDelegationView (global)
- **bpm-ui/src/screens/admin/imports/**: ImportRunList, ImportRunDetail with stepper UI
- **bpm-ui/src/screens/admin/calendars/**: CalendarsList, CalendarEditor with year view
- **bpm-ui/src/screens/admin/tenant-config/TenantConfigForm.tsx**: new
- **bpm-ui/src/screens/admin/health/HealthDashboard.tsx**: new
- **bpm-ui/src/screens/admin/audit/AuditLogViewer.tsx**: new
- **bpm-ui/src/screens/admin/notifications/DispatchedNotifications.tsx**: new
- **bpm-ui/src/components/AppLayout.tsx**: route registration `/admin/*` requires admin role
- **bpm-svc/src/Api/**: small additions where existing endpoints don't cover admin needs:
  - `GET /api/admin/health` aggregating workers + counts
  - `GET /api/admin/audit?event_type=&from=&to=&user=` cross-source audit query (TaskHistory + comment changes + role changes)
  - `GET /api/tenant-config` and `PUT /api/tenant-config` (new endpoints)
- **No DB migration**
- **No new NuGet/NPM dependencies** (uses existing UI primitives + lightweight tree libs already in package.json)
- **Demo guard**: 9 mock-up forms, Home, Search, Report, lib/workflow.ts NOT modified; admin SPA is a separate route
