# 全部 demo flow 變真 + SeedCli — 規劃文件

**狀態：規劃中（尚未動工）**
**作者：Claude (Opus 4.7) for Jason**
**日期：2026-05-11**

## 目標

1. **11 個 demo flow 全部走 ProcessRuntime**（不是前端 state mock）
2. **每個都能在 sandbox mode 跑完一遍 UAT**（capture / advance time / persona switch / reset）
3. **新增 console app 專案** 一鍵清空 DB + seed 完整角色 + 安裝所有 bundle，讓任何人 clone 下來 5 分鐘有可 demo 環境

## 成功標準

驗收一句話：

> `dotnet run --project bpm-svc/src/SeedCli -- reset && dotnet run --project bpm-svc/src/SeedCli -- seed --include-bundles` 跑完之後，打開 bpm-ui 用 employee persona 選任何一個 flow 提交 → 切到對應簽核 persona → 簽完 → 在 LiveCases 看得到、Mailbox 有捕捉信、Reports 有計數。**11 個 flow 都能跑通**。
>
> （日常 seed 預設 *不含* bundles — `seed` 只給乾淨組織 + 角色；要 demo state 才加 `--include-bundles`，Jason 2026-05-11 確認）

---

## 現況清單（30ee9e4 為基準）

### 11 個 demo flow（workflow.ts 定義）

| Code  | 中文       | 簽核鏈                                    | 已有 spec.json?             |
|-------|-----------|-------------------------------------------|------------------------------|
| LEAVE | 請假申請   | employee → manager → hr                   | ✅ `leave_v1.json`           |
| GEE   | 員工費用   | employee → manager → finance × 2          | ⚠️ `expense_employee_v1.json` 接近，需確認 |
| GEV   | 廠商費用   | employee → manager → finance × 2          | ❌                           |
| APE   | 預支費用   | employee → manager → finance × 2          | ❌                           |
| HWP   | 硬體採購   | employee → it × 2 → manager × 2 → finance | ✅ `hardware_purchase_v1.json` |
| ITPR  | IT 軟體採購 | employee → it × 2 → manager × 2 → finance | ❌                           |
| TRQ   | 差旅申請   | employee → manager → admin                | ❌                           |
| TEO   | 差旅費報銷 | employee → manager → finance × 2          | ⚠️ `expense_with_threshold_v1.json` 接近 |
| EXTOB | 外部到職   | manager → hr                              | ❌                           |
| RESIGN| 離職申請   | employee → manager → hr                   | ❌（另有 HrFlowsController 硬編；保留並存）|
| DEPTX | 部門異動   | employee → manager → hr                   | ❌（同上）|

**淨缺口：7 個全新 spec（GEV / APE / ITPR / TRQ / EXTOB / RESIGN / DEPTX）+ 2 個既有 spec 對齊（GEE / TEO 重新審視欄位）**

> **HrFlowsController / HrFlowService 並存說明（Jason 2026-05-11 確認）：** RESIGN / DEPTX 是真實業務流程，不是 legacy。legacy 的是「在 process-runtime 之前先用獨立的 HrFlows controller hard-code 出來」這條捷徑。Phase 1 兩條路並存：(a) HrFlowsController 維持運作給既有 demo / API 客戶用；(b) 新的 `resign_v1.json` / `deptx_v1.json` 跑 ProcessRuntime，提供統一體驗 + sandbox / 報表 / bundle 整合。Phase 2 之後若決定統一，再切 HrFlowsController 退役（單獨一個 PR）。Phase 1 不動 HrFlows 程式碼。

### 6 個 persona（workflow.ts 已定義）

employee / manager / hr / finance / it / admin

11 個 flow 的所有 owner 都落在這 6 個 persona 內，不需要新增。

---

## 路徑選擇

兩條路最終結果一樣（demo 都變真），差在「form UI 怎麼來」。

### 🅐 Lean — 保留 hand-coded form，後面接 runtime

**做法：**
- 11 個 form component 保留視覺
- 新建一個 hook `useFlowSubmit(flowCode)` 把 form 收集的 data 打 `POST /api/processes`
- form 增加「我是 task 處理者」模式：傳入 `taskId`，render 變唯讀 + 顯示 Approve/Reject 按鈕，提交打 `POST /api/tasks/{id}/submit`
- inbox（已有 useMyTasks hook）→ 點 task → 開對應 form 在 task 模式

**優點：** 視覺零變動、~3 天工程
**缺點：** spec 改欄位 form 不會自動跟，要手動同步（兩個 source of truth）
**Demo guard 影響：** 必解（要動 forms/、Home、Search、Report、workflow.ts）

### 🅑 Full — add-form-runtime-rendering，spec 動態渲染

**做法：**
- 寫 11 種 field type 的 React renderer（text / number / select / multiselect / date / daterange / file / textarea / repeater / conditional / derived）
- 一個 `<DynamicForm spec={...} />` component 讀 spec.userTasks[].fields 直接渲染
- 11 個 hand-coded form 全砍

**優點：** 真正 spec is source of truth，spec 改一個欄位 form 立刻改
**缺點：** ~2 週工程，視覺要重新設計
**Demo guard 影響：** 也是必解

### 建議：A 先做，B 列下一輪

A 在 3 天內可拿出「全部走真 runtime」的 demo；B 等 form-runtime-rendering 那波再做（已是 P1 proposal 之一）。

---

## Phase 1 計畫：A + SeedCli

### PR-L1: Spec 補完（7 個新 + 3 個對齊／改名）— ✅ DONE 2026-05-11

**狀態：** 完成。`sample_specs/` 與 workflow.ts 的 11 個 FormCode 達成 1:1 命名 + flowCode 對齊。

#### 檔案總覽（PR-L1 結束後）

| FormCode | sample_specs 檔                    | flowCode | 變動    |
|----------|-----------------------------------|----------|---------|
| LEAVE    | `leave_v1.json`                   | LEAVE    | 既有    |
| GEE      | `gee_v1.json`                     | GEE      | 改名 `expense_employee_v1.json` → `gee_v1.json`，flowCode `EXPENSE_EMP` → `GEE`，補齊 finance × 2 步 |
| GEV      | `gev_v1.json`                     | GEV      | 新增    |
| APE      | `ape_v1.json`                     | APE      | 新增    |
| HWP      | `hwp_v1.json`                     | HWP      | 改名 `hardware_purchase_v1.json` → `hwp_v1.json`，flowCode `HW_PURCHASE` → `HWP`，重寫為 it × 2 → manager × 2 → finance(po) 七步流程 |
| ITPR     | `itpr_v1.json`                    | ITPR     | 新增（與 HWP 同 7 步骨架，欄位偏軟體 / SaaS） |
| TRQ      | `trq_v1.json`                     | TRQ      | 新增    |
| TEO      | `teo_v1.json`                     | TEO      | 改名 `expense_with_threshold_v1.json` → `teo_v1.json`，flowCode `EXPENSE` → `TEO`，保留 50,000 門檻 gateway + any 2/3 collection |
| EXTOB    | `extob_v1.json`                   | EXTOB    | 新增（manager-initiated；submitter = role:manager） |
| RESIGN   | `resign_v1.json`                  | RESIGN   | 新增（與 HrFlowsController 並存） |
| DEPTX    | `deptx_v1.json`                   | DEPTX    | 新增（與 HrFlowsController 並存） |
| (額外)   | `purchase_v1.json`                | PURCHASE | 既有 sample；非 workflow.ts FormCode，保留作為複雜分流範例 |

#### 已套用的決策（給後續 PR 參考）

- **GEE / GEV / APE / TEO 都採 employee → manager → finance(confirm) → finance(fin_review) → close**（5 步）— 兩個 finance step 拆成獨立 userTask 以對齊 workflow.ts 的 ownerByStep。
- **HWP / ITPR 都採 employee → it_spec → quote → confirm(manager userTask) → approve(manager approval) → po(finance) → close**（7 步）— 同 manager persona 的兩步分為一個 userTask 一個 approval node。
- **EXTOB submitter = role:manager**（用人主管發起；非 self），HR 是 userTask 不是 approval（建立帳號是執行動作，非簽核決策）。
- **TRQ admin 為 userTask** (`task_admin_notify`)，主管簽核後通知行政協助訂位。
- **RESIGN / DEPTX 結構簡單：apply → manager(approval) → hr(approval) → close**，跟舊的 HrFlowsController 行為對齊。
- **TEO 保留 expense_with_threshold 的 any-N-of-M 機制**：總額 ≥ 50000 → `approval_extra` collection (dept.head, Finance, CEO) 任 2/3 通過。
- **每個 spec 至少 2 個 testCase**：1 個 happy path + 1 個 reject path（除 EXTOB 沒 approval 故只有 1 個 happy）。
- **line items 折成 scalar**（`extend-field-types-line-items` proposal scope）— 所有 spec `meta.notes` 註明此 v1 簡化。
- **role names**：`role:HR`、`role:Finance`、`role:IT`、`role:Admin`、`role:manager`、`role:CEO`。SeedCli (PR-L4) 負責 seed users + role assignments。

#### 測試與驗收

- 新增 `bpm-svc/tests/Bpm.Tests/Persistence/Process/AllFlowsSpecValidationTests.cs`：12 tests（11 spec × theory + 1 coverage check）。
- 更新 `SpecImportServiceTests.Sample_specs_with_cel_helpers_validate_cleanly` theory 從 3 個 → 10 個 sample（涵蓋全部新 spec）。
- 更新 `ProcessRuntimeE2EFixture` 三個 expense_with_threshold 測試 → TEO 測試（含 happy_under_threshold / over_threshold_2of3 / not-advance-until-min）。
- 更新 `All_sample_specs_parse_with_SpecSnapshot` 涵蓋 11 個 spec 全部 parse。
- 全測試：256 → 275 全綠（`dotnet test bpm-svc/bpm-svc.slnx`）。

#### 寫過程遇到的設計決策

1. **HWP 跟 ITPR 7 步骨架完全相同** — workflow.ts 的 ownerByStep 一字不差；硬體偏採購邏輯、ITPR 偏 SaaS / 服務，差異只在欄位。維持兩個獨立 spec 而非共用 base，方便客戶獨立調整。
2. **TEO 沒走「重複 finance × 2 → 失去 threshold 加簽」的捷徑** — 在新 5 步骨架前面插入 `approval_manager` + `gateway_threshold` + 條件 `approval_extra`，既符合 workflow.ts 又保留高額加簽功能。
3. **沒有寫 `sampleOrg` section** — 按指示這是 bundle-level 概念，PR-L4 SeedCli 才需要。spec 只用 `role:HR` 等抽象角色名。

### PR-L1 原始任務清單（保留作為 reference）

寫 9 個 spec.json：
- `sample_specs/gee_v1.json`（覆蓋 GEE，重新審視 expense_employee_v1.json 改名或 deprecate）
- `sample_specs/gev_v1.json`
- `sample_specs/ape_v1.json`
- `sample_specs/itpr_v1.json`
- `sample_specs/trq_v1.json`
- `sample_specs/teo_v1.json`（從 expense_with_threshold_v1.json 對齊或改名）
- `sample_specs/extob_v1.json`
- `sample_specs/resign_v1.json`
- `sample_specs/deptx_v1.json`

**RESIGN / DEPTX 並存策略（Jason 2026-05-11 拍板）：** 這兩個 spec **不取代** 既有的 HrFlowsController / HrFlowService。Phase 1 兩條路並行 — 舊的 `/api/hr-flows/*` 端點保留供既有畫面 / 自動化測試使用，新的 spec.json 走 ProcessRuntime / Bundle / Sandbox 全套。HrFlows 是否退役 Phase 2 再決定。

每個 spec 包含：
- meta（flowCode 對齊 workflow.ts 的 FormCode）
- flow.nodes + edges
- userTasks（fields 從 hand-coded form 抽出）
- approvals（用 ActorRef 處理動態 routing）
- decisions / gateways（如有金額分流）
- notifications（on_assign + on_complete 至少各一）
- testCases（每個 flow 至少 1 個 happy path + 1 個 reject）
- sampleOrg（最小組）

驗收：每隻 spec 都通過 `BundleReproducibilityRunner`。

### PR-L2: useFlowSubmit hook + task 模式 form — ✅ DONE 2026-05-11

**狀態：** 完成。11 個 form 全部支援 `mode?: 'create' | 'task'` + `taskId?` + `onSubmitted?` 三個 runtime props。

#### 新增 hook
- `bpm-ui/src/hooks/useFlowSubmit.ts` — `submit(specCode, formData)` → 包 `POST /api/processes`，回傳 `{ instanceId, firstTaskId }`，pending/error state 給 UI 用。
- `bpm-ui/src/hooks/useFlowTask.ts` — 載入 `getTask(id)` snapshot + `submit/return/claim` actions。
- `bpm-ui/src/hooks/useFormRuntime.tsx` — combined wrapper，每隻 form 用同一個 hook 處理 create vs task mode、toast 顯示、所有 action 包 try/catch + onSubmitted callback。

#### FormShell ActionBar 擴展
- 新增 `mode?: 'create' | 'task'` prop。
- 新增 `nodeKind?: NodeKind` prop（task mode 用來決定 Approve/Reject vs Submit）。
- 新增 `pending?: boolean` 控制 disabled state + spinner。
- 新增 `onApprove(comment)` / `onReject(comment)` / `onReturnTask(comment)` callbacks。
- 內建 CommentDialog（用 Textarea + danger/default 按鈕，Reject/Return 強制需要 comment，Approve 為 optional）。
- create mode 行為完全保留（debug jump-to-step bar 也只在 create mode 顯示）。

#### 11 個 form 改動
| Form | 主要欄位 → spec field id 對應 |
|------|------------------------------|
| LeaveForm | `leave_type, date_range.{start,end}, days, reason` |
| GEEForm | `category, expense_date, amount, currency, invoice_no, description` (帶 UI category → spec enum 的 mapping helper) |
| GEVForm | `vendor_name, vendor_tax_id, invoice_no, invoice_date, category, amount, currency, vat_rate, description, invoice_file` |
| APEForm | `advance_amount, currency, purpose, expected_spend_date, expected_settle_date, charge_dept` |
| HWPForm | `category, item_name, spec, qty, shipping_loc, purpose, expected_date` (HW_CATS / PURPOSES 字面值 → spec enum mapping) |
| ITPRView | `category, item_name, vendor_name, spec, estimated_amount, currency`；任務型節點 (`task_it_spec/quote/confirm/finance_po`) 透過 `nodeId` 切換顯示對應的 input panel |
| TEOView | `trq_no, destination, purpose, travel_dates, actual_amount, total_amount, expense_breakdown, receipts`；任務型 finance_confirm/finance_review 各自 panel |
| TRQView | `destination, purpose, travel_dates, transport, estimated_amount, needs_pickup, pickup_address`；admin notify task 顯示 `booking_status + admin_note` |
| EXTOBView | `first_name, last_name, business_title, company, onboard_date, department, needs_mailbox, contract_*`；HR account task 顯示 `ad_login + account_created + ticket_no + hr_note` |
| ResignForm | task mode 走新 spec runtime 的 `expected_last_day, reason_category, reason_detail, handover_to, handover_items`；create mode 仍走 legacy HrFlowsController（兩條路並存） |
| DeptxForm | task mode 走新 spec runtime 的 `current_department, target_department, effective_date, reason, salary_impact, new_manager`；create mode 走 legacy HrFlowsController |

#### 4 個原本是純 read-only view 的 form（EXTOBView / ITPRView / TEOView / TRQView）
- 決定：**直接 promote 為 dual-mode form**（不開新檔案）。理由：原本 view 只是寫死的 closed case 截圖，沒有實質互動價值；換成 spec-driven editable form 同時支援 create + task mode，覆蓋面更廣。命名暫時保留 `*View.tsx` 避免改 import 路徑（PR-L3 整理 inbox 時可以一起改名 → `*Form.tsx`）。

#### App.tsx plumbing
- `Screen` type 加入可選 `taskId?: string`：`{ kind: 'form'; code: FormCode; taskId?: string }`。
- App.tsx 對 11 個 form 全部 pass `mode={taskId ? 'task' : 'create'}`、`taskId`、`onSubmitted={() => 回 home}`。
- 真正的 inbox routing 留給 PR-L3。

#### Demo guard
- workflow.ts FORMS map 保留（label / 中文名 / step list 給 stepper 用），符合計劃「Don't deprecate workflow.ts FORMS map」。
- Form 內部的 `STEP/PERSONA` 切換邏輯：create mode 仍可用（debug jump bar）；task mode 隱藏並由 runtime 提供 nodeKind。

#### 測試
- bpm-ui 沒有 vitest/jest 設定，按指示**不引入新 test framework**。
- `tsc -p tsconfig.app.json --noEmit` 全綠。
- `dotnet test bpm-svc.slnx` 仍 275/275 全綠（沒動 backend）。

#### 寫過程的小決策
1. `useFormRuntime` 改成 `.tsx`（含 FlowToast component），把 toast / error handling 集中在一處，11 個 form 都用同一個 hook 就好。
2. ResignForm / DeptxForm 在 component entry 早期 `if (mode === 'task') return <TaskMode>`，保留 legacy HrFlowsController 路徑於 create mode，符合「兩條路並存」決策。
3. GEE/GEV/HWP 的 UI category 字串（`'Internet Access, ADSL'` 等）跟 spec 的 enum (`'business' | 'misc'` 等) 不一致，加 mapping helper 兩邊轉換；React state 維持原樣（不重命名變數）。
4. `task.task.nodeId` 用來決定多步 task 表單顯示哪一段 panel（ITPRView / TEOView / TRQView / EXTOBView 都這樣處理），避免每個 task 都開一個 component。

### PR-L3: bpm-ui Inbox 整合 — ✅ DONE 2026-05-11

**狀態：** 完成。Home + Search 改吃真實 runtime 資料；inbox 點 task 直接帶 `taskId` 進 form task 模式。

#### Backend
- 新 endpoint `GET /api/processes/mine?status=active|completed|all&limit=N` — 回 `MyInstanceSummaryDto[]`（id / specCode / specVersion / status / startedAt / completedAt / lastActivityAt / openTaskCount / currentNodeLabel）。
- `IProcessQueryService.GetMyInstancesAsync(initiatorUserId, status, limit, ct)` — order by `LastActivityAt DESC`，clamp limit 1–200。`currentNodeLabel` 透過 `SpecSnapshot.GetNode(openTask[0].nodeId)?.Label` 取得（per-row 解一次 JsonDocument，list 規模有限可接受）。
- `ProcessTaskDto` 加上 `SpecCode` 欄位（從 `instance.SpecCode` 帶過來）— 讓 inbox 點擊不需再 fetch instance。`GetMineAsync` 多打一次 `db.ProcessInstances.Where(i => instanceIds.Contains(i.Id)).Select(i => new { Id, SpecCode })` 組成 dictionary。
- 3 個 stub `ThrowingQueryService`（ProcessAdmin / Reports / Simulate endpoint tests）補上新介面實作。
- 3 個新 controller test：`Mine_returns_only_caller_initiated_instances` / `Mine_status_filter_completed_excludes_running` / `Mine_invalid_status_throws_conflict`。
- `dotnet test bpm-svc.slnx` 275 → 278 全綠。

#### Frontend
- `bpm-ui/src/types/process.ts` 加 `MyInstanceSummaryDto` interface + `ProcessTaskDto.specCode: string`。
- `bpm-ui/src/lib/api/process.ts` 加 `myInstances(status, limit)` client function（含 enum 反序列化 boundary normalisation）。
- `bpm-ui/src/hooks/useMyInstances.ts` 新 hook（mirror `useMyTasks` lifecycle，30s polling、cancel guard、refresh()）。
- `Home.tsx` 完全 rewire：
  - `Pending Action` table 改吃 `useMyTasks('open')`，row 點擊用 `task.specCode` 直接 `setScreen({ kind: 'form', code, taskId: task.id })`。
  - `My Recent Cases` table 改吃 `useMyInstances('all')`。
  - StatCards 改用真實 inbox 計數 + my-instances 衍生（active / completed / cancelled / total）；persona 別固定的「Approved Today / Closed Today / Onboardings 30d」等 scoreboard 數字今天 API surface 沒給，標 demo 後續 add-real-reporting 補（PR-L3 v1 寫死為 0 / 移除）。
  - Activity Feed + Reminders 兩個 panel 標記 `demo` 小標，data 暫留 `MOCK_ACTIVITY` / `MOCK_REMINDERS`。
- `Search.tsx` 完全 rewire：
  - Filter 改 backend status (`Running|Completed|Cancelled|Errored`) + FormCode + 日期範圍 + keyword（match case id / spec code / current node label）。
  - Search button 改成 Refresh — 沒 cross-user search endpoint，client-side filter 已足。
  - Results table 顯示 case id 前 8 位 / type / current step label / started / last activity / open tasks / status badge。
  - SearchModal 同步改吃 `useMyInstances`。
- `App.tsx` 完全沒改 — PR-L2 的 `Screen.taskId` plumbing 已就位，這次只是真的帶 taskId 進來。
- `lib/mocks.ts` 保留：`MOCK_ACTIVITY` / `MOCK_REMINDERS` 還在用，其他 `MOCK_CASES` / `MOCK_USERS` / `MOCK_LEAVE_BALANCES` 等保留給 Attendance / Sandbox / 即將砍的 demo flow 用，PR-L3 不刪。
- `tsc -p tsconfig.app.json --noEmit` 全綠。

#### 寫過程的小決策
1. **`ProcessTaskDto.SpecCode` 實作策略**：原本想加在 `ProcessTask` entity 上但會牽動 migration；改成在 mapper 邊 join 邊組（mapper signature 變 `ToTaskDto(ProcessTask, string specCode)`）。`ToInstanceDto` 已有 instance object 可直接傳，`GetTaskAsync` 也已 fetch instance；只有 `GetMineAsync` 多一次 dictionary lookup。3 個 internal call site 改完即可，沒有外部 caller。
2. **Completed instance click-through**：spec 提到 v1 不做 view-only mode，Search.tsx 現在 completed case 不可點。等 add-real-reporting 或新的 view-only proposal。
3. **Admin persona 的 Home stat cards 沒有真正的 cross-user 計數**：今天唯一的 admin 看版是 `/api/admin/process-admin/cases/active`（admin gated）。PR-L3 v1 admin 仍看自己的數據，cross-tenant roll-up 是 add-real-reporting 的事。
4. **`MOCK_CASES` 不刪**：Attendance.tsx / 一些舊截圖 view 仍 import；移除是另一個整理 PR。

### PR-L4: SeedCli console app

新 project `bpm-svc/src/SeedCli/SeedCli.csproj`（控制台，引用 Persistence + Application）。

CLI shape：
```bash
dotnet run --project bpm-svc/src/SeedCli -- <command> [options]

commands:
  reset                    drop bpm.db, re-apply migrations (no seed)
  seed                     seed users + departments + roles + role assignments only
  seed --include-bundles   above + build & install all sample_specs as bundles
  status                   show current users / bundles count
```

實作：
- `reset`：File.Delete(bpm.db) → `db.Database.MigrateAsync()`
- `seed`：呼叫已存在的 `OrgFixture.RunAsync(db, logger)`（已 idempotent），再用新的 `FullPersonaFixture` 補齊 6 個 persona × 各部門角色（HR Manager Yang / IT Lead Lin / Finance Head Sue / Travel Admin Pat / etc.）
- `seed --include-bundles`：對 sample_specs/*.json 每隻：
  - 用 `IBundleBuilder.BuildAsync` 建 bundle（含 sample-org + test-cases）
  - 透過 `IBundleParser` + `IBundleValidator` + `IBundleRuntimeLoader` 走完一次 install 路徑
  - 寫一個 SpecBundle row，Status = Installed

**為什麼用獨立 console app 而不是 web API endpoint？**
- 純 ops 工具，不該透過 HTTP / 不需要 auth
- 可整合到 GitHub Actions / Docker entrypoint
- Jason / 夥伴在新環境 1 個 command 就有可 demo state

### PR-L5: E2E 大驗收測試

`bpm-svc/tests/Bpm.Tests/Integration/AllFlowsRealE2ETests.cs`：

對每個 flow code：
1. 用 SeedCli 邏輯 seed 一個 fresh in-memory DB
2. 開沙箱
3. 模擬 employee 提交 → 抓 inbox → 切 persona → submit → 重複到 instance.Status = Completed
4. assert：mailbox 有對應 on_assign + on_complete 信、reports 計數 +1、history 完整
5. reset → 重跑一遍

11 個 flow × 2 case (happy + reject)= ~22 個 sub-test。

### PR-L6: 文件 + 收尾

- `docs/all-flows-demo-script.md`：sales 用的「11 個 flow 都怎麼 demo」一頁式腳本
- 更新 `bpm-svc/CLAUDE.md`：SeedCli usage
- 更新根目錄 `CLAUDE.md`「現在進度」
- 把 `add-form-runtime-rendering` 標為 next-up（Phase 2 入口）

---

## Phase 1 工作量估計

| PR    | 內容                          | 估 task  |
|-------|------------------------------|----------|
| PR-L1 | 8 個 spec.json 撰寫 + 對齊    | 8        |
| PR-L2 | hook + form 兩種模式          | ~15      |
| PR-L3 | Home/Search 接 runtime + inbox| ~10      |
| PR-L4 | SeedCli project               | ~12      |
| PR-L5 | E2E 大驗收測試                | ~22      |
| PR-L6 | docs + 收尾                   | 4        |
| **合計** |                              | **~71**  |

對應規模：跟 spec-bundle 8 PR / acceptance-sandbox 6 PR 同層級，一輪可結束。

---

## SeedCli 詳細設計

### Project 結構
```
bpm-svc/src/SeedCli/
├── SeedCli.csproj          # net10.0 console, refs Application + Persistence + Infrastructure
├── Program.cs              # Main: parse args, build host, dispatch command
├── Commands/
│   ├── ResetCommand.cs
│   ├── SeedCommand.cs
│   ├── StatusCommand.cs
│   └── BundleInstallCommand.cs
├── Fixtures/
│   ├── FullPersonaFixture.cs   # 補齊 OrgFixture 沒覆蓋的角色
│   └── BundleSeedFixture.cs    # 把 sample_specs 包成 bundle 並 install
└── README.md
```

### Persona seed shape

| Email                  | FullName        | Role(s)                       | Department    | Manager      |
|-----------------------|-----------------|-------------------------------|---------------|--------------|
| wilson@acme.test      | Wilson You 游上毅 | employee                      | Engineering   | yang         |
| yang@acme.test        | Yang Wei 楊偉   | employee, manager             | Engineering   | chen         |
| chen@acme.test        | Chen Vi 陳偉   | employee, manager, vp         | Engineering   | (CEO)        |
| ceo@acme.test         | CEO Liu 劉執行  | ceo                           | Executive     | null         |
| mary@acme.test        | Mary Chen 陳瑪麗 | employee, hr, manager:HR      | HR            | hr_lead      |
| hr_lead@acme.test     | HR Lead 黃人事   | employee, manager, hr_admin   | HR            | ceo          |
| sue@acme.test         | Sue Wang 王蘇   | employee, finance             | Finance       | finance_head |
| finance_head@acme.test| Finance Head 賴 | employee, manager, finance    | Finance       | ceo          |
| lin@acme.test         | Lin Tu 屠林     | employee, it                  | IT            | it_lead      |
| it_lead@acme.test     | IT Lead 邱資訊   | employee, manager, it_admin   | IT            | ceo          |
| pat@acme.test         | Pat Lo 羅派     | employee, admin               | Operations    | admin_lead   |
| admin_lead@acme.test  | Admin Lead 張總 | employee, manager, admin      | Operations    | ceo          |
| jason_test@acme.test  | Jason 測試員    | tenant_admin                  | (none)        | null         |

**13 個 user，4 個 department，覆蓋所有 11 個 flow 的所有 routing 需求。**

### Role 體系

System roles: `tenant_admin`
Flow-scoped roles: `manager`, `hr`, `finance`, `it`, `admin`, `vp`, `ceo`
Spec-scoped roles: `manager:HR`, `it_admin`, `hr_admin`（讓 HR 部門主管能簽 HR 流程的特殊角色）

### Bundle install 邏輯

對 sample_specs/*.json 每一隻：
1. 讀 spec.json
2. 從 seed 出來的 org 建 SampleOrgSnapshot
3. 從 spec 的 testCases section（spec 自帶）建 TestCaseSnapshot[]
4. `IBundleBuilder.BuildAsync` → bytes
5. 直接走 `FlowLibraryController.Import`(install mode) 邏輯（提取為 service 方法 `IBundleInstallService` 共用）
6. 失敗就 abort 整個 seed 並印 reason（不留半 seeded state）

---

## Phase 2 預告（不在這次計畫內，列出避免再做時撞坑）

`add-form-runtime-rendering`（47 tasks）做完後，PR-L2 的 hand-coded form 會被一個 `<DynamicForm spec={spec} mode="..." />` 取代。屆時：
- form code 從 ~2000 行 React 縮成 1 個 component
- spec 改欄位真正自動同步
- view-only forms（EXTOBView / ITPRView / TEOView / TRQView）可以直接合併

Phase 2 入口：等 Jason 與夥伴看完 Phase 1 demo 後再啟動。

---

## 風險

1. **Demo guard 解鎖** — Home / Search / Report / forms / lib/workflow.ts 都會動，這是專案開始時設的「不要動」清單。要明確跟 Jason 確認他 OK（看樣子他要的就是這個結果）。
2. **9 個 form 都接 runtime 後，前端視覺有沒有 bug？** — 每 form 個別 PR 結尾都要實際 boot bpm-ui 跑一遍，chrome-devtools 截圖驗證（fullPage=true 預設）。
3. **HrFlowsController 重複** — RESIGN / DEPTX 在 HrFlows 也是 hard-coded。這次新 spec 對齊後，建議併存一陣，再排第三波 PR 把 HrFlows 整個體系 deprecate。
4. **SeedCli 跟 Program.cs 的 startup seed 邏輯重複** — 用同一個 `IFullPersonaFixture` service 共用，避免兩處不同步。
5. **bundle install 路徑要 idempotent** — re-run seed 不該爆掉。靠 ManifestChecksum unique index（已存在）+ 409 處理。

---

## 給 Jason 的問題（已 2026-05-11 決議）

1. ✅ **A** — Phase 1 走 A（保留 hand-coded form 接 runtime）；B 留給 form-runtime-rendering 那波。
2. ✅ **HrFlows 並存** — RESIGN / DEPTX 兩條路並行（舊 HrFlowsController 保留；新 spec.json 走 ProcessRuntime）。Phase 2 再決定是否退役舊路。
3. ✅ **SeedCli 預設不含 bundles** — `seed` 只 seed 組織 / 角色；要 demo state 就明確 `seed --include-bundles`。
4. ✅ **Demo guard 正式解封** — Phase 1 在 CLAUDE.md 寫明：解封 `forms/`、`workflow.ts`、`Home.tsx`、`Search.tsx`；`Report.tsx` 暫留保護等 add-real-reporting；`lib/workflow.ts` 改寫為 spec-driven 入口。
