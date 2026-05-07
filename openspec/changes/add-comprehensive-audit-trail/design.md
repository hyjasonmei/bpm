# Design notes

## 1. Why a unified AuditEvent table vs federated

We have multiple audit-y tables already (TaskHistory, ActorResolutionAudits, etc.). Could we just add interceptors and unify by query?

Trade-off:

- **Federated**: each domain keeps its specialized table; query layer joins — TaskHistory has rich payload semantics, easier to maintain
- **Unified**: one table for everything — simpler to consume, simpler to retain / purge

Decision: keep both. TaskHistory remains for its dense process semantics. AuditEvent captures the *cross-cutting* events (auth, configuration, file access) that don't have a natural home elsewhere. Query layer (IAuditLogReader) presents a unified feed by joining.

For things already audited (TaskHistory etc.), the query layer adapts those rows into AuditEvent shape on read. Single mental model for consumers.

## 2. Append-only enforcement

Same pattern as TaskHistory:

- EF SaveChanges interceptor checks for AuditEvent entries with state Modified / Deleted; throws InvalidOperationException

Audit hardness: we DON'T enforce immutability at the SQL level (would require triggers / RLS). App-layer is sufficient for our compliance posture; if a customer demands SQL-level, add Postgres-RLS rules later.

## 3. Sensitive payload handling

`BeforeJson` / `AfterJson` may include personal data (User profile fields). Storage:

- Stored verbatim in DB (encryption at rest is the storage layer's concern)
- Returned via API only to tenant_admin (audit page) — non-admin users don't see these payloads even for their own events

For GDPR compliance: a "right to be forgotten" tenant action (out of scope here; documented as future) would include redacting AuditEvent payloads referencing the deleted user.

## 4. IP anonymization schedule

Daily janitor pass:

- For AuditEvents older than 90 days: replace `ActorIpAddress` last octet with `0` (e.g., `203.0.113.45` → `203.0.113.0`)
- Idempotent

Privacy regulator expectation: keep IPs only as long as necessary; 90 days for security investigation; then anonymize.

## 5. Retention configuration

Per-category retention table:

- `AuditRetentionPolicy(Category, RetentionDays)` configured per tenant
- Defaults: Auth = 365, Org = 365, Authz = 365, Spec = 365, TenantConfig = 365, File = 90, Delegation = 365, Calendar = 365, HrSync = 365, Process = 365, Notification = 90

Daily janitor purges AuditEvents past their category's retention. Admin can override per-tenant via System Admin UI.

## 6. Login / logout audit

Explicit calls from `AuthService`:

- On successful login: `AuditLogger.LogAsync(Auth, "login.success", ActorUserId, ip, agent)`
- On failed login: `AuditLogger.LogAsync(Auth, "login.failure", null, ip, agent, MetadataJson { reason: "bad_password", attempted_user: "wilson@x.com" })`
- On logout: `AuditLogger.LogAsync(Auth, "logout", ActorUserId, ip, agent)`
- On token refresh: optional; v1 skip

Failed login audit useful for security investigation: "wilson@x.com had 30 failed attempts in 5 minutes from a single IP" → escalation rule (defer; alerting is future work).

## 7. Performance considerations

Audit emission is on the write path. Per save:

- 1-3 AuditEvent inserts (interceptor + explicit calls)
- Bulk fast inserts; SQLite handles tens of thousands per second
- Postgres handles equivalent

For high-volume reads (admin running a query over 90 days of events): index on `(TenantId, OccurredAt DESC)` + `(Category, Action)` is critical. Defer materialized views / Postgres partitioning until profiled.

## 8. Open questions

- **External SIEM forwarding**: customers with mature SOC may want events streamed to Splunk / Datadog / Sentinel. Add an outbound webhook/forwarder in a future change.
- **Tamper-evidence**: Merkle-chained audit log so deletion is detectable. Compliance bonus; defer.
- **Log shipping** to an immutable store (S3 Object Lock): defer.
- **Per-event severity / level**: should some events have HIGH severity for alerting? Defer; for now everything is INFO.
- **Audit on read events**: do we audit "User X read instance Y"? For most flows: no (too noisy). For sensitive cases: yes (configurable). Defer; for v1 only audit writes + auth + file downloads.
