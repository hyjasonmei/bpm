# Design notes

## 1. Why split System Admin and Process Admin

Two distinct audiences:

- **System Admin (IT / ops)**: cares about plumbing — users, roles, tenant config, infra health, HR sync. Mostly read/write configuration data.
- **Process Admin (business user / consultant)**: cares about flows — designs / monitors / fixes broken cases. Mostly works with spec definitions and running instances.

Lumping them into one UI would force IT folks to navigate past flow-design clutter; would force business users to wade through tenant-config they don't care about. Two separate sidebars + auth gates → cleaner mental model.

Both sidebars share the AppLayout chrome (top bar, persona switcher off in admin areas). System Admin lives at `/admin/*`; Process Admin at `/processes/*` (later proposal).

## 2. Why no admin override on delegation

Per `add-delegation` proposal's design: admin override is intentionally absent because the use case ("user is sick + delegate is also sick") is organizational chaos that should escalate to people-management, not get a button.

For this UI: delegation page is read-only for admins. They can see all delegations but cannot create / cancel on others' behalf. If they need to fake it, they use the user-impersonation tooling (out of scope for this proposal; future engineering).

## 3. Tree visualization for departments

Two viable libraries: `react-arborist` (richer DnD, ~50 KB) vs `react-d3-tree` (visual, ~80 KB). For SME scale (10-50 departments), DnD is more useful than fancy visuals. Pick `react-arborist`.

If perf becomes an issue (>1000 departments) we revisit.

## 4. Year-grid calendar

For exception authoring, a 12-month grid is more intuitive than a list. Each cell shows date number + colored dot for exception type. Click → modal to add/remove. Hover → tooltip showing the exception description.

Build with native `<table>` + CSS grid; don't pull in a calendar library.

## 5. Health dashboard

A minimal version:

- Counts via aggregation queries cached for 60s
- Worker status: each `IHostedService` exposes "last successful run" via a static field; admin endpoint reads
- File storage: `du` against the local files dir or `s3 ls` with size aggregation; cached 5 min

For real ops, integrate with metrics tools (Prometheus / Grafana) — defer.

## 6. Audit log unification

The system has multiple audit-y tables:
- TaskHistory (process runtime)
- ActorResolutionAudits (resolver)
- NotificationDispatchAudits
- DelegationChangeLog (mentioned in delegation; future)
- (implicit: row-level CreatedAt / LastModifiedAt + AuditableEntity changes)

The `/admin/audit` UI unifies them via a query layer — single endpoint that joins these into a chronological feed with `event_type` filter. The frontend doesn't need to know the underlying tables; it sees a unified `{ event_type, actor, timestamp, payload }` shape.

Implementation: backend has an `IAuditLogReader` service that pages across the source tables and merges results. Performance: defer optimization until we hit it.

## 7. Open questions

- **Cross-tenant admin** for our internal ops team: not addressed (per Jason's "no multi-tenant"). For now, admin = tenant-admin; no separate "global admin" concept.
- **Inline help**: tooltips for tricky fields (e.g., `function_tag`) → defer; v1 ships docs separately.
- **Bulk operations**: depends on demand. Bulk-deactivate users, bulk-assign roles. Defer.
- **CSV export everywhere**: the audit page exports; some other lists may also want it. Add per-page as needed.
