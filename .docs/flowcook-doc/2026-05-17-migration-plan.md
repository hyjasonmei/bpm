# flowcook — Migration Plan（monorepo in-place 演化）

> Status: **DRAFT**
> Date: 2026-05-17（v2，調整為 monorepo + in-place）
> Related: `2026-05-16-flowcook-pivot-design.md`（總體設計）

從舊 BPM 平台 pivot 到 flowcook 四服務架構的執行計畫。方向：**clean + simple**，**同一 monorepo 內 in-place 演化既有服務 + 新增三個空殼服務**。

---

## 1. 為什麼 monorepo + in-place

放棄原 v1「greenfield 4 repo + 凍結舊」方案，改為單 monorepo 漸進演化，因為：

- 既有 bpm-svc 的 ProcessRuntime / SpecSnapshot / CelNet 引擎 / Bundle Builder 投入大，**能重用就重用**，不適合 rewrite from scratch
- 同 repo 便於共用 db schema 版控、跨服務 contract 對齊、單 CI、單 PR review 工作流
- 「`bpm-` 前綴」沿用既有命名，新增 admin BE 與兩個輔助服務即可
- 設計上仍是「四服務架構」（admin / bpm / chef / syncer），只是同 repo 內以資料夾呈現

新架構與舊的差異（仍要 refactor）：

| 差異點 | 舊（現況） | 新（目標） |
|---|---|---|
| 服務數 | 3（bpm-svc / bpm-ui / bpm-admin-ui） | 6 個資料夾：上 3 個 + bpm-admin-svc / chef / syncer |
| DB 邊界 | 單一 DB（bpm-svc 內 EF Context） | admin DB + bpm DB 兩顆，syncer 橋接 |
| 組織模型 | Persona / Role / Dept（PersonaSeedService 13/6/14） | Principal（user/dept/group）+ Role + Delegation 七表 + materialized view |
| ActorRef | user / persona / role | principal / role；persona 移到 sandbox |
| Flow types | 單一概念 | wizard / custom 兩種 |
| Wizard | 9 step（5 個 placeholder） | 11 step 重組（含 TRIGGER&ACCESS / VARIABLES / INTEGRATIONS / TRANSLATION / NOTES） |
| Sandbox | mailbox UI + IResetService + persona JWT 全在 bpm-svc | 信件轉送、freeze、軟刪除分散到 bpm + Site Setting |
| Lifecycle | 沒明確 | 7 state（draft / submitted / cooking / on hold / committed / approved / rejected） |
| 通知 | Notify hardcoded 規則 | INTEGRATIONS 走 OpenAPI；NOTIFY 純通知信號 |
| 軟刪除 | 沒（IResetService 整批） | 有（`deleted_at`）；管理員手動 |

---

## 2. monorepo 結構

```
/Users/jason/claude/bpm/
├── .docs/
│   ├── flowcook-doc/          ← 新版規格（本文件 + 設計總文件 + chef-design）
│   ├── old-docs/              ← 舊設計文件歸檔
│   ├── refs/ sample_specs/ spikes/ dogfood-screenshots/
├── bpm-svc/             (既有 → 演化)    ← bpm BE / runtime / DB
├── bpm-ui/              (既有 → 演化)    ← bpm FE / 表單 / inbox / ops 4 區
├── bpm-admin-svc/       ✨ NEW          ← admin BE / Principal / Audit aggregate
├── bpm-admin-ui/        (既有 → 演化)    ← admin FE / 五大頁
├── chef/                ✨ NEW          ← AI 流水線
├── syncer/              ✨ NEW          ← admin ↔ bpm 橋樑
├── db/
├── openspec/
├── CLAUDE.md / README.md
```

**「演化」實作策略：**

| 資料夾 | 路徑 | 演化方式 |
|---|---|---|
| bpm-svc | `bpm-svc/` | 在現有 .NET 專案內：① 移除 admin / onboarding 邏輯（搬去 bpm-admin-svc）② Refactor PersonaSeedService → Principal schema ③ ActorResolver 走 Principal ④ 所有 entity 加 `deleted_at` |
| bpm-ui | `bpm-ui/` | 在現有 React 專案內：① 接收 ops 4 區（從 bpm-admin-ui 搬過來：Live Cases / Completed / Reports / 通知 / 介入）② DynamicForm migration（Phase 2 add-form-runtime-rendering）③ 軟刪除按鈕 UI |
| bpm-admin-ui | `bpm-admin-ui/` | 在現有 React 專案內：① 重組為五大頁（AI Kitchen / User & Role / Sandbox / Audit / Site Setting）② AI Kitchen 從 9 step 改 11 step ③ 移除 Process Admin Console 內 ops 4 區（搬去 bpm-ui）④ 串新 bpm-admin-svc API |
| bpm-admin-svc | `bpm-admin-svc/` | 全新 .NET 專案：Clean Architecture / Principal 七表 / Audit aggregate / flow lifecycle / `${var}` resolver / chef on-hold callback |
| chef | `chef/` | 全新服務：pull spec → 跑 Claude Code SDK → output bundle |
| syncer | `syncer/` | 全新服務：admin ↔ bpm 雙向同步 |

舊設計文件已歸 `.docs/old-docs/`，作為 reference 不再修改；新規格放 `.docs/flowcook-doc/`。

---

## 3. 實作順序（AI Kitchen first）

每 step 是一個可獨立 PR 的里程碑。

### Step 1：bpm-admin-svc + Principal schema
- 新建 .NET 專案，Clean Architecture 分層（API / Application / Persistence）
- EF Core entity 七表：Principal / UserDept / DeptParent / GroupMember / Role / PrincipalRole / Delegation
- 寫 `effective_principal_role` materialized view（草創可先 query-time 計算）
- 第一支 controller：`GET /api/principals`、`GET /api/roles`
- DB conventions 沿用既有（無 raw SQL、EF Owned types、optimistic concurrency 等）

### Step 2：bpm-admin-ui 重組五大頁 skeleton
- 在既有 React 專案內改 nav，五大頁空殼：AI Kitchen / User & Role / Sandbox / Audit / Site Setting
- User & Role 頁串 Step 1 的 `/api/principals` 顯示 list
- 老舊 onboarding wizard 頁面暫保留（下一 step 重做）
- 帳號密碼登入（Site Setting 內配置）

### Step 3：AI Kitchen 11 步 wizard
- bpm-admin-ui AI Kitchen 頁實作 11 step：
  - SOURCE / TRIGGER&ACCESS / VARIABLES / FORMS / DECISIONS / APPROVERS / NOTIFY / INTEGRATIONS / SLA / TRANSLATION / NOTES
- 翻舊 9-step wizard 的 SOURCE / FORMS step UI 重用
- 4 個 placeholder step (DECISIONS / APPROVERS / NOTIFY / SLA) 從零做
- 5 個全新 step (TRIGGER&ACCESS / VARIABLES / INTEGRATIONS / TRANSLATION / NOTES) 從零做
- Submit 按鈕串 lifecycle transition `draft → submitted`；無 chef 時停在 submitted、spec JSON 給人類看

### 🎯 Milestone A：AI Kitchen 可產 spec JSON
- 客戶在 admin 走完 11 step，輸出完整 spec
- 不需要 bpm runtime / chef / syncer 都能 demo

### Step 4：bpm-svc refactor + 軟刪除
- 在既有 .NET 專案內：
  - 移除 admin / onboarding / Process Admin Console 後端邏輯（搬去 bpm-admin-svc 或刪）
  - PersonaSeedService → Principal-based SeedService（admin DB 也要 seed）
  - ActorResolver 改用 Principal model（保留 Persona 概念但只在 Sandbox 用）
  - 所有 instance / task / history schema 加 `deleted_at` + EF global filter
  - SeedCli `seed clear` / `seed --org`（兩顆 DB drop + 塞組織假資料）
- ProcessRuntime / SpecSnapshot / CelNet / Bundle 引擎保留沿用（重用！）

### Step 5：bpm-ui 演化 + ops 4 區搬遷
- 在既有 React 專案內：
  - 從 bpm-admin-ui 搬：Live Cases / Completed / Reports / 通知 / 人工介入
  - 接 Step 4 refactor 後的 bpm-svc API
  - 軟刪除按鈕 UI（限 persona-switch user list）
  - DynamicForm migration（Phase 2 add-form-runtime-rendering）

### Step 6：syncer v0
- 全新 service（.NET console / hosted service）
- v0 範圍：
  - Push admin → bpm：Principal / Role / Delegation 變動
  - Pull bpm → admin：audit log（每 5 分鐘 batch）
  - Push admin → bpm：`${var}` variable 值
- 失敗 retry at-least-once + dedupe by event_id
- 認證：v0 shared secret

### Step 7：chef v0
- 全新 service
- v0：「pull submitted spec → 跑 Claude Code → output bundle」最小路徑
- on hold 回 callback admin API
- 不開 PR；tech 客戶 PR 模式留下版迭代
- 詳見 `2026-05-17-chef-design.md`

### 🎯 Milestone B / Cutover：請假流程 e2e
- admin AI Kitchen 走完 11 步 → submit → chef cook → syncer push → bpm 跑請假 case → audit 同步回 admin
- 此時舊 onboarding wizard / Process Admin Console / PersonaSeedService 的歷史程式碼可以正式刪除

---

## 4. Test 策略

- 313 個現有 test **不丟**，跟 bpm-svc refactor 同步演化：
  - Step 4 時 PersonaSeedService 相關 test 改成 Principal-based
  - Process / Task / Sandbox 相關 test 加 `deleted_at` 適配
  - 其他保留
- 新資料夾每 step 自己長 test：
  - Step 1：Principal schema + materialized view 邏輯
  - Step 3：每個 wizard step validator + lifecycle transition
  - Step 6：syncer push/pull + dedupe
  - Step 7：chef happy path + on-hold callback
- E2E：Milestone B 後寫跨服務 e2e test

---

## 5. 風險與緩解

| 風險 | 緩解 |
|---|---|
| Step 4 refactor bpm-svc 時打壞 313 個既有 test | 分 PR 漸進改：先 Principal entity 新增（不刪舊），ActorResolver 新舊並行；舊路徑廢棄後再清 |
| 兩個 DB schema 變動失同步 | bpm-admin-svc / bpm-svc 共享 db schema 文件；syncer Step 6 用 contract test 鎖住 |
| 軟刪除實作漏掉某張表 | base entity 加 `ISoftDeletable` interface + EF global filter，新表自動帶 |
| Principal materialized view 重算成本 | 草創先 query-time 計算；view 後續性能瓶頸時加 |
| chef 未做時 demo 不到 cooking 後行為 | Milestone A 已能 demo spec 產出；Step 4-5 完還能手動把 spec 餵 bpm 跑（bypass chef）；chef 是錦上添花 |
| 既有 ops 4 區搬遷時資料連動 | Step 5 期間 bpm-admin-ui Process Admin Console 跟 bpm-ui 並存一段；新 UI 接 API 後再下舊頁 |
| openspec 25 proposal 對齊度 | 移植過程逐一檢視，與新架構衝突的標 archived（移到 `.docs/old-docs/`） |

---

## 6. 估時（粗）

| Step | 估時 |
|---|---|
| 1. bpm-admin-svc + Principal | 1.5 週 |
| 2. bpm-admin-ui 五大頁 skeleton | 0.5 週 |
| 3. AI Kitchen 11 步 wizard | 3 週 |
| 🎯 Milestone A | — |
| 4. bpm-svc refactor + 軟刪除 | 2 週 |
| 5. bpm-ui ops 搬遷 + DynamicForm | 1.5 週 |
| 6. syncer v0 | 1 週 |
| 7. chef v0 | 1.5 週 |
| 🎯 Milestone B / Cutover | — |
| **合計** | **~11 週** |

> Jason 副業節奏（晚上 + 週末）下推估，全職全速可壓到 5-6 週。

---

## 7. 已決 / 待決議

**已決**
- 使用者認證 v0 = 簡單帳號密碼；SSO 之後再加
- admin / bpm 不做 SSO（v0 各自登入）
- bpm 部署形態 v0：我們雲端 host（self-host packaging 之後再加）
- chef v0 機制：直接 push bundle 不開 PR；tech 客戶 PR 模式 v1 起做
- monorepo + in-place 演化（不再走 greenfield 4 repo）
- 既有 ProcessRuntime / SpecSnapshot / CelNet / Bundle 引擎重用，不 rewrite

**待決議**
- chef 開發語言選 .NET / Node / Python？建議 Node（用既有 Claude Code SDK runner）
- 服務間認證：admin ↔ bpm syncer 用 shared secret v0 → 之後升 mTLS 還 OAuth client creds
- bpm-svc 內 admin 後端邏輯（onboarding controllers、Process Admin Console API、Sandbox Mailbox controller）→ 全搬 bpm-admin-svc，還是部分留 bpm-svc？預設搬

---

## 8. 下一步

1. Jason 拍板 plan
2. 在 bpm-admin-svc/ 內 `dotnet new` 起 Clean Architecture 骨架 → 開 Step 1 PR
3. 同步在 bpm-admin-ui/ 內準備五大頁 nav 改造（Step 2）
