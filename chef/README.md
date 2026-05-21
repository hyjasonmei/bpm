# chef — the flowcook codegen pipeline

chef is the agent that turns a frozen `spec.json` (produced by admin's
AI Kitchen wizard) into per-flow runtime code under `bpm-svc/` and
`bpm-ui/`. It is the engine behind the `cooking` lifecycle state.

## v0 (MVP) — chef is a manual Claude session

The first cut of chef is **not a service**. Operating chef in v0 means:

1. Jason downloads a bundle `.zip` from admin → unzips somewhere local.
2. Jason creates an isolated worktree (`git worktree add`) on a fresh
   `chef/<FLOWCODE>-v<n>-<ts>` branch.
3. Jason starts a Claude Code session inside that worktree, invokes the
   `chef-codegen` skill, and hands chef the bundle path.
4. chef reads the skill + the bundle + the relevant repo references,
   then writes:
   - `bpm-svc/features/<CODE>/V<N>/**` — handlers, controllers, DTOs,
     EF migrations, tests
   - `bpm-ui/src/features/<CODE>/V<N>/**` — React component + tests
5. chef commits to the worktree branch.
6. Jason reviews the diff in GitKraken, runs `tsc` / `dotnet test`
   himself if needed, pushes the branch when satisfied.

No queue, no auto-pickup, no service deployment — just a disciplined
human-in-the-loop run that uses the same conventions a future service
will follow. v1 (queue puller + Claude Code SDK runner) lives in
`openspec/changes/flowcook-step7-chef-v0`; this MVP is its precursor.

## Layout

```
chef/
├── README.md           ← this file
└── skill/
    ├── SKILL.md        ← the "system prompt source of truth" per
    │                     flowcook-chef §5. Read this first.
    ├── conventions.md  ← naming, paths, feature flag, variables
    └── workflow.md     ← step-by-step MVP run
```

The skill is also mirrored at `.claude/skills/chef-codegen/SKILL.md`
so the Claude Code Skill tool can load it by name (`/chef-codegen`)
when Jason is inside a chef session.

## Boundary

chef writes only inside:

- `bpm-svc/features/<CODE>/V<N>/**`
- `bpm-ui/src/features/<CODE>/V<N>/**`

chef reads but never modifies:

- `bpm-svc/{Core,Runtime,Bundle,Principal,Sandbox,Application,Api}/**`
- `bpm-admin-svc/**`
- `bpm-admin-ui/**`
- `syncer/**`
- `chef/**` itself

Anything outside the allowed-write set must be flagged to Jason — chef
never silently expands its remit.

## References

- `openspec/specs/flowcook-chef` — the canonical rule set
- `openspec/specs/flowcook-lifecycle` — when chef gets invoked
- `openspec/changes/flowcook-mvp-chef-bootstrap` — this MVP plan
- `openspec/changes/flowcook-step7-chef-v0` — the future service version
