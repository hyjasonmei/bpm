## ADDED Requirements

### Requirement: Admin SPA route auth-gated to tenant_admin

The system SHALL serve a separate admin SPA under route prefix `/admin/*`. Access SHALL require the `tenant_admin` role. Non-admin users navigating to `/admin/*` SHALL be redirected to `/` (or to a login page if not authenticated). The persona switcher (RoleSwitcher) SHALL be disabled inside `/admin/*` to prevent accidental persona switches that would lose admin context.

#### Scenario: Admin reaches /admin

- **WHEN** a user with tenant_admin role navigates to /admin
- **THEN** the admin shell renders with sidebar

#### Scenario: Non-admin redirected

- **WHEN** an employee navigates to /admin
- **THEN** they are redirected to /

#### Scenario: Persona switch disabled inside admin

- **WHEN** an admin is on /admin/users
- **THEN** the RoleSwitcher dropdown is disabled (or hidden) so persona switching cannot lose admin context

### Requirement: Admin shell sidebar lists all admin sections

The sidebar in `/admin/*` SHALL include navigation to: Users, Departments, Groups, Roles, Permissions, Role Assignments, Delegation (global view), HR Sync, Calendars, Tenant Config, System Health, Audit Log, Notifications Dispatched. Each navigation item links to its corresponding admin sub-route.

#### Scenario: Sidebar items visible

- **WHEN** an admin opens /admin
- **THEN** the sidebar lists 13 navigation items as above; clicking each navigates to /admin/<section>

### Requirement: Users management supports search, edit, deactivation

The `/admin/users` page SHALL allow:

- Search by name / email / department / role
- Sort by columns (name, email, department, last activity)
- Click a row to open user detail in a side panel
- Edit user fields (full_name, manager, department, title_raw, attributes JSON)
- Deactivate / reactivate users
- Bulk deactivate when multiple rows selected

`Email` MUST NOT be editable after creation.

#### Scenario: Search filters list

- **WHEN** the admin types "wilson" in search
- **THEN** the table shows only matching users

#### Scenario: Email read-only

- **WHEN** the admin opens an existing user's detail
- **THEN** the Email field is displayed but disabled

#### Scenario: Bulk deactivate

- **WHEN** the admin selects 3 users via checkboxes and clicks Bulk Deactivate
- **THEN** all 3 have IsActive set to false; UI refreshes; an audit log entry is created per user

### Requirement: Departments tree supports drag-drop reorganization

The `/admin/departments` page SHALL render the department tree using a tree component (e.g., react-arborist) supporting drag-and-drop to reparent departments. Reparenting SHALL trigger a confirmation dialog showing the affected paths. After confirm, the API is called to update the parent_id.

#### Scenario: Drag dept to new parent

- **WHEN** the admin drags Department A onto Department B
- **THEN** a confirm dialog shows "Move A under B (was: original parent)"
- **AND** on confirm, the API updates A.parent_id to B; the tree re-renders

#### Scenario: Drag rejected on cycle

- **WHEN** the admin drags Department A onto its own descendant
- **THEN** the drag is visually blocked or the confirm dialog shows an error explaining the cycle

### Requirement: HR Sync stepper UI guides upload → apply

The `/admin/org-imports` UI SHALL implement a 4-step stepper:

1. **Upload** — file drop zone; calls /api/files; calls /api/org-imports start
2. **Mapping** — column-to-field picker with auto-suggestions visualized; admin can override
3. **Dry-run** — tabbed view of inserts / updates / deactivations / errors / warnings
4. **Apply** — confirmation modal with summary count; "Apply" button enabled only if no errors

The stepper SHALL preserve state across steps; admin can go back and forth without losing data.

#### Scenario: Stepper progresses

- **WHEN** the admin uploads a CSV
- **THEN** the stepper advances to step 2 (Mapping)
- **AND** the suggested mapping is pre-populated based on column names

#### Scenario: Errors block apply

- **GIVEN** the dry-run report contains errors
- **WHEN** the admin reaches step 4
- **THEN** the Apply button is disabled with a message "fix errors before applying"

### Requirement: Calendar editor with year-grid exception authoring

The `/admin/calendars/{id}` editor SHALL display:

- The weekly working windows in tabular form (each weekday with its windows; add/remove window buttons)
- A 12-month year-grid where each date cell is colored by exception type (Holiday red, WorkDay green, SpecialHours blue, none gray)
- Clicking a date opens a modal to add / edit / remove an exception
- An "Import 2026 holidays" button calls the holiday import endpoint

#### Scenario: Year grid shows seeded holidays

- **GIVEN** Taiwan default calendar with 春節 holidays imported
- **WHEN** the admin opens the calendar editor
- **THEN** Feb dates of 春節 are colored red

#### Scenario: Click date adds exception

- **WHEN** the admin clicks a Wednesday in October
- **THEN** a modal opens; admin selects "Holiday" type; saves; the cell turns red

### Requirement: System health dashboard displays worker status

The `/admin/health` page SHALL display:

- Stat cards: Running ProcessInstances, Open Tasks, Overdue Tasks, Queued Notifications
- Worker status table: each background service with last successful run timestamp + status
- File storage usage gauge
- "Run worker now" buttons calling admin trigger endpoints

The page SHALL auto-refresh every 30 seconds.

#### Scenario: Worker shown as healthy

- **GIVEN** NotificationDispatchWorker last ran 2 minutes ago successfully
- **WHEN** the admin opens /admin/health
- **THEN** the worker is shown with green status + "2 minutes ago"

#### Scenario: Stalled worker flagged

- **GIVEN** SlaTimerJob last ran 5 hours ago (much greater than its 1-minute interval)
- **WHEN** the admin opens the page
- **THEN** the worker is shown red / yellow with the stale timestamp

### Requirement: Audit log unifies sources

The `/admin/audit` page SHALL render a chronological feed combining: TaskHistory, ActorResolutionAudits, NotificationDispatchAudits, and role assignment changes (via change tracking). Filters: event type, user, date range. Each row shows: timestamp, event type, actor, summary, "show details" expander.

The page SHALL support CSV export of currently filtered rows.

#### Scenario: Filter by event type

- **WHEN** the admin selects "TaskSubmitted" in the event-type filter
- **THEN** only TaskSubmitted rows are shown across instances

#### Scenario: CSV export

- **WHEN** the admin clicks Export CSV with current filters applied
- **THEN** a CSV file downloads containing the filtered rows

### Requirement: Demo screens unmodified

The change SHALL NOT modify any of the existing employee-facing screens — `bpm-ui/src/screens/Home.tsx`, `bpm-ui/src/screens/forms/*.tsx`, `bpm-ui/src/screens/Search.tsx`, `bpm-ui/src/screens/Report.tsx`, `bpm-ui/src/lib/workflow.ts`. The admin SPA is a separate route with its own components.

#### Scenario: Mock-up forms unchanged

- **WHEN** the admin UI ships
- **AND** a non-admin opens the existing forms
- **THEN** the visuals are byte-identical to pre-change
