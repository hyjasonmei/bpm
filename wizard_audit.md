# 9-Step Wizard Audit (Phase A)

> Date: 2026-05-03
> Branch surveyed: `dogfood-purchase` (same wizard code as `main`)
> Goal: inventory what works, what's stubbed, and what's blocking the wizard
> from emitting a valid `spec.json` end-to-end (the front half of the dogfood
> loop).

---

## TL;DR

**The wizard cannot currently produce a complete `spec.json` even on its
happiest path** (load LEAVE preset → click through). 3 of 9 steps have real
UI; 1 is read-only display; 5 are placeholder. The placeholders are exactly
the steps that fill the parts of the spec the dogfood pipeline needs
(decisions, approvals, notifications, sla, testCases).

The plumbing is in good shape — types in `lib/onboarding.ts` mirror
`spec_schema.md` 1:1, validators are wired to gate Next, draft persists in
localStorage, export-to-JSON works. The work is bounded: build 5 step
component UIs, wire them to the existing `setDraft()` pattern.

---

## Step-by-step status

| # | Step ID    | UI status                        | Spec parts populated                  | Blocker on next |
|---|------------|----------------------------------|---------------------------------------|------------------|
| 1 | source     | 🟢 working (Templates only)      | `meta`, `flow.nodes`, `flow.edges`, `userTasks` (via preset) | none |
| 2 | structure  | 🟡 read-only display             | none (uses what step 1 produced)      | none |
| 3 | forms      | 🟢 working (full editor)         | `userTasks[].fields[]`                | none |
| 4 | decisions  | 🔴 placeholder (Construction icon) | none                                | **YES — validator fails** if any gateway exists |
| 5 | approvers  | 🔴 placeholder                   | none                                  | **YES — validator fails** if any approval exists |
| 6 | notify     | 🔴 placeholder                   | none                                  | no (validator passes by default) |
| 7 | sla        | 🔴 placeholder                   | none                                  | no (validator passes by default) |
| 8 | test       | 🔴 placeholder                   | none                                  | **YES — requires ≥1 test case** |
| 9 | go_live    | 🟢 working (mocked submit)       | nothing — read-only summary + console.log | n/a (last step) |

Legend: 🟢 real UI · 🟡 read-only · 🔴 placeholder

---

## Detail per step

### 1. SOURCE — `StepSource.tsx` 🟢
**Captures:** `meta.tenant`, `meta.flowCode`, `meta.flowName`.
**Three source paths:**
- 🟢 **Templates** — only `LEAVE` is wired (`LEAVE_PRESET` in `lib/onboarding.ts`). One-click loads nodes, edges, userTasks (but NOT decisions / approvals / notifications / sla / testCases).
- 🔴 **Upload** (PPT / Visio / 手繪 / Excel) — disabled, label says "Phase B 後啟用（VLM 抽 BPMN）".
- 🔴 **From Scratch** — disabled, label says "Phase B 後啟用（需即時 Claude API）".

**Gap to spec:** Only Templates path gets users somewhere. We have one
template (LEAVE), no PURCHASE / TRAVEL / etc. presets — even though we
already have spec.json fixtures for them.

### 2. STRUCTURE — `StepStructure.tsx` 🟡
**Reads** `draft.flow.nodes` + `draft.flow.edges` and renders a node strip +
edge table. **Cannot edit.** Inline note: "Phase A：拓撲只能從 preset / spec
import 來、無法在這裡編輯。Phase B 會接 bpmn-js 編輯器."

**Gap to spec:** No way to add/rename/connect nodes. Whatever the preset
gives is what you've got.

### 3. FORMS — `StepForms.tsx` 🟢
**Most polished step.** Editor for each user task:
- formCode (auto-derived from flowCode + node label)
- field list with full editor: id, label (zh-TW), type (10 types from
  `FieldType`), required toggle, conditional expression, select options
- Add/remove fields, options.

**Missing fields not in editor:** `label.en`, `validator`, `default`,
`hint`, `derivedFrom`. These exist in the type but the editor doesn't surface
them. (The PURCHASE spec uses `validator` and `hint` — those would need to
be hand-written or the editor extended.)

**Permissions block** — UI doesn't expose `permissions.submitter` /
`permissions.viewers`. They get whatever the preset gave (or the default
`{submitter: 'self', viewers: ['self']}`).

### 4. DECISIONS — placeholder 🔴
Falls through to `StepPlaceholder.tsx` ("Construction" icon).

**What's needed:** For each `flow.nodes[type=gateway]`, the user should be
able to define `decisions[]` entries:
- `id` (matches the gateway node id)
- `type` ('exclusive' | 'parallel' | 'inclusive')
- `branches[]` with `edgeId`, `condition` string, `isDefault` flag
- The branches' edgeIds must reference real edges from this gateway

**Why blocking:** The validator `decisions: (s) => ...` requires
`s.decisions.length >= gatewayCount`. Loading the LEAVE preset (which has
`gateway_days`) makes this validator FAIL because `LEAVE_PRESET` doesn't
include a `decisions` array. The preset path is broken at step 4.

**Quick fix path:** Extend `LEAVE_PRESET` (and add `PURCHASE_PRESET`) to
include their `decisions[]`. Build the edit UI later.

### 5. APPROVERS — placeholder 🔴
**What's needed:** For each `flow.nodes[type=approval]`, define `approvals[]`:
- `id`, `rule` (one of 7 ApprovalRule shapes per spec_schema.md §2.5)
- optional `fallback`
- optional `requiresAll`

The hardest schema in the wizard — `ApprovalRule` is a tagged union with
nested `amount_threshold` / `duration_threshold` / `composite` shapes.

**Why blocking:** Same as decisions — validator requires
`approvals.length >= approvalCount`.

### 6. NOTIFY — placeholder 🔴
**What's needed:** `notifications[]`: trigger, channels, recipients,
template (subject/body bilingual + variables). Multi-recipient editor.

**Not blocking** the wizard (validator returns `{valid: true}` always; "warn
but allow"). But the **spec** needs them — without notifications, the
generated code's `NotificationEmitter` has nothing to emit and the
integration tests for `expectedNotifications.recipientCount` fail.

### 7. SLA — placeholder 🔴
**What's needed:** Per-node duration + escalation policy.

**Not blocking** the wizard. But spec says spec.sla.perNode is required;
generated code currently ignores it (we haven't implemented SLA in code yet
either — that's a back-half gap too).

### 8. TEST — placeholder 🔴
**What's needed:** UI to enumerate `testCases[]` with `inputs`,
`expectedPath`, `expectedApprovers`, `expectedNotifications`. Probably
auto-suggested by traversing the BPMN graph.

**Why blocking:** Validator requires `testCases.length >= 1`.

### 9. GO LIVE — `StepGoLive.tsx` 🟢
**Reads** all 8 prior validators, shows pass/fail table, spec summary, JSON
preview, copy-to-clipboard. Submit button is currently `console.log(draft)`
+ "Spec submitted" success screen. Tracking ID is mocked.

**Gap to spec:** No actual API call. This is the integration point we'd
later wire to `POST /api/spec` (or similar) to trigger the back-half pipeline.

---

## Plumbing (the good news)

These foundations are already in place and don't need to change:

- **Types in `lib/onboarding.ts`** — `DraftSpec`, `FlowNode`, `FlowEdge`,
  `UserTask`, `FormField`, `FieldType`, `OnboardingStepId`. All mirror
  `spec_schema.md` accurately. The `decisions: unknown[]` and
  `approvals: unknown[]` are typed as `unknown` deliberately — they need
  proper types when those steps get UIs.
- **Step shell** — `Onboarding.tsx` has a stepper, validation gate, draft
  persistence (localStorage), step persistence, reset, export-as-JSON. All
  works.
- **Validators** — one per step, called on every keystroke, gate the Next
  button. Easy to extend for new steps.
- **CoPilotCanvas** — chat panel + canvas split layout. Chat is scripted
  (no AI yet); canvas renders the per-step component. Phase B will swap
  scripted replies for real Claude API calls.

---

## Path to "wizard produces a real spec.json"

Ranked by minimum-viable to fully-real:

### Tier 1: Cheapest unblock (preset path can finish)
1. **Extend `LEAVE_PRESET`** to include `decisions`, `approvals`, `notifications`, `sla`, `testCases` — copy from `sample_specs/leave_v1.json` directly. One commit.
2. **Add `PURCHASE_PRESET`** to `lib/onboarding.ts` from `sample_specs/purchase_v1.json`. Same commit or follow-up.
3. **Result:** User can load preset → click Next 9 times → Submit. The exported JSON would match the existing sample_specs/. **Validates the export format** but doesn't really exercise authoring.

### Tier 2: Editable steps
Build real UIs for the 5 placeholder steps, in order of impact:
1. **TEST** (blocking, simple) — list editor for testCases. Most schemas are flat (id, name, inputs object, expectedPath array).
2. **DECISIONS** (blocking) — for each gateway node, dropdown to pick branch type + table to enter `condition` per outgoing edge. Defaults can be auto-generated (the gateway already knows its edges from `flow.edges`).
3. **APPROVERS** (blocking, complex) — discriminated-union editor for `ApprovalRule`. Needs care because of nested shapes.
4. **NOTIFY** (non-blocking but needed) — bilingual subject/body editor with variable picker.
5. **SLA** (non-blocking) — per-node duration + escalation.

### Tier 3: Real authoring
1. **STRUCTURE editor** — currently read-only. Need bpmn-js or similar to add/connect/rename nodes from scratch (without preset).
2. **SOURCE upload + from-scratch paths** — currently disabled. Phase B requires VLM (for diagram upload) + real-time Claude (for from-scratch dialogue).
3. **CoPilotCanvas chat → real AI** — replace scripted replies with actual Claude API; teach AI to mutate `draft` via patches.
4. **Submit** — `POST /api/spec` to back-half pipeline (close the loop).

---

## What I'd build first

If you tell me to start building this, my recommendation:

**Sprint 1 (~half day):**
- Extend LEAVE_PRESET + add PURCHASE_PRESET (copy from sample_specs)
- Build TEST step editor (small + blocks Submit)
- Build DECISIONS step editor (medium + blocks Submit)
- Build APPROVERS step editor (medium-large but bounded)
- After this: preset → click through → Submit produces a real spec.json that
  passes the back-half dogfood pipeline. **Front-half MVP closes.**

**Sprint 2 (~half day):**
- NOTIFY editor
- SLA editor
- Both are non-blocking but spec-completeness matters for code quality.

**Sprint 3 (multi-day):**
- bpmn-js editor for STRUCTURE
- Real Claude API in chat
- POST /api/spec wiring (the integration piece)

---

## Open questions

1. **Authoring UX assumption check.** The current `setDraft()` pattern
   is "user edits a JSON-shaped object via UI". For a non-engineer customer
   the JSON-y feel of FORMS step (id field, conditional expression
   `leave_type === '病假'` typed as a string) might already be too low-level.
   Should DECISIONS / APPROVERS expose the same level of detail, or should
   they be "natural-language → AI parses → JSON" from day 1?

2. **Validator strictness.** Decisions + Approvers + Test all gate Next on
   completeness. NOTIFY + SLA pass even when empty. Is that intentional? If
   we're shipping real spec.json downstream, NOTIFY-empty means generated
   code has nothing to emit at on_assign / on_complete — that's probably
   not OK.

3. **Preset coverage.** Phase A demo will probably show 1-2 presets to
   customers. Is `LEAVE` enough, or should we have at least PURCHASE +
   one more for the "your industry" pitch?

4. **Submit path.** When Submit eventually does something real, what does
   "send to back-half pipeline" mean — write to a folder Claude Code is
   watching? Open a PR? Trigger a remote job? Each has different latency /
   feedback story for the customer.
