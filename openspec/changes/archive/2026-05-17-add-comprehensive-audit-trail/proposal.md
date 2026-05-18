## Why

Audit coverage today is partial:

- TaskHistory (process events, in `add-process-runtime`) — ✅
- ActorResolutionAudits (resolver outcomes) — ✅
- NotificationDispatchAudits — ✅
- Comment edit/delete (15-min window) — ⚠️ partial via LastModifiedAt

But missing:

- Login / logout / failed login attempts
- User profile changes (admin edits to a user's department, manager, role assignments)
- Role / Permission / RoleAssignment mutations
- Spec edits (every spec.json publish or draft save)
- Tenant config changes
- File downloads (who accessed which file when)
- Delegation lifecycle (who created / cancelled which delegation when)
- Calendar / holiday changes
- HR sync apply (already audited via OrgImportRun, but cross-link to per-row audit needed)

For ISO 9001 / IATF 16949 / SOC 2 audit, "we know who changed what when" must cover the entire system — not just process flow events. Customers in regulated industries will ask "show me your audit log for the last quarter" and expect a complete answer.

## What Changes

### Audit trail capability (NEW `bpm-audit-trail`)

**Entity** — `AuditEvent`:

- `Id` (Guid, monotonic ULID-based for chronological ordering)
- `TenantId`, `OccurredAt` (UTC, microsecond precision)
- `ActorUserId` (Guid, nullable for system events)
- `ActorIpAddress` (string, anonymized after 90 days per privacy policy)
- `ActorUserAgent` (string, truncated to 200 chars)
- `Category` (enum): `Auth` / `Org` / `Authz` / `Spec` / `TenantConfig` / `File` / `Delegation` / `Calendar` / `HrSync` / `Process` / `Notification`
- `Action` (string, e.g., `"login.success"`, `"role.assignment.created"`, `"spec.published"`)
- `TargetType` (string, e.g., `"User"`, `"Spec"`, `"Role"`)
- `TargetId` (Guid or string identifier)
- `BeforeJson` (nullable, prior state for changes)
- `AfterJson` (nullable, new state)
- `MetadataJson` — additional context (e.g., for download: file size; for login: success/failure)

**Append-only** — same enforcement pattern as TaskHistory: SaveChanges interceptor blocks UPDATE / DELETE.

### Audit emission strategy

Two patterns:

1. **Direct write** — services explicitly call `IAuditLogger.LogAsync(eventData)` for events they know are audit-worthy
2. **Interceptor** — for entity changes (User edits, Role mutations, Spec edits, etc.), an EF SaveChanges interceptor automatically emits an AuditEvent capturing before/after JSON

For login/logout: explicit calls in the auth controllers.

For file downloads: explicit call in FilesController on read.

For configuration changes: explicit calls in admin controllers.

Interceptor handles the bulk of "data changed" events with minimal code.

### Query API

`IAuditLogReader.QueryAsync(filters)` — supports:

- Category, Action, ActorUserId, TargetType, TargetId, OccurredAfter, OccurredBefore
- Pagination + sort
- Returns chronological feed

`/admin/audit` page (in `add-system-admin-ui`) becomes a comprehensive query UI atop this.

### Privacy considerations

- IP addresses anonymized after 90 days (last octet zeroed; rest preserved for region)
- User agent strings truncated
- BeforeJson / AfterJson may include sensitive data → Admin role required to view; download API returns censored fields for non-tenant-admin

### Retention

- Default: keep AuditEvents forever (no auto-purge)
- Admin can configure per-category retention (e.g., "Auth events older than 1 year purged")
- Configuration UI in System Admin → Tenant Config

### API endpoints

- `GET /api/audit?category=&action=&actor=&target_type=&target_id=&from=&to=&page=&size=` — list with filters
- `GET /api/audit/{id}` — single event detail
- `POST /api/audit/export` body `{ filters }` — async CSV export job (uses bulk-export pattern)
- `GET /api/audit/categories` — list of categories + actions for filter UI

### Out of scope (future changes)

- SIEM integration (e.g., Splunk forwarder)
- Real-time audit alerts (e.g., notify on suspicious patterns)
- Audit log signing (cryptographic chain — Merkle tree)
- Compliance report templates (auto-generate ISO 9001 report)
- Cross-tenant audit aggregation (no multi-tenant scope)
- Auto-anomaly detection / fraud detection
- Audit replay (re-execute events)

## Capabilities

### New Capabilities

- `bpm-audit-trail` — AuditEvent entity (append-only), IAuditLogger emission service, EF interceptor for entity-change capture, IAuditLogReader query service, comprehensive REST endpoints, IP anonymization, admin-only sensitive field access, configurable retention.

### Modified Capabilities

- `bpm-system-admin-ui` — `/admin/audit` page upgraded to consume the unified audit endpoints (replaces the prior plan that only unified existing audit-y tables).

## Impact

- **bpm-svc/src/Domain/Entities/Audit/AuditEvent.cs**: new
- **bpm-svc/src/Domain/Entities/Audit/AuditCategory.cs**: enum
- **bpm-svc/src/Application/Audit/IAuditLogger.cs / AuditLogger.cs**: emission service
- **bpm-svc/src/Application/Audit/IAuditLogReader.cs / AuditLogReader.cs**: query service
- **bpm-svc/src/Persistence/Interceptors/AuditEventCaptureInterceptor.cs**: auto-capture entity changes for relevant types (User, Role, RoleAssignment, Permission, Spec, BusinessCalendar, etc.)
- **bpm-svc/src/Persistence/Configurations/Audit/AuditEventConfiguration.cs**: EF config
- **bpm-svc/src/Persistence/Migrations/AddAuditTrail**: 1 new table
- **bpm-svc/src/Api/Audit/AuditController.cs**: 4 endpoints
- **bpm-svc/src/Application/Auth/AuthService.cs**: explicit login/logout/failed-login audit calls
- **bpm-svc/src/Api/Files/FilesController.cs**: audit on read
- **bpm-svc/src/Application/...**: audit calls in HR sync apply, calendar edits, etc.
- **bpm-ui/src/screens/admin/audit/AuditLogViewer.tsx**: rebuilt to use unified endpoint
- **DB migration**: 1 new table; index on (TenantId, OccurredAt DESC), (Category, Action), (ActorUserId, OccurredAt DESC), (TargetType, TargetId)
- **Demo guard**: 9 mock-up forms NOT modified
