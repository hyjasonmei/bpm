# Tasks

## 1. Service skeleton

- [ ] 1.1 `syncer/` .NET worker service project + solution
- [ ] 1.2 Config schema: per-customer endpoints + shared secret
- [ ] 1.3 CI build / test wiring

## 2. Push: Principal / Role / Delegation

- [ ] 2.1 admin emits change events to a `pending_push` table
- [ ] 2.2 syncer polls and POSTs to bpm `/api/principal-sync/apply`
- [ ] 2.3 bpm applies upserts / soft-deletes idempotently
- [ ] 2.4 Audit event on each successful push batch

## 3. Pull: audit log

- [ ] 3.1 bpm exposes `GET /api/audit/since?cursor=&limit=`
- [ ] 3.2 syncer reads cursor from admin, fetches batch, writes to admin AuditEvent table
- [ ] 3.3 admin dedupes by `event_id`
- [ ] 3.4 Default 5-minute interval; configurable

## 4. Push: variable values

- [ ] 4.1 admin emits change events to `pending_push` table on variable value update
- [ ] 4.2 syncer POSTs to bpm `/api/variables/apply`
- [ ] 4.3 bpm updates per-tenant variable values (no re-cook)
- [ ] 4.4 Audit event on success

## 5. Push: spec bundle (chef-produced, Step 7 dependency)

- [ ] 5.1 syncer accepts bundle handoff from chef
- [ ] 5.2 POST bundle to bpm `/api/specs/apply` (with version + flag info)
- [ ] 5.3 bpm validates + persists
- [ ] 5.4 On success: admin lifecycle `committed` (no state regression)

## 6. Push: bpm-affecting Site Setting subset

- [ ] 6.1 Site Setting keys that affect bpm tagged with `target=bpm`
- [ ] 6.2 syncer pushes those keys on update
- [ ] 6.3 bpm stores in `BpmSettings` table

## 7. Auth + secret rotation

- [ ] 7.1 Shared-secret header validation in both directions
- [ ] 7.2 Config reload on secret change without service restart

## 8. Conflict policy enforcement

- [ ] 8.1 Principal admin-wins resolver
- [ ] 8.2 Process bpm-wins resolver
- [ ] 8.3 Audit `sync_conflict_resolved` events

## 9. Failure handling

- [ ] 9.1 Exponential backoff per channel
- [ ] 9.2 `sync_failure` audit on persistent failure
- [ ] 9.3 No crash / process exit on transient errors

## 10. Tests

- [ ] 10.1 Unit: dedupe, cursor, backoff
- [ ] 10.2 Integration (TestServers for admin + bpm + syncer): push principal end-to-end, pull audit end-to-end
- [ ] 10.3 Failure injection: bpm 500, admin 500, network timeout
