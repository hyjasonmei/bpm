# flowcook-step1-admin-svc-skeleton

## Why

flowcook pivot turns the legacy single-service BPM platform into a four-service architecture (see `openspec/specs/flowcook-architecture`). The new `admin` service has no backend yet — `bpm-admin-ui` historically calls `bpm-svc`. Without a dedicated `bpm-admin-svc`, the Principal model, Audit aggregation, lifecycle state, and customer-side onboarding sequence (AI Kitchen) have nowhere to live.

Step 1 bootstraps `bpm-admin-svc` with the minimum surface area required to unblock Step 2 (admin FE five-page skeleton) and Step 3 (AI Kitchen wizard): Clean Architecture skeleton, Principal seven-table model, User & Role REST API, SeedCli for dev / demo data, and username / password authentication.

## What Changes

### `bpm-admin-svc/` — new .NET project

- New solution `bpm-admin-svc.sln` independent of `bpm-svc.sln`
- Clean Architecture layout: `Bpm.Admin.Api / Application / Domain / Persistence / SeedCli`
- xUnit test projects per src project
- SQLite (Postgres-ready via EF Core conventions in CLAUDE.md)

### Principal model (per `flowcook-principal-model`)

- EF entities: Principal / UserDept / DeptParent / GroupMember / Role / PrincipalRole / Delegation
- `ISoftDeletable` interface + EF global filter
- `EffectiveRoleResolver` (query-time; materialized view deferred)

### SeedCli

- `seed clear` — drop + recreate admin DB (and bpm DB once Step 4 lands)
- `seed --org` — populate ~13 user, ~6 dept, 1+ group, ~14 role, sample assignments and one delegation
- Dev-only guard refusing to run with `ASPNETCORE_ENVIRONMENT=Production`

### REST API

- Principal CRUD + UserDept / DeptParent / GroupMember endpoints
- Role / PrincipalRole / Delegation endpoints
- `GET /api/principals/{userId}/effective-roles`
- All mutating endpoints emit audit events (per `flowcook-audit`)

### Authentication

- `UserCredential` (password hash) + `UserSession` (cookie session)
- POST `/api/auth/login` / `/api/auth/logout`
- Cookie middleware on protected endpoints
- SeedCli sets a default password for demo users

## Out of Scope

- AI Kitchen wizard backend logic (Step 3)
- bpm DB schema migration / soft-delete (Step 4)
- syncer integration (Step 6)
- SSO / external IdP (deferred to v1+)
- Materialized view for effective roles (added when perf demands)

## Design Notes

- `bpm-admin-svc` solution sits at monorepo root `bpm-admin-svc/`; entirely independent of `bpm-svc/`.
- Two-DB world: this change creates and migrates admin DB; bpm DB is left alone. SeedCli's `clear` is responsible for both, but Step 1 only exercises the admin half until Step 4.
- Audit writes go to admin DB locally; syncer (Step 6) eventually replicates from bpm to admin, but admin's own audit events live here from day one.
- Variables and lifecycle state models live in admin DB but their schemas are added in Step 3 (when AI Kitchen actually produces specs); Step 1 only ships Principal-related tables plus auth.

## References

- `openspec/specs/flowcook-architecture`
- `openspec/specs/flowcook-principal-model`
- `openspec/specs/flowcook-audit`
- `.docs/flowcook-doc/2026-05-17-step1-bpm-admin-svc-skeleton.md` (pre-openspec design notes; flowcook-doc README will point here)
