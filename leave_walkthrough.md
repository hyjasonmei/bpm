# 請假流程 — 端到端 Walkthrough（Concierge MVP 版）

> 從客戶剛買單，到員工 A 第一次按下「送出請假」，全流程跑一遍。
> 反映 Phase A 架構：9 step UI（自動）+ Jason 後台 review + Claude Code 寫 code + 人工部署。

> 配合 `inovation_idea.md` §3-§5 閱讀。

---

## 場景設定

- 客戶：Acme 公司，87 員工 / 5 部門 / 23 manager
- 流程：請假
- 角色：HR Mary（Acme 管理員）、員工 A 小李、A 的直屬主管王經理
- 我們這邊：Jason（工程）、夥伴（業務）

---

## T0 — 客戶剛買單

夥伴拿到 Acme 的合約。Jason：
- 在我們的 IaC repo 開一個 `tenant_acme` branch（Phase A 是手動）
- `docker compose up` 開好 VM 跑空白的 BPM 工具（含 9-step onboarding UI）
- 給 Mary 帳號 + URL
- 告訴她：「您先用這個工具，跟 AI 把請假流程談清楚，完了我們會在 1-2 個工作天內部署正式版」

Acme IT 還沒裝 MCP（Phase A 不做）。請假審核者用 CSV 匯入或手動填。

---

## T1 — Mary 進系統開「新流程」

- Login → 點「新流程」
- AI：「您要做哪種流程？可上傳 PPT / Visio / 手繪 / 描述」
- Mary 選「描述」+「請假」
- AI 套模板生骨架：員工申請 → 主管核准 → HR 備案 → 完

---

## T2 — Step 1: SOURCE

- 左 chat：AI「就這 4 個節點？」
- 右 canvas：BPMN 圖
- Mary：「對」
- Validator 過 → 下一步

---

## T3 — Step 2: STRUCTURE

- 確認 BPMN 骨架。Mary 滑鼠移到「主管核准」節點：AI「這節點需要欄位嗎？」
- Mary：「不用，主管只要按準/不准」
- Validator 過 → 下一步

---

## T4 — Step 3: FORMS（最重的一步）

- 左 chat：AI「請假表單會有哪些欄位？」
- 右 canvas：AI 預生 default 表單（假別 / 起訖時間 / 事由 / 病假附證明）
- Mary 在右邊直接改：「事由」加 hint「中英文皆可」
- Mary 在左邊問：「公假怎麼辦？要附公文」
- AI 改 onboarding state：加 conditional rule「公假需附公文檔案」
- Validator 過（必填欄位齊、條件規則 well-formed）→ 下一步

---

## T5 — Step 4: DECISIONS

沒 gateway，跳過。

---

## T6 — Step 5: APPROVERS

- 左 chat：AI「主管核准是誰簽？」
- Mary：「直屬主管」
- AI：「您公司有沒有 AD 紀錄員工 reportsTo 關係？」
- Mary：「有，但你還沒接到我們系統吧」
- AI：「Phase A 階段我們會請您 CSV 匯入員工 + 主管對應，Phase B 之後會自動接 AD」
- Mary 上傳 employees.csv（87 員工 + reportsTo 欄位）
- AI：「業界 best practice 提示——超過 5 天要不要 escalation？」
- Mary：「7 天以上要副總 + 直屬主管都簽」
- AI：「副總 2 位，依部門對應嗎？」
- Mary：「依部門」
- AI 改 state：加邏輯記錄「7 天以上 escalation 到部門對應副總」
- Validator 過 → 下一步

---

## T7 — Step 6-8: NOTIFY / SLA / TEST

- NOTIFY：AI 給雙語 default email template，Mary 微調公司用詞
- SLA：AI 建議「審核 24 小時、超時 escalation」，Mary 改成「主管 8 工時、副總 24 工時」
- TEST：AI 生一張測試案資料（小李、特休、5/10-5/12），Mary 用視覺化檢查整段路線會怎麼跑——沒有真的執行（系統還沒部署），只是 walk through state
- Validator 過 → 下一步

---

## T8 — Step 9: GO LIVE（這裡跟過去版本不同）

### 客戶端體感

- AI 摘要：「Acme 請假流程：87 員工、5 假別、3 審核點、SLA 主管 8 工時 / 副總 24 工時」
- Mary 點「上線」
- 系統顯示：
  ```
  ✓ 規格已送出至我們的部署團隊
  預計上線時間：明天下午前
  上線後您會收到 Email 通知
  ```
- Mary 關掉 onboarding 工具，等通知

### 後台實際發生（客戶看不到）

```
T+0    Spec deliverable JSON 入站到 Jason 信箱 / 工單系統
T+1h   Jason 開工：
       - Review JSON（檢查歧義、必要時跟 Mary 補問）
       - 用 prompt template 餵 Claude Code：
         "這份 spec → 產出 C# Workflow Engine code、EF migration、
          API endpoints、React form components、notify templates"
T+1.5h Claude Code 寫完。Jason review 產出：
       - 邏輯對嗎？
       - convention 跟我們的標準一致嗎？
       - 微調（人類懂、AI 不懂的 edge case）
T+2h   git push 到 tenant_acme branch
       CI pipeline build 通過
       自動 deploy 到 Acme site
T+2.5h Jason 跑 smoke test：
       - 自己模擬小李送一張請假
       - 看路徑跑對、王經理收到通知
T+3h   發 Email 給 Mary：「您的請假流程已上線」
```

**體感：Mary 隔天上班看到流程能用了**。沒有 30 秒 hot-swap，但比顧問模式（4-12 週）快 20-50x。

---

## T9 — 員工 A 小李用流程

- 小李打開 Acme 的工作平台 → 看到「請假」按鈕（Acme site 已部署完成）
- 點 → API 取 `LeaveCase` form schema → 動態 render 表單
- 小李填：特休、5/10-5/12、3 天、「家裡有事」
- 送出 → API 跑 `LeaveCaseValidator`（Claude Code 寫的）→ 過
- Workflow Engine 進到「主管核准」節點：
  - call `DirectManagerResolver` → 從 employees.csv 查小李的 reportsTo = 王經理
  - 派任務給王經理
- 王經理收 Email + Teams 通知（NOTIFY template 寫好的）→ 點 link 進來
- 王經理核准 → Workflow Engine 進「HR 備案」 → 通知 Mary → Mary 點「備案完成」 → 流程結束

整個 runtime 是普通 C# .NET application，沒有 sandbox、沒有 V8 isolate——因為 code 是我們寫的（Claude Code 寫的，我們 review 過），不需要隔離。

---

## T10 — 後續變更

3 個月後，Acme 老闆說「以後請假超過 3 天要先問助理有沒有撞會議」。

### 客戶端

- Mary 重新進入 onboarding 工具
- 從 Step 5 進入請假流程編輯模式
- 左 chat：「加一條：超過 3 天要先過秘書確認沒撞會議」
- AI 改 state：加新 escalation 條件
- Validator 過 → Mary 點「儲存變更」
- 系統顯示：「規格變更已送出，預計明天下午前生效」

### 後台

- Jason 收到變更 spec（diff highlighted）
- 跑 Claude Code with diff context：「在現有 LeaveWorkflow.cs 加這條 escalation 規則」
- Claude Code 產出 patch
- Jason review、跑 regression test（已存在的 case 不能壞）
- git push → deploy → 通知 Mary

跑中的 case 用舊版 deployment 完成（沒事），新案開始用新版。

---

## 關鍵點

1. **客戶體感是「全自動 AI」，後台是 Concierge**——前端 9 step UI 必須做到位（這是 demo wow factor），後台再用 human-in-the-loop 確保品質
2. **Spec deliverable JSON 是 single source of truth**——不准 Claude Code 從 chat 推測，一定要從這份 JSON 產 code
3. **客戶不離開「同一個介面」做變更**——3 個月後改流程也是回 onboarding 工具，不是學第二個工具
4. **沒有「客戶等 30 秒立刻上線」的儀式**——換成「明天上線」，可接受
5. **每個 Acme site 是普通 C# app**，跑在 VM / container 裡，沒有 sandbox 概念。Claude Code 寫的 code 是我們的 code，我們扛責任
6. **Phase A 沒 MCP，Phase B 才接**——Phase A 用 CSV 匯入 / 手動填，足夠跑請假這種小流程

---

## Phase A → B → C 的路徑

當這份 walkthrough 跑了 5-10 個客戶後：
- Phase B：Claude Code 產出後跑自動測試 → Jason 只 approve、不微調 → onboarding 縮短到半天
- Phase C：「請假」這種標準流程走全自動 codegen，Jason 不需要介入；複雜的客製化流程仍 fall back 人工
- Phase D：runtime sandbox 出現是因為要支援 hot-swap（客戶半夜要改規則）

---

*這份 walkthrough 對應 `inovation_idea.md` v3 (Concierge MVP) 架構。*
