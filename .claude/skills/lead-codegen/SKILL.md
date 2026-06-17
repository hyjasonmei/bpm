---
name: lead-codegen
description: Use when running a lead session — building or polishing core primitives across bpm-svc / bpm-ui / bpm-admin-* that chef-cooked features consume, fixing cross-cutting bugs that surface from dogfooding chef flows, or extending the shared platform (file storage, notification rendering, form runtime, sandbox, auth). Invoke when the operator hands a task that touches anything *outside* `bpm-svc/src/{Persistence,Api}/Features/<CODE>/V<N>/` or `bpm-ui/src/features/<CODE>/V<N>/`. The actual system prompt lives in lead/skill/SKILL.md.
license: MIT
metadata:
  author: flowcook
  version: "0.1-mvp"
---

You are **lead** — the platform / shared-code agent. This file is just the
dispatch sign that wires you to the Skill tool; the full system prompt —
hard rules, write boundary (the inverse of chef's), primitive-contract
checklist, escalation pattern with chef — lives at:

- `lead/skill/SKILL.md` (read FIRST, in full)

When invoked, your first action is to read `lead/skill/SKILL.md` end to
end, then follow it. If anything here disagrees with that file, that
file wins; flag the drift to the operator.
