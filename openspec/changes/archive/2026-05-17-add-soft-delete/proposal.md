## Why

Soft-delete is partial today:

- `User.IsActive = false` covers user deactivation (HR sync sets it on missing rows)
- File storage's `Status = Deleted` covers files
- Comment soft-delete via `DeletedAt`

But several entities can be hard-deleted today:

- `Department` — hard delete via cascade if all members reassigned
- `Group` — same
- `BusinessCalendar` — has Status = Archived but admin can hard delete in some paths
- `Role`, `Permission`, `RoleAssignment` — hard delete on revoke
- `WebhookSubscription`, `SsoConfiguration`, `EntraSyncConfiguration` — hard delete

Hard delete breaks audit and historical queries:

- "Who was assigned to this completed instance's task X?" — assignee was a user in dept D; D was deleted; lookup fails
- "Show me all role grants in 2026 Q1" — some role revocations show as missing rows because the Role was hard-deleted
- "Why did this notification fire?" — subscription deleted means dispatcher audit can't link back

This change makes soft-delete uniform: every entity that has historical references gets a `DeletedAt` (or `IsActive` / `Status` semantics consistent across the codebase). Hard deletes are limited to: AuditEvent retention purges, file binary purges, ImportRun cleanup of incomplete drafts.

## What Changes

### Soft-delete capability (NEW `bpm-soft-delete`)

**Convention** — every entity that may be referenced historically SHALL be soft-deletable:

- Add `DeletedAt` (DateTime?, nullable) — null = active; non-null = deleted
- Add `DeletedByUserId` (Guid?, nullable) — who soft-deleted

EF Core query filter: by default exclude `DeletedAt IS NOT NULL`. Override with explicit `IgnoreQueryFilters()` for admin / audit views.

### Affected entities

Add DeletedAt + DeletedByUserId to:

- Department
- Group
- Role
- Permission
- RolePermission
- RoleAssignment
- BusinessCalendar
- CalendarException
- WebhookSubscription
- SsoConfiguration
- EntraSyncConfiguration
- Notification (the spec definition; not deliveries)

Already soft-deletable (no change):

- User (IsActive)
- StoredFile (Status)
- Comment (DeletedAt)

### Cascade strategies

When User soft-deleted: leave references intact (org chart history queryable; historical task assignees resolvable).

When Department soft-deleted: members' department_id remains pointing at the soft-deleted dept; admin must explicitly reassign to active dept (UI nudges); soft-deleted dept's `head_user_id` references continue; audit queries find historical state.

When Role soft-deleted: existing RoleAssignments stay; UI shows the role with "已刪除" tag; permissions check ignores soft-deleted roles for active authorization but historical audit shows them.

### Restore capability

Admin can `POST /api/admin/{entity}/{id}/restore` — clears DeletedAt + DeletedByUserId; idempotent.

### UI behavior

- Default lists exclude soft-deleted
- Admin pages have a "顯示已刪除" toggle showing soft-deleted with strikethrough + restore button
- Soft-deleted entities show with reduced opacity / "deleted" badge wherever they appear historically (e.g., a completed case's assignee was in a now-deleted department)

### Out of scope (future changes)

- Tombstone tables (separate archive table for very-long-term storage)
- GDPR right-to-be-forgotten (would require pseudo-anonymization of references rather than full delete)
- Time-bounded soft-delete (auto-purge after N years) — depends on customer retention policy
- Cascading soft-delete (e.g., delete a Department auto soft-deletes its child departments)
- Recovery audit (record who restored when) — already covered by AuditEvent capture interceptor

## Capabilities

### New Capabilities

- `bpm-soft-delete` — uniform DeletedAt / DeletedByUserId convention, EF query filters, restore endpoint pattern, UI conventions for showing/hiding deleted entities, audit on delete + restore.

### Modified Capabilities

- `bpm-org-model` — Department / Group rows acquire DeletedAt fields; existing User.IsActive convention preserved.

## Impact

- Migration `AddSoftDeleteColumns` adds DeletedAt / DeletedByUserId to ~12 tables
- EF configurations: HasQueryFilter on each affected entity excluding soft-deleted by default
- Admin endpoints: per-entity DELETE handlers updated to soft-delete (set DeletedAt) instead of hard-delete; per-entity restore endpoints added
- UI: admin lists gain "Show deleted" toggle; deleted indicator across the app
- AuditEventCaptureInterceptor: emit `<entity>.deleted` and `<entity>.restored` events
- No new NuGet
- Demo guard: 9 mock-up forms NOT modified
