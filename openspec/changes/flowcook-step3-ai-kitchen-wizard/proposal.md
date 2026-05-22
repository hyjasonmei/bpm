# flowcook-step3-ai-kitchen-wizard

## Why

The flowcook brand promise is "客戶把 flow spec 當原料給我們，AI chef 把它炒成可運轉的菜". AI Kitchen is the kitchen — the eleven-step wizard where customer admins (or flowcook internal) author the spec. Without it, chef has nothing to cook and bpm has nothing new to run.

Step 3 is the first **Milestone A** — once it lands, flowcook can demo the entire authoring path and produce a complete spec JSON, even though chef / bpm / syncer are not yet in place.

## What Changes

### `bpm-admin-ui/` — AI Kitchen page implementation

- Eleven-step wizard inside the AI Kitchen page (already nav-stubbed in Step 2)
- Step order: SOURCE / TRIGGER & ACCESS / VARIABLES / FORMS / DECISIONS / APPROVERS / NOTIFY / INTEGRATIONS / SLA / TRANSLATION / NOTES (per `flowcook-wizard`)
- Per-step validator gate
- Submit button on step 11 triggering `draft → submitted` lifecycle transition
- Cancel + version overlay for `approved` flows (new draft inherits prior spec)

### `bpm-admin-svc/` — flow lifecycle backend

- `FlowDraft` / `FlowVersion` entities with `lineage_id`, `version`, `state` (per `flowcook-lifecycle`)
- Spec persistence (JSON column) + audit on every state transition
- Flow lifecycle controllers: list flows / submit / cancel / resume
- Variables table (flow-scoped values storage) — declarations live on spec, values become bpm-side later in Step 6
- No chef interaction yet: submit moves to `submitted` and the flow stays there pending Step 7

### Sandbox cross-link

- The wizard's "Test in Sandbox" link from any draft pre-loads the Sandbox page with the current draft (cross-page state)
- Sandbox UI itself does not yet execute against bpm (Step 4); the link just teaches the UX flow

## Milestone A — Spec JSON Demo

After Step 3 lands:

- An admin can walk a customer through all 11 steps and produce a valid spec JSON
- The submitted flow sits at `submitted` waiting for chef
- No bpm runtime needed yet; this is purely design-time

## Out of Scope

- chef execution (Step 7)
- bpm runtime that consumes the spec (Step 4)
- Variable runtime resolver (Step 4)
- syncer pushing variables / specs (Step 6)
- Sandbox actually running flows (Step 4 + 6)

## Design Notes

- The 9-step legacy wizard had 5 placeholder steps. Step 3 implements all 11 steps from scratch using the new Principal model for APPROVERS / TRIGGER & ACCESS.
- TRIGGER & ACCESS v1 is restricted to single form trigger; schema is `triggers[]` for future expansion.
- VARIABLES step's UI must mask sensitive values; storage is plain text in admin DB (v0 simplicity).
- INTEGRATIONS step parses an uploaded OpenAPI spec; the editor lists endpoints + maps fields + sets auth (with sensitive-value masking).
- TRANSLATION step's "AI fill empties" calls Anthropic API; the API key lives in Site Setting.

## References

- `openspec/specs/flowcook-wizard`
- `openspec/specs/flowcook-lifecycle`
- `openspec/specs/flowcook-principal-model`
- `openspec/changes/flowcook-step1-admin-svc-skeleton`
- `openspec/changes/flowcook-step2-admin-ui-skeleton`
