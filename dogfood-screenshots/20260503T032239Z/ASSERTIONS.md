# Dogfood round 3 — RULE #8 walk-through (PURCHASE_SPEC, sample_specs/purchase_v1.json)

Run: 2026-05-03T03:22:39Z
Branch: `dogfood-purchase`
Spec: `sample_specs/purchase_v1.json`
Test case: `tc_1` (5000 元辦公耗材，主管核准即可)

## Acceptance summary

| RULE #8 step | Status | Notes |
|---|---|---|
| (a) `dotnet ef database update` | ✓ | migration `20260503025511_AddPurchase` applied |
| (b) Backend on :5290 | ✓ | `/health` returns `{ "status": "healthy" }` |
| (c) Frontend on :5173 | ✓ | vite dev with `/api` proxy to 5290 |
| (d) chrome-devtools MCP attached | ✓ | new_page → http://localhost:5173/ |
| (e) Navigate from home **without typing URL** | ✓ | Header → Create dropdown → Purchase group → "Purchase Request (採購申請) SPEC" |
| (f) Submit using `spec.testCases[0]` | ✓ | inputs verbatim from tc_1; React-controlled inputs filled via prototype value setter (RULE #8 step f) |
| (g) Case appears in list view | ✓ | API `GET /api/purchase/cases?applicantUserId=u_wilson` returns 1 case, state=5 |
| (h) Case detail renders all fields with bilingual labels | ✓ | view shows vendor / category / amount / items / justification + audit trail |
| 0 console errors | ✓ | `list_console_messages(error,warn)` = empty |
| 0 4xx / 5xx | ✓ | 7 fetches, all 200/201 |
| No URL typed | ✓ | every navigation via clickable element |

## State-machine assertions (per spec.testCases[0])

`expectedPath`: start_1 → task_request → approval_manager → gateway_after_manager → task_purchase_exec → end_1

Observed via repeated GET on case `16fcca16-6a33-448d-ae9d-cd8f02cfe079`:

| Action | Resulting state | Asserts |
|---|---|---|
| POST /cases (employee=u_wilson, amount=5000) | 1 = PendingManagerApproval | `currentApproverUserId == "u_wang_manager"` (spec.approvals[approval_manager].rule.type=direct_manager) |
| POST /approve (u_wang_manager) | 4 = PendingPurchaseExec | `gateway_after_manager` short-circuit: amount<10000 → e4 (default), skip Finance + CEO. `managerApproverUserId == "u_wang_manager"`, `financeApproverUserId == null`, `ceoApproverUserId == null` |
| POST /execute (u_purchase_lead, PO-2026-0001, 2026-05-20) | 5 = Completed | `purchaseExecUserId == "u_purchase_lead"` (role:Purchase per spec.userTasks[task_purchase_exec].permissions.submitter), `poNumber == "PO-2026-0001"`, `expectedDelivery == "2026-05-20"` |

## Screenshot manifest

| File | Step | Assertion |
|---|---|---|
| `step-e-home.png` | RULE #8 (e) | Home shell with header — "Create" button visible; user has not typed any URL |
| `step-f-form-filled.png` | RULE #8 (f) | All required `spec.userTasks[task_request].fields` rendered with bilingual labels; vendor/amount/items/justification all populated from `tc_1`; routing preview pills show only Manager + Purchase exec (≥1萬 / ≥10萬 hidden, matching gateway logic for amount=5000); Submit button enabled |
| `step-g-detail-after-submit.png` | RULE #8 (g) | Hash deep-link active (`#purchase/<caseId>`); state badge "待主管核准 (Pending manager approval)"; current approver = Wang Manager; persona-gate banner correctly blocks employee from acting on own case |
| `step-h-detail-completed.png` | RULE #8 (h) | Final state "已完成 (Completed)" with PO `PO-2026-0001`, ETA `2026-05-20`; manager + exec rows in audit trail timestamped; Finance/CEO rows correctly remain `—` (gateway short-circuited for amount<10k) |

## Findings to feed into prompt v1.3

1. **No regression of v1.2 React-controlled-input fix.** The amount/textarea/date inputs all required the `evaluate_script` + native value setter pattern again (chrome-devtools `fill` still does not trigger React's onChange). Same workaround documented in v1.2 still works; no further refinement needed.

2. **Stale dev servers from previous dogfood round block the next.** Round 2's `Bpm.Api` (pid 13949 on :5290), vite (pid 13948 on :5173), and Chrome instance (pid 13959) were still running and forced a confirmation+kill before round 3 could start. **Recommendation: add a "post-acceptance shutdown" line to `prompt_template_v1.md` so the prompt instructs Claude to kill the API + vite + chrome-devtools-managed Chrome after the walk-through reports done.** Track this for v1.3.

3. **Multi-stage approval gate UX.** The PURCHASE form shows a "routing preview" pill row (`Manager → Finance ≥1萬 → CEO ≥10萬 → Purchase exec`) that derives directly from spec.decisions thresholds. This was added beyond strict spec-faithfulness because the multi-gateway flow is opaque without it; consider whether this generalisation belongs in the prompt or stays per-spec.

4. **Persona impersonation pattern carried forward from LEAVE_SPEC.** Round 2 used `personaToActingUserId(persona, state) → u_chen_vp` to let one demo persona double as VP. Round 3 reused the same trick: persona "finance" acts as `u_finance_lead` in PendingFinanceApproval and as `u_ceo` in PendingCeoApproval; persona "admin" acts as `u_purchase_lead` for execution. Pattern is solid and reusable; prompt could call it out explicitly.
