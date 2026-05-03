# Dogfood round 3 follow-up — tc_3 three-tier approval walk-through

Run: 2026-05-03T03:49:30Z
Branch: `dogfood-purchase` (commit `59fb0ae`)
Spec: `sample_specs/purchase_v1.json`
Test case: `tc_3` (200000 元服務委外，三層核准)

This walk-through closes the coverage gap from the round-3 main report
(`dogfood-screenshots/20260503T032239Z/`), which only walked tc_1 (single-tier)
through the UI. tc_3 exercises the full manager → finance → CEO chain via
`personaToActingUserId` re-mapping (the `finance` persona acts as `u_finance_lead`
in state=PendingFinanceApproval and switches to `u_ceo` in state=PendingCeoApproval).

## Acceptance summary

| Step | Status | Detail |
|---|---|---|
| Submit (employee, amount=200000) | ✓ | `quote_200k.pdf` attached via DataTransfer (file inputs need synthetic FileList; plain prototype-setter doesn't carry through `e.target.files`); state → PendingManagerApproval, `currentApproverUserId == u_wang_manager` |
| Manager approve | ✓ | state → PendingFinanceApproval; `currentApproverUserId == u_finance_lead` (resolved via `IIdentityProvider.FindByRoleAsync("Finance")`) |
| Finance approve | ✓ | state → PendingCeoApproval; `currentApproverUserId == u_ceo`; `personaToActingUserId('finance', 3)` correctly remaps to `u_ceo` so the same finance persona keeps the Approve button visible — verified by the "Acting as `u_ceo`" caption flipping in place without the user needing to change persona |
| CEO approve | ✓ | state → PendingPurchaseExec; all 3 approver rows in audit trail show real names + timestamps |
| Admin execute (PO-2026-0003, 2026-06-01) | ✓ | state → Completed; `purchaseExecUserId == u_purchase_lead` |
| 0 console errors | ✓ | `list_console_messages(error,warn)` empty |
| 0 4xx / 5xx | ✓ | 11 fetches, all 200/201 (3 approves + 1 execute + 7 GETs) |
| No URL typed | ✓ | every navigation via clickable element (Create dropdown → Purchase Request → persona switcher) |
| RULE #8 step (k) self-test | will verify on shutdown | (this run is shutting down servers + Chrome per the rule we just added in v1.3) |

## State-machine assertions (per spec.testCases[2])

`expectedPath`: start_1 → task_request → approval_manager → gateway_after_manager → approval_finance → gateway_after_finance → approval_ceo → task_purchase_exec → end_1

Observed via repeated GET on case `faf5d5f1-2d91-4513-9b1e-12785bc430b0`:

| Action | Result state (int) | Approver row populated |
|---|---|---|
| POST /cases (employee=u_wilson, amount=200000) | 1 PendingManagerApproval | — |
| POST /approve (u_wang_manager) | 2 PendingFinanceApproval | manager: u_wang_manager · 2026/5/3 03:51:43 |
| POST /approve (u_finance_lead) | 3 PendingCeoApproval | finance: u_finance_lead · 2026/5/3 03:52:12 |
| POST /approve (u_ceo) | 4 PendingPurchaseExec | ceo: u_ceo · 2026/5/3 03:52:34 |
| POST /execute (u_purchase_lead, PO-2026-0003) | 5 Completed | exec: u_purchase_lead · 2026/5/3 03:53:16, PO=PO-2026-0003, ETA=2026-06-01 |

Matches spec.testCases[2].expectedApprovers exactly:
- approval_manager: u_wang_manager ✓
- approval_finance: u_finance_lead ✓
- approval_ceo: u_ceo ✓

## Screenshot manifest

| File | Step | Assertion |
|---|---|---|
| `step-f-form-200k.png` | submit | All required `task_request` fields populated for tc_3; routing preview pills now show **all four tiers** (MANAGER · FINANCE ≥1萬 · CEO ≥10萬 · PURCHASE EXEC) — derived live from amount=200000 hitting both gateway thresholds; `quote_200k.pdf` shown as attached |
| `step-g1-after-manager.png` | post-manager | state badge `待財務核准 (Pending finance approval)`; manager row in audit trail timestamped; current approver = Lin Finance (財務); finance/ceo/exec rows still `—` |
| `step-g2-after-finance.png` | post-finance | state badge `待 CEO 核准 (Pending CEO approval)`; finance row populated; "Acting as `u_ceo`" caption (proof of `personaToActingUserId(finance, 3) == u_ceo`); Approve button still visible without persona switch |
| `step-g3-after-ceo.png` | post-CEO | state badge `待採購處理 (Pending purchase exec)`; all three approver rows (manager, finance, CEO) populated with real names + timestamps; only exec row remains `—`; persona-gate banner directs to switch to Admin |
| `step-h-completed.png` | post-execute | state `已完成 (Completed)`; full audit trail of 4 rows; PO `PO-2026-0003` and ETA `2026-06-01` displayed |

## Findings to feed into prompt v1.4

1. **File-input attachment requires DataTransfer trick.** The chrome-devtools
   MCP `fill` tool doesn't apply to `<input type="file">` (browser security boundary). The
   prototype-value-setter trick from v1.2 also doesn't help — `e.target.files`
   is a `FileList`, not a string. Working pattern:
   ```js
   const file = new File(['mock'], 'quote_200k.pdf', { type: 'application/pdf' });
   const dt = new DataTransfer();
   dt.items.add(file);
   input.files = dt.files;
   input.dispatchEvent(new Event('change', { bubbles: true }));
   ```
   This should be appended to RULE #8 step (f) for the next prompt rev.

2. **`<select>` fill needs the displayed option label, not the `value`.** The
   chrome-devtools `fill` tool failed with "Could not find option with text 'service'"
   when given the option `value`. Re-running with the visible label `服務委外 / Service`
   succeeded. Worth noting in step (f) — the tool matches against display text, not
   `<option value="">`.

3. **`personaToActingUserId` second-tier remap works in production.** The finance
   persona seamlessly switches to `u_ceo` for state=3 without forcing the user to pick
   a different persona — verified live in step-g2. This pattern is now battle-tested
   for both LEAVE_SPEC (manager → VP) and PURCHASE (finance → CEO); worth promoting
   from "trick" to documented convention in the prompt.

4. **Persona-gate UX is correct on every transition.** The view's "Persona X
   cannot act on this case. Expected approver: Y" banner appears exactly when needed
   (state ∈ {1,2,3} and `actingUserId != currentApproverUserId`) and disappears when
   you switch to the correct persona. No flicker, no stale state.

## Step (k) live verification

This run shuts down API + Vite + Chrome immediately after this file is written.
The next dogfood round will inherit empty ports as the proof. If it does NOT, v1.3
step (k) needs more teeth.
