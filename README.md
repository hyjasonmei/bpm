# flowcook（前身 BPM Platform）

商業 BPM 平台 POC — 中小企業（50–300 人）為主，目標可整合微軟生態（Entra ID / AD）。

兩大賣點：

1. **AI Kitchen onboarding** — wizard + AI 對話 + 即時生成的問卷，產出 spec bundle (zip)
2. **無痛上線驗收** — 完善 sandbox：mail capture、webhook redirect、persona switch、time advance、state reset，一個驗收員就能跑完整 UAT

詳細產品定位、六大專案邊界、Codegen 模式（model B）、SharedIdentity 模型、DB conventions 全在 [`CLAUDE.md`](./CLAUDE.md)。

## 六大專案

| 層 | 資料夾 | 角色 |
|---|---|---|
| **ADMIN** | `bpm-admin-svc/` + `bpm-admin-ui/` | 內部 + 客戶管理員：AI Kitchen、User & Role、Sandbox、Audit、Site Setting；canonical identity 表存這 |
| **CHEF（codegen）** | `.claude/skills/` + `chef/skill/` + `lead/skill/` | AI codegen workflow — chef 把 spec.json 翻成 per-flow 程式；lead 維護共用 primitive |
| **BPM** | `bpm-svc/` + `bpm-ui/` | 客戶端 runtime：表單、unified inbox、案件詳情、簽核 |
| **Product Website** | `bpm-www/` | 對外行銷站（Astro + Tailwind） |
| **Guide Docs** | `bpm-docs/` | 客戶導入/使用手冊（Astro + Starlight）→ <https://guide.flowcook.ai> |

`syncer/` 規劃已取消 — admin ↔ bpm 改走單一 DB source（unify-user-store 系列完成）。

## 專案結構

兩個後端專案（`bpm-svc` / `bpm-admin-svc`）**完全同 Clean Architecture 五層**：Api / Application / Domain / Persistence / SeedCli。

```
bpm-admin-svc/        admin 後端 (.NET 10)
  src/
    Bpm.Admin.Api          — controllers + DTOs
    Bpm.Admin.Application  — 商業邏輯（services / handlers）
    Bpm.Admin.Domain       — 型別（entity / value object / enum）
    Bpm.Admin.Persistence  — EF Core（DbContext / configuration / migrations）
    Bpm.Admin.SeedCli      — 種 demo 資料的 console
  tests/{Api, Application, Persistence}.Tests

bpm-admin-ui/         admin 前端 (Vite + React)
  src/flowcook/       現行 shell（AppShell / pages / auth / api）
  src/screens/onboarding/  AI Kitchen 9-step wizard（被 flowcook shell 載入）

bpm-svc/              bpm 後端 (.NET 10) — 同五層
  src/
    Api/         controllers + DTOs
      Features/<CODE>/V<N>/     ← chef territory（main 目前空）
    Application/ 商業邏輯（services / state machine / notification / inbox provider）
      Features/<CODE>/V<N>/     ← chef territory（main 目前空）
    Domain/      型別（entity / value object / enum）
      Features/<CODE>/V<N>/     ← chef territory（main 目前空）
    Persistence/ EF Core
      Features/<CODE>/V<N>/     ← chef territory，只放 EF mapping（main 目前空）
      Migrations/               ← chef 的 <CODE>_V<N>_* migration 落這
    Functions/   非 HTTP 的長跑（background / cron）
    SeedCli/

bpm-ui/               bpm 前端 (Vite + React)
  src/features/<CODE>/V<N>/   ← chef territory（main 目前只有 registry.ts）
  src/screens/forms/Reference_*.tsx  ← 11 隻 model A reference forms（可讀，新流程不擴）

bpm-www/              對外行銷站 (Astro)
chef/                 chef agent 的 system prompt（SKILL.md + conventions.md + workflow.md）
lead/                 lead agent 的 system prompt（SKILL.md）
.claude/skills/       Claude Code skill dispatch（chef-codegen / lead-codegen / openspec-*）
.docs/                MVP_DEMO_RUNBOOK + spikes
db/                   開發 SQLite 落地點
openspec/             空殼（specs 在 6a063b0 清掉，待需要時再啟用）
```

進行中的 chef testbed 在 `leave-test-N` 系列分支（目前最新 `leave-test-5`），尚未 merge 回 main。

## 前置需求

- .NET 10 SDK + `dotnet-ef` global tool
- Node 18+（推薦 22.x）/ npm

```bash
dotnet --version    # 10.x
dotnet ef --version # 10.x
node --version      # 18+
```

## 啟動

### bpm 端

```bash
export BPM_JWT_SECRET=$(openssl rand -hex 32)
export BPM_AUTH_MODE=dev

cd bpm-svc/src/Api && dotnet run    # 5290
cd bpm-ui && npm install && npm run dev   # 5173
```

### admin 端

```bash
cd bpm-admin-svc/src/Bpm.Admin.SeedCli
ASPNETCORE_ENVIRONMENT=Development dotnet run -- seed --org    # 13 user / 6 dept / 1 group / 14 role

cd bpm-admin-svc/src/Bpm.Admin.Api && dotnet run --launch-profile http   # 5266
cd bpm-admin-ui && npm install && npm run dev   # 5174
```

預設 demo 登入：`alice@acme.example` / `flowcook2026`

### 行銷站

```bash
cd bpm-www && npm install && npm run dev    # 4321（Astro 預設）
```

### Ports

| 服務 | Port |
|---|---|
| bpm-svc | 5290 |
| bpm-admin-svc | 5266 |
| bpm-ui | 5173 |
| bpm-admin-ui | 5174 |
| bpm-www | 4321 |

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
dotnet run -- seed                     # 與 admin 對齊的 persona / role
dotnet run -- seed --include-bundles   # 同上 + sample_spec bundle
dotnet run -- status                   # 印 DB 狀態
```

Sample specs 路徑預設 `<repo>/sample_specs/`，用 `--sample-specs <dir>` override。

### bpm-admin-svc SeedCli

```bash
cd bpm-admin-svc/src/Bpm.Admin.SeedCli
ASPNETCORE_ENVIRONMENT=Development dotnet run -- seed --org
ASPNETCORE_ENVIRONMENT=Development dotnet run -- status
ASPNETCORE_ENVIRONMENT=Development dotnet run -- clear
```

### 測試

```bash
cd bpm-svc && dotnet test
cd bpm-admin-svc && dotnet test
cd bpm-ui && npx tsc -p tsconfig.app.json --noEmit
cd bpm-admin-ui && npx tsc -p tsconfig.app.json --noEmit
```

前端兩個 SPA 沒有 vitest / jest — 靠 `tsc` + 手動 boot + chrome-devtools 截圖驗證。

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

**注意**：bpm-svc 端的 SharedX entity（`SharedPrincipal` / `SharedUserManager` / ...）都標 `ExcludeFromMigrations`，所以 identity 表的 migration 只會在 admin-svc 那邊產，bpm-svc 端不會雙寫 schema。

## DB Conventions（POC SQLite，預留 Postgres）

1. 永遠用 EF Core，禁 raw `IDbConnection` / `Dapper`
2. 禁 SQLite 特有函式（`json_extract`、`unixepoch` 等）
3. 不寫 raw SQL migration（除非兩版都附）
4. 全文搜尋藏在 `ISearchService` 介面後面
5. 不依賴 SQLite 的 DB-wide write lock 做 serialization
6. JSON 欄位用 EF Owned types 或純 TEXT，避 query 內部 JSON path
7. 並發控制用 EF OptimisticConcurrency（RowVersion）

詳細理由見 `CLAUDE.md`。

## Codegen 模式 — Model B

每隻流程 = 一支獨立 state machine + EF entity + REST controller + React form + inbox provider。**沒有**通用 spec interpreter。Chef 把 `spec.json` 當設計文件、手寫對應的 per-flow 程式，按 Clean Arch 分層落在 bpm-svc 各 csproj：

```
bpm-svc/src/Domain/Features/<CODE>/V<N>/**              ← entity / enum / VO
bpm-svc/src/Application/Features/<CODE>/V<N>/**         ← state machine / notification / inbox provider
bpm-svc/src/Persistence/Features/<CODE>/V<N>/**         ← EF mapping only
bpm-svc/src/Persistence/Migrations/<ts>_<CODE>_V<N>_*.cs
bpm-svc/src/Api/Features/<CODE>/V<N>/**                 ← controller + DTO
bpm-svc/tests/Bpm.Tests/Features/<CODE>/V<N>/**
bpm-ui/src/features/<CODE>/V<N>/**
```

Lead 維護所有 chef 邊界以外的 share code + primitive（`components/ui/*`、`Bpm.Application.Inbox.ITypedInboxProvider`、`IFileStorageService`、SharedIdentity、auth、sandbox、bundle install 等），以及整個 `bpm-admin-svc` / `bpm-admin-ui`。

Reference cook：**LEAVE V1**（testbed 在 `leave-test-N` 系列分支，目前最新 `leave-test-5`）。chef session 從這隻 copy shape。⚠️ 該 testbed 目前還把 entity / state machine / inbox provider 都擠在 `Persistence/Features/`，與本文件分層**不符**；refactor 排隊中。**main 目前還沒有任何 chef-cooked flow merge 進去**。

完整邊界 + 命名 + primitive 對照：

- `chef/skill/SKILL.md` + `chef/skill/conventions.md` + `chef/skill/workflow.md`
- `lead/skill/SKILL.md`

### Model A（已退役）

`IProcessRuntime` / `SpecSnapshot` / `ISpecLoader` 是舊路；舊 code 仍編譯但不再延伸。詳見 `CLAUDE.md`。

## 目前進度（2026-05-25）

- ADMIN：bpm-admin-svc Clean-Arch skeleton + bpm-admin-ui flowcook V0 shell（AI Kitchen + User & Role）已上線
- BPM：unify-user-store 收尾（U2 / U5 / U7 + finale），bpm-svc 完全切到 SharedX；unified inbox + bundle BPMN passthrough 已 land
- CHEF：chef skill v3（model B）+ lead skill v1 + chef skill v2 unify-user-store update；first cook LEAVE V1 在 `leave-test-5` testbed 進行中（尚未 merge 回 main）
- WWW：bpm-www Astro skeleton + 7 個首批頁面

## 已知 follow-up

- **chef/skill + LEAVE V1 testbed 對齊 Clean Arch 分層** — 目前 chef skill 把 entity + state machine + inbox provider 全寫進 Persistence；要拆到 Domain / Application / Persistence。同時 `Persistence/DependencyInjection.cs` 的 `ITypedInboxProvider` assembly scan 要搬到 `Application/DependencyInjection.cs`
- Model A code 清理（runtime engine + 舊 hooks）尚未排
- 第二支 chef-cooked flow 還沒做 — 證明流程可重複
- NotificationDispatchAudit 表（生產通知稽核）未建
- Reports in-memory percentile — 百萬級 instance 後切 DB function
- bpm-ui / bpm-admin-ui 沒 vitest / jest
- bpm-admin-svc 沒有 `DbPathResolver`，SeedCli 跟 Api 各自 cwd 對應 `admin.dev.db` 兩份（暫時用 `cp` 同步）— 學 bpm-svc 修
- `openspec/` 空殼，等需要 RFC 時再啟用

## 文件

- [CLAUDE.md](./CLAUDE.md) — 專案背景、六大專案邊界、Codegen 模式、SharedIdentity、DB conventions
- [chef/skill/SKILL.md](./chef/skill/SKILL.md) — chef agent
- [lead/skill/SKILL.md](./lead/skill/SKILL.md) — lead agent
- [bpm-www/README.md](./bpm-www/README.md) — 行銷站
- 各 app 的 `CLAUDE.md` — app 邊界
