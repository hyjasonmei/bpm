# flowcook BPM 專案

開發者 正在開發一個商業 BPM 平台，兩人副業團隊（開發者 負責開發，夥伴負責業務導入）。夥伴從前公司帶回十幾個真實流程圖，涵蓋請假、採購、差旅、發公告等，作為 MVP 的需求基礎。

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

## 六大專案

| 層 | 資料夾 | 角色 |
|---|---|---|
| **ADMIN** | `bpm-admin-svc/` + `bpm-admin-ui/` | flowcook 內部 + 客戶管理員：AI Kitchen / User & Role / Sandbox / Audit / Site Setting。Canonical identity 表存這 |
| **CHEF（codegen pipeline）** | `.claude/skills/{chef-codegen,lead-codegen,openspec-*}` + `chef/skill/` + `lead/skill/` | AI codegen workflow — chef 把 spec.json 翻成 per-flow 程式；lead 維護共用 primitive |
| **BPM** | `bpm-svc/` + `bpm-ui/` | 客戶端 runtime：表單 / unified inbox / 案件詳情 / 簽核；斷約後可獨立運轉 |
| **Product Website** | `bpm-www/` | 對外行銷站（Astro + Tailwind） |
| **Guide Docs** | `bpm-docs/` | 客戶導入/使用手冊（Astro + Starlight）→ <https://guide.flowcook.ai>（noindex、不從官網連） |

部署模型：per-customer，無 multi-tenant；每客戶各一套堆疊。原本規劃的 `syncer/` 已取消 — admin ↔ bpm 改走單一 DB source（unify-user-store 系列已完成）。

### ⚠️ 功能/設計改動必同步 bpm-docs

`bpm-docs/` 是**客戶看得到的手冊**——功能行為、API 屬性、UI 操作、流程規則有任何改動時，對應的手冊頁**必須一起改**，過時的手冊＝誤導客戶。守則：

- 手冊視角＝**對貴公司（客戶）說話**：沒有「我們 vs 客戶」、沒有內部話術（賣點/暖場/帶客戶）；第三方只有導入期的「flowcook 顧問」
- API 文件的 request/response 範例要**打真 API 驗過**再寫（狀態碼、屬性大小寫）
- 改完重佈：`cd bpm-docs && npm run build`，然後 `swa deploy ./dist --deployment-token $(az staticwebapp secrets list -n poc-flowcook-docs -g rg-poc --query properties.apiKey -o tsv) --env production`

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

Reference cook：**LEAVE V1**，已在 main 並對齊本文件的 Clean Arch 分層（entity/enum→Domain、service/inbox/templates/CaseStore 介面→Application、EF mapping+CaseStore impl→Persistence）。新 chef session 從任一隻已 merge 的 flow copy shape。**main 上目前共有 10 隻 chef-cooked flow**（APE / EOB / ETM / FAD / FAP / PURCHASE_REQUEST / TEO / TRQ / VENDOR_EXPENSE / LEAVE），全部正確分層。

### Model A（已退役）

`IProcessRuntime` / `SpecSnapshot` / `ISpecLoader` 是舊的「spec-driven runtime」路（一支通用引擎吃 spec.json 解釋 runtime）。複雜度比 model B 預期還高 — `leave-test-3` 是 model A 最後一次嘗試，結果 submit 進 DB 但 UI 看不到。

新流程一律走 model B。**UI 端的 model A 已清除**：`useFormRuntime` / `useFlowSubmit` / `useFlowTask` / `useMyInstances` / `useMyTasks` hooks、`screens/forms/Reference_*.tsx`（11 隻）、`lib/api/hrFlows.ts` 都已刪（unrouted，沒有 live code import）。**尚未清的**（因為還接著 live feature）：bpm-svc 的 runtime engine（`IProcessRuntime` / `ProcessRuntime` / `SpecSnapshot` / `ISpecLoader` / `ActorResolver` / `CelNetExpressionEvaluator` / 舊 `INotificationDispatcher` / `ProcessInstance` 表）+ 其上的 admin Reports / Simulator / ProcessAdmin 功能，以及 bpm-ui 的 `/cases/:instanceId` + `/tasks/:taskId` 舊路由（`screens/CaseDetail.tsx` / `lib/api/process.ts`）。要清這層等於拿掉或改寫 Reports/Simulator——是產品決策。

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

## Azure 上線狀況（2026-06-10）

flowcook POC 已部署到 Azure 並掛上 `flowcook.ai`（HTTPS/TLS 正常，AI Kitchen 可用）。provisioning script 在 `infra/azure/`（`00-config.sh` 共用設定 → `01-provision` → `02-configure` → `03-deploy`）。

**資源（resource group `rg-poc`）**

| 服務 | 型態 | Region | 名稱 |
|---|---|---|---|
| bpm-svc | App Service (Linux .NET 10) | **japaneast** | `poc-flowcook-api` |
| bpm-admin-svc | App Service (Linux .NET 10) | **japaneast** | `poc-flowcook-admin-api` |
| bpm-ui | Static Web App | eastasia | `poc-flowcook-ui` |
| bpm-admin-ui | Static Web App | eastasia | `poc-flowcook-admin-ui` |
| bpm-www | Static Web App | eastasia | `poc-flowcook-www` |
| DB | Postgres Flexible Server (B1ms Burstable) | japaneast | `pg-poc-flowcook` |
| 機密 | Key Vault（managed identity + KV references） | japaneast | `kv-poc-flowcook` |

App Service plan = B1；整套用最便宜 SKU。auth 走 unify-jwt（單一 `BPM_JWT_SECRET`，admin-svc 簽 token、bpm-svc 認）；`BPM_AUTH_MODE=prod` 關掉 `/api/dev/login`。

**⚠️ demo dev-mode（2026-06-10 為周五 demo 暫時開啟）**：bpm-ui 的 persona 快速切換器（在右上角 **Account menu 頭像下拉**裡的「SWITCH ROLE (DEV MODE)」，**不是**「代理人」按鈕——那是 delegation 委任）靠 `/api/dev/login`，prod 模式會關掉。要讓它在雲端動需要**兩件事**：① `BPM_AUTH_MODE=dev`；② persona 對應表——它只寫在 `appsettings.Development.json`，Production 環境不載入，所以要把對應補成 App Service 環境變數 `Personas__employee=bob@acme.example`、`Personas__manager=alice@…`、`Personas__finance=frank@…`、`Personas__it=dave@…`、`Personas__hr=henry@…`、`Personas__admin=jack@…`。改完 restart bpm-svc 即生效（純增量，真實登入照常）。**demo 結束務必把 `BPM_AUTH_MODE` 改回 `prod`**（dev 模式 = 任何人可呼叫 `/api/dev/login` 切成任意 persona，無密碼）。
seed demo 帳號：`{alice,bob,carol,dave,erin,frank,grace,henry,iris,jack,kate,leo,mia}@acme.example` / 密碼 `flowcook2026`（Jack=SYSTEM_ADMIN，admin-ui 登入用）。persona→帳號：employee=bob、manager=alice、finance=frank、it=dave、hr=henry、admin=jack。

**POC 網址（2026-06-10 實際 live）**

| 用途 | 網址 |
|---|---|
| 官網 bpm-www | <https://flowcook.ai>（apex/www）· default：<https://gentle-stone-058e93100.7.azurestaticapps.net> |
| 客戶端 bpm-ui | <https://nice-dune-045efff00.7.azurestaticapps.net> |
| 管理端 bpm-admin-ui（AI Kitchen 登入入口） | <https://polite-field-06fac5e00.7.azurestaticapps.net> |
| bpm-svc API | <https://poc-flowcook-api.azurewebsites.net>（health：`/health`） |
| bpm-admin-svc API | <https://poc-flowcook-admin-api.azurewebsites.net>（AI：`POST /api/chat`、`/api/spec-extract`） |

> SWA default hostname 由 Azure 隨機產生（`nice-dune-…` 等），重建 SWA 會變；以 `az staticwebapp show -n <name> -g rg-poc --query defaultHostname` 為準。
> AI Kitchen 雲端端到端已驗證（2026-06-10：直打部署 admin-svc `/api/chat` 回 200 + 真實 Anthropic message id + usage，KV key 正確）。

**region 為什麼這樣切**：原本全放 eastasia（香港），但 Anthropic 對香港 IP geoblock → AI Kitchen 403。把兩個 API + DB + KV 搬到 **japaneast** 解掉；SWA 只在 5 個 region 提供且本身是全球 CDN（location 只是 metadata），留在 eastasia 無妨。japaneast App Service 配額初始為 0，用 `az quota update`（B1 → value=3）即時開通。

**DNS**：GoDaddy 管 `flowcook.ai`，**只有官網（bpm-www）掛 `flowcook.ai`**（apex / www → www SWA）。其餘 4 個 app（兩 API + bpm-ui + admin-ui）都用 Azure default hostname（`*.azurewebsites.net` / `*.azurestaticapps.net`）——`00-config.sh` 的 `USE_DEFAULT_HOSTNAMES=true`，02/03 會把 CORS + 前端 API URL 指到 default host，零 DNS 設定。

**Anthropic key**：放 Key Vault（secret），**由 開發者 自己更新真 key**，Claude 不碰。KV 重建時 key 會自動保留。

**成本與停機**：running ~$31/mo，停機後 ~$3/mo（只剩 storage）。停 / 開用 `infra/azure/flowcook-stop.sh` / `flowcook-start.sh`（停兩個 API + Postgres；SWA 是 Free 不停，官網照常）。
⚠️ **Postgres 停機滿 7 天會被 Azure 強制自動重啟**（平台硬性規則，無法取消）。要長期省錢需「每 6 天自動停機」排程，否則最差 ~$16/mo。**目前 stack 為停機狀態。**

**部署踩過的雷**（都已修在 script 內）：① `az webapp deploy` 加 `--track-status false`（冷啟 + migrate + seed 超過 10 分鐘 deploy timeout 會誤判失敗）② bpm-svc assembly 名是 `Bpm.Api` 不是 `Api` ③ seed 連線字串要從 config 取（`GetDbConnection().ConnectionString` 被 Npgsql 去掉密碼 → SCRAM「No password」）④ 兩個 SPA 都要 `staticwebapp.config.json` 的 `navigationFallback`（否則 deep-link / reload 404）⑤ fresh subscription 要先 `az provider register`。

## 目前進度（2026-05-25）

- **ADMIN**：`bpm-admin-svc` Clean-Arch skeleton（auth + principal / role / delegation / dept controllers）+ `bpm-admin-ui` flowcook V0 shell（AI Kitchen + User & Role pages；legacy admin shell 已 purge）已上線
- **BPM**：unify-user-store 收尾（U2 / U5 / U7 + finale）— bpm-svc 完全切到 SharedX 讀 admin 的 identity；bpm-ui Login auto-fill demo credentials；JWT roles claim normalized to array；unified inbox + bundle BPMN passthrough 都已 land
- **CHEF**：chef skill v3（model B）+ lead skill v1 + chef skill v2 unify-user-store update 都已 land；**10 隻 chef-cooked flow 已在 main 且全部對齊 Clean Arch 分層**（含 reference cook LEAVE V1）
- **WWW**：`bpm-www` Astro skeleton + 7 個首批頁面（index / about / features / how-it-works / pricing / use-cases / why-flowcook）

## 仍要做的事（未排程）

- chef skill SKILL.md 的 LEAVE 範例段落（§195-217 / 364 / 522）還指著舊的 `Persistence/Features/` 形狀，與已對齊的實作不符 — conventions.md + 路徑表已正確，只剩 SKILL.md 的 worked-example 沒同步
- Model A code 清理 — UI 端已完成（hooks + 11 隻 `Reference_*.tsx` + `hrFlows.ts` 已刪）。**剩 bpm-svc runtime engine**（`ProcessInstance` / `ProcessRuntime` / `ISpecLoader` / `ActorResolver` / `CelNetExpressionEvaluator` / 舊 `INotificationDispatcher`）+ 其上的 admin Reports / Simulator / ProcessAdmin、以及 bpm-ui `/cases/:instanceId` + `/tasks/:taskId` 舊路由——清這層 = 拿掉或改寫那些功能，待產品決策
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
