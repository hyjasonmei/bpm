# bpm-svc — runtime notes

Project-wide conventions live in the root `CLAUDE.md`. This file covers the
process-runtime engine added by `add-process-runtime` (PR-A through PR-E).

## Process Runtime overview

`Bpm.Application.Process.Runtime.IProcessRuntime` (impl in
`Bpm.Persistence.Process.ProcessRuntime`) drives every running case. Five
operations: `StartInstanceAsync`, `SubmitTaskAsync`, `ReturnTaskAsync`,
`ClaimTaskAsync`, `CancelInstanceAsync`. Each runs inside one EF transaction
so partial-progress writes never leak.

### SpecSnapshot — immutable at start

`StartInstanceAsync` calls `ISpecLoader.LoadAsync(specCode)` and serializes
the resolved spec into `ProcessInstance.SpecSnapshot` (raw JSON column).
Every later step on the instance reads through `SpecSnapshot.From(instance)`
— the live spec file can be edited or deleted and **already-running cases
keep their original behaviour**. Spec authors get safe iteration; ops get
deterministic replay. Re-evaluating gateway expressions, looking up node
kinds, walking edges — all go through the snapshot, never through
`ISpecLoader` again.

### Hook invocation order (per task spawn)

For each candidate produced when advancing past a node:

1. `IActorResolver.ResolveAsync(actor, ctx)` expands the spec's `actor`
   block into concrete user ids (initiator/manager/dept-head/role/expr).
2. `IDelegationService.RewriteAsync(originalUserId)` swaps in the
   delegate when one is active. Original + actual ids are both stored
   on the spawned `ProcessTask` so the audit trail survives the rewrite,
   and a `DelegationApplied` history row is written when they differ.
3. `INotificationDispatcher.DispatchAsync(trigger, ctx)` fires for the
   matching trigger (`on_submit` / `on_assign` / `on_approve` /
   `on_complete` / `on_cancel`). v1 dispatcher is the logging stub;
   `add-notification-engine` will swap in real `NotificationDelivery`
   rows. Dispatch happens **inside the same SaveChanges transaction**
   so a notification failure rolls back the state change.

### Gateway evaluation via CelNet

`IExpressionEvaluator` (CelNet 1.0.0 wrapper, `CelNetExpressionEvaluator`)
evaluates edge `condition` strings against a context built from
`{instance, formData, initiator}`. `bpm-cel-v1` subset only — see
`add-cel-expressions` for the validator.

### Append-only TaskHistory

`AuditSaveChangesInterceptor` rejects `EntityState.Modified` or `Deleted`
on any `TaskHistory` entity (throws `InvalidOperationException` from the
SaveChanges pipeline). Replay/audit users can trust the row sequence is
write-once. Tests live in
`tests/Bpm.Tests/Persistence/Interceptors/TaskHistoryAppendOnlyTests.cs`.

### Cursor pagination for history

`GET /api/processes/{id}/history` paginates with a composite cursor
`{CreatedAt}|{Id}` so concurrent inserts at the same timestamp don't
duplicate or skip rows. Default page size 50.

## API surface

- `POST /api/processes` — start an instance from a spec code + form payload
- `GET /api/processes/{id}` — read header + open tasks
- `GET /api/processes/{id}/history` — paginated history (cursor, limit)
- `POST /api/processes/{id}/cancel` — initiator-only cancel
- `GET /api/tasks/mine?status=open|completed|all&limit=N`
- `GET /api/tasks/{id}` — task + merged form snapshot
- `POST /api/tasks/{id}/claim` — atomic Pending→InProgress
- `POST /api/tasks/{id}/submit` — `{ formDataPatch?, decision?, comment? }`
- `POST /api/tasks/{id}/return` — Approval-only, walks back to nearest userTask
