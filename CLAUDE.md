專案背景
Jason 正在開發一個商業 BPM 平台，兩人副業團隊（Jason 負責開發，夥伴負責業務導入）。夥伴從前公司帶回十幾個真實流程圖，涵蓋請假、採購、差旅、發公告等，作為 MVP 的需求基礎。

技術棧

前端：React 18 SPA + Tailwind + shadcn

後端：C# .NET Core Clean Architecture
EF Core
POC用SQL LITE即可
專案分層
API - controllers
Application - Business Logic (Services, Handlers, Helpers)
Persistence - EF Core

流程引擎：自建 C# Workflow Engine

DB Conventions (POC 用 SQLite，code 預留 Postgres 路徑)

POC 期跑 SQLite，但客戶上線後很可能要 Postgres / SQL Server。code
要遵守下列 conventions，這樣遷移時改 connection string + 跑
migrations 即可，業務邏輯不動。

1. 永遠用 EF Core，禁 raw `IDbConnection` / `Dapper` — EF 兩邊跑，raw SQL 各 DB 語法不同
2. 禁 SQLite 特有函式（`json_extract`、`unixepoch`、SQLite 版 ROW_NUMBER 等）— Postgres 沒有
3. 不寫 raw SQL migration（除非 SQLite + Postgres 兩版都附）— 大多 schema 改用 EF migration 即可
4. 全文搜尋藏 `ISearchService` 介面 — FTS5 (SQLite) vs tsvector + GIN (Postgres) 完全不同
5. 不依賴 SQLite 的「DB-wide write lock」做 serialization — Postgres 是 MVCC row-level，依賴 SQLite 的鎖會在 Postgres 炸
6. JSON 欄位用 EF Owned types 或純 TEXT，避 query 內部 JSON path — SQLite/Postgres JSON query 語法差很多
7. 並發控制用 EF OptimisticConcurrency (RowVersion) — 兩邊都支援

產品定義
目標客群： 中小企業（50-300人）為起點，之後再攻大企業，用成功案例敲門
MVP 功能範圍：

根據PDF生成POC，其中夥伴非常喜歡 "TREND BPM"的設計風格
只要頁面圖案有TREND BPM都可當作參考作為我們的網頁風格

最終目標: 賣出POC給潛在客戶並可跟微軟生態 (Entra ID, AD) 整合

兩大賣點 (2026-05-10 brainstorm 拍板)
1. AI onboarding — 9-step stepper + AI 對話 + 即時生成的問卷，產 spec bundle (zip)
2. 無痛上線驗收 — 完善 sandbox（mail capture、webhook redirect、persona switch、time advance、state reset），一個驗收員就能跑完整 UAT
   合規 (FDA / 21 CFR Part 11) 賣點降級到「不主打」(audit-immutability proposal archived)

商業模式
第一年導入費（含送幾隻流程）+ 之後每年 30% 導入費 + 顧問點數，大量需求另購點數。多客戶時 Anthropic API 用我們代墊，bill 客戶。Bundle 可由客戶自 host 也可以我們代管。

現在進度 (2026-05-11 晚 — PR-L6 後)
foundation 三角 + 兩大賣點 + Process Admin + 全部 demo flow 真化（all-flows-real Phase 1）已完。openspec 25 個 active proposal。實作面：

bpm-svc：
- Foundation: Org/Authz/Sandbox/Auth/HrFlows/Spec(ActorRef)
- Runtime: ProcessRuntime(IProcessRuntime+SpecSnapshot+CelNet 引擎) + REST API
- Bundle: BundleBuilder/Parser/Validator + SpecBundle 持久化 + RuntimeLoader（scratch tenant 隔離）+ ReproRunner（含 notification assertions）+ /api/admin/flow-library
- Sandbox: SandboxClock decorator + capture-only OutboundGate + IResetService（ExecuteDelete 繞過 interceptor）+ persona switch JWT + Mailbox API
- Process Admin: IProcessSimulator（dry-run）+ admin intervention 4 endpoints + IProcessReportingService（5min cache）+ /api/admin/process-admin
- All flows real (PR-L1..L6): 11 個 sample_specs/*.json 全對齊 workflow.ts FormCode + AllFlowsRealE2ETests 22 sub-test 涵蓋全 happy/reject path
- SeedCli console app: `dotnet run --project bpm-svc/src/SeedCli -- reset|seed|status|seed --include-bundles`，PersonaSeedService 13 user / 6 dept / 14 role 一鍵到位
- 313 tests 全綠 (256 → 313 PR-L 期間 +57)

bpm-ui：
- 11 個 demo form 全部走 ProcessRuntime（PR-L2 dual mode: create + task）
- Home / Search 接 GET /api/processes/mine + GET /api/tasks/mine 真實 inbox（PR-L3）
- 3 個新 hook: useFlowSubmit / useFlowTask / useFormRuntime；FormShell ActionBar 內建 Approve/Reject/Return + comment dialog
- RoleSwitcher（含沙箱 persona 模式）+ process API client + SandboxBanner（capture/clock 計數）+ Sandbox Mailbox

bpm-admin-ui：
- 9-step onboarding + CoPilotCanvas + ActorRefEditor + ExpressionInput
- Flow Library（list/detail/import/export/repro；BundleInstaller primitive PR-L4 起 SeedCli / FlowLibraryController 共用）
- Sandbox Mailbox（Mail/Webhooks/SMS/Clock 4 tabs + 計數 badge）
- Process Admin Console（Definitions/Designer/Simulator/Live Cases/Completed/Reports/Notifications 7 sections）

Demo guard 解封狀態（Phase 1）：
- 已解封：bpm-ui `screens/forms/`、`screens/Home.tsx`、`screens/Search.tsx`、`lib/workflow.ts`（lib/workflow.ts FORMS map 留作 label / 中文名 / step list 設定，非流程定義）
- 仍受保護：bpm-ui `screens/Report.tsx`（等 add-real-reporting）

當前目標 / 下一步：
**Phase 2 入口：`add-form-runtime-rendering`** — Jason + 夥伴看完 Phase 1 demo 後啟動。完成後 11 個 hand-coded form → `<DynamicForm spec={...} />`，spec 改欄位自動同步、view-only forms 合併、~2000 行 React 縮成 1 component。

Phase 2 之後剩下的 24 個 proposal 多為單點增強（SLA timer / outbound webhooks / file storage / soft delete / pdf export / i18n / tenant branding / sso oidc / mcp entra sync 等）。優先排序待 Jason 與夥伴 align。

已知 follow-up（記在原處 + 各 PR commit message）：
- Cel.NET 1.0.0 的 sum(list) runtime overload-id dispatch bug — gateway 用 flat field workaround
- BPMN canvas active-node 高亮、live form preview 在 Designer 裡都還是 placeholder
- 第一級 NotificationDispatchAudit 表（生產環境通知稽核）尚未建表，目前靠 SandboxCapturedMessages（沙箱模式）涵蓋
- Reports 用 in-memory percentile 計算（百萬級 instance 後需切到 DB function）
- HrFlowsController（RESIGN/DEPTX 舊路）與新 spec 並存中，PR-LZ 排程退役（Phase 2 後再決定）
- bpm-ui / bpm-admin-ui 沒 vitest/jest，前端 form 邏輯靠 tsc + 手動 boot 驗證
- Home `Activity Feed` / `Reminders` widget 仍 MOCK data（小 demo 標記留著）