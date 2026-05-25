# openspec

Specs were wiped in commit `6a063b0 clean docs`. `changes/` and
`specs/` are empty by intent.

Canonical architecture contracts now live in:

- [`../chef/skill/SKILL.md`](../chef/skill/SKILL.md) +
  [`../chef/skill/conventions.md`](../chef/skill/conventions.md) —
  per-flow codegen contract (model B), naming, primitive table
- [`../lead/skill/SKILL.md`](../lead/skill/SKILL.md) — shared-platform /
  primitive contract, chef ↔ lead boundary
- [`../CLAUDE.md`](../CLAUDE.md) — product context, five-project
  architecture, Clean Architecture five-layer convention for both
  backends, SharedIdentity model, DB conventions, current state

New openspec proposals will land here when work that warrants the
RFC ceremony resumes. Use the openspec skills (`/openspec-propose`,
`/openspec-apply-change`, `/openspec-archive-change`) at that point.

`config.yaml` is kept so the openspec tooling still recognises the
folder.
