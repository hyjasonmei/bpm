# Tasks

## 1. Schema changes

- [ ] 1.1 Migration `AddSoftDeleteColumns` adds DeletedAt + DeletedByUserId to: Department, Group, Role, Permission, RolePermission, RoleAssignment, BusinessCalendar, CalendarException, WebhookSubscription, SsoConfiguration, EntraSyncConfiguration
- [ ] 1.2 Filtered indexes on DeletedAt IS NULL where useful

## 2. EF query filters

- [ ] 2.1 Update `OnModelCreating` for each affected entity: `.HasQueryFilter(e => e.DeletedAt == null)`
- [ ] 2.2 Audit existing queries; identify any that should explicitly IgnoreQueryFilters

## 3. Service / API updates

- [ ] 3.1 Department / Group / Role / Permission delete handlers: soft-delete + audit
- [ ] 3.2 Block delete when dependent rows exist (Department members, Role assignments, etc.)
- [ ] 3.3 Add per-entity restore endpoints: `POST /api/admin/{entity}/{id}/restore`
- [ ] 3.4 Audit `<entity>.deleted` + `<entity>.restored`

## 4. Admin UI

- [ ] 4.1 Each admin list page gains "顯示已刪除" toggle
- [ ] 4.2 Deleted rows shown with strikethrough + Restore button
- [ ] 4.3 Across the app, references to deleted entities show with reduced opacity + tag

## 5. End-to-end verification

- [ ] 5.1 Apply migration; verify DeletedAt columns exist
- [ ] 5.2 Soft-delete a Department; verify it disappears from default user list; appears with strikethrough in admin "show deleted"
- [ ] 5.3 Restore; verify it returns
- [ ] 5.4 Try delete with dependents; verify block + clear error
- [ ] 5.5 Verify a completed ProcessInstance whose initiator's department is now deleted still loads correctly with the deleted-dept tag visible
- [ ] 5.6 **Demo guard**: 9 mock-up forms NOT modified

## 6. Commit

- [ ] 6.1 Commit in chunks
- [ ] 6.2 Push via GitKraken
