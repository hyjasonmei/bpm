# flowcook-step5-bpm-ui-evolution

## Why

bpm-side UI must be self-sufficient: end users fill forms, supervisors inspect live cases, admins (privileged ones) intervene and soft-delete — all without admin connectivity. After Step 4 moves admin-side BE out of `bpm-svc`, this step does the same on the UI: `bpm-ui` absorbs the ops 4 sections that previously lived in `bpm-admin-ui`'s Process Admin Console.

This is also when DynamicForm migration finally happens (legacy `add-form-runtime-rendering` proposal, realigned to flowcook).

## What Changes

### `bpm-ui/` — absorb ops 4 sections

- New routes / pages: Live Cases / Completed / Reports / Notifications / 介入
- Move React components from `bpm-admin-ui/screens/admin/*` to `bpm-ui/screens/ops/*`
- Reuse `bpm-svc` runtime APIs (already in place from Step 4)
- Authorize via Principal effective roles (admin-class users see ops; regular users see only inbox)

### Soft-delete UI

- "Delete" button on Live Cases / Completed / Tasks for persona-switch allow-listed users only
- Confirm modal + reason input
- Optimistic update → API call → toast on confirm

### DynamicForm migration (Phase 2)

- New `<DynamicForm>` component reading spec userTask fields and rendering generically
- Migrate 11 hand-coded form components to use it
- `lib/workflow.ts` retains only FORMS map (label / step list config)
- Rule-based renderer registry per field type (text / number / date / file / repeater / etc.)

### Sandbox banner consolidation

- SandboxBanner already exists; verify it shows redirect target + freeze time when sandbox mode is active

### `bpm-admin-ui` deprecation cleanup

- Remove the legacy Process Admin Console pages from `bpm-admin-ui` (they've been gated behind a flag since Step 2)
- Update README / CLAUDE.md to remove references

## Out of Scope

- New report types beyond what existed (this is migration, not feature expansion)
- syncer ↔ admin Audit page integration (Step 6)
- chef-produced new form types (Step 7)

## Design Notes

- The 11 hand-coded forms migrate one by one with the new DynamicForm; each form is its own PR.
- Reports remain in-memory percentile calc for now (DB function migration is a future spec) — the migration faithfully ports current behavior.
- All `bpm-ui` ops actions emit audit events into bpm DB (per `flowcook-audit`); syncer (Step 6) pulls them to admin.

## References

- `openspec/specs/flowcook-architecture` (option B: ops in bpm)
- `openspec/specs/flowcook-audit`
- `openspec/specs/flowcook-sandbox`
- `openspec/changes/add-form-runtime-rendering` (the realigned proposal whose work is consolidated here)
- `openspec/changes/flowcook-step4-bpm-svc-refactor` (BE that this depends on)
