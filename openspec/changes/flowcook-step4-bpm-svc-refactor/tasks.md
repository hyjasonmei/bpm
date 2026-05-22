# Tasks

## 1. Principal entities in bpm DB

- [ ] 1.1 Mirror Principal seven-table schema in `bpm-svc` (read-only replica of admin's)
- [ ] 1.2 Migration for the seven tables
- [ ] 1.3 EF global filter for `ISoftDeletable`

## 2. ActorResolver migration

- [ ] 2.1 New `PrincipalActorResolver` implementation
- [ ] 2.2 Keep old Persona-based resolver behind a config flag during transition
- [ ] 2.3 Plumb Delegation lookup (post-Principal resolution layer)
- [ ] 2.4 Unit tests with Principal scenarios from `flowcook-principal-model`

## 3. Move admin BE to `bpm-admin-svc`

- [ ] 3.1 Onboarding API controllers → `bpm-admin-svc`
- [ ] 3.2 CoPilot endpoints → `bpm-admin-svc`
- [ ] 3.3 Flow Library / Bundle admin endpoints → `bpm-admin-svc`
- [ ] 3.4 Simulator endpoint → `bpm-admin-svc`
- [ ] 3.5 Delete moved controllers from `bpm-svc`
- [ ] 3.6 Update integration tests accordingly

## 4. Soft-delete entity-wide

- [ ] 4.1 Add `deleted_at` column to all bpm DB entities
- [ ] 4.2 `ISoftDeletable` adoption + global filter
- [ ] 4.3 Migration
- [ ] 4.4 Replace any usage of `IResetService` with soft-delete API (next task)

## 5. Manual soft-delete API + UI integration

- [ ] 5.1 `DELETE /api/process-instances/{id}` (soft) — visible to persona-switch user list only
- [ ] 5.2 Same for tasks and history
- [ ] 5.3 Audit events on each
- [ ] 5.4 Delete `IResetService` integration once tests are green

## 6. PersonaSeedService → Principal seed

- [ ] 6.1 Rewrite seed to populate Principal / Role / Delegation
- [ ] 6.2 SeedCli `clear` drops both DBs
- [ ] 6.3 SeedCli `--org` populates both DBs
- [ ] 6.4 Dev-only guard

## 7. Sandbox runtime adjustments

- [ ] 7.1 Mailbox API: emit redirect outgoing emails instead of storing to mailbox (per `flowcook-sandbox`)
- [ ] 7.2 Remove SMS-capture path
- [ ] 7.3 Disable webhook intercept (sandbox emits real webhooks now)
- [ ] 7.4 Clock decorator stays; verify freeze mode
- [ ] 7.5 Persona switch reads allow list from Site Setting (via syncer; pre-syncer reads from local config)

## 8. Test migration

- [ ] 8.1 Inventory the 313 tests
- [ ] 8.2 Categorize: passes-without-change / refactor / move-to-admin-svc / delete
- [ ] 8.3 Refactor Persona tests to Principal
- [ ] 8.4 Move admin-side tests
- [ ] 8.5 Confirm bpm-svc test count + green
- [ ] 8.6 Confirm bpm-admin-svc test count includes migrated tests

## 9. Audit local writes

- [ ] 9.1 `AuditEvent` entity + migration in bpm DB
- [ ] 9.2 EF interceptor capturing mutating actions
- [ ] 9.3 Append-only enforcement
- [ ] 9.4 Pending `/api/audit/since` endpoint (consumed by syncer in Step 6)
