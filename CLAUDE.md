# flowcook BPM 專案

Jason 正在開發一個商業 BPM 平台，兩人副業團隊（Jason 負責開發，夥伴負責業務導入）。夥伴從前公司帶回十幾個真實流程圖，涵蓋請假、採購、差旅、發公告等，作為 MVP 的需求基礎。

## 發行商

flowcook 的發行商為 **諮優系資訊科技有限公司**（Tsuyoshi Tech）。

- 無對外官網（`tsuyoshitech.com` 尚未啟用，網站上不要放連結）
- 公司資訊（104）：<https://www.104.com.tw/company/1a2x6bnq6x>（Cloudflare 擋 bot，無法自動抓，公司簡介需人工提供）

`bpm-www` 的 about 頁與 footer 都會標示此發行商（純文字，不附連結）。

## 產品定義

- 目標客群：中小企業（50–300 人）為起點，之後再攻大企業，用成功案例敲門
- 最終目標：賣出 POC 給潛在客戶 + 可跟微軟生態（Entra ID / AD）整合
- 設計風格：夥伴看過 "TREND BPM" 後決定的色調 / 排版（slate / blue / amber + DM Sans + Noto Sans TC）

兩大賣點（2026-05-10 拍板）：

1. **AI Kitchen onboarding** — 9-step stepper + AI 對話 + 即時生成的問卷，產 spec bundle (zip)
2. **無痛上線驗收** — sandbox：mail capture、webhook redirect、persona switch、time advance、state reset，一個驗收員就能跑完整 UAT

商業模式：第一年導入費（含送幾隻流程）+ 之後每年 30% 導入費 + 顧問點數。客戶可自 host 或我們代管。Anthropic API 多客戶時由我們代墊再 bill。

合規（FDA / 21 CFR Part 11）賣點降級為「不主打」。

## 五大專案

| 層 | 資料夾 | 角色 |
|---|---|---|
| **ADMIN** | `bpm-admin-svc/` + `bpm-admin-ui/` | flowcook 內部 + 客戶管理員：AI Kitchen / User & Role / Sandbox / Audit / Site Setting。Canonical identity 表存這 |
| **CHEF（codegen pipeline）** | `.claude/skills/{chef-codegen,lead-codegen,openspec-*}` + `chef/skill/` + `lead/skill/` | AI codegen workflow — chef 把 spec.json 翻成 per-flow 程式；lead 維護共用 primitive |
| **BPM** | `bpm-svc/` + `bpm-ui/` | 客戶端 runtime：表單 / unified inbox / 案件詳情 / 簽核；斷約後可獨立運轉 |
| **Product Website** | `bpm-www/` | 對外行銷站（Astro + Tailwind） |

部署模型：per-customer，無 multi-tenant；每客戶各一套堆疊。原本規劃的 `syncer/` 已取消 — admin ↔ bpm 改走單一 DB source（unify-user-store 系列已完成）。

## 技術棧

- **bpm-svc / bpm-admin-svc** — C# .NET 10 Clean Architecture；兩個後端**完全同架構**，五層：
  - **Api** — controllers + DTOs
  - **Application** — 商業邏輯（services / handlers / state machine / notification templates / inbox providers）
  - **Domain** — 型別（entity / value object / enum），無依賴
  - **Persistence** — EF Core（DbContext / configuration / migrations）+ EF-bound infrastructure
  - **SeedCli** — 種 demo 資料的 console
- **bpm-ui / bpm-admin-ui** — React 18 SPA + Vite + Tailwind v4 + shadcn；兩個 UI 共用同套設計 token
- **bpm-www** — Astro + Tailwind
- **codegen** — Claude Code Skill 系統，model B 手寫 per-flow（無 runtime interpreter）

## Codegen 模式 — Model B（per-flow hand-written）

每隻流程 = 一支獨立 state machine + EF entity + REST controller + React form + inbox provider。**沒有**通用 spec interpreter；`spec.json` 是設計文件，不是 runtime input。兩隻流程 = 兩個獨立狀態機 + 兩支 controller + 兩個 React component。

### chef 的 per-flow 寫入範圍（嚴格守 Clean Arch 分層）

`<CODE>` = spec 的 `meta.flowCode`（upper-case）；`<N>` = `meta.flowVersion`。

| 層 | 路徑 | 放什麼 |
|---|---|---|
| **Domain** | `bpm-svc/src/Domain/Features/<CODE>/V<N>/**` | Entity（`<CODE>_V<N>_Case`）、enum（`<CODE>_V<N>_CaseStatus`）、value object — **無依賴** |
| **Application** | `bpm-svc/src/Application/Features/<CODE>/V<N>/**` | 商業邏輯：state machine service（`<CODE>_V<N>_LeaveService`）、notification templates、`ITypedInboxProvider` impl、actor 解析 helper |
| **Persistence** | `bpm-svc/src/Persistence/Features/<CODE>/V<N>/**` | 只有 EF mapping（`<CODE>_V<N>_CaseConfiguration`） |
| **Persistence/Migrations** | `bpm-svc/src/Persistence/Migrations/<ts>_<CODE>_V<N>_*.cs` | `dotnet ef migrations add` 產的檔（含 `AppDbContextModelSnapshot.cs` 重生）|
| **Api** | `bpm-svc/src/Api/Features/<CODE>/V<N>/**` | controller + DTO |
| **Tests** | `bpm-svc/tests/Bpm.Tests/Features/<CODE>/V<N>/**` | unit + integration |
| **UI** | `bpm-ui/src/features/<CODE>/V<N>/**` | React form、case-detail、`manifest.ts`、`.bpmn.xml`（registry auto-globs `*/V*/manifest.ts`）|

### lead 的範圍

chef 邊界以外的**所有** code + 共用 primitive：

- `bpm-svc/src/{Api, Application, Domain, Persistence, Functions, SeedCli}/**` outside `Features/<CODE>/V<N>/`
- `bpm-ui/src/{components,hooks,lib,screens,assets,styles}/**` + `App.tsx` / `router.tsx`
- `bpm-admin-svc/**`（整個 admin 後端 — 同 Clean Arch 五層；chef 不碰）
- `bpm-admin-ui/**`（整個 admin 前端；chef 不碰）
- `bpm-www/**`
- `.claude/**`、`chef/**`、`lead/**`、`openspec/**`、`docs/**`

完整 system prompt：

- `chef/skill/SKILL.md` + `chef/skill/conventions.md` + `chef/skill/workflow.md`
- `lead/skill/SKILL.md`

Reference cook：**LEAVE V1**（testbed 在 `leave-test-N` 系列分支，目前最新 `leave-test-5`）— 完整一支流程的範例，新 chef session 從這隻 copy shape。⚠️ 目前 testbed 上實作把 entity / state machine / inbox provider 全擠在 `Persistence/Features/LEAVE/V1/`，與本文件的 Clean Arch 分層**不符**；先 refactor 對齊再合 main。**main 目前還沒有任何 chef-cooked flow merge 進去**。

### Model A（已退役）

`IProcessRuntime` / `SpecSnapshot` / `ISpecLoader` 是舊的「spec-driven runtime」路（一支通用引擎吃 spec.json 解釋 runtime）。複雜度比 model B 預期還高 — `leave-test-3` 是 model A 最後一次嘗試，結果 submit 進 DB 但 UI 看不到。

**舊 code 仍編譯通過但不再延伸**；新流程一律走 model B。舊 code（runtime engine、`useFormRuntime` / `useFlowSubmit` / `useFlowTask` hooks、`screens/forms/*` 的 dual-mode 11 隻表單、`/api/processes` / `/api/tasks` 通用路）的清理是獨立後續工作。

## SharedIdentity 模型

admin-svc 是 canonical identity 來源；BPM 端讀但不寫。

- admin-svc 自己有 `Bpm.Admin.Persistence` 的 principal / dept / role / delegation 表（admin 自己的 Application 層維護寫入邏輯）
- bpm-svc 的 `AppDbContext` 透過 SharedX DbSet（`SharedPrincipal` / `SharedUserManager` / `SharedUserDept` / `SharedDeptHead` / `SharedRole` / `SharedPrincipalRole`）對同一份 DB 讀；這些 entity 都標 `ExcludeFromMigrations`，EF migration 只由 admin-svc 那邊產
- chef-cooked feature 用 `db.Set<SharedXxx>()` 取數；actor 解析（manager / dept head / role）走這條路（屬 Application 層的責任）
- bpm-svc 不再有自己的 user / principal / role 表（U2 finale 已 drop）

## DB Conventions（POC SQLite，預留 Postgres / SQL Server）

POC 期跑 SQLite，但客戶上線後很可能要 Postgres / SQL Server。Code 要遵守下列 conventions，這樣遷移時改 connection string + 跑 migrations 即可，業務邏輯不動。

1. 永遠用 EF Core，禁 raw `IDbConnection` / `Dapper` — EF 兩邊跑，raw SQL 各 DB 語法不同
2. 禁 SQLite 特有函式（`json_extract`、`unixepoch`、SQLite 版 ROW_NUMBER 等）— Postgres 沒有
3. 不寫 raw SQL migration（除非 SQLite + Postgres 兩版都附）— 大多 schema 改用 EF migration 即可
4. 全文搜尋藏 `ISearchService` 介面 — FTS5（SQLite）vs tsvector + GIN（Postgres）完全不同
5. 不依賴 SQLite 的「DB-wide write lock」做 serialization — Postgres 是 MVCC row-level，依賴 SQLite 的鎖會在 Postgres 炸
6. JSON 欄位用 EF Owned types 或純 TEXT，避 query 內部 JSON path — SQLite / Postgres JSON query 語法差很多
7. 並發控制用 EF OptimisticConcurrency（RowVersion）— 兩邊都支援

## 目前進度（2026-05-25）

- **ADMIN**：`bpm-admin-svc` Clean-Arch skeleton（auth + principal / role / delegation / dept controllers）+ `bpm-admin-ui` flowcook V0 shell（AI Kitchen + User & Role pages；legacy admin shell 已 purge）已上線
- **BPM**：unify-user-store 收尾（U2 / U5 / U7 + finale）— bpm-svc 完全切到 SharedX 讀 admin 的 identity；bpm-ui Login auto-fill demo credentials；JWT roles claim normalized to array；unified inbox + bundle BPMN passthrough 都已 land
- **CHEF**：chef skill v3（model B）+ lead skill v1 + chef skill v2 unify-user-store update 都已 land；first reference cook（LEAVE V1）在 `leave-test-5` testbed 進行中 — **尚未 merge 回 main**
- **WWW**：`bpm-www` Astro skeleton + 7 個首批頁面（index / about / features / how-it-works / pricing / use-cases / why-flowcook）

## 仍要做的事（未排程）

- **chef/skill + LEAVE V1 testbed 對齊 Clean Arch 分層** — 目前 chef skill 把 entity + state machine + inbox provider 全寫進 `Persistence/Features/`；要拆到 Domain / Application / Persistence / Features/。`Persistence/DependencyInjection.cs` 的 `ITypedInboxProvider` assembly scan 也要跟著搬到 `Application/DependencyInjection.cs`，否則 chef 把 inbox provider 放對位置但 runtime 找不到
- LEAVE V1 testbed 先對齊新分層，再 merge 進 main 當第一支真實 chef cook
- Model A code 清理（runtime engine + 11 隻 `screens/forms/Reference_*.tsx` 之外的舊 hooks）
- 第二支 chef-cooked flow — 驗證 chef 流程真的可重複（LEAVE 之後 GEE / APE / 其他流程）
- NotificationDispatchAudit 表（生產通知稽核）— 目前只有 sandbox 的 `SandboxCapturedMessages`
- Reports 改用 DB-side percentile（百萬級 instance 後切）
- bpm-ui / bpm-admin-ui 加 JS test runner（目前只有 tsc + 手動 boot + chrome-devtools 截圖）
- `openspec/` 目前空殼（specs 在 `6a063b0 clean docs` 清掉了）— 等需要正式 RFC 時再啟用

## 文件入口

- [`README.md`](./README.md) — 怎麼跑、seed、test、migrate
- [`chef/skill/SKILL.md`](./chef/skill/SKILL.md) + [`chef/skill/conventions.md`](./chef/skill/conventions.md) — chef agent 的 system prompt + 路徑邊界 + primitive 對照表
- [`lead/skill/SKILL.md`](./lead/skill/SKILL.md) — lead agent 的 system prompt + chef ↔ lead 切換時機
- [`bpm-www/README.md`](./bpm-www/README.md) — 行銷站
- 各 app 的 `CLAUDE.md` — app 邊界 + 該層的最小注意事項
