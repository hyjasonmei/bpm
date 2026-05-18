# flowcook-step7-chef-v0

## Why

After Step 6, syncer can move data both ways but specs in `submitted` state still sit unprocessed. chef is the AI pipeline that turns a spec into runnable code, completing the **Milestone B / Cutover** end-to-end: admin authoring → submit → chef cooks → syncer pushes bundle → bpm runs → audit returns to admin.

## What Changes

### `chef/` — new service

- Node project (per design recommendation; alternative .NET if Jason picks)
- Pulls `submitted` flows from a per-customer queue (one admin endpoint exposes this)
- Invokes Claude Code SDK with the chef skill as system prompt
- Emits bundle output to syncer for delivery to bpm

### chef skill

- `chef/skill/skill.md` — the canonical system prompt enforcing all rules in `openspec/specs/flowcook-chef`
- `chef/skill/naming.md` — naming convention reference
- `chef/skill/forbidden-paths.md` — explicit list of paths chef cannot modify

### On-hold callback

- chef calls `bpm-admin-svc /api/flows/{id}/on-hold` (Step 3 already implements receiver)
- Resume path already in place (admin Submit → submitted → chef re-pickup)

### Output: bundle (no PR in v0)

- Single bundle artifact per flow version containing all generated code, tests, flag config, spec snapshot
- Bundle handed to syncer which POSTs to bpm
- Per-customer serial queue prevents concurrent cooking on the same customer

## Out of Scope

- PR mode for tech-tier customers (v1+)
- Internal Dev / Review / E2E sub-agent decomposition (chef is single-pass in v0)
- Iteration cap / cost cap
- Multi-customer parallelism beyond the per-customer-serial default

## Design Notes

- chef has no business state; it reads from admin's queue and writes to syncer.
- Skill content is also tracked in this repo (`chef/skill/`), making the system prompt visible in code review.
- On v0, chef pushes bundle via syncer (Step 5); chef itself does not call bpm directly.
- chef writes `source_system = chef` audit events directly to admin (not via syncer) since admin owns the queue.

## References

- `openspec/specs/flowcook-chef`
- `openspec/specs/flowcook-lifecycle`
- `openspec/specs/flowcook-wizard` (consumes spec output)
- `openspec/changes/flowcook-step3-ai-kitchen-wizard` (lifecycle backend dependency)
- `openspec/changes/flowcook-step6-syncer-v0` (delivery dependency)
- `.docs/flowcook-doc/2026-05-17-chef-design.md` (pre-openspec design notes; flowcook-doc README will point here)
