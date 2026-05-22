# flowcook-mvp-chef-bootstrap

## Why

The flowcook pipeline promises **admin authors a spec → chef writes
code → bpm runs it**. Today only the first leg works end-to-end —
admin's AI Kitchen wizard produces a richly-laid-out spec.json
(including the new Tier 1 + Tier 2 layout elements), but there's no
chef yet to consume it.

`flowcook-step7-chef-v0` (the full service version) is a substantial
build — Node project, Claude Code SDK runner, per-customer queue,
on-hold callback, bundle handoff to syncer. We're not ready to commit
to all of that until we've felt the end-to-end loop ourselves.

This proposal does the **bootstrap layer**: chef as a human-driven
Claude session with strict conventions and a checked-in skill, so we
can prove the full loop on one real flow (LEAVE) before we automate
anything. Everything authored here is reusable by step7's service —
the skill IS the system prompt that step7's Claude Code SDK runner
will load.

## What Changes

### `chef/` — docs + skill, no service

- `chef/README.md` — entry doc explaining MVP vs. v0 service
- `chef/skill/SKILL.md` — system-prompt source of truth for chef
  (hard rules, inputs, outputs, reading order, stop-and-ask triggers,
  output checklist)
- `chef/skill/conventions.md` — naming / paths / feature-flag /
  variables / ActorRef / form-layout / tests / commit-shape
- `chef/skill/workflow.md` — step-by-step MVP run (pre-flight, freeze
  spec, worktree, session, generate, report, ship, EF migration)

### `.claude/skills/chef-codegen/SKILL.md` — Claude-loadable skill

- Frontmatter `name: chef-codegen` so the Skill tool loads it on
  demand inside a chef session.
- Body is a dispatch — re-affirms hard guardrails and points at
  `chef/skill/*` for the full content.

### Conventions (mirror `openspec/specs/flowcook-chef`)

The conventions chef follows in MVP are **identical** to the full
service version:

- Writes only inside `bpm-svc/features/<CODE>/V<N>/**` and
  `bpm-ui/src/features/<CODE>/V<N>/**`.
- Every identifier prefixed `<CODE>_V<N>_`.
- One feature flag per version, gating every entry point.
- No hardcoded env values — all via `${var}` from `spec.variables[]`.
- Mandatory tests per artifact (notify / branch / approve+reject /
  integration mock / form layout + validation / e2e).
- Stop-and-ask whenever the spec is ambiguous on something material.

### Worktree convention

- chef runs inside a worktree under `../bpm-chef-worktrees/<FLOWCODE>-v<N>/`
  on branch `chef/<FLOWCODE>-v<N>-<YYYYMMDD-HHMM>`.
- Bundle is unzipped under `~/claude/flowcook-bundles/<FLOWCODE>-v<N>-<YYYYMMDD>/`.
- chef never pushes — Jason pushes via GitKraken after diff review.

### What chef gets (input contract)

- Path to an unzipped bundle directory containing the wizard's
  `spec.json`, `bpmn.xml`, `sampleOrg.json`, `testCases.json`, and an
  optional `notes/` mirror of all chef-readable free-text.

### What chef writes (output contract)

- Per-flow C# code under `bpm-svc/features/<CODE>/V<N>/**`
  (entities, EF migration class — not run, handlers, controller,
  notification templates, tests).
- Per-flow React component under `bpm-ui/src/features/<CODE>/V<N>/**`
  (form component rendering the spec's layout tree, types, route
  registration, tests). **Bespoke per-flow component, NOT a generic
  DynamicForm runtime** — chef writes the JSX the spec describes.
- One commit per logical chunk; final report listing stops, orphan
  fields, and review notes.

## Out of Scope

- **No service.** chef is a Claude session; no daemon, no queue, no
  poller, no auto-pickup. `flowcook-step7-chef-v0` covers the
  service when we're ready.
- **No on-hold callback.** Stop-and-ask is a chat message, not an
  API call. Step 7 formalises it.
- **No PR open / branch push from chef.** Jason handles all SCM I/O.
- **chef does not run EF migrations.** Migration classes are written;
  Jason runs `dotnet ef database update` from main checkout after
  merge. Avoids the chef worktree mutating shared SQLite under `db/`.
- **No multi-customer logic.** v0 service has per-customer serial
  queue; MVP has one human running one session at a time.
- **No `<DynamicForm spec />` runtime.** We considered authoring a
  spec-driven renderer in bpm-ui and decided against — chef writing
  bespoke React per flow is the actual sell point ("AI engineer
  writes you a working app", not "AI engineer writes you a JSON the
  renderer interprets").

## Design Notes

- The skill is **checked into this repo** at `chef/skill/` so it's
  visible in code review and version-controlled alongside the rules
  it enforces. `.claude/skills/chef-codegen/SKILL.md` is the
  Skill-tool entry point that points at it.
- The `Reference_*.tsx` rename in `bpm-ui/src/screens/forms/` (landed
  in Phase A) is part of this design — those files are chef's visual
  reference set, not active production routes.
- The MVP flow proves three things at once: (a) the wizard's
  spec.layout tree is expressive enough for a real form; (b) chef's
  conventions produce code reviewable in GitKraken; (c) the
  feature-flag + per-version folder structure keeps multiple flows
  isolated. If any of those breaks, we update the skill before
  building the service.
- "Manual" doesn't mean "loose" — chef's skill is as strict as the
  service version's system prompt will be. The only thing that's
  manual is the trigger and the SCM I/O.

## References

- `openspec/specs/flowcook-chef` — the canonical chef rules
- `openspec/specs/flowcook-lifecycle` — when chef gets invoked
- `openspec/specs/flowcook-wizard` — what the spec means
  semantically (so chef understands the data it got)
- `openspec/changes/flowcook-step7-chef-v0` — future service version
  that supersedes this MVP
- `chef/README.md` + `chef/skill/*` — the operational artefacts this
  proposal lands
