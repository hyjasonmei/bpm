# flowcook-step6-syncer-v0

## Why

After Step 4 / Step 5, admin and bpm each run with their own DB and APIs. They need an explicit bridge to move design-time data down (Principal, spec bundles, variable values, bpm-affecting site settings) and operational data up (audit log, process telemetry). Without syncer, customer admins on admin would not see what bpm produces and vice-versa.

syncer also implements graceful degradation: when admin is offline, bpm keeps running with last-synced state.

## What Changes

### `syncer/` — new service

- .NET Hosted Service (background worker), independent project
- Two-direction sync engine with batched runs
- Per-customer shared-secret auth
- Dedupe by `event_id`; at-least-once semantics

### Push direction (admin → bpm)

- Principal / Role / Delegation deltas
- Spec bundles emitted on lifecycle `cooking → committed` (Step 7 produces them, syncer transports them)
- Variable value updates (fast-path, see `flowcook-syncer`)
- bpm-affecting Site Setting subset (notification defaults / timezone / language / branding)

### Pull direction (bpm → admin)

- Audit log via `GET /api/audit/since?cursor=...` every 5 minutes
- Process telemetry summary (counts of running / completed / sandbox-mode flows) — used for admin Audit page filters and basic dashboards

### Authentication

- v0: per-customer shared secret in both admin and bpm config
- Rotated manually on contract events

### Conflict policy

- Principal / Role / Delegation: admin wins (designed source)
- Process / task / history: bpm wins (runtime source); admin never edits these

## Out of Scope

- Customer IdP integration (Entra / AD / HRIS) — captured as the existing yellow proposals `add-mcp-entra-sync` and `add-hr-sync-csv`, to be implemented after v0
- mTLS / OAuth client credentials (v0 uses shared secret)
- Real-time / push-based sync (v0 is poll-based)

## Design Notes

- syncer holds no business state itself; it is a relay with retry queues and offset cursors per customer.
- Failure handling: exponential backoff per channel; on persistent failure, audit (`source_system = syncer`, `action_type = sync_failure`) records the issue without crashing.
- syncer respects `bpm-svc` rate limits if necessary; default poll is 5min for audit and on-change for principal pushes.

## References

- `openspec/specs/flowcook-syncer`
- `openspec/specs/flowcook-audit`
- `openspec/specs/flowcook-principal-model`
- `openspec/changes/add-mcp-entra-sync` (yellow — future IdP integration)
- `openspec/changes/add-hr-sync-csv` (yellow — future)
