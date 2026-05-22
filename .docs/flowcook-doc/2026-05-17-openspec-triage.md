# openspec Triage — flowcook Pivot Realign

> Date: 2026-05-17
> Related: `2026-05-16-flowcook-pivot-design.md`、`2026-05-17-migration-plan.md`

flowcook 架構 pivot 後，對既有 `openspec/changes/` 25 個 active proposal + `openspec/specs/` 18 個 finalized specs 做一輪 triage。本文件記錄分類與處理結果。

---

## 1. 處理原則

- **🟢 Green — Superseded**：概念已被 flowcook 新設計 cover；proposal 移到 `openspec/changes/archive/2026-05-17-{name}/`
- **🟡 Yellow — Realign**：概念仍適用但要對齊新架構；原地保留，每個資料夾加 `FLOWCOOK_STATUS.md` 標 realign target
- **🔴 Red — Obsolete**：被新架構整個取代；同 green 移 archive

`openspec/specs/` 18 個 finalized specs **全保留**，作為 in-place refactor `bpm-svc` 期間查閱舊行為的 reference。

---

## 2. Green — Superseded（已 archive，11 件）

| Proposal | flowcook 對應位置 |
|---|---|
| add-comprehensive-audit-trail | §7 Audit（7 欄 schema、append-only、syncer batch 5min） |
| add-delegation | §3.6 Delegation（user-to-user、active 區間、不 transitive） |
| add-soft-delete | 已決 — Principal/Process/Task 都加 `deleted_at` + EF global filter |
| extend-actor-and-org-for-ai-routing | §3 Principal & Role Model（七表 + materialized view） |
| extend-usertask-assignee-by-role | AI Kitchen wizard APPROVERS step（Principal + role + inherit checkbox） |
| add-i18n-locale-switching | AI Kitchen wizard TRANSLATION step（§5.5） |
| add-onboarding-ai-translate | AI Kitchen wizard TRANSLATION step（一鍵 AI 補空） |
| add-notification-engine | AI Kitchen wizard NOTIFY step（§5.2） |
| add-sla-timer-escalation | AI Kitchen wizard SLA step（§5.2 step 9） |

## 3. Red — Obsolete（已 archive，2 件）

| Proposal | 為何 obsolete |
|---|---|
| add-bpm-frontend | 既有 `bpm-ui/` 已存在；Step 5 演化即可 |
| add-system-admin-ui | 既有 `bpm-admin-ui/` 已存在；Step 2 重組成五大頁即可 |

## 4. Yellow — Realign（原地保留 + FLOWCOOK_STATUS.md，14 件）

| Proposal | Realign target |
|---|---|
| add-api-observability | Step 4+ 跨服務觀測（bpm-svc + bpm-admin-svc + syncer + chef） |
| add-calendar-and-business-hours | AI Kitchen wizard SLA step |
| add-comments-and-rejection-feedback | `bpm-ui/` Step 5 ops UX |
| add-file-storage | `bpm-svc/` Step 4 attachment storage |
| add-form-runtime-rendering | `bpm-ui/` Step 5 DynamicForm migration（Phase 2） |
| add-hr-sync-csv | `syncer/` 客戶資料源 adapter（CSV 場景） |
| add-mcp-entra-sync | `syncer/` 客戶資料源 adapter（Entra ID 主路徑） |
| add-outbound-webhooks | NOTIFY（純信號）/ INTEGRATIONS（結構化 + OpenAPI） |
| add-pdf-export | `bpm-ui/` Step 5 ops export |
| add-real-reporting | `bpm-ui/` Step 5 Reports 頁 |
| add-real-search | `bpm-ui/` Step 5 Search 頁（走 ISearchService 介面） |
| add-sso-oidc | v1+，v0 帳號密碼足夠；之後跟 mcp-entra-sync 一併 |
| add-tenant-branding | admin Site Setting 頁 |
| extend-field-types-line-items | AI Kitchen wizard FORMS step 欄位類型擴充 |

---

## 5. specs/（保留作 reference，18 件）

| spec | 用途 |
|---|---|
| bpm-acceptance-sandbox | 舊 sandbox 行為 — Step 4 refactor 時翻 |
| bpm-actor-dsl | 舊 ActorRef 行為 — Principal migration 時對照 |
| bpm-auth-jwt | 舊 JWT 邏輯 — Step 1 帳號密碼新 auth 替代，但仍當 reference |
| bpm-cel-expressions | CelNet 引擎 — Step 4 重用 |
| bpm-delegation | 舊 delegation 描述 — 新版 §3.6 對齊 |
| bpm-flow-library | Flow Library 行為 — Step 2 admin FE 重用概念 |
| bpm-form-stepper | 舊 9-step wizard — Step 3 改 11 步 |
| bpm-notification-engine | 通知派送 — Step 4 在 bpm-svc 端重用 |
| bpm-org-model | 舊 org 模型 — 新 Principal §3 取代 |
| bpm-process-admin-ui | 舊 admin console — Step 2-5 重組 |
| bpm-process-runtime | ProcessRuntime — Step 4 重用核心引擎 |
| bpm-roles-and-permissions | 舊 RBAC — 新 Principal model 取代 |
| bpm-sandbox-clock-and-state | Sandbox runtime — Step 4 保留 + refactor |
| bpm-sandbox-message-capture | Mailbox UI — 拿掉，改直接轉送 |
| bpm-spec-bundle | Bundle 結構 — Step 4 重用 |
| bpm-spec-reproducibility | Spec snapshot 重現 — Step 4 重用 |
| bpm-wizard-actor-editor | ActorRefEditor — Step 3 wizard 內改寫 |
| bpm-workflow-resolver | Workflow resolver — Step 4 refactor for Principal |

---

## 6. 統計

| 類別 | 數量 |
|---|---|
| Green — Superseded（archived） | 9 |
| Red — Obsolete（archived） | 2 |
| Yellow — Realign（in-place + FLOWCOOK_STATUS） | 14 |
| **changes/ 合計** | **25** |
| specs/（保留作 reference） | 18 |

---

## 7. 後續維護

- 新 feature 想動工時：先看是否有現成 yellow proposal 可 realign；沒有則新開 proposal
- yellow proposal 動工時：根據 FLOWCOOK_STATUS.md 的 realign target，重寫 proposal.md / design.md / tasks.md 對齊新架構，再走 openspec implementation 流程
- 完成的 proposal 跟既有約定一樣移 `archive/{date}-{name}/`
- specs/ 不直接編輯；in-place refactor 影響舊行為時，新 spec 寫在 `flowcook-doc/` 或 `bpm-svc/CLAUDE.md`

---

## 8. 下一步

- ✅ Triage 完成
- 開始 Step 1 implementation（bpm-admin-svc skeleton）
- 第一個會用到的 yellow proposal：add-form-runtime-rendering（Step 5）、add-mcp-entra-sync（Step 6）
