# flowcook-doc — Brainstorm History

The canonical source of truth for flowcook design now lives in **`openspec/`**. This folder retains the early brainstorm notes for historical reference. Going forward, all new design work and implementation tasks live in openspec.

---

## Source of truth (openspec)

### Specs (`openspec/specs/`)

| Spec | Topic |
|---|---|
| `flowcook-architecture` | Four-service architecture + monorepo layout + business model |
| `flowcook-principal-model` | Principal (user/dept/group) + Role + Delegation seven-table model |
| `flowcook-lifecycle` | 7-state lifecycle of flow designs (draft → approved) |
| `flowcook-wizard` | 11-step AI Kitchen wizard |
| `flowcook-sandbox` | Sandbox three controls + soft-delete |
| `flowcook-audit` | 7-column audit schema, append-only, syncer batch 5 min |
| `flowcook-chef` | chef AI pipeline + skill |
| `flowcook-syncer` | admin ↔ bpm bridge with at-least-once + dedupe |

### Changes (implementation plan, `openspec/changes/`)

| Change | Milestone |
|---|---|
| `flowcook-step1-admin-svc-skeleton` | bpm-admin-svc + Principal + SeedCli + auth |
| `flowcook-step2-admin-ui-skeleton` | bpm-admin-ui five-page reorg |
| `flowcook-step3-ai-kitchen-wizard` | 11-step wizard 🎯 **Milestone A: spec JSON demo** |
| `flowcook-step4-bpm-svc-refactor` | bpm-svc admin/runtime split + Principal + soft-delete |
| `flowcook-step5-bpm-ui-evolution` | ops 4 區搬遷 + DynamicForm |
| `flowcook-step6-syncer-v0` | admin ↔ bpm bridge |
| `flowcook-step7-chef-v0` | chef cooking 🎯 **Milestone B: e2e cutover** |

### Yellow proposals (realign in place)

See `2026-05-17-openspec-triage.md` for the 14 yellow proposals retained under `openspec/changes/` with `FLOWCOOK_STATUS.md` per folder.

### Superseded legacy specs

13 legacy `bpm-*` specs carry `_SUPERSEDED.md` pointing to their flowcook replacement. 5 specs are preserved verbatim as runtime reference (`bpm-cel-expressions`, `bpm-notification-engine`, `bpm-process-runtime`, `bpm-spec-bundle`, `bpm-spec-reproducibility`).

---

## Historical brainstorm notes (this folder)

These files captured the brainstorm progression on 2026-05-16 / 2026-05-17. They are NOT the source of truth — they predate the openspec restructure. Read them only for context.

| File | Topic |
|---|---|
| `2026-05-16-flowcook-pivot-design.md` | Initial pivot brainstorm (品牌 + 四服務 + 五大頁 + Principal + lifecycle + sandbox + audit + wizard) |
| `2026-05-17-chef-design.md` | chef early design draft (skill 大綱 + on-hold protocol) |
| `2026-05-17-migration-plan.md` | Migration strategy notes (monorepo + in-place, AI Kitchen first) |
| `2026-05-17-step1-bpm-admin-svc-skeleton.md` | Step 1 implementation plan (now also in `openspec/changes/flowcook-step1-...`) |
| `2026-05-17-openspec-triage.md` | Triage of legacy openspec proposals (green archived / yellow realign / red obsolete) |

When in doubt, **prefer `openspec/`**. This folder will not be actively maintained.

---

## Process going forward

- New feature ideas → new `openspec/changes/{name}/`
- Reframe an existing yellow proposal → edit the existing `openspec/changes/{name}/` per its `FLOWCOOK_STATUS.md` realign target
- After change complete → `archive/{date}-{name}/` per existing convention
- Specs live in `openspec/specs/{name}/spec.md` — they ARE the truth
