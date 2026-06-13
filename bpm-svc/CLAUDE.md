# bpm-svc

Customer-facing BPM runtime — .NET 10 Clean Architecture, **same five
layers as `bpm-admin-svc`**:

| Layer | csproj | Purpose |
|---|---|---|
| Api | `src/Api` | Controllers + DTOs |
| Application | `src/Application` | Business logic — services, handlers, state machines, notification templates, inbox providers |
| Domain | `src/Domain` | Types — entities, value objects, enums; no deps |
| Persistence | `src/Persistence` | EF Core — `AppDbContext`, EF configurations, migrations, EF-bound infrastructure |
| SeedCli | `src/SeedCli` | demo data seeder |

Plus `src/Functions` for non-HTTP background work (cron / queue
consumers).

Storage: EF Core + SQLite (POC); code keeps the Postgres path open
(see root `CLAUDE.md` DB conventions).

## Ownership inside this tree

Per-flow code is sharded by `Features/<CODE>/V<N>/` sub-folders
inside every layer that needs to participate. **Each layer keeps its
Clean-Arch responsibility — entities don't drop into Persistence,
business logic doesn't drop into Api.**

| Path | Owner | Holds |
|---|---|---|
| `src/Domain/Features/<CODE>/V<N>/**` | **chef** | `<CODE>_V<N>_Case` entity, `<CODE>_V<N>_CaseStatus` enum, value objects |
| `src/Application/Features/<CODE>/V<N>/**` | **chef** | state machine service (`<CODE>_V<N>_LeaveService`), notification templates, `ITypedInboxProvider` impl, actor-resolution helpers |
| `src/Persistence/Features/<CODE>/V<N>/**` | **chef** | EF mapping only (`<CODE>_V<N>_CaseConfiguration`) |
| `src/Persistence/Migrations/<ts>_<CODE>_V<N>_*.cs` | **chef** | `dotnet ef migrations add` output (+ `AppDbContextModelSnapshot.cs` regenerated) |
| `src/Api/Features/<CODE>/V<N>/**` | **chef** | controller + DTOs |
| `tests/Bpm.Tests/Features/<CODE>/V<N>/**` | **chef** | per-flow unit + integration |
| Everything else under `src/{Api,Application,Domain,Persistence,Functions,SeedCli}/**` | **lead** | Shared platform — `AppDbContext`, SharedIdentity DbSets, auth, sandbox, unified inbox plumbing, bundle install, REST scaffolding, primitives |

As of 2026-06-13 **main carries 10 chef-cooked flows**, all conforming
to the Clean-Arch split above: APE, EOB, ETM, FAD, FAP,
PURCHASE_REQUEST, TEO, TRQ, VENDOR_EXPENSE, and LEAVE (the reference
cook, aligned in-place from its old Persistence-only shape). Each lives
across `Domain/Features/<CODE>/V1/` (entity + enum),
`Application/Features/<CODE>/V1/` (service + inbox + templates +
`I<CODE>_V1_CaseStore`), and `Persistence/Features/<CODE>/V1/` (EF
config + `<CODE>_V1_CaseStore` impl). Copy any of them for shape.

## SharedIdentity (read-only)

bpm-svc no longer owns user / principal / role tables (U2 finale
dropped them). `AppDbContext` exposes SharedX DbSets —
`SharedPrincipal`, `SharedUserManager`, `SharedUserDept`,
`SharedDeptHead`, `SharedRole`, `SharedPrincipalRole` — all flagged
`ExcludeFromMigrations`. EF migrations for identity tables ship from
`bpm-admin-svc`; bpm-svc reads against the same DB file.

Chef features resolve actors (`submitter.manager`,
`submitter.department.head`, `role:VP`, etc.) through these DbSets
from the **Application** layer service. See
`chef/skill/conventions.md` § "Actor resolution helpers" for the
table mapping.

## Inbox provider DI scan (resolved)

`ITypedInboxProvider` impls are auto-registered by **two** scans:
`src/Application/DependencyInjection.cs` scans the Application
assembly (where Clean-Arch cooks put their provider — this is the
path chef uses) and `src/Persistence/DependencyInjection.cs` keeps a
legacy scan of the Persistence assembly. Both are additive; chef
drops `<CODE>_V<N>_InboxProvider.cs` into
`Application/Features/<CODE>/V<N>/` and it's picked up automatically —
no DI edit needed.

## Model A is retired

`Bpm.Application.Process.Runtime.IProcessRuntime`,
`Bpm.Persistence.Process.ProcessRuntime`, `ISpecLoader`,
`SpecSnapshot`, the generic `/api/processes` + `/api/tasks` REST
surface, `ActorResolver`, `CelNetExpressionEvaluator`, and the
generic `INotificationDispatcher` stack are the old "spec-driven
runtime" path. **Compiles, not extended.** Cleanup is separate work.

New flows ship via chef (model B): one bespoke state machine per
flow under `Application/Features/<CODE>/V<N>/`, exposed at
`/api/<flow>/v<n>/...`.

## Conventions

- Root [`../CLAUDE.md`](../CLAUDE.md) — product context, 5-project
  architecture, Clean Architecture five-layer convention, 7 DB rules
- [`../chef/skill/SKILL.md`](../chef/skill/SKILL.md) +
  [`../chef/skill/conventions.md`](../chef/skill/conventions.md) —
  per-flow folder shape, naming, primitive table, test patterns —
  conventions.md + the path table teach the Clean-Arch split above;
  ⚠️ SKILL.md's LEAVE *worked-example* sections still show the old
  `Persistence/Features/`-only shape and want syncing
- [`../lead/skill/SKILL.md`](../lead/skill/SKILL.md) — what lead may
  edit here vs. what chef owns
- [`../README.md`](../README.md) — run / seed / test / migrate
