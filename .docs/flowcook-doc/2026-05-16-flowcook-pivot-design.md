# flowcook — 架構與品牌 Pivot 設計筆記

> Status: **DRAFT — brainstorming 進行中**
> Date: 2026-05-16
> Author: Jason + Claude

本文件記錄將「BPM 平台」改名為 **flowcook** 並調整為四服務架構的設計討論。本文是 brainstorm 過程的中段快照——chef 細節、syncer 資料合約、API surface、migration 計畫都還沒談定。

---

## 1. 品牌與比喻

| 舊 | 新 |
|---|---|
| AI Onboarding | **AI Kitchen** |
| 客戶整理流程需求 | 客戶準備「原料」（flow spec） |
| AI 生成 workflow code | **AI Chef** 把原料炒成菜 |
| 平台名稱 BPM | **flowcook** |

「flow spec → AI chef → 可運轉的 workflow」是核心心智模型。

---

## 2. 四服務架構（monorepo 內 6 個資料夾）

實作上同一個 monorepo（既有 `bpm/`），架構上是「四服務」（admin / bpm / chef / syncer），每個服務由 fe + be 對應的資料夾組成：

| 服務 | 資料夾 | 狀態 |
|---|---|---|
| **admin** | `bpm-admin-svc/`（fe） + `bpm-admin-ui/`（be） | ui 既有改造；svc 新建 |
| **bpm** | `bpm-svc/`（be） + `bpm-ui/`（fe） | 都既有改造 |
| **chef** | `chef/` | 新建 |
| **syncer** | `syncer/` | 新建 |

```
┌─────────────────────── flowcook（我們的商業核心）──────────────────────┐
│                                                                         │
│   ┌────────────────────┐               ┌────────────────┐              │
│   │  admin             │ ── spec ───► │  chef          │              │
│   │  bpm-admin-svc +   │ ◄─ run ────  │  (AI pipeline) │              │
│   │  bpm-admin-ui      │               └────────┬───────┘              │
│   └─────┬──────────────┘                        │                       │
│         │                                        │                       │
│         │ syncer                                 │ generated bundle      │
│         │ (push spec/org/var, pull audit & data) │                       │
│         │                                        │                       │
└─────────┼────────────────────────────────────────┼──────────────────────┘
          ▼                                        ▼
┌──────────────────── bpm（客戶側）────────────────┐
│                                                  │
│  bpm-svc + bpm-ui                                │
│  自給自足 runtime + ops UI                       │
│  表單 / inbox / live cases / reports / 介入     │
│                                                  │
│  客戶不續約後仍可獨立運轉                         │
└──────────────────────────────────────────────────┘
```

### 2.1 admin (fe + be)
- **誰用**：我們 flowcook 內部 + 該客戶的管理員（草創階段「多客戶就多 admin」，不做 multi-tenant，每客戶一套）
- **結構**：五大頁
  1. **AI Kitchen (flow management)** — onboarding wizard、AI CoPilot、spec / flow library、Designer、Simulator、chef 控制台
  2. **User & Role** — org / dept / persona / role 管理（推送下去 bpm）
  3. **Sandbox** — sandbox 工具（信件攔截轉發、時鐘 freeze）；UI 在 admin，runtime 在 bpm（admin 透過 API 驅動）
  4. **Audit** — 從 bpm 同步回來的 audit log 檢視
  5. **Site Setting** — 全域設定，含：
     - admin 自我設定：SMTP、Anthropic API key、Persona switch 允許 user list
     - bpm 全域行為設定：預設通知 sender、timezone、預設語言、所有流程共通設定
     - （原先「bpm setting」概念併進這頁，不另開）
- **不負責**：執行期 process state、live cases、reports、人工介入、通知派送（這些在 bpm）

### 2.2 bpm (fe + be)
- **誰用**：客戶組織內部所有人（end users、客戶 ops 管理員）
- **負責**：workflow runtime、表單 (DynamicForm)、inbox、persona/role/dept、live cases、reports、人工介入、通知派送
- **草創階段定位**：完整可獨立運轉的 BPM 產品；admin 斷線後不影響日常營運
- **部署**：看客戶選擇——我們雲端 host 或客戶內網 self-host

### 2.3 chef
- **角色**：AI 流水線（Claude Code runner）
- **輸入**：admin 推來的 flow spec
- **輸出**：依 bpm 規範產生 workflow code / form / notification template
- **草創階段先抽象成「pull spec + 跑 AI + 走 bpm skill 規範」**，內部 Dev/Review/E2E agent 拆解延後設計
- **業務 fork**：
  - 客戶有技術能力 → chef 開 branch / PR，客戶自己審
  - 客戶沒技術能力 → 我們審（加價收費）
- **永遠 flowcook 持有，不交付客戶**

### 2.4 syncer
- **角色**：admin ↔ bpm 之間的橋樑
- **Push（admin → bpm）**：spec bundle、user / org / role / persona、flow library 更新、bpm setting
- **Pull（bpm → admin）**：三大類資料同步
  1. **流程 data**（含 sandbox 執行狀態）
  2. **Audit log**（全收：process / 通知 / role / 系統 / 介入 — 全部）
  3. **Org data**（來源：客戶資料源；syncer 負責把客戶的組織資料 map 進我們的 schema —— 細節待 principal 設計重整後補）
- **斷線行為**：
  - 客戶斷約 → admin 停止推 → bpm 用最後一次同步的狀態繼續跑
  - 客戶斷約後 audit log 也不再同步回 admin（客戶想看自己的紀錄，要在 bpm 本機查）

---

## 3. Principal & Role Model

### 3.1 Principal types
- **user** — 真實人員
- **dept** — 部門（正式組織結構，慢變、governance 嚴）
- **group** — 臨時集合 / 跨部門虛擬團隊（快變、case-by-case）

### 3.2 Inter-principal containment

| 容器 → 可收 | user | dept | group |
|---|---|---|---|
| **dept** | ✓ n:n | ✓ 嚴格 tree（單一 parent） | ✗ |
| **group** | ✓ | ✓ | ✓ 允許嵌套 |
| **user** | — | — | — |

規則：
- `user.primary_dept_id` 可 null（**有但非必填**）
- dept 嚴格 tree —— 跨部門表達一律走 group，不允許 dept-DAG
- group 允許嵌套 —— admin BE 在 save 時做 cycle detection

### 3.3 Role 模型
- Role ↔ Principal 是 n:n
- 任何 principal（user / dept / group）都能被指派任何 role
- 每筆「principal × role」assignment 帶一個 **`inherit_to_members`** checkbox（per-assignment 決定）：
  - **true** → role 透過 dept tree / group graph **向下繼承** 給所有 descendant user
  - **false** → role 只給該 principal 本身（適合「dept 當 actor 收件」、「group 自己持有 role」場景）
- UX：assignment 編輯介面預設給 checkbox，由指派者決定語意

### 3.4 Effective role 解析
- 建 `effective_principal_role` materialized view：`user_id × role_id`（含 source principal_id 用於追蹤）
- 觸發重算：user join/leave dept、dept move parent、group 成員調整、role assignment 增刪、`inherit_to_members` flag 改變
- spec ActorRef 解析時查 effective view，O(1)

### 3.5 與既有 bpm-svc 的對應
- 現有 PersonaSeedService 13 user / 6 dept / 14 role → 重 model 進 Principal / Role / Inheritance schema
- 現有 Persona 概念：保留作為「sandbox 登入身分切換」用（屬於 Sandbox 功能），跟 Principal 是不同概念
- ActorRef 解析改走 effective_principal_role view

### 3.6 代理人（Delegation）
- 獨立 table，跑在 effective role 解析「之後」這層 —— principal 先解到，再查代理人表
- 表結構（草案）：`delegator_principal_id` × `delegate_to_user_id` × `start_at` × `end_at` × `active` × `reason?`
- 解析語意：
  - actor 解析時若 delegator 有 active 代理 → 改用 delegate_to_user 的 effective_principal_role
  - **代理對象限 user**（不允許代理給 dept/group，避免歧義）
  - **不做 transitive chain**（A→B 且 B→C，A 不會自動 → C；要鏈式就再開一筆 A→C）
- 變更（新增 / 啟停 / 修改時間範圍）進 audit log
- 配置權限：user 自己 + admin 都能設

### 3.7 syncer 視角
- principal/dept/group/role 的「source of truth」由 syncer 決定（取決於客戶資料源）
- syncer 負責把客戶的組織資料 map 進此 schema（細節待後續釐清）

### 3.7a 草創階段實作策略（User & Role 功能）

| 項目 | 草創（MVP） | 未來 |
|---|---|---|
| 資料來源 | **Seed CLI** 一鍵塞 demo data（沿用 bpm-svc 既有 SeedCli 改寫到 admin） | syncer 對接客戶 Entra ID / AD / HRIS |
| Principal 編輯 | admin User & Role UI 可手動 CRUD（demo / 客戶內部小修） | 維持手動，但大量同步走 syncer pull |
| 客戶資料源整合 | **TODO（保留）**——草創不做、demo 不需要 | syncer 內部模組，per-data-source 寫 adapter |
| Demo flow | seed → admin 顯示組織樹 → 推送 bpm → 走 wizard 產 flow → run | end-to-end 跟既有相同 |

- Seed CLI 任務：sample 13 user / 6 dept / 含一個 group / 14 role / Principal-Role assignment / 若干 delegation 範例
- 沿用 bpm-svc PersonaSeedService 的精神，但目標 schema 變成新 Principal 七表（見 §3.8）
- demo 可重設：`seed reset` / `seed --include-bundles`（保留現有 SeedCli 介面）

### 3.8 Coverage Audit — 這套底層夠不夠用？

對照現有 bpm-svc 的 actor 場景跑一遍：

**✅ 直接 cover 的場景**

| 場景 | 怎麼表達 |
|---|---|
| 派給工程部任一人簽 | `actor = dept_id`，PrincipalRole `inherit_to_members=true`，effective view 解出全部成員 |
| 派給整個部門共享收件匣 | `actor = dept_id`，`inherit_to_members=false`，dept 本身收件，個別 user 不繼承 |
| 跨部門專案小組 approval | group 含跨部門成員 + role + `inherit=true` |
| 員工兼任兩個部門 | user_dept n:n（一個為 primary 一個不為） |
| 主管請假找代理 | delegation table（限 user-to-user，活躍時間區間） |
| VP-level escalation | role 指派給特定 user，或給「VP 群組」group |
| 通知對象解析 | actor → principal → effective_principal_role → user 集合 → 派 |
| Sandbox 身分切換 | Persona 概念保留，獨立於 Principal（屬 Sandbox feature） |
| 多 tenant 隔離 | 一客戶一套 admin+bpm，schema 天然以 instance 為 boundary |
| ActorRef snapshot 重現 | spec 存 principal_id，effective_principal_role view 加上 snapshot timestamp 仍可重建 |

**🟡 故意不做（YAGNI；之後若必要再補）**

- **Role-of-role 繼承**（Manager implies Approver）—— 現有 14 個 role 都是 flat，沒這需求；要的時候加 `role_parent_role` 表
- **Role 本身的有效期 / 時間視窗** —— 短期用 delegation；長期需要時加 `principal_role.expires_at`
- **Conditional role**（X 在條件 Z 才有 Y role）—— spec 用 Cel 表達式處理，不是 schema 問題
- **Transitive 代理鏈** —— 要鏈就明寫一筆 A→C，避免 cycle/ambiguity
- **跨 tenant 共享 principal** —— 不支援

**📋 建議 Schema 雛形（EF Core entity 層）**

| Table | 主要欄位 |
|---|---|
| `Principal` | id, type(user/dept/group), display_name, email?, active, timestamps, RowVersion |
| `UserDept` | user_id, dept_id, is_primary（user.primary_dept_id 用此推導） |
| `DeptParent` | dept_id (PK), parent_dept_id（強制單一 parent = 嚴格 tree） |
| `GroupMember` | group_id, member_principal_id, member_type(user/dept/group) |
| `Role` | id, name, is_system, description |
| `PrincipalRole` | principal_id, role_id, inherit_to_members, assigned_at, assigned_by |
| `Delegation` | delegator_principal_id, delegate_to_user_id, start_at, end_at, active, reason? |
| `EffectivePrincipalRole`（materialized view） | user_id, role_id, source_principal_id, via_inherit:bool |

**⚠️ 對既有 spec ActorRef 的衝擊**

- 現有 ActorRef 三種值：user / persona / role
- 新模型 ActorRef 改為兩種值：**principal**（自帶 type 區分 user/dept/group）/ **role**
- Persona 從 ActorRef 移除，改放 Sandbox（沙箱身分切換 ≠ runtime principal 解析）
- bpm-admin-ui 的 ActorRefEditor 要改：選 principal 時要先選 type (user/dept/group) 再選 id
- 既有 11 個 sample_specs/*.json 需 migration script 把 persona ref 改寫成對應的 principal ref

**結論：底層 schema 對 MVP 的所有 actor 場景都覆蓋得到。**

---

## 4. AI Kitchen Flow Lifecycle

每個在 admin AI Kitchen 內被孕育的 flow（spec）有自己的生命週期狀態。

### 4.1 States

| State | 意義 | 誰能改 |
|---|---|---|
| `draft` | 客戶 / 我們在 admin 內編輯中，尚未送 chef | user |
| `submitted` | 已送進 chef queue，等待開工 | system（user 送出） |
| `cooking` | chef AI 開發中 | system（chef） |
| `on hold` | （optional）chef 卡住、需釐清問題 | system（chef raise）→ user 解答 |
| `committed` | chef 開好 branch、PR 已發出 | system（chef） |
| `approved` | chef 偵測到 PR 被合進 main | system（chef poll） |
| `rejected` | PR 被審核拒絕 → 回 `draft` | user / 我們的審核員 |

### 4.2 Transitions

```
   draft ──submit──► submitted ──pick up──► cooking ──opens PR──► committed ──merged──► approved
     ▲              │                │                            │                          │
     │              │ withdraw       │ cancel                     │ PR rejected              │ (new version)
     │              │                │                            │                          │
     │              ▼                ▼                            ▼                          ▼
     └──────────────┴────────────────┴── (back to draft, audit) ──┴──► rejected ────────►  new draft
                                                                                              (prefilled
                                                                                              from approved)

                                                          on hold ◄──── cooking
                                                             │            ▲
                                                  (user 改 NOTES + Resume)│
                                                             ▼            │
                                                          submitted ──────┘
                                                          (chef 重 pick up)
```

`on hold → submitted → cooking` 的 resume 機制跟初次 submit 路徑一致——user 答完問題按「Resume」，flow 回 submitted queue，chef 重 pick up。

### 4.3 Edge case 規則（已決）

| Edge case | 決議 |
|---|---|
| submitted → draft（撤回） | ✅ 允許 |
| cooking → 中途取消 | ✅ 允許，退回 draft（**沒有獨立的 `cancelled` state**） |
| approved 後改版 | 開**新版本**（new draft），帶入舊版 spec 給 user 編輯 |
| rejected → draft | rejected **紀錄保留**（audit 歷史），不覆蓋 |
| 多 spec 並行 cooking | 草創階段先考慮**單一客戶 queue**，per-customer 序列化（之後 scale 再回頭看） |

### 4.4 Versioning 與 rejected 歷史的 schema 含義
- Flow 需要 `lineage_id`（同一條 flow 的版本族） + `version` 序號
- 「新版本帶入舊版 spec」= 開新 row（state=draft, version=N+1）並複製舊版 spec content
- rejected 紀錄不被同條 flow 的下一輪覆蓋——audit log 永遠保留 state transition 流水
- chef 中途被退回 draft：chef 端應清理當輪 branch / temp artifact（清理動作待 chef 設計章節定）

### 4.4 與既有 openspec 的對應
- openspec 25 個 active proposal 多在「flow 的設計改動」層級，跟這套 lifecycle 不直接對齊
- AI Kitchen 內的 flow lifecycle 是 admin 的 first-class entity；openspec 文件當設計 reference
- 每個 lifecycle 狀態轉換進 audit log

---

## 5. Flow Types & 9-step Wizard 重訂

### 5.1 兩種 flow type

| Type | 來源 | 進 chef? | 走 wizard? |
|---|---|---|---|
| **`wizard`** | 客戶在 admin AI Kitchen 走完 10 步 wizard 產的 | ✅ | ✅ |
| **`custom`** | 顧問與客戶溝通後寫的純文字規格 | ❌（不過 chef） | ❌（沒 wizard） |

- `custom` flow 直接進人工開發 queue（顧問寫 spec、工程師接手寫 code）
- 兩種 type 共用同一套 lifecycle（draft / submitted / cooking* / on hold / committed / approved / rejected），但「cooking」對 `wizard` = chef AI 開發；對 `custom` = 工程師寫 code
- audit log 兩種都記，state transition 行為一致

### 5.2 Wizard 步驟最終排列（11 步）

調整：`GO LIVE` 退化成 lifecycle submit 按鈕；`TEST` 移除（改用 admin Sandbox tab 試跑）；`STRUCTURE` 併進 `SOURCE` 變預覽區；新增 `TRIGGER & ACCESS` / `VARIABLES` / `INTEGRATIONS` / `TRANSLATION` / `NOTES`。

| # | Step | 重點內容 | Spec 產出 |
|---|---|---|---|
| 1 | SOURCE | preset / upload / scratch + **內含 BPMN 骨架預覽**（取代舊 STRUCTURE step） | `meta`, `flow.nodes`, `flow.edges` |
| 2 | **TRIGGER & ACCESS** 🆕 | 流程怎麼啟動 + flow-level access 一頁設定。**v1：單 trigger 單 type（form）**；schema 設 `triggers[]` array 預留多 trigger / 多 type 擴充。Access 欄位：`launchable_by`、`visible_to`、（optional）`watcher` | `triggers[]`, `access` |
| 3 | **VARIABLES** 🆕 | flow-scoped 變數宣告（name / default / description / sensitive flag）；後續 step 可 reference `${var_name}` | `variables[]` |
| 4 | FORMS | userTask 欄位設計 | `userTasks[].fields[]` |
| 5 | DECISIONS | gateway 規則 (Cel)；可 reference `${var}` | `decisions[].rule` |
| 6 | APPROVERS | **Principal selector**（user/dept/group）+ role + `inherit_to_members` checkbox | `approvalNodes[].rule` |
| 7 | NOTIFY | 通知模板（email/sms/webhook 純信號）；可 reference `${var}` | `notifications[]` |
| 8 | **INTEGRATIONS** 🆕 | 外部系統結構化資料整合（endpoint + payload mapping + 觸發節點，例：結案打 ERP / HR）；URL / token 多用 `${var}` | `integrations[]` |
| 9 | SLA | 時限 + escalation | `sla`, `escalation` |
| 10 | **TRANSLATION** 🆕 | 列出所有 label（form / button / notification 等），預設空白；一鍵 AI 補空 | `labels[locale]` |
| 11 | **NOTES** 🆕 | 自由文字 textarea：搞不定的細節、特殊規則、給 chef 或顧問的 hint | `notes` |

→ wizard 結束後 user 可選擇跳 admin **Sandbox** tab 試跑驗證 → 點 **Submit 按鈕**：lifecycle transition `draft → submitted`，spec 進 chef queue

### 5.3 TRIGGER & ACCESS step 規則

**TRIGGER 部分（流程怎麼啟動）：**
- v1 限制：單一 trigger 且 type 必為 `form`
- Schema：`triggers[]` 陣列，每筆 `{ type, config }`
- form trigger 的 config：`{ form_template_ref }`（用哪張表單啟）
- 未來支援 type：`cron` / `webhook` / `mail` / `api`

**ACCESS 部分（flow-level 權限）：**
- `launchable_by_principal_ref[]` — 誰能啟動此流程
- `visible_to_principal_ref[]` — 誰看得到此 flow 在 catalog（即使不能 launch）
- `watcher_principal_ref[]` — (optional) 誰可旁觀別人的 instance（草創多數客戶用不到，先 optional）
- 注意：instance-level access（actor 看自己 inbox / admin 全看）從 actor 與 system role 自然推導，**不在 wizard 設定**

### 5.3a VARIABLES step 規則

- **Scope**：v0 限 **flow-scoped** —— 每 flow 自己一套 variables，不跨 flow 共用；global 變數之後再加（會放 Site Setting）
- **Schema**：
  ```json
  "variables": [
    {
      "name": "ERP_URL",
      "default_value": "https://erp.acme.com",
      "description": "ERP base URL",
      "sensitive": false
    },
    {
      "name": "ERP_TOKEN",
      "default_value": null,
      "description": "ERP API token",
      "sensitive": true
    }
  ]
  ```
- **Reference 格式**：`${var_name}`；可在 DECISIONS / NOTIFY / INTEGRATIONS / SLA 等後續 step 內使用
- **Sensitive 機制（v0 最簡）**：
  - 標 `sensitive: true` 的變數，admin UI 顯示時 mask（****）
  - DB 仍是 plain text 存（不接外部 vault）
  - audit log 不記錄 sensitive 值，只記變數名稱與動作（who set / when）
- **Runtime 雙層儲存**：
  - spec 帶 `variable_declarations`（schema + default）
  - bpm 另存「實際值」table（per-tenant），值可隨時 admin 上改、不用重 cook
  - bpm runtime 解 spec 看到 `${ERP_URL}` → 先讀 tenant 變數值 table，沒設用 spec 的 default
- **編輯介面**：admin AI Kitchen 該 flow 詳情頁加「變數值」tab，可即時 update（透過 syncer 推到 bpm）

### 5.4 INTEGRATIONS step 規則
- **客戶餵 OpenAPI spec**（JSON/YAML）描述外部系統介面 — 標準格式、chef AI 可直接讀
- UI 流程：
  1. 上傳 / 貼 OpenAPI spec → 系統 parse 列出 endpoints
  2. 客戶選要呼叫的 endpoint(s)
  3. 設「在哪個 flow node 觸發」（例：approved 結點後打 ERP）
  4. 設 field mapping（flow 變數 ↔ API parameter）
  5. 設 auth（bearer / api key / OAuth client creds / basic），敏感值存 secret store、spec 只存 ref
- Schema 雛形：
  ```json
  "integrations": [
    {
      "id": "...",
      "name": "...",
      "openapi_ref": "<full spec 或 URL>",
      "endpoint": { "path": "...", "method": "POST", "operationId": "..." },
      "trigger_node": "<node_id>",
      "field_mapping": { "flow.amount": "body.totalAmount" },
      "auth": { "type": "bearer", "config_ref": "secret://erp-prod-token" }
    }
  ]
  ```
- chef 收到 spec 後 read OpenAPI 直接產 HTTP client code、payload builder、retry / error handling

### 5.5 TRANSLATION step 規則
- 預設兩語：zh（主語言） + en（次語言）；schema 設 `Record<locale, string>` 預留 N 語言擴充
- AI 一鍵填空只動「空白欄位」，已填的不覆蓋
- user 仍可手動修
- bpm-ui 根據 user locale 偏好取對應 label，缺則 fallback 主語言

### 5.6 NOTES step 規則
- 草創用單一 textarea，存進 spec.notes（單一字串）
- 進 chef 時當 system prompt 補述：「客戶補充的特殊規則 / 上下文」
- 進 `custom` flow 時當顧問接手的工作筆記
- 未來可進化為「每 step 角落 sticky 側欄」聚合按 step 分組，當下念頭立即捕捉

### 5.7 對既有 wizard_audit 的衝擊
- 5 個 placeholder step (DECISIONS / APPROVERS / NOTIFY / SLA / TEST) 中 TEST 移除，其餘 4 個仍需做
- STRUCTURE 從獨立 step 降級為 SOURCE 的預覽區
- GO LIVE 從 step 改成按鈕
- 新增 4 個 step（TRIGGER / INTEGRATIONS / TRANSLATION / NOTES）需從零實作
- APPROVERS 因 Principal model 變化幅度最大（從「pick user/role」變「pick principal + role + inherit」）

---

## 6. Admin Sandbox 功能

Sandbox 是 admin 的四大功能之一，UI 在 admin、runtime 在 bpm（admin 透過 API 驅動 bpm 的 sandbox mode）。

### 6.1 草創階段 sandbox 控制項（定案）

| 控制 | 規則 |
|---|---|
| **套用對象** | `all` 全 flow / `specific` 特定 flow（多選 flow_id） |
| **信件攔截** | on / off；on 時設轉發目的地（**支援多收件人，逗號分隔**）；body 自動含原收件人提示；**不再用 SandboxCapturedMessages mailbox UI**，直接轉送 |
| **設定系統時間** | on / off；on 時設覆寫時間 — **草創只做 freeze（凍結在某時刻）**，offset / speed 模式之後再加 |
| **OutboundGate 其他通道** | 草創不攔 — webhook 真的打、SMS 移除 |

### 6.2 既有 sandbox feature 去留決議

| 既有 feature | 決議 |
|---|---|
| Mailbox 收件夾 UI | **拿掉** — 改直接轉送 |
| Persona switch（沙箱身分切換 JWT） | **保留，搬到 site setting**（不在 sandbox 控制項裡）；設定允許可切的 user list |
| State reset（整批清空） | **拿掉** — 取代為「管理員手動軟刪除」（見下方） |
| Webhook 攔截 | **拿掉** — sandbox 內 webhook 真的打 |
| SMS 攔截 | **拿掉** — SMS feature 整個移除 |
| OutboundGate（mail 之外） | mail 留（轉發）；其他全部直通 |

### 6.3 Sandbox config schema 雛形
```json
{
  "scope": { "mode": "all" | "specific", "flow_ids": [...] },
  "mail_intercept": {
    "enabled": bool,
    "redirect_to": "qa@flowcook.com, dev@flowcook.com",  // 多收件人逗號隔
    "preserve_original_recipient_in_body": true  // 既有行為
  },
  "clock_override": {
    "enabled": bool,
    "mode": "freeze",       // 草創只支援 freeze
    "fixed_time": "2026-05-20T00:00:00Z"
  }
}
```
- Persona switch / outbound 其他通道 — **不在 sandbox config**，分別在 site setting / 直通
- State reset 整批機制移除；改為 admin Audit / Process 管理介面提供管理員「手動軟刪除」instance / task（見 §6.5）

### 6.4 admin → bpm API 合約（待細談）
- admin Sandbox UI → call bpm `PUT /api/sandbox/config` 寫入設定
- bpm runtime 在 mail dispatch / clock 讀時 enforce 此設定
- per-tenant，bpm setting 跟 sandbox config 是不同概念（settings 是永久 production 行為，sandbox 是測試覆寫）


### 6.5 管理員手動軟刪除（取代 IResetService）

- **權限**：限「可切換 persona 的 user list」這群管理員（由 Site Setting 設定）
- **介面位置**：**bpm 上的 process 管理介面**（live cases / completed），管理員看得到刪除按鈕。**admin Audit 頁不提供刪除 action**（admin 只看不動）
- **行為**：管理員可單筆 / 多筆**軟刪除** process instance / task / history
- **軟刪除實作**：DB schema 加 `deleted_at` (nullable timestamp)；查詢 default filter `deleted_at IS NULL`
- **Audit**：每次軟刪除動作必進 audit log（誰刪、刪什麼、何時、reason 欄位 optional）；audit log 由 syncer 拉回 admin Audit 頁
- **不可硬刪**：production 不提供 hard delete；如需 compliance / GDPR purge，未來另設 retention 流程

### 6.6 SeedCli 重新定義（dev-only）

- **核心職責 = 清空兩顆 DB**（admin + bpm hard drop / truncate）
- demo 資料填入是 **optional**、看需求才跑（subcommand 或 flag 控制）
- 建議 subcommand 分層：
  - `seed clear` — 純清空兩顆 DB，不塞任何資料
  - `seed --org` — 清空 + 塞組織假資料（~13 user / ~6 dept / 1+ group / ~14 role / PrincipalRole / 範例 delegation）
  - `seed --org --bundles` — 清空 + 組織 + 範例 flow bundle（之後做）
- **限 dev / demo 環境**；production 禁跑（環境變數 / 啟動 guard 檢查）
- 與 production 軟刪除路徑完全分開（production 不會觸及 SeedCli）

---

## 7. Admin Audit 功能

Audit 是 admin 五大頁之一，內容由 syncer 從 bpm pull 回來。

### 7.1 Audit event schema（7 欄）
```
actor_user_id        # 誰做的
actor_principal_id   # 以哪個 role/dept/group 身份做
action_type          # created/updated/deleted/approved/rejected/login/login_fail/sync/persona_switch/...
target_type + id     # 動到什麼（process_instance / spec / principal / config / ...）
timestamp
before/after JSON    # 資料變更類動作存前後 snapshot
source_system        # admin / bpm / chef / syncer 哪邊產的
reason?              # optional comment
```

### 7.2 Append-only / Immutable
- audit row 沒 update、沒軟刪除、沒 hard delete
- 寫錯了補一筆 `correction` action_type 不動原 row
- 否則 audit 失去公信力（不能是「可被竄改的紀錄」）

### 7.3 syncer sync 策略
- syncer 從 bpm pull audit：**batch 每 5 分鐘**（草創）
- 失敗重試 at-least-once；admin 端 dedupe by `event_id`
- 未來高頻需求才升 near-realtime 或事件推送

### 7.4 之後再煩惱
- Retention policy（保多久？compliance 時再談）
- Filter / search UI：先做基本 by time + action_type，其他需要時加
- Export / download audit（合規客戶要時加）

---

## 8. 商業模型支柱

| 決策 | 意義 |
|---|---|
| admin + chef = 我們持有 | 軟體 IP 不外流；客戶要更新流程就得續約 |
| bpm 可獨立運轉 | 客戶資料主權；不續約也不會抓瞎；談判時的承諾籌碼 |
| 客戶分技術 / 非技術 tier | 非技術客戶 = 我們審 chef 產出，多收顧問費 |

---

## 8.5 Migration 策略（總結）

走 **monorepo + in-place 演化**：同一 git repo（既有 `bpm/`）內 6 個 service 資料夾。既有 `bpm-svc / bpm-ui / bpm-admin-ui` 沿用名稱演化（不凍結），新增 `bpm-admin-svc / chef / syncer` 三個空殼。舊 ProcessRuntime / SpecSnapshot / CelNet / Bundle 引擎重用不 rewrite。順序：**AI Kitchen first**（admin BE + Principal → admin FE skeleton → wizard 11 步），跑得通產 spec 再啟動 bpm refactor + syncer + chef。詳細執行計畫見 `2026-05-17-migration-plan.md`。

---

## 9. 與既有 codebase 的對應

| 現有 | 新位置（monorepo 資料夾） |
|---|---|
| `bpm-svc/` Foundation / Runtime / Bundle | 留在 `bpm-svc/`；refactor ActorResolver 走 Principal、所有 entity 加 `deleted_at`；引擎重用 |
| `bpm-svc/` admin / onboarding controllers | 搬到 `bpm-admin-svc/`（新建） |
| `bpm-svc/` Sandbox runtime（Clock / OutboundGate / Reset / Persona / Mailbox API） | 留 `bpm-svc/`；admin 透過 API 驅動 |
| `bpm-svc/` PersonaSeedService 13/6/14 | refactor 為 Principal-based SeedService，目標兩顆 DB（admin+bpm）|
| `bpm-svc/` SeedCli `reset/seed/status` console app | 改為 `seed clear` / `seed --org`（dev-only） |
| `bpm-ui/` 表單 / inbox / home / search | 留 `bpm-ui/`；DynamicForm migration（Phase 2） |
| `bpm-ui/` 既有頁面 | 留 + 接 Step 4 refactor 後的 bpm-svc API |
| `bpm-admin-ui/` Onboarding + CoPilot + ActorRefEditor | 留 `bpm-admin-ui/`，重組進新 AI Kitchen 五大頁 |
| `bpm-admin-ui/` Flow Library / Bundle 工具 | 留 `bpm-admin-ui/` → AI Kitchen |
| `bpm-admin-ui/` Sandbox Mailbox UI | **拿掉**（直接轉送，不顯示 mailbox） |
| `bpm-admin-ui/` Process Admin Console 7 區 | **拆**：Definitions / Designer / Simulator 留 `bpm-admin-ui/`（AI Kitchen 內）；Live Cases / Completed / Reports / Notifications / 人工介入 **搬到** `bpm-ui/` |
| TaskHistory / NotificationDispatchAudit | `bpm-svc/` 寫入；`syncer/` pull 回 `bpm-admin-svc/` 提供 admin **Audit** 頁 |

---

## 10. 已決事項

- [x] 品牌改為 flowcook
- [x] 四服務架構（admin / bpm / chef / syncer）
- [x] 客戶不續約 → admin 斷 / bpm 繼續運轉
- [x] 「多客戶就多 admin」，先不做 multi-tenant
- [x] 草創階段 ops UI 放 bpm 那邊（option B）
- [x] chef 的「拼字」：chef（主廚），非 chief
- [x] chef 業務 fork：技術客戶 PR、非技術客戶我們審
- [x] admin 簡化為 4 大功能（AI Kitchen / User & Role / Sandbox / Audit）+ 1 頁 bpm setting
- [x] syncer 從第一天就要 pull bpm audit log 回 admin（不是延後做）
- [x] syncer 同步三類：流程 data（含 sandbox）+ audit log（全收）+ org data
- [x] Audit 範圍：E（bpm 全部 audit event 都收）
- [x] Principal types：user / dept / group 三種
- [x] dept 嚴格 tree、group 允許嵌套（cycle 由 admin BE 擋）
- [x] user.primary_dept 可選不必填
- [x] Role assignment 帶 per-assignment `inherit_to_members` checkbox（D 選項）
- [x] 代理人獨立 table；解析順序 = principal 解完 → 查代理；對象限 user；不做 transitive chain
- [x] 底層 Principal schema 已 audit 過，cover 所有現有 bpm-svc actor 場景；YAGNI 項目（role-of-role / 時間視窗 role / conditional role / transitive 代理）明列
- [x] AI Kitchen flow lifecycle 7 個 state：draft / submitted / cooking / on hold / committed / approved / rejected
- [x] submitted / cooking 都可退回 draft；沒有獨立 cancelled state
- [x] approved 後改版 = 開新版本 draft 帶入舊 spec；版本歷史保留
- [x] rejected 紀錄保留進 audit
- [x] 草創階段 chef queue per-customer 序列化
- [x] 兩種 flow type：`wizard`（9 步 → chef）vs `custom`（顧問純文字 → 不過 chef，人工開發）
- [x] Wizard 最終 11 步：SOURCE / TRIGGER&ACCESS / VARIABLES / FORMS / DECISIONS / APPROVERS / NOTIFY / INTEGRATIONS / SLA / TRANSLATION / NOTES
- [x] STRUCTURE 併進 SOURCE 變預覽區；TEST 移除改用 admin Sandbox 試跑；GO LIVE 變按鈕
- [x] TRIGGER step v1 限「單 trigger 單 form」；schema `triggers[]` 預留多 trigger / 多 type
- [x] Step 2 改名「TRIGGER & ACCESS」，併入 flow-level 權限（launchable_by / visible_to / watcher）；instance-level 從 actor 自然推導
- [x] INTEGRATIONS step 處理結構化外部系統整合（ERP / HR / 等），與 NOTIFY（純通知信號）分開
- [x] TRANSLATION step 預設 zh+en，一鍵 AI 補空，schema 預留 N 語言
- [x] NOTES step 草創用單 textarea；未來進化為每 step sticky 側欄
- [x] INTEGRATIONS 餵 OpenAPI spec：客戶上傳 → 選 endpoint → 設觸發節點 / field mapping / auth；chef 讀 OpenAPI 直接產 HTTP client code
- [x] User & Role 草創階段：Seed CLI 塞 demo data + admin 手動 CRUD；客戶資料源 syncer 整合保留為 **TODO**（demo 不需要）
- [x] Sandbox 三大草創控制：套用對象 (all/specific) / 信件攔截 on/off + 轉發信箱 / 系統時間 on/off
- [x] Sandbox 拿掉 admin 端 mailbox UI，改直接轉送（簡化 UX）
- [x] Sandbox 信件轉發支援多收件人逗號分隔
- [x] Sandbox 時鐘覆寫草創只做 freeze（凍結時刻）
- [x] Persona switch 保留，移到 site setting，設可切 user list
- [x] State reset 保留但 admin UI 不顯示按鈕，動作進 audit
- [x] Sandbox 內 webhook 不攔（真的打），SMS 整個移除
- [x] admin 五大頁：AI Kitchen / User & Role / Sandbox / Audit / **Site Setting**（bpm 全域行為設定 + admin 自我設定併在這頁）
- [x] State reset 整批機制移除；改為管理員手動軟刪除 + audit 保留
- [x] 軟刪除介面在 bpm process 管理頁面（live/completed），admin Audit 只看不動
- [x] SeedCli 核心職責 = 清空兩顆 DB；demo 資料填入是 optional flag（dev-only）
- [x] Audit event schema 7 欄（actor / principal / action / target / timestamp / before-after / source / reason?）
- [x] Audit append-only immutable（沒 update / 沒刪除）；錯誤補 correction event
- [x] Audit sync 策略：syncer batch 每 5 分鐘 pull；at-least-once + event_id dedupe
- [x] Migration 走 monorepo + in-place 演化（單 git repo，6 個 service 資料夾），舊 ProcessRuntime 等引擎重用；詳見 migration-plan 文件
- [x] 新增 VARIABLES step（第 3 步），flow-scoped 變數宣告 + `${var}` 引用 + 變動不需重 cook；sensitive 機制 v0 最簡（mask UI、plain DB）
- [x] 認證模型 v0 = 帳號密碼，SSO 之後再加
- [x] chef v0 設計拍板：skill 規範一版一 flag / 不動核心 / flat 命名 `[CODE]_[VER]_*` / 變數一律 `${var}` 不 hardcode；詳見 chef-design 文件
- [x] on hold → submitted（不是直接 cooking）；user 改 NOTES 按 Resume 觸發；chef 重 pick up
- [x] on hold iteration cap / cost cap 草創不做

## 11. 待決議事項

### 11.1 chef 細節（v0 已拍板大方向，細部待開工調整）
- chef pipeline 內部是否拆 Dev / Review / E2E sub-agent（v0 不拆）
- chef iteration cap / cost cap（草創不做）
- chef ↔ admin 認證細節（v0 shared secret，之後升 mTLS / OAuth）
- chef 開發語言（建議 Node，待確認）

### 11.2 syncer 合約（v0 範圍已定，細部開工時對齊）
- 推送資料模型版本控制（schema migration 怎麼跨 admin/bpm 邊界）
- 衝突解決（admin 改了 org 但 bpm 也改了同一個 user — 誰贏？）
- 連線中斷 retry / 補償機制（草創 at-least-once + dedupe by event_id）
- 客戶 IdP 整合（Entra/AD/HRIS）→ 草創用 Seed CLI 假資料；正式整合保留 TODO

### 11.3 admin / bpm 邊界細節
- Reports：bpm 自帶基本 reports，admin 是否要做「跨客戶分析」的進階版？
- Notifications dispatcher：bpm 跑，但 template 在 admin 設計，syncer 怎麼帶？
- 變數值即時同步：admin 改 `${var}` 值，syncer push 延遲（草創接受 5 分鐘 batch）

### 11.4 部署 / 認證（v0 已決，餘留待擴展）
- 多 admin instance 怎麼開：草創我們手動 provision；自動化之後加
- bpm 雲端 vs self-host 切換點：草創我們雲端 host；客戶 self-host packaging 之後加
- 客戶側 SSO（Entra ID）整合：草創不做，跟客戶 IdP 整合一起延後

---

## 12. 下一輪 brainstorm 主題（依序）

1. **syncer 資料合約 v0**：先列要同步什麼 + payload 格式，不討論協議
2. **chef pipeline 細節**：用請假流程當例子走一遍 chef 路徑（Step 7 開工時）
3. **bpm-admin-svc Clean Architecture 骨架定**：Step 1 開工前確認分層
4. **客戶側 SSO + IdP 整合（v1 範圍）**

---

*文件持續更新中。每輪 brainstorm 完一塊就回來補。*
