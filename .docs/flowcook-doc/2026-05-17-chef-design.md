# flowcook — chef Design v0

> Status: **DRAFT**
> Date: 2026-05-17
> Related: `2026-05-16-flowcook-pivot-design.md`、`2026-05-17-migration-plan.md`

chef = flowcook 的 AI 流水線。輸入 spec、輸出可運轉的 workflow 程式碼。本文件鎖 v0 行為 + chef 的 skill 大綱。

---

## 1. 角色與邊界

- **誰用**：flowcook 內部（admin 內 AI Kitchen 控制台）；客戶看不到 chef 內部
- **輸入**：admin AI Kitchen 走完 wizard / 顧問寫好的 spec（type `wizard` 或 `custom`）
- **輸出**：依 spec 產生的 workflow 程式碼 + 設定，包成 bundle 給 bpm（或開 git branch 給 tech 客戶 review）
- **不負責**：執行流程（bpm 的事）、修核心 code（見 §3.2）、改別人版本（見 §3.3）

---

## 2. 與 lifecycle 的對接

```
admin submit ─► state=submitted ─► chef pick up ─► state=cooking ─► 完成 ─► state=committed
                    ▲                                  │
                    │                                  │ 卡住
                    │                                  ▼
                    └─────── user 改 NOTES + Resume ──── state=on hold
```

- chef 從 `state=submitted` 的 flow queue 拉任務（per-customer 序列）
- 開工 → 把 state 改 `cooking`
- 卡住 → call admin API 把 state 改 `on hold`，並 append 問題到 NOTES
- user 改完 NOTES 按 Resume → state 回 `submitted` → chef 重新 pick up

---

## 3. 開發規範（chef 必遵）

### 3.1 一版一 feature flag

- 每個 flow 的每個版本（`[CODE]_[VER]`）整版包一個 flag
- Config file 條目 `[CODE]_[VER]: on/off`
- 整版啟用或停用，細粒度未來再加
- chef 產的所有 entry point（API endpoint、event handler、UI route）開頭都檢查 flag，off 就 return 404 / no-op

### 3.2 不動核心 code

- chef 只在 **`bpm-svc/features/[CODE]/[VER]/`** 與 **`bpm-ui/src/features/[CODE]/[VER]/`** 之下寫程式碼
- 不修 `bpm-svc/` 內 runtime（ProcessRuntime / Bundle / Spec engine 等）、base entity、`bpm-admin-svc/` 的 Principal schema、`syncer/` / `chef/` 自己
- 不做 CI gate enforce（草創 trust skill）；future hardening 時可加 PR-diff lint

### 3.3 Class 命名 = flat prefix `[CODE]_[VER]_[CLASSNAME]`

- 全 flat、不用 C# namespace（草創一致簡單，chef AI 操作 prefix 比 namespace 容易）
- 例：
  - `LEAVE_V1_ApprovalHandler`
  - `LEAVE_V1_ApprovalHandlerTests`
  - `LEAVE_V1_LeaveFormDto`
  - DB table：`leave_v1_leave_request`
  - EF migration class：`LEAVE_V1_Initial`
- 跨 flow 共用工具（非 feature-specific）不在此範圍，走原本 namespace

### 3.4 變數一律走 `${var}` 不 hardcode

- 外部 URL / token / 環境相關常數一律走 spec.variables 宣告 + `${var_name}` 引用（見主文件 §5.3a）
- chef 產的 code 從 runtime 的 variables resolver 拿值
- chef PR 出來如 grep 到 hardcode 外部 URL / secret → 退回 chef 重做

### 3.5 Output 結構

chef 在 monorepo 內各服務資料夾下追加 feature 程式碼：

```
bpm-svc/
  features/LEAVE/V1/
    Domain/          # entities / value objects
    Application/     # services / handlers
    Persistence/     # EF entity configs, migrations
    Api/             # controllers
    Notifications/   # templates
    Integrations/    # HTTP clients (generated from OpenAPI)
    Tests/
    LEAVE_V1.flag.json   # feature flag default + metadata
    LEAVE_V1.spec.json   # snapshot of input spec

bpm-ui/
  src/features/LEAVE/V1/
    Forms/           # React form components
    Workflows/       # task-specific UI
    index.ts         # registers under feature flag
```

兩邊的 `[CODE]/[VER]/` 都有 feature flag 守門；同 flag off 時 BE 端 controller 與 FE 端 route 都失效。

---

## 4. on hold 機制細節

### 4.1 chef 觸發 on hold

chef 卡住時 call admin API：

```
POST /api/flows/{flow_id}/on-hold
{
  "iteration_id": "{chef iteration uuid}",
  "question": "請假天數欄位寫的是「天」還是「小時」？若混用要怎處理？"
}
```

admin BE 收到：
- 改 flow state → `on hold`
- append question 到 spec.notes（時間戳 + chef 標示前綴 `[chef Q ${iter}]`）
- 觸發 audit event：`action_type=flow_on_hold`

### 4.2 user 答覆 + resume

- on hold 狀態下，admin UI 只開放 NOTES step 編輯（其他 step lock）
- 若 user 發現前面 step 設計錯，按「Cancel & back to draft」 → state=draft，可重編
- user 編完 NOTES 按「Resume cooking」按鈕 → state=submitted，chef queue 重 pick up
- chef 重 pick up 時讀整份 spec.notes（含 chef 自己上輪的 question + user 的答覆）

### 4.3 v0 暫不設 iteration cap

- 草創不設累計次數上限 / cost cap（簡單為主）
- 之後加：cap 觸發 → escalate flowcook 內部通知

---

## 5. chef Skill 大綱（給 chef AI 的系統 prompt）

skill 是 chef AI 的 system prompt + 內附 reference。維護位置：`chef/skill/` 內，每次 chef invocation 拼進去。

大綱：

```
# flowcook chef skill v0

You are chef. Your job: take one flowcook spec and produce a working
implementation in monorepo bpm/, following the conventions below.
Do NOT exceed the boundaries.

## Allowed paths
- Write/modify only:
  - bpm-svc/features/{CODE}/V{N}/**
  - bpm-ui/src/features/{CODE}/V{N}/**
- Read but do not modify:
  - bpm-svc/{Core,Runtime,Bundle,Principal,Sandbox,Application,Api}/**
  - bpm-admin-svc/**
  - syncer/**
  - chef/** (self)

## Naming conventions (flat prefix, no namespace)
- Every class, table, migration, file: {CODE}_V{N}_{name}
- Examples:
  - LEAVE_V1_ApprovalHandler
  - DB table leave_v1_leave_request
  - EF migration class LEAVE_V1_Initial

## Feature flag wrap
- All public entry points (controllers, event handlers, UI routes) must check
  flag `{CODE}_V{N}` from config first
- Off → 404 / no-op / fall back to previous version

## Variables
- External URLs, tokens, IDs: NEVER hardcode
- Always reference spec.variables via ${var_name}
- Resolve at runtime via bpm-svc's VariableResolver

## When stuck
- If spec is ambiguous, contradictory, or missing critical info:
  1. Do NOT guess
  2. Call POST {admin_base_url}/api/flows/{flow_id}/on-hold with your question
  3. Stop work
- After admin reactivates (state=submitted), re-read spec.notes for user's answer

## Output
- v0: push bundle to bpm; no PR
- (v1) tech tier: open PR with summary + which flag controls it
- Always include unit tests:
  - every userTask has form + tests
  - every gateway has branch test
  - every approval node has approve/reject test
  - notification templates have one rendering test each
  - integration calls have happy path mock test
```

詳細 skill 檔案在 `chef/skill/` 內持續迭代（可參考 `.docs/old-docs/prompt_template_v1.md` 改寫）。

---

## 6. v0 範圍

- pick up `submitted` flow，產 bundle
- 不開 PR（直接 push bundle 給 bpm；tech 客戶 PR 模式留下版迭代）
- 走 Claude Code SDK runner（語言之後拍板：建議 Node）
- 沒 cost cap、沒 iteration cap、沒並發控制（per-customer 序列足夠草創）
- 卡住走 on hold，等 user 答覆

---

## 7. 待決議

- chef 開發語言（Node / .NET / Python）—— 我推 Node，跟 Claude Code SDK 對齊
- chef → admin 的 callback 認證（POST on-hold 那支）——簡單 shared secret v0
- chef PR 行為（tech 客戶用）——v1 起做
- chef iteration cap / cost cap —— 草創不做
- 多客戶並發 —— v0 只 per-customer 序列；多客戶間並行需後續設計

---

## 8. 下一步

1. Jason 拍板 chef design v0
2. migration-plan Step 7 開始 implement
3. 在 `chef/skill/` 內建立 skill 完整版（拓展 §5 大綱）
