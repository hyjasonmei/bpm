# flowcook-step4-bpm-svc-refactor

## Why

`bpm-svc` was built as a single all-in-one BPM service. In flowcook, it becomes the runtime-only half of bpm (with `bpm-ui`). Its admin / onboarding responsibilities move to `bpm-admin-svc`. Its ActorResolver must learn the new Principal model. Every entity needs soft-delete support. SeedCli's PersonaSeedService becomes Principal-aware.

This is the largest mechanical change in the pivot, but the heart of `bpm-svc` — ProcessRuntime, SpecSnapshot, CelNet, Bundle Builder — is preserved verbatim. The legacy 313 tests are not discarded; they are evolved alongside the refactor.

## What Changes

### Move out of `bpm-svc`

- Onboarding controllers and AI CoPilot endpoints → `bpm-admin-svc`
- Process Admin Console BE (admin intervention 4 endpoints, reporting service) → split:
  - Definitions / Designer / Simulator / Flow Library → `bpm-admin-svc`
  - Live Cases / Completed / Reports / 通知 / 介入 → stays in `bpm-svc` (callers move to `bpm-ui` in Step 5)
- Sandbox Mailbox API stays (now only emits redirect, not capture)

### Refactor in `bpm-svc`

- ActorResolver: drop Persona concept (moved to Sandbox); use Principal model directly
- IDelegationService: align with `flowcook-principal-model` (user-to-user, time-window)
- All entities implement `ISoftDeletable` with EF global filter
- PersonaSeedService → Principal-based seed (works against both admin and bpm DBs)
- SeedCli (in `bpm-svc/`) reorganises to: `seed clear` / `seed --org` (matches `bpm-admin-svc` SeedCli design)

### Preserved verbatim

- ProcessRuntime + SpecSnapshot
- CelNet evaluator
- Bundle Builder / Parser / Validator
- RuntimeLoader and tenant isolation
- ReproRunner
- Sandbox runtime hooks (Clock decorator, OutboundGate — now mail-only, persona switch — now from Site Setting)

### 313 existing tests

- Sandbox / persona-related tests refactor to new model
- ActorResolver tests rewritten with Principal scenarios from `flowcook-principal-model`
- Process Admin Console tests split: admin-side go to `bpm-admin-svc.Api.Tests`; bpm-side stay
- The remaining ~200 tests touch unchanged code (Runtime / CelNet / Bundle) and pass without rewrite

## Out of Scope

- bpm-ui UI changes (Step 5)
- syncer integration (Step 6)
- chef bundle ingestion (Step 7)

## Design Notes

- Refactor is incremental within `bpm-svc/`: Principal entities are added new alongside old Persona / Role; ActorResolver gets a new path while old path stays compiling; tests for both paths run side-by-side until old is removed.
- Both DBs (admin + bpm) live behind their own DbContext; the only data still shared in v0 is Principal (admin owns), pushed to bpm by syncer in Step 6. Until syncer exists, SeedCli's `seed --org` writes to both DBs directly.
- Notify dispatcher template loading temporarily reads from spec snapshot in bpm DB; admin-side template editing arrives with syncer in Step 6.

## References

- `openspec/specs/flowcook-principal-model`
- `openspec/specs/flowcook-architecture`
- `openspec/specs/flowcook-sandbox`
- `openspec/specs/flowcook-audit` (bpm emits audit locally; syncer pulls later)
- `openspec/specs/bpm-process-runtime` (preserved)
- `openspec/specs/bpm-cel-expressions` (preserved)
