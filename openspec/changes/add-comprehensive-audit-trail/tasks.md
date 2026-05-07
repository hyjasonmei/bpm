# Tasks

## 1. Domain + persistence

- [ ] 1.1 Create `bpm-svc/src/Domain/Entities/Audit/AuditCategory.cs` enum
- [ ] 1.2 Create `bpm-svc/src/Domain/Entities/Audit/AuditEvent.cs` entity
- [ ] 1.3 EF config; migration `AddAuditTrail`
- [ ] 1.4 Indexes: (TenantId, OccurredAt DESC), (Category, Action), (ActorUserId, OccurredAt DESC), (TargetType, TargetId)

## 2. Append-only enforcement

- [ ] 2.1 Extend AuditSaveChangesInterceptor (or new `AuditEventAppendOnlyInterceptor`) to reject UPDATE/DELETE on AuditEvent
- [ ] 2.2 Tests: modify-then-SaveChanges → throws

## 3. Audit logger service

- [ ] 3.1 Create `IAuditLogger.cs` with `LogAsync(category, action, actorUserId, targetType, targetId, before?, after?, metadata?, ip?, ua?)`
- [ ] 3.2 Implementation persists AuditEvent
- [ ] 3.3 Tests with various input shapes

## 4. Entity-change auto-capture interceptor

- [ ] 4.1 Create `AuditEventCaptureInterceptor.cs`: on SaveChanges, scan for changed entities of audited types
- [ ] 4.2 Audited types: User, Department, Group, Role, Permission, RolePermission, RoleAssignment, BusinessCalendar, CalendarException, Spec metadata, Delegation, TenantConfig
- [ ] 4.3 For each: build AuditEvent with Category / Action / Before / After (JSON serialized)
- [ ] 4.4 Skip noisy fields (timestamps already captured); skip admin-system actor when actor == system user
- [ ] 4.5 Tests: modify a Role → AuditEvent inserted with Category = Authz, Action = "role.updated"

## 5. Explicit audit calls

- [ ] 5.1 AuthService: login success / failure / logout
- [ ] 5.2 FilesController: read (download); admin delete already audited via interceptor
- [ ] 5.3 HR sync apply: per-row inserts / updates / deactivations summarized as one event with Counts in metadata
- [ ] 5.4 Spec publish: explicit on new version creation
- [ ] 5.5 Delegation create / cancel
- [ ] 5.6 Calendar exception add / remove

## 6. Reader service + endpoints

- [ ] 6.1 Create `IAuditLogReader.cs` with QueryAsync, GetAsync, ExportAsync
- [ ] 6.2 The reader unifies AuditEvent + TaskHistory + ActorResolutionAudits + NotificationDispatchAudits via a presentation layer (adapt to common AuditEvent shape on read)
- [ ] 6.3 `GET /api/audit?category=&action=&actor=&target_type=&target_id=&from=&to=&page=&size=` paginated
- [ ] 6.4 `GET /api/audit/{id}` single event
- [ ] 6.5 `POST /api/audit/export` body `{ filters }` async CSV via background job
- [ ] 6.6 `GET /api/audit/categories` list of valid categories + actions for filter UI

## 7. Privacy + retention

- [ ] 7.1 Daily janitor pass: anonymize ActorIpAddress for events older than 90 days
- [ ] 7.2 Daily janitor pass: purge events past their category's retention (configurable)
- [ ] 7.3 Default retention table; admin override per tenant via TenantConfig
- [ ] 7.4 Tests: insert event, fast-forward 91 days (DB tweak), run janitor → IP anonymized

## 8. Frontend AuditLogViewer

- [ ] 8.1 Update `bpm-ui/src/screens/admin/audit/AuditLogViewer.tsx` to use the unified endpoint
- [ ] 8.2 Filter UI: category dropdown, action autocomplete, actor picker, target type, date range
- [ ] 8.3 CSV export button (calls async export, polls job, downloads when ready)

## 9. End-to-end verification

- [ ] 9.1 `dotnet build` clean
- [ ] 9.2 Apply migration; verify AuditEvents table
- [ ] 9.3 Login as Wilson; logout; verify 2 audit events with Category = Auth
- [ ] 9.4 Admin edits a User's department; verify Org category event with Before/After JSON
- [ ] 9.5 Admin downloads a file; verify File category event
- [ ] 9.6 Run HR sync apply; verify HrSync category event with summary metadata
- [ ] 9.7 Visit /admin/audit; filter by category Auth; see login events
- [ ] 9.8 Export CSV; verify download
- [ ] 9.9 Fast-forward 91 days (DB tweak); run janitor; verify IP anonymized
- [ ] 9.10 **Demo guard**: 9 mock-up forms, Home, Search, Report, lib/workflow.ts NOT modified

## 10. Commit

- [ ] 10.1 Commit in chunks (entity + migration; interceptor + logger; explicit calls; reader + endpoints; janitor; frontend; verification)
- [ ] 10.2 Push via GitKraken
