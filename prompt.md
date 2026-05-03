在當前 repo 上模擬 dogfood（按 prompt 走一輪生成程式碼

 1. Form rewrite vs preserve. Strict RULE #1 = rewrite LeaveForm.tsx from spec (lose the prettier mockup with half_day/proxy/contact). Pragmatic = keep the mockup, only mark     
  spec-divergence as TODO. Which do you want? (I'd recommend rewrite — that's the whole point of dogfooding the prompt.)
  2. Browser walk-through. RULE #8 requires chrome-devtools MCP, screenshots, assertions. Do you have chrome-devtools MCP installed in this Claude Code session? If not, I'll do   
  build+test only and flag the walk-through as deferred.                                                                                                                           
  3. Commit cadence. One big commit at the end, or checkpoint commits per layer (Domain → Persistence → Application → Api → Frontend → Tests)? Checkpoints make rollback easier if
  a layer drifts off-spec.   

   1 create new along feature 2 y 3 one big commit

---

## Dogfood run #2 result (2026-05-03)

LEAVE_SPEC, sample_specs/leave_v1.json, branch `test`.

**Pass:**
- RULE #8 a–j 全綠（commit 24b1c43）
- 端到端：Submit (employee) → Approve (manager) → Archive (HR)，state 1 → 3 → 4
- Gateway days = 2 正確跳過 VP（spec.decisions[gateway_days].branches[e5]）
- 0 console error、0 4xx/5xx、no URL typed
- 截圖 + ASSERTIONS.md 在 `dogfood-screenshots/20260503T015916Z/`
- API :5290 + Vite :5173 仍在跑（IDs `bs1ayig7e` / `bg1152wcp`），驗收完可 kill

**新發現 → 已寫進 prompt v1.2:**
- React-controlled `<input type="date">` 對 chrome-devtools `fill` 沒反應，
  因為 `fill` 直接設 DOM `value`，React 沒收到 onChange。
- 修法：`evaluate_script` + prototype value setter + bubbling `input` event。
- 同樣會中招的還有所有 `<Input>` controlled 元件（number/textarea 之類）。

**還沒做、可以下一輪 dogfood 補:**
- review_checklist.md 還沒生（prompt_template_v1 §"Dogfood plan" 提到）
- v1.2 的 RULE #8 改動還沒實際被新一次 dogfood 驗證過
- bonus path（tc_2 8-day VP escalation、tc_3 病假 cert 400、reject）目前
  只有 curl smoke + 整合測試，沒走過瀏覽器

---

## Dogfood run #3 result (2026-05-03)

PURCHASE, sample_specs/purchase_v1.json, branch `dogfood-purchase`.

**Pass:**
- RULE #8 a–j 全綠
- 端到端：Submit (employee) → Approve (manager) → Execute (admin/u_purchase_lead)，state 1 → 4 → 5
- Gateway amount=5000 正確跳過 Finance + CEO（spec.decisions[gateway_after_manager].e4 default）
- 0 console error、0 4xx/5xx、no URL typed
- 截圖 + ASSERTIONS.md 在 `dogfood-screenshots/20260503T032239Z/`
- 26 個 dotnet test 全綠（unit + integration，cover spec.testCases tc_1/2/3/4 + reject + role-mismatch）
- v1.2 React-controlled input 修法在第 3 輪 number / textarea / date 上全部 OK，沒有再撞牆

**新發現 → 已寫進 prompt v1.3:**
- 第 3 輪一開頭被第 2 輪留下的 Bpm.Api (5290) + vite (5173) + Chrome 實例
  擋住，必須先確認 + kill 才能跑。
- 修法：RULE #8 加 step (k)「跑完報告後關掉 dotnet API / vite / chrome
  Chrome instance」，下一輪才不會再卡。

**還沒做、可以下一輪 dogfood 補:**
- review_checklist.md 還是沒生（連續 3 輪都沒做到）
- v1.3 的 step (k) 還沒被下一輪驗證過
- 三層核准（tc_3 200000 元 manager+finance+CEO）只在 integration test 走過，
  瀏覽器只走了 tc_1（5000 元只走 manager）。