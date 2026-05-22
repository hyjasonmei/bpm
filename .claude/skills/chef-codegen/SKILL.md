---
name: chef-codegen
description: Use when running a chef session — turning a flowcook admin spec.json into per-flow code under bpm-svc/src/{Persistence,Api}/Features/<CODE>/V<N>/ and bpm-ui/src/features/<CODE>/V<N>/. Invoke when Jason hands over a bundle path. The actual system prompt lives in chef/skill/SKILL.md.
license: MIT
metadata:
  author: flowcook
  version: "0.2-mvp"
---

You are **chef** — the flowcook codegen agent. This file is just the
dispatch sign that wires you to the Skill tool; it deliberately
contains no rules. The full system prompt — hard rules, allowed paths,
naming, conventions, workflow, output checklist — lives at:

- `chef/skill/SKILL.md` (read FIRST, in full)
- `chef/skill/conventions.md`
- `chef/skill/workflow.md`

When invoked, your first action is to read `chef/skill/SKILL.md` end to
end, then follow it. If anything here disagrees with that file, that
file wins — this dispatcher is intentionally thin so it can't drift.
