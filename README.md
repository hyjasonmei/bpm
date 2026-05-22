# flowcook（前身 BPM Platform）

商業 BPM 平台 POC — 中小企業（50–300 人）為主，目標可整合微軟生態（Entra ID / AD）。

兩大賣點：
1. **AI Kitchen onboarding** — wizard + AI 對話 + 即時生成的問卷，產出 spec bundle (zip)
2. **無痛上線驗收** — 完善 sandbox：mail capture、webhook redirect、persona switch、time advance、state reset，一個驗收員就能跑完整 UAT

> **Pivot 進行中（2026-05-16 起）**：原單一 BPM 服務拆成 flowcook 四服務架構（admin / bpm / chef / syncer）。設計文件：[`.docs/flowcook-doc/2026-05-16-flowcook-pivot-design.md`](./.docs/flowcook-doc/2026-05-16-flowcook-pivot-design.md)。施工計畫見 `openspec/changes/flowcook-step1..step7-*`。

## 四服務架構

| 邏輯服務 | 資料夾 | 角色 |
|---|---|---|
| **admin** | `bpm-admin-svc/` + `bpm-admin-ui/` | flowcook 內部 + 客戶管理員用；五頁：AI Kitchen / User & Role / Sandbox / Audit / Site Setting |
| **bpm** | `bpm-svc/` + `bpm-ui/` | 客戶端 runtime；表單 / inbox / live cases / reports / 介入。**斷約後可獨立運轉** |
| **chef** | `chef/`（placeholder） | AI pipeline，Claude Code runner，產 workflow code。永遠 flowcook 持有 |
| **syncer** | `syncer/`（placeholder） | admin ↔ bpm 橋樑：push spec / user / role；pull 流程 data / audit / org |

部署模型：per-customer，無 multi-tenant；每客戶各一套堆疊。

## 技術棧

- **bpm-svc / bpm-admin-svc** — C# .NET 10 Clean Architecture (Api / Application / Domain / Persistence / SeedCli) + EF Core + SQLite (POC，code 預留 Postgres) + 自建 C# Workflow Engine
- **bpm-ui** — React 18 SPA + Vite + Tailwind v4 + shadcn（員工端）
- **bpm-admin-ui** — React 18 SPA + Vite + Tailwind v4 + shadcn + bpmn-js + lucide-react（管理端）
- 兩 UI 共用同套設計 token（slate / blue / amber + DM Sans + Noto Sans TC）

## 專案結構

```
bpm-admin-svc/        admin 後端 (.NET 10) — flowcook-step1
  src/{Bpm.Admin.Api, ...Application, ...Domain, ...Persistence, ...SeedCli}
  tests/{Api, Application, Persistence}.Tests
bpm-admin-ui/         admin 前端 — flowcook-step2
  src/flowcook/       新 shell（AppShell / pages / auth）
  src/screens/        legacy AdminLayout（flag 後可切回）
bpm-svc/              bpm 後端（Phase 1 既有，pivot step4 待 refactor）
bpm-ui/               bpm 前端（Phase 1 既有，pivot step5 待 evolve）
chef/                 placeholder — flowcook-step7
syncer/               placeholder — flowcook-step6
db/                   開發 SQLite 落地點
openspec/
  changes/            19 個 active（含 flowcook-step3..7）
  changes/archive/    12 個 2026-05-17 archived（pivot 取代）+ 2 個 2026-05-18 archived (step1/step2)
  specs/              26 個 spec（含 8 個新 flowcook-*）+ 11 個 _SUPERSEDED 標記
.docs/                pivot 設計筆記、舊 docs、dogfood-screenshots、sample_specs、spikes、screens
```

## 前置需求

- .NET 10 SDK + `dotnet-ef` global tool
- Node 18+（推薦 22.x）/ npm

```bash
dotnet --version    # 10.x
dotnet ef --version # 10.x
node --version      # 18+
```

## 啟動

### bpm 端（既有 Phase 1）

```bash
export BPM_JWT_SECRET=$(openssl rand -hex 32)
export BPM_AUTH_MODE=dev

cd bpm-svc/src/Api && dotnet run    # 5290
cd bpm-ui && npm install && npm run dev   # 5173
```

### admin 端（pivot 後新建）

```bash
cd bpm-admin-svc/src/Bpm.Admin.SeedCli
ASPNETCORE_ENVIRONMENT=Development dotnet run -- seed --org    # 13 user / 6 dept / 1 group / 14 role

cd bpm-admin-svc/src/Bpm.Admin.Api && dotnet run --launch-profile http   # 5266
cd bpm-admin-ui && npm install && npm run dev   # 5174
```

預設 demo 登入：`alice@acme.example` / `flowcook2026`

### Ports

| 服務 | Port |
|---|---|
| bpm-svc | 5290 |
| bpm-admin-svc | 5266 |
| bpm-ui | 5173 |
| bpm-admin-ui | 5174 |

## 環境變數

### bpm-svc

| Var | Default | Notes |
| --- | --- | --- |
| `BPM_AUTH_MODE` | `dev` | `dev` = JWT + `/api/dev/login`；`prod` = JWT only；`disabled` = anonymous |
| `BPM_JWT_SECRET` | — | **Required** when auth mode != `disabled`。HS256 ≥ 32 bytes |
| `BPM_SEED_ON_STARTUP` | `true` | 啟動後跑 `PersonaSeedService` 種 persona / role |
| `BPM_AI_BACKEND` | `cli` | `cli` 借用 Claude Code 訂閱；`api` 用 `ANTHROPIC_API_KEY` |
| `Spec__IncomingFolder` | — | wizard hand-off 落地 `spec.json` 的位置 |

### bpm-admin-svc

| Var | Default | Notes |
| --- | --- | --- |
| `ASPNETCORE_ENVIRONMENT` | `Production` | SeedCli 拒跑除非 `Development` 或 `FLOWCOOK_ALLOW_SEED=1` |

## 常用工具

### bpm-svc SeedCli

```bash
cd bpm-svc/src/SeedCli
dotnet run -- reset                    # 清掉 process / task / instance
dotnet run -- seed                     # 13 user / 6 dept / 14 role
dotnet run -- seed --include-bundles   # 同上 + 11 個 sample_spec bundle
dotnet run -- status                   # 印 DB 狀態
```

Sample specs 路徑 default `<repo>/sample_specs/`，pivot 後搬到 `.docs/sample_specs/`，用 `--sample-specs .docs/sample_specs/` override。

### bpm-admin-svc SeedCli

```bash
cd bpm-admin-svc/src/Bpm.Admin.SeedCli
ASPNETCORE_ENVIRONMENT=Development dotnet run -- seed --org
ASPNETCORE_ENVIRONMENT=Development dotnet run -- status
ASPNETCORE_ENVIRONMENT=Development dotnet run -- clear
```

### 測試

```bash
cd bpm-svc && dotnet test                       # Phase 1 runtime tests
cd bpm-admin-svc && dotnet test                 # admin Clean-Arch tests
cd bpm-admin-ui && npx tsc -p tsconfig.app.json --noEmit
cd bpm-ui && npx tsc -p tsconfig.app.json --noEmit
```

前端兩個 SPA 無 vitest / jest — 靠 `tsc` + 手動 boot + chrome-devtools 截圖驗證（`.docs/screens/`）。

### EF Core migrations

```bash
# bpm-svc
cd bpm-svc/src/Api
dotnet ef migrations add <Name> -p ../Persistence -s .
dotnet ef database update -p ../Persistence -s .

# bpm-admin-svc
cd bpm-admin-svc/src/Bpm.Admin.Api
dotnet ef migrations add <Name> -p ../Bpm.Admin.Persistence -s .
dotnet ef database update -p ../Bpm.Admin.Persistence -s .
```

## DB Conventions（POC SQLite，預留 Postgres）

1. 永遠用 EF Core，禁 raw `IDbConnection` / `Dapper`
2. 禁 SQLite 特有函式（`json_extract`、`unixepoch` 等）
3. 不寫 raw SQL migration（除非兩版都附）
4. 全文搜尋藏在 `ISearchService` 介面後面
5. 不依賴 SQLite 的 DB-wide write lock 做 serialization
6. JSON 欄位用 EF Owned types 或純 TEXT，避 query 內部 JSON path
7. 並發控制用 EF OptimisticConcurrency（RowVersion）

## 進度（2026-05-18）

### Phase 1（pre-pivot，已完）

- **bpm-svc**: Foundation (Org / Authz / Sandbox / Auth / HrFlows / Spec ActorRef)、Runtime (ProcessRuntime + SpecSnapshot + Cel.NET)、Bundle (Builder / Parser / Validator + RuntimeLoader + ReproRunner)、Sandbox (clock decorator / OutboundGate / ResetService / persona switch JWT / Mailbox API)、Process Admin (simulator + 4 intervention endpoints + reporting service)
- **bpm-ui**: 11 個 demo form 全走 ProcessRuntime；Home / Search 接真 inbox；3 個 runtime hook + RoleSwitcher + SandboxBanner + Mailbox
- **bpm-admin-ui** (legacy): 9-step onboarding + CoPilotCanvas + Flow Library + Sandbox Mailbox + Process Admin Console
- All-flows-real PR-L1..L6: 11 sample_specs 對齊 workflow.ts + 22 sub-test E2E

### Pivot 進度（flowcook-step1..step7）

| Step | 範圍 | 狀態 |
|---|---|---|
| 1 | `bpm-admin-svc` skeleton（Clean Arch + auth + principal/role/delegation/dept controllers） | ✅ archived `2026-05-18-flowcook-step1` |
| 2 | `bpm-admin-ui` flowcook shell（5-page nav + LoginPage + User & Role page 含完整 principal/role 管理 UI） | ✅ archived `2026-05-18-flowcook-step2` |
| 3 | AI Kitchen wizard（11-step） | next — proposal in flight |
| 4 | bpm-svc refactor 對齊新 spec | TBD |
| 5 | bpm-ui DynamicForm migration（原 Phase 2 `add-form-runtime-rendering`） | TBD |
| 6 | syncer v0（push spec / pull audit） | placeholder |
| 7 | chef v0（AI pipeline） | placeholder |

### 還在排程的舊 proposal（13 個）

`add-form-runtime-rendering` / `add-real-reporting` / `add-sso-oidc` / `add-mcp-entra-sync` / `add-pdf-export` / `add-tenant-branding` / `add-file-storage` / `add-outbound-webhooks` / `add-real-search` / `add-comments-and-rejection-feedback` / `add-calendar-and-business-hours` / `add-hr-sync-csv` / `add-api-observability` / `extend-field-types-line-items` — 每一個都有 `FLOWCOOK_STATUS.md` 標記要 reframe 到 pivot 後的服務邊界再實作。

## 已知 follow-up

- AppShell 右上 page hint 不會跟著 sub-tab 切換更新（永遠顯示 sidebar nav 的 hint）
- RoleEditor 的「Usage」count 是 N+1 probe（拉每個 principal 的 role list 數）；20 個 principal 內可用，scale up 需要 BE 加 `GET /api/roles/{id}/usage`
- bpm-admin-svc 沒有 `DbPathResolver`，SeedCli 跟 Api 各自 cwd 對應 `admin.dev.db` 兩份（暫時用 `cp` 同步）— 學 bpm-svc 修
- Cel.NET 1.0.0 sum(list) overload-id dispatch bug — gateway flat field workaround
- BPMN canvas active-node 高亮、Designer live preview 仍 placeholder
- NotificationDispatchAudit 表（生產通知稽核）未建表
- Reports in-memory percentile — 百萬級 instance 後切 DB function
- HrFlowsController（RESIGN/DEPTX 舊路）與新 spec 並存
- bpm-ui / bpm-admin-ui 沒 vitest/jest

## 文件

- [CLAUDE.md](./CLAUDE.md) — 專案背景、技術棧、DB conventions
- [.docs/flowcook-doc/2026-05-16-flowcook-pivot-design.md](./.docs/flowcook-doc/2026-05-16-flowcook-pivot-design.md) — pivot 設計筆記
- [openspec/](./openspec) — 19 個 active proposal（flowcook-step3..7 + 14 個舊 reframe-pending） + 26 個 spec
- [.docs/screens/](./.docs/screens/) — UI 對齊 / 驗證 reference 截圖
