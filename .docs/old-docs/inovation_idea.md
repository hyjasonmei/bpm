# BPM Innovation — AI-Native Process Platform

> **One-liner**：客戶帶著 PPT / 手繪 / Visio 流程圖來，跟 AI 對話 + 填 AI 即時生成的問卷，1-2 個工作天後內含 AD 整合、表單、審核規則的 workflow 就在跑。Camunda 看了會緊張。

> **真正的 vision**：**打破使用者與原始碼的隔閡**——客戶用 9 個 step 跟 AI 把規格談清楚。後台 Claude Code 接手寫程式碼、人工 review 把關品質，1-2 個工作天部署到客戶的站台。客戶從來沒寫過一行 code、沒看過一張 BPMN，但他的流程已經在跑。

> **Status**：vision draft，2026/05/02。Jason + 夥伴的 BPM side-project 的長線目標。

---

## 1. 問題

中小企業（50-300 人）想數位化既有流程，但卡在：

- **流程文件已經存在**（PowerPoint / Visio / 手繪 / 紙本 / 白板照片）
- 既有 BPM 平台（Camunda、Bizagi、Pega、Appian）要求**用他們的工具重畫一次**
- 重畫一次需要：
  - 學 BPMN 規範
  - 配每個欄位的 form
  - 寫每條規則
  - 設審核者、SLA、通知
  - 接 HR / AD / ERP
- 結果：
  - 上線時間 = 數週至數月
  - 必須請顧問（NT$ 50K-500K / 流程）
  - SME 老闆放棄，繼續用 email + Excel

**真正的瓶頸不是工程，是 onboarding**。市場上沒有 SME-friendly 的 onboarding 體驗。

---

## 2. Vision：AI-Native BPM Platform

不是「BPM 加 AI 功能」，是**重新設計 BPM 的 onboarding 體驗**：

```
傳統流程：
  客戶 → 顧問訪談 → 顧問畫 BPMN → 顧問寫設定 → 上線
  時間：4-12 週
  成本：NT$ 100K-500K / 流程
  人月：顧問 0.5-2 人月

我們的流程（Phase A，Concierge MVP）：
  客戶上傳流程圖 → AI 釐清細節 → spec → Jason + Claude Code → 部署
  時間：1-2 個工作天
  成本：API call 費 + Jason 4-6 小時 + 平台訂閱
  人月：客戶自己 0.5-2 小時、我們半天

我們的流程（Phase C，自動化後）：
  客戶 → AI → 30 分鐘上線
```

差距：對客戶 20-50x 改進（跟顧問比）。Phase A 已經足以打開市場，Phase C 才是 vision 終態。

---

## 3. 核心架構：9-Step Spec → Human Review → Claude Code → Deploy

### 3.1 設計取捨：Concierge MVP

走「Concierge MVP / Wizard of Oz」——客戶看到全自動 AI 神器，後台是我們 + Claude Code 在工作。

| 路線 | 上線時間 | Phase A infra 成本 | 需先蓋的東西 |
|---|---|---|---|
| V8 isolate 全自動 | 30 分鐘 | 3-4 個月 | sandbox runner / static analyzer / module store / hot-swap / IaC |
| **9 step + Claude Code（人工 review）** | 1-2 工作天 | **6-8 週可接首位** | 9 step UI + spec exporter + prompt template |
| 純顧問（傳統 BPM） | 4-12 週 | 0（但每流程 50K-500K NTD） | — |

**選人工版的理由**：
1. **盡早接觸客戶 + 收錢**——技術風險比商業風險小，先驗證 willingness to pay
2. **Claude Code 寫 code 品質夠好**——給結構化 spec 能寫出可 review 的 C# / TS code
3. **不蓋自動化舊路徑**——等到自動化真的有 ROI（10+ 客戶）再蓋
4. **客戶體感差距還夠大**——1-2 天 vs 4-12 週仍是 20-50x

**放棄的東西要誠實面對**：
- Day 1 的「30 分鐘上線」承諾——改成「1-2 工作天」
- 全自動 onboarding——前面 5-10 客戶都需要我們親手 review
- 純 SaaS 現金流——人工 review 是線性 cost，不是 marginal-cost-zero

**長期演化**：撐到 5-10 客戶後，看 spec → code 的轉換哪些已經夠標準（譬如「請假」這種大家長一樣），抽成 codegen template 走全自動，剩下複雜的 fall back 人工。**先有客戶、再做抽象**。

### 3.2 三段式工作流

```
┌─ 客戶端 ───────────────────────────────────────────────┐
│ 1. 9-step onboarding UI（co-pilot canvas + AI 生 form） │
│ 2. 客戶填完 → 系統匯出「spec deliverable」JSON          │
└────────────────────────────┬────────────────────────────┘
                             ↓
┌─ Jason / 夥伴端（後台）────────────────────────────────┐
│ 3. Review spec（檢查歧義、補問客戶）                    │
│ 4. 餵給 Claude Code（用 prompt template）               │
│ 5. Claude Code 產出：                                   │
│      • C# Workflow Engine state machine                │
│      • EF migration（case-specific tables）            │
│      • API endpoints / DTOs                            │
│      • 表單 schema / React components                  │
│      • Notify template                                 │
│ 6. Code review + 跑測試 + 微調                         │
│ 7. git push → CI deploy → 客戶 site 上線                │
└────────────────────────────┬────────────────────────────┘
                             ↓
┌─ 客戶 site（single-tenant per site）───────────────────┐
│ • C# .NET API + Workflow Engine（自建，跟 CLAUDE.md 對） │
│ • SQLite (POC) / SQL Server (prod)                     │
│ • React SPA                                            │
│ • 普通 ASP.NET app，不需 sandbox                        │
└────────────────────────────────────────────────────────┘
```

### 3.3 關鍵：spec deliverable 是 single source of truth

9 step 結束時匯出的 JSON 是「設計藍圖」。客戶看不到、Claude Code 看得到、Jason review 用。

```json
{
  "tenant": "acme",
  "flowName": "請假",
  "userTasks": [
    {
      "id": "leave_apply",
      "fields": [
        {"id":"leave_type","type":"select","options":["特休","病假","事假"],"required":true},
        {"id":"date_range","type":"daterange","required":true},
        {"id":"reason","type":"textarea","required":true},
        {"id":"cert","type":"file","conditional":"leave_type === '病假'"}
      ]
    }
  ],
  "approvalNodes": [
    {"id":"manager","rule":{"type":"direct_manager"}},
    {"id":"vp","rule":{"type":"by_department_vp"},"trigger":"days >= 7"}
  ],
  "decisions": [],
  "notifications": [...],
  "sla": {...},
  "integrations": {"ad":"reportsTo"}
}
```

**鐵則**：Claude Code 只能從這份 JSON 出發，**不能** 從 chat history 推測。

理由：
- spec 可版控、可 diff（客戶以後改流程，我們知道改了什麼）
- Prompt 穩定（每次餵 Claude Code 的 context 一樣，產出可預期）
- 跨客戶可累積：第 5 個客戶要請假，spec 結構一樣，prompt template 直接重用

### 3.4 Phase A 實際 deliverable：Spec Bundle（不是 codegen）

**重要修正（2026-05）**：Phase A 的 deliverable 已從「Claude Code 產生的 C# / React 程式碼」改成「**spec bundle (zip) 存到客戶的 Flow Library**」。流程引擎是同一份 production runtime，所有客戶共用，bundle 只描述「這個流程要怎麼跑」（spec.json + bpmn.xml + forms + sample-org + test-cases），由 runtime 直接吃。

理由：
- runtime 已自建完成（add-process-runtime / IProcessRuntime + SpecSnapshot），不需要每客戶 codegen
- bundle 是 portable artifact——客戶可以 export / re-import，跨 instance 完全 reproducible（PR-I8 的 acceptance test 證明）
- onboarding → bundle 路徑是純資料 transform，沒有人類 review bottleneck
- 真正需要 codegen 的場景（特殊 widget / 客製 logic）延到 Phase B / D

Phase A 的 GO LIVE step 因此產的是 zip，不是 PR。Claude Code pipeline（dev agent / review agent / E2E agent）保留為 Phase B 規劃，待真有 codegen 需求時再啟用。

### 3.5 部署：每客戶一個 site

Single-tenant per site。Phase 演進：

- **Phase A**：手動開 VM、docker compose up、Jason 親手部署
- **Phase B**：CI/CD pipeline，Jason approve → 自動 deploy
- **Phase C**：IaC（Terraform / Pulumi），客戶簽單 → 自動 provision site

每客戶 site 內：
- 一份完整的 .NET solution（Domain / Application / Persistence / Api）
- 客戶 specific 的 workflow code 是 Claude Code 寫進這份 solution 的
- 普通 git repo per customer（monorepo + customer branch 也是選項）
- 普通部署流程，不需要 sandbox

### 3.6 Generative UI / V8 isolate（後續）

當以下任一條件成立才考慮：
- 客戶要的 widget 標準 form 庫蓋不到 → AI 生 React component
- 人工 review 變 bottleneck（>10 客戶 / 月）→ AI 寫的 code 自動跑 sandbox（V8 isolate / ClearScript）
- 客戶要 hot-swap 流程改動（不能等下個 deploy）→ runtime sandbox

Phase A-C 都不做。

---

## 4. 互動模式：Co-Pilot Canvas（Phase A 也要做）

這是客戶看到的前端 UX——也是整個產品的「殺手鐧」。後台用人工 review，但**前端 UX 不能用人工**——因為這是客戶 demo 時看到的「全自動感」來源。

每個 onboarding step 是一個分屏畫面：

```
┌─ 客戶瀏覽器 ─────────────────────────────────────────┐
│                                                      │
│  ┌─ 左：Chat 視窗 ─┐  ┌─ 右：AI 生成的問卷 HTML ──┐  │
│  │ 客戶：「請假    │  │ <form>                    │  │
│  │   假別...」      │  │   假別: <select>...       │  │
│  │                 │  │   主管: <select>...       │  │
│  │ AI：「了解,     │  │     [從 AD 載入] (MCP)    │  │
│  │   給您一份表單」 │  │   ☐ 病假需附醫師證明      │  │
│  │                 │  │   [送出]                   │  │
│  └─────────────────┘  └────────────────────────────┘  │
│         ↓ ↑                  ↓ ↑                      │
│         └───── Onboarding State (canonical) ─┐        │
└────────────────────────────────────────┬─────         │
                                         ▼              │
   ┌─ Backend ─────────────────────────────────────────┐│
   │ • Onboarding Session API                          ││
   │ • AI Proxy → Claude API                           ││
   │ • Validator（per-step rules）                     ││
   │ • Spec Exporter（→ JSON 給後台）                  ││
   │ • MCP Connector Hub（Phase B 加）                 ││
   └───────────────────────────────────────────────────┘
```

### 4.1 關鍵 discipline：onboarding state 是 single source of truth

每個 step 底下有一份 canonical state，HTML 是它的 view：

- AI 生 HTML 時：HTML 跟 state 綁定（每個 input 對應 state 一個 slot）
- 使用者改 HTML：UI 觸發 state patch（不改 HTML 字串）
- AI 從 chat 改：AI tool call 改 state → UI 自動 re-render
- Validator：檢查 state、不檢查 HTML
- Spec Exporter：state 過了所有 validator → 匯出 spec deliverable JSON

### 4.2 Validator gate

每個 stepper step 有一個 validator，state 過了才能下一步：

```typescript
validators.forms = (state) => {
  const errors: string[] = []
  for (const task of state.userTasks) {
    if (!task.fields.length) errors.push(`${task.id} 沒有欄位`)
    if (!task.fields.some(f => f.required)) errors.push(`${task.id} 沒必填`)
  }
  return { valid: !errors.length, errors }
}
```

UI 行為：
- Validator 即時跑、errors 顯示
- 「下一步」valid 才亮
- AI chat 也看得到 errors，會主動修
- 強制下一步 escape hatch（避免客戶卡死）

### 4.3 MCP Connector Hub（Phase B 才加）

Phase A 不做 MCP——「主管」「副總」這種欄位讓客戶手動輸入或從 CSV 匯入。
Phase B 加 Entra ID MCP 之後就有「魔法時刻」：

```
AI：「金額超過 50K 要副總？我查您 AD 找到 2 位 title='副總'：
     王副總、陳副總。要依部門 route 嗎？」
```

對 SME 老闆：他從沒告訴 AI 公司有誰，AI 卻知道——這是讓他驚艷然後簽單的瞬間。

---

## 5. Onboarding Stepper（9 步）

```
SOURCE → STRUCTURE → FORMS → DECISIONS → APPROVERS → NOTIFY → SLA → TEST → GO LIVE
```

| # | Step | 客戶做什麼 | AI 在 onboarding state 加什麼 |
|---|---|---|---|
| 1 | SOURCE | 上傳流程圖 / 選範本 / 從零 | flow 骨架（節點 + 邊） |
| 2 | STRUCTURE | 確認 BPMN 骨架 | flow 確認、低信心節點清單 |
| 3 | FORMS | 確認每個 user task 的欄位 | userTasks[].fields |
| 4 | DECISIONS | 設每個 gateway 規則 | decisions[].rule |
| 5 | APPROVERS | 設每個 approval 的審核者 | approvalNodes[].rule |
| 6 | NOTIFY | 設通知模板 | notifications[] |
| 7 | SLA | 設時限 + escalation | sla, escalation |
| 8 | TEST | 試跑一張測試案 | testCases[] |
| 9 | GO LIVE | 確認後送出 spec | 觸發 spec deliverable export |

每個 step 的核心 UX 都是同一個 pattern：**chat + AI 生 HTML 問卷 + state validator gate**。Step 3 是最複雜的，把它做好其他是縮小版。

GO LIVE 不是「立刻上線」，是「spec 提交給後台」——後台 Jason + Claude Code 接手後 1-2 工作天部署。客戶 UI 顯示「processing」+ 預期上線時間。

完整的請假流程 walkthrough 見 `leave_walkthrough.md`。

---

## 6. 為什麼是現在（Why Now）

四個趨勢交疊，這個產品 1 年前還做不出來：

1. **VLM 真的能讀流程圖**（Claude Opus 4.7 vision 達到 2576px、85%+ 抽取準確率）
2. **Claude Code 寫 production code 達到品質**——給結構化 spec 能寫出可 review 的 C# / React，而且寫得快
3. **Claude Artifacts pattern**（co-pilot canvas）成熟——AI 生 UI 不再是 demo，是 production pattern
4. **MCP 標準**讓 AI 安全 access 客戶內部資源（Phase B 用）

這四個能力 2024 年中之前都不齊。**現在是窗口**。

---

## 7. 為什麼是我們

兩人 side-project 看似不利，但有獨特的 fit：

- **夥伴有 Trend BPM 的 domain expertise + SME 客戶關係**
- **Jason 是工程師，可以快速 prototype + 親自觸碰程式 + 親自 review Claude Code 產出**
- **Concierge MVP 對小團隊特別有利**——大廠不能讓「資深工程師親手 review 每個客戶」這種事規模化，我們前 5-10 個客戶這樣做正好
- **2 人 lean team 可以實驗、轉向，大廠不能**
- **沒有 enterprise consulting revenue 要保護**——大廠不敢自動化 onboarding，怕殺掉自己的 services 收入。我們從零開始，沒這個包袱

---

## 8. 防禦性 / Moat

**AI 抽取 + 寫 code 不是真 moat**——大廠 18 個月內會跟進。

真正的 moat：
1. **客戶關係**（夥伴的優勢）
2. **Workflow Engine 的 stickiness**——一旦業務跑在我們引擎上，搬家成本超高
3. **每客戶累積的 customization 是他的「DNA」**——Claude Code 為他寫的 approval rule、表單 logic，移走 = 整套重寫
4. **Prompt template + spec schema 累積的 know-how**——做完 5-10 個客戶後，我們知道哪種 spec 寫法 Claude Code 會輸出最乾淨的 code，這是 trade secret 級的 know-how
5. **MCP Connector ecosystem**——每多支援一個系統 1-2 週工時、但每多一個讓 churn 變低
6. **SME 整合 expertise**（Entra ID / SAP / Workday / 在地 HR 系統）

短期靠 AI onboarding 開門，長期靠 engine + customizations + 整合站穩。

---

## 9. MVP 切點

```
Phase A（6-8 週）：9 step UI + Spec Bundle → 共用 runtime
- 1 個流程（請假），完整 9 step
- Spec exporter → spec bundle (zip) 存到客戶的 Flow Library
- runtime 直接吃 bundle（共用 IProcessRuntime + SpecSnapshot），不 codegen
- 1-3 個友善客戶用真實流程跑
- 我們手動上線、手動維運
- 沒 MCP，approvers 寫死或手動匯入
- 目標：證明「客戶 9 step → 同一天 bundle 上線跑」夠 wow

Phase B（2-3 個月）：Multi-agent dev pipeline + MCP（codegen 才啟用）
- 我們公司一台 always-on 機器跑 spec poller
- 客戶送 spec → 自動觸發 pipeline：
  - **Dev Agent**（Claude Code CLI）讀 spec → 寫 code → 開 PR
  - **Review Agent**（獨立 Claude）審 PR → APPROVE / REQUEST_CHANGES（reject 回 Dev）
  - merge → CI/CD → dev 環境
  - **E2E Agent**（Claude with HTTP / browser tools）建測資跑流程驗收
  - Telegram 通知 Jason ← 人類 gate 1
  - Jason approve → push tag → 客戶 STG
  - 客戶（Mary）驗收核准 ← 人類 gate 2
  - → 客戶 PRD
- Phase A 手動 review 累積的 checklist 變 Review Agent prompt v1
- Phase A 跑過的 case 變 E2E Agent 的 test fixture
- 加 Entra ID MCP（魔法時刻）
- 3-5 個付費 design partners
- 目標：onboarding 時間 1-2 天 → 半天，Jason 主要時間花在最終 dev 驗收，不再寫 / 改 code
- 詳見附錄 `pipeline_architecture.md`

Phase C（3 個月）：全自動常見路徑 + Self-service
- 「請假 / 公告 / 簡單 approval」走全自動 codegen，0 人工
- 複雜流程仍 fall back 人工 review
- IaC site provisioning
- 計費 / SLA
- 目標：10+ 付費客戶 / month $5K+ MRR

Phase D（後續）：runtime sandbox / generative UI / 共用 module library
- 當 hot-swap、即時改規則變需求時才動 V8 isolate
- 客戶要客製 widget 才動 generative UI
```

**從 STEP 3 開始**——9 step 中最複雜的，做完其他都是縮小版。

---

## 10. 已知風險與 Open Questions

### 風險

1. **三狀態同步**（state / chat / HTML）是 state machine 問題——亂了客戶會抓狂
2. **Validator 永遠 fail** 客戶卡住——需要 escape hatch
3. **AI 生 HTML 安全性**——sandbox iframe + CSP，禁任意 script
4. **人工 review 是 bottleneck**——10 客戶 / 月 ≈ 40-60 小時純 onboarding 工。第 5-10 客戶開始撐不住，要往自動化推
5. **「1-2 天」SLA promise 超過會傷信用**——SLA 寫進合約，超時退費 / 折扣
6. **Claude Code 寫的 code 我們扛責任**——bug 是「我們開發的問題」，不是「AI 的問題」，要 own。但相對地我們可以選不接做不出來的需求
7. **N 個 site 的 ops surface**（Phase C 才嚴重）——靠 IaC + 監控自動化壓低

### Open Questions

1. **Workflow Engine**：自建 C# state machine（CLAUDE.md 已定）vs 接 Elsa——POC 期實驗看哪個產出 Claude Code 寫起來順
2. **Repo 模型**：每客戶一個 repo？monorepo + customer branch？monorepo + customer folder？影響 IaC 跟 deploy pipeline 的設計
3. **Spec schema 設計**：JSON / YAML / 自訂 DSL？目標是「人讀得懂、Claude Code 寫得出對應 code、可版控」
4. **Prompt template 演化**：第 1-3 客戶會發現哪些 convention 要寫進 prompt——預計 prompt template 會迭代 5-10 次
5. **AI cost cap**：客戶 onboarding 用 Claude API 的 budget 上限多少？超過跳付費頁
6. **失敗模式 escalation**：客戶卡 onboarding，自動轉介給夥伴顧問服務（順帶提高 ARPU）
7. **第一個流程是什麼**：請假最簡單但 demo 不夠 wow；採購複雜但有 wow factor。先做哪個？
8. **前端 onboarding UI vs 已開發完的 BPM SPA 怎麼整合**——onboarding 是另一個 sub-app？或同一個 app 的 admin mode？
9. **(Phase B) Pipeline iteration cap**：Dev → Review 來回幾次後升給 Jason？預設 3 次？
10. **(Phase B) Pipeline cost cap**：每 spec 最多 X 美元 Claude API budget？超過暫停 + 通知
11. **(Phase B) Review Agent checklist 怎麼版控**：跟 Dev Agent prompt 同 repo？分開？checklist 演化要 review 嗎（誰審審查者）？
12. **(Phase B) 客戶 STG approval UX**：Email link 點「核准」？客戶 site 內的 admin page？

---

## 11. 名稱 brainstorm（暫）

- ProcessLoop
- FlowMint
- Stepwise
- Cascade BPM
- Glyph（流程像 glyph 一樣 expressive）
- Tracejet
- Flux
- (TBD with 夥伴)

---

## 12. 下一步

1. **跟夥伴討論這份 doc**——確認方向、補 SME 客戶 input
2. **找 1-3 個友善客戶聊**——驗證痛點、看流程圖樣本、確認 willingness-to-pay
3. **開始 Phase A**：
   - 9 step UI（先用既有 bpm-ui prototype，加 onboarding stepper module）
   - Spec deliverable schema v1
   - 寫第一版 Claude Code prompt template
   - 用請假流程當 dogfood——自己走一遍 9 step、自己跑 Claude Code、檢驗產出
4. **Spec schema RFC**：拍板 JSON schema 結構

---

## 附錄

- `leave_walkthrough.md` — 請假流程從 T0 到 T10 的完整 walkthrough（Concierge MVP 版）
- `pipeline_architecture.md` — Phase B 的 multi-agent dev pipeline 詳細設計
- (TODO) `prompt_template_v1.md` — Claude Code 的 prompt template
- (TODO) `spec_schema.md` — spec deliverable JSON schema
- (TODO) `review_checklist.md` — Phase A 累積、Phase B 變成 Review Agent prompt

---

*Last updated: 2026-05-02 by Jason + Claude（協作 brainstorm，pivot 到 Concierge MVP：9 step + Claude Code + 人工 review）*
