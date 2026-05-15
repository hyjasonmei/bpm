# BPM Platform

商業 BPM 平台 POC — 中小企業（50–300 人）為主，目標可整合微軟生態（Entra ID / AD）。

兩大賣點：
1. **AI onboarding** — 9-step stepper + AI 對話 + 即時生成的問卷，產出 spec bundle (zip)
2. **無痛上線驗收** — 完善 sandbox：mail capture、webhook redirect、persona switch、time advance、state reset，一個驗收員就能跑完整 UAT

## 技術棧

- **bpm-svc** — C# .NET 10 Clean Architecture（Api / Application / Domain / Persistence / Functions / SeedCli）+ EF Core + SQLite（POC，code 預留 Postgres 路徑）+ 自建 C# Workflow Engine
- **bpm-ui** — React 18 SPA + Vite + Tailwind + shadcn（員工端：表單、待辦、流程查詢）
- **bpm-admin-ui** — React 18 SPA + Vite + Tailwind + shadcn + bpmn-js（管理端：onboarding、Flow Library、Sandbox Mailbox、Process Admin Console）

## 專案結構

```
bpm-svc/          後端 (.NET 10)
  src/
    Api/          Controllers
    Application/  Services / Handlers / Helpers
    Domain/       Entities / Aggregates
    Persistence/  EF Core DbContext / Migrations
    Functions/    Workflow engine functions
    SeedCli/      reset / seed / status console app
  tests/Bpm.Tests
bpm-ui/           員工端 SPA
bpm-admin-ui/     管理端 SPA
sample_specs/     11 個 demo flow spec.json（leave / purchase / trq / hwp / teo / gee / gev / extob / ape / itpr / deptx）
openspec/         OpenSpec proposal（active / archived）
docs/
spikes/
```

## 前置需求

- .NET 10 SDK + `dotnet-ef` global tool
- Node 18+（推薦 22.x）/ npm
- （可選）`gh`、`jq`、chrome-devtools MCP — 詳見 [SETUP.md](./SETUP.md)

```bash
dotnet --version    # 10.x
dotnet ef --version # 10.x
node --version      # 18+
```

## 環境變數（bpm-svc）

| Var | Default | Notes |
| --- | --- | --- |
| `BPM_AUTH_MODE` | `dev` | `dev` = JWT + `/api/dev/login`；`prod` = JWT only；`disabled` = anonymous |
| `BPM_JWT_SECRET` | — | **Required** when auth mode != `disabled`。HS256 簽章金鑰，≥ 32 bytes |
| `BPM_SEED_ON_STARTUP` | `true` | 啟動後跑 `OrgFixture` 種 persona / role |
| `BPM_AI_BACKEND` | `cli` | `cli` 用 Claude Code subscription；`api` 用 `ANTHROPIC_API_KEY` |
| `Spec__IncomingFolder` | — | wizard hand-off 落地 `spec.json` 的位置 |

本機 dev 一鍵設定：

```bash
export BPM_JWT_SECRET=$(openssl rand -hex 32)
export BPM_AUTH_MODE=dev
```

## 啟動

### 一鍵（BE + FE）

```bash
./launch.sh
# log 在 .launch-logs/be.log、.launch-logs/fe.log
# Ctrl+C 同時停掉兩個
```

`launch.sh` **不包含 bpm-admin-ui** — admin 要另開 terminal。

### 個別啟動

| 服務 | Port | 指令 |
| --- | --- | --- |
| BE (bpm-svc) | 5290 | `cd bpm-svc/src/Api && dotnet run` |
| FE (bpm-ui) | 5173 | `cd bpm-ui && npm run dev` |
| Admin (bpm-admin-ui) | 5174 | `cd bpm-admin-ui && npm run dev` |

前端首次啟動前：

```bash
cd bpm-ui && npm install
cd ../bpm-admin-ui && npm install
```

## 常用工具

### SeedCli — 重置 / 種子 / 狀態

```bash
cd bpm-svc/src/SeedCli
dotnet run -- reset                    # 清掉所有 process / task / instance
dotnet run -- seed                     # 種 13 user / 6 dept / 14 role
dotnet run -- seed --include-bundles   # 同上 + 11 個 sample_spec bundle
dotnet run -- status                   # 印目前 DB 狀態
```

### 測試

```bash
cd bpm-svc && dotnet test              # 313 tests
```

前端目前無 vitest / jest，靠 `tsc` + 手動 boot 驗證。

### EF Core migrations

```bash
cd bpm-svc/src/Api
dotnet ef migrations add <Name> -p ../Persistence -s .
dotnet ef database update -p ../Persistence -s .
```

## DB Conventions（POC SQLite，預留 Postgres）

1. 永遠用 EF Core，禁 raw `IDbConnection` / `Dapper`
2. 禁 SQLite 特有函式（`json_extract`、`unixepoch` 等）
3. 不寫 raw SQL migration（除非兩版都附）
4. 全文搜尋藏在 `ISearchService` 介面後面
5. 不依賴 SQLite 的 DB-wide write lock 做 serialization
6. JSON 欄位用 EF Owned types 或純 TEXT，避 query 內部 JSON path
7. 並發控制用 EF OptimisticConcurrency（RowVersion）

## 進度（2026-05-14）

Phase 1 已完，**Phase 2 入口 = `add-form-runtime-rendering`**（11 個 hand-coded form → `<DynamicForm spec={...} />`）。

已完成：
- Foundation: Org / Authz / Sandbox / Auth / HrFlows / Spec(ActorRef)
- Runtime: `ProcessRuntime` + `SpecSnapshot` + Cel.NET 引擎 + REST API
- Bundle: Builder / Parser / Validator + 持久化 + RuntimeLoader（scratch tenant 隔離）+ ReproRunner
- Sandbox: clock decorator、capture-only OutboundGate、ResetService、persona switch JWT、Mailbox API
- Process Admin: simulator（dry-run）+ 4 intervention endpoints + reporting service（5min cache）
- All flows real (PR-L1..L6): 11 spec 全對齊 `workflow.ts` + 22 sub-test E2E
- 313 tests 全綠

詳細狀態見 [CLAUDE.md](./CLAUDE.md)。

## 文件

- [CLAUDE.md](./CLAUDE.md) — 專案背景、技術棧、進度、conventions
- [SETUP.md](./SETUP.md) — 開發環境前置（含 dogfood self-check）
- [spec_schema.md](./spec_schema.md) — flow spec JSON schema
- [pipeline_architecture.md](./pipeline_architecture.md) — pipeline 設計
- [review_checklist.md](./review_checklist.md) — code review checklist
- [openspec/](./openspec) — 25 個 active proposal
