## Why

夥伴 dogfood 後第二批需求：客戶 demo 時希望看到 HR 領域更多流程，不只請假。離職、部門異動是中小企業 HR 第二高頻的流程（請假之後就是這兩個），是業務談單的重要展示素材。流程結構刻意做最簡單版本：**員工申請 → 主管簽 → HR 簽 → 結案**，外加主管能退回。

非目標：

- 工作交接的詳細追蹤系統（離職交接清單、知識移交模板）
- 異動後的 AD/Entra 群組自動同步（之後 `add-mcp-entra-sync` 才做）
- 部門異動的薪資/考績連動
- 跨主管會簽（原主管 + 新主管）— POC 只簽原主管
- 離職面談排程

## What Changes

### Two new form codes

- `RESIGN` — Resignation（離職申請）
- `DEPTX` — Department Transfer（部門異動申請）

### Flow shape (identical for both)

```
APPLY → MANAGER APPROVE → HR APPROVE → CLOSED
員工      原主管           HR         —
```

主管在 MANAGER APPROVE 步驟可選 Approve / Return。Return 會把 case 退回 APPLY，由原申請人修改後重送。HR APPROVE 步驟不允許 Return（POC 簡化），只能 Approve。

### Form fields

**RESIGN（離職申請）**

- `expectedLastDay` (date, required) — 預計離職日
- `reason` (textarea, required) — 離職原因
- `handover` (text input, optional) — 工作交接對象（POC 階段只是 free text；未來改 user picker）
- `note` (textarea, optional) — 備註

**DEPTX（部門異動申請）**

- `currentDepartment` (auto-fill from initiator profile, read-only)
- `targetDepartment` (select from departments, required)
- `effectiveDate` (date, required) — 生效日
- `reason` (textarea, required)

### Frontend (capability `bpm-hr-flows-ui`)

- `bpm-ui/src/lib/workflow.ts`
  - `FormCode` 加入 `RESIGN | DEPTX`
  - `FORMS` Record 加 `RESIGN` / `DEPTX` 兩筆，3 步驟 + ownerByStep `['employee', 'manager', 'hr', null]`
- `bpm-ui/src/components/AppLayout.tsx`
  - `FORM_GROUPS` 的 `HR` 組加：
    - `{ id: 'RESIGN', label: 'Resignation (離職申請)' }`
    - `{ id: 'DEPTX', label: 'Department Transfer (部門異動)' }`
- `bpm-ui/src/screens/forms/ResignForm.tsx`（NEW）— 4 欄位 + 主管 / HR 步驟切換的審批 panel
- `bpm-ui/src/screens/forms/DeptxForm.tsx`（NEW）— 4 欄位 + 同上
- 兩個 form 共用 `FormShell` 既有元件
- API client：`bpm-ui/src/lib/api/hrFlows.ts`（NEW）

### Backend (capability `bpm-hr-flows`)

注意：通用 `add-process-runtime` 尚未實作完成。為了讓 POC 端到端可串，本 change 提供**過渡用的最小可用後端**，刻意不重新發明 process runtime 的所有抽象，只支援 `RESIGN` + `DEPTX` 這兩個 spec_code 寫死的特例。當 `add-process-runtime` 正式實作完成後，這層程式碼會被移除/收編，spec 文件相應 archived。

**Domain entities**

- `HrFlowInstance`
  - `Id` (Guid), `TenantId`, `SpecCode` (`RESIGN` / `DEPTX`)
  - `InitiatorUserId`
  - `Status` (enum: `PendingManager`, `PendingHr`, `Returned`, `Completed`, `Cancelled`)
  - `CurrentStep` (enum: `Apply`, `ManagerApprove`, `HrApprove`, `Closed`)
  - `FormDataJson` (text)
  - `StartedAt`, `LastActivityAt`, `CompletedAt`, `CancelledAt`
- `HrFlowAction` (append-only audit)
  - `Id`, `InstanceId`, `ActorUserId`, `Action` (enum: `Submit`, `Approve`, `Return`, `Cancel`)
  - `FromStep`, `ToStep`
  - `Comment` (string, nullable)
  - `CreatedAt`

**Application service**

- `IHrFlowService`
  - `StartAsync(specCode, formData, initiatorUserId)` — create instance, set Status=PendingManager
  - `GetMineAsync(userId)` — instances initiated by user
  - `GetMyTodoAsync(userId)` — instances waiting on user (manager or HR)
  - `GetByIdAsync(instanceId, requesterUserId)` — read with permission check
  - `ApproveAsync(instanceId, actorUserId, comment)` — advance one step
  - `ReturnAsync(instanceId, actorUserId, comment)` — only at ManagerApprove; set Status=Returned, CurrentStep=Apply
  - `ResubmitAsync(instanceId, actorUserId, formData)` — only by initiator on Returned instance; reset to PendingManager
  - `CancelAsync(instanceId, actorUserId)` — initiator-only; only when not Completed

**API endpoints** (under `/api/hr-flows`)

- `POST /api/hr-flows/{specCode}` — start new instance (specCode in `{RESIGN, DEPTX}`)
- `GET /api/hr-flows/mine` — instances I started
- `GET /api/hr-flows/todo` — instances waiting on me
- `GET /api/hr-flows/{id}` — single instance with form data + history
- `POST /api/hr-flows/{id}/approve` — body: `{ comment? }`
- `POST /api/hr-flows/{id}/return` — body: `{ comment }` (required for return)
- `POST /api/hr-flows/{id}/resubmit` — body: `{ formData }`
- `POST /api/hr-flows/{id}/cancel`

**Persistence**

- 2 new EF entities + Configurations
- SQLite migration `AddHrFlows`

**Manager / HR resolution**

- Manager = `User.ManagerUserId` (looked up from initiator at instance start; cached on instance as `ResolvedManagerUserId` to be deterministic if org changes mid-flight)
- HR = any user with role `hr` in same tenant; instance enters PendingHr status, ANY hr-role user can approve (first-come-first-served, others see it disappear)

## Impact

- Affected specs: NEW `bpm-hr-flows` (interim backend), NEW `bpm-hr-flows-ui` (frontend forms)
- Affected code:
  - `bpm-svc/src/Domain/Entities/HrFlows/`
  - `bpm-svc/src/Application/HrFlows/`
  - `bpm-svc/src/Persistence/Configurations/HrFlows/`
  - `bpm-svc/src/Api/Controllers/HrFlowsController.cs`
  - `bpm-ui/src/lib/workflow.ts`
  - `bpm-ui/src/components/AppLayout.tsx`
  - `bpm-ui/src/screens/forms/ResignForm.tsx`, `DeptxForm.tsx`
  - `bpm-ui/src/lib/api/hrFlows.ts`
- 不影響既有 LEAVE / GEE / GEV 等流程（那些今天還是純前端 mock）
- DB 新增 2 張表，零修改既有表

### Sunset note

當 `add-process-runtime` 實作落地後，本 change 的後端部分（`HrFlowInstance` / `HrFlowAction` / `IHrFlowService` / `HrFlowsController`）SHALL 被遷移至通用 ProcessInstance / ProcessTask / TaskHistory 模型，並改由 `IProcessRuntime` 驅動。前端 form 元件保留但 API client 改指 `/api/process-instances`。`bpm-hr-flows` capability spec 屆時會 archive。
