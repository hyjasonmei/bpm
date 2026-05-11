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

## Auth — JWT + dev-login + org seed (`add-actor-and-org-model`)

The API is gated by JWT bearer (HS256). The mode is selected by
`BPM_AUTH_MODE`:

| Value      | Behaviour                                                        |
|------------|------------------------------------------------------------------|
| `dev`      | JWT validated locally; `/api/dev/login` mints persona JWTs       |
| `prod`     | JWT validated locally; `/api/dev/login` is **not** registered    |
| `disabled` | No auth middleware; all endpoints anonymous (legacy demo bypass) |

Default is `dev` so the wizard's `RoleSwitcher` works zero-click. The signing
secret comes from `BPM_JWT_SECRET` and **must be ≥ 32 bytes** — `Program.cs`
fails fast at startup if it isn't.

### Persona mapping

`appsettings.Development.json` → `Personas` maps the six demo roles onto
seed-fixture user emails (employee/manager/finance/it/hr/admin). The wizard's
`RoleSwitcher` POSTs `/api/dev/login` with a `personaCode`; the
`PersonaLoginService` looks up the seed user and `JwtTokenService` mints a
token with `sub`, `persona_code`, `tenant_id`, `roles`, and `exp` claims.

### Seed fixture

`Bpm.Persistence.Seed.OrgFixture.RunAsync` creates ~10 users, 3 departments
(2-level tree), 2 groups, the system roles (admin/designer/viewer), and the
`RoleAssignment` rows that wire each persona to its role(s). It is
**idempotent** — keyed on `User.Email` uniqueness, so re-running on top of an
existing DB is safe.

When `BPM_SEED_ON_STARTUP=true` (default in dev), the seed runs automatically
after EF migrations apply. Production boots leave it off so fixture data
never lands in a real DB.

### ActorRef + ActorResolver

Spec `approver` / `recipients` fields use a typed discriminated union
(`Bpm.Domain.Spec.ActorRef`) covering six shapes: `expr` / `role` / `group`
/ `user` / `conditional` / `collection` (see `spec_schema.md` §2.10).
`ActorRefValidator` is wired into `SpecImportService` so importing a spec
with an off-whitelist path or malformed conditional fails the import with a
clean error.

`ActorResolver` (used inside `ProcessRuntime` task spawning) walks each
ActorRef into a list of concrete user ids using `IOrgChartReader`. Every
top-level `ResolveAsync` call writes one `ActorResolutionAudit` row with the
input ref, the resolved user ids, and any error kind — the audit table is
the runtime-side source of truth for "why did this approver get picked".

## SeedCli console app (PR-L4)

Located at `src/SeedCli/`. Entry: `dotnet run --project bpm-svc/src/SeedCli -- <command>`.

Common workflows:

- Fresh dev environment with full demo state:
  `dotnet run --project bpm-svc/src/SeedCli -- reset && dotnet run --project bpm-svc/src/SeedCli -- seed --include-bundles`
- After branch switch (idempotent):
  `dotnet run --project bpm-svc/src/SeedCli -- seed`
- Health check:
  `dotnet run --project bpm-svc/src/SeedCli -- status`

The `seed` command extends what `Program.cs` does at startup when
`BPM_SEED_ON_STARTUP=true`. Both call `PersonaSeedService.RunAsync` so the
13-user persona shape (departments, users, roles, role assignments) stays
consistent regardless of how the DB was bootstrapped.

`--include-bundles` builds & installs every `sample_specs/*.json` as a
`SpecBundle` row in `Status = Pending`. **Repro is NOT run automatically**
— it requires sandbox + per-spec test cases that drive the runtime
end-to-end, which is too slow / brittle for SeedCli. Open Flow Library and
click "Repro Check" per bundle to verify, or hit
`POST /api/admin/flow-library/{id}/repro-check` directly.

The CLI reads `bpm-svc/src/Api/appsettings.json` for the SQLite
connection string (so the dev server and the CLI share one DB) and
honours a `--connection "Data Source=..."` override for tests / one-off
inspection. Sample-specs path defaults to `<repo>/sample_specs/` —
override with `--sample-specs <dir>`.

`PersonaSeedService` (in `Bpm.Persistence.Seed`) replaced the older
`OrgFixture`. The 13 users + 6 departments + flow-scoped roles cover every
routing path in the 11 demo flows. Persona email mapping in
`appsettings.Development.json` was migrated from `*@bpm.local` →
`*@acme.test` to match the new fixture.
