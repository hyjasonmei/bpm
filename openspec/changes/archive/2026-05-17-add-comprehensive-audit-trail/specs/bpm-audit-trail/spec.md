## ADDED Requirements

### Requirement: AuditEvent entity is append-only

The system SHALL persist `AuditEvent` rows for every audit-worthy event. Rows MUST be append-only — UPDATE and DELETE attempts SHALL be blocked at the EF SaveChanges interceptor level. Each row carries Id (ULID-based for chronological ordering), TenantId, OccurredAt (microsecond precision), ActorUserId (nullable for system events), ActorIpAddress, ActorUserAgent, Category, Action, TargetType, TargetId, BeforeJson, AfterJson, MetadataJson.

#### Scenario: Modify AuditEvent throws

- **GIVEN** an AuditEvent row exists
- **WHEN** code loads it, mutates a field, calls SaveChanges
- **THEN** SaveChanges throws InvalidOperationException with "AuditEvent is append-only"

#### Scenario: ULID ordering

- **GIVEN** events created in chronological order
- **WHEN** queried by Id ASC
- **THEN** the order matches OccurredAt ASC (ULID is monotonic)

### Requirement: Auto-capture changes to audited entities

A SaveChanges interceptor SHALL automatically emit an `AuditEvent` whenever an entity of an audited type is created, modified, or deleted. Audited types include: User, Department, Group, Role, Permission, RolePermission, RoleAssignment, BusinessCalendar, CalendarException, Delegation, TenantConfig. The Before/After JSON SHALL capture the relevant fields (excluding noisy fields like CreatedAt for updates).

#### Scenario: User edit captured

- **GIVEN** an admin edits Wilson's department from "工程部" to "產品部"
- **WHEN** SaveChanges commits
- **THEN** an AuditEvent is inserted with Category = Org, Action = "user.updated", TargetType = "User", TargetId = Wilson.Id, BeforeJson contains old department, AfterJson contains new

#### Scenario: Role assignment captured

- **GIVEN** an admin assigns role X to user Y
- **WHEN** SaveChanges commits
- **THEN** an AuditEvent with Category = Authz, Action = "role_assignment.created"

### Requirement: Explicit audit emission for non-entity events

Audit logger SHALL be invoked explicitly for events not tied to entity changes:

- Auth: login success / failure / logout / token refresh
- File: download (read action)
- HR sync: apply summary
- Spec: publish (new version creation)
- Bulk operations (admin force-reassign, bulk export, etc.)

#### Scenario: Login emits Auth audit

- **WHEN** Wilson successfully logs in via dev-login
- **THEN** an AuditEvent is inserted with Category = Auth, Action = "login.success", ActorUserId = Wilson.Id, ActorIpAddress = client IP

#### Scenario: Failed login emits with attempted user

- **WHEN** a login attempt fails for username "wilson@x.com"
- **THEN** an AuditEvent with Category = Auth, Action = "login.failure", ActorUserId = null, MetadataJson includes `{ attempted_user: "wilson@x.com", reason: "bad_password" }`

#### Scenario: File download audited

- **WHEN** Wilson downloads a file
- **THEN** an AuditEvent with Category = File, Action = "file.read", TargetType = "StoredFile", TargetId = file id, MetadataJson includes file size + content_type

### Requirement: Unified query API across audit-y tables

`IAuditLogReader.QueryAsync(filters)` SHALL return a unified chronological feed including: AuditEvent rows, TaskHistory rows, ActorResolutionAudits rows, NotificationDispatchAudits rows. The reader adapts each source's schema into a common AuditEvent-shaped DTO for the response.

The unified result SHALL be filterable by Category / Action / ActorUserId / TargetType / TargetId / OccurredAfter / OccurredBefore.

#### Scenario: Single query spans sources

- **GIVEN** TaskHistory has rows; AuditEvent has Auth rows; NotificationDispatchAudits has dispatch rows
- **WHEN** an admin queries `/api/audit?from=2026-05-01&to=2026-05-08`
- **THEN** the response includes events from all four sources in chronological order

#### Scenario: Filter by category

- **WHEN** the admin filters `category=Auth`
- **THEN** only Auth-category events are returned (login/logout); TaskHistory rows are NOT included even though they're in the unified read

### Requirement: IP anonymization after 90 days

A daily janitor SHALL anonymize `ActorIpAddress` for AuditEvent rows older than 90 days by zeroing the last octet (IPv4) or the last 64 bits (IPv6). The operation MUST be idempotent.

#### Scenario: 91-day-old IP anonymized

- **GIVEN** an AuditEvent inserted 91 days ago with ActorIpAddress = "203.0.113.45"
- **WHEN** the daily janitor runs
- **THEN** the row's ActorIpAddress = "203.0.113.0"

#### Scenario: Less than 90 days kept

- **GIVEN** an AuditEvent inserted 80 days ago
- **WHEN** the janitor runs
- **THEN** the IP is unchanged

### Requirement: Configurable per-category retention

The system SHALL support per-category retention configuration. Defaults: Auth = 365 days, File = 90, Notification = 90, others = 365. Admin can override per tenant via TenantConfig. The daily janitor purges (hard-deletes) AuditEvents past their category's retention.

#### Scenario: Default retention purges old events

- **GIVEN** an Auth AuditEvent inserted 366 days ago and the default retention applies
- **WHEN** the janitor runs
- **THEN** the row is hard-deleted

#### Scenario: Admin extends retention

- **GIVEN** the admin sets Auth retention to 730 days
- **WHEN** a 366-day-old event exists
- **THEN** it is NOT purged; remains in DB

### Requirement: BeforeJson / AfterJson visible only to admin

The audit detail endpoint SHALL return `BeforeJson` and `AfterJson` only to `tenant_admin` role. Other authorized callers (e.g., a flow_admin viewing process events) receive the AuditEvent metadata without the before/after payload (`null` substitution).

#### Scenario: Tenant admin sees payload

- **WHEN** a tenant_admin calls GET /api/audit/{id}
- **THEN** response includes BeforeJson and AfterJson

#### Scenario: Flow admin sees redacted

- **WHEN** a flow_admin (without tenant_admin) calls the same endpoint
- **THEN** BeforeJson and AfterJson are null in response; other fields visible
