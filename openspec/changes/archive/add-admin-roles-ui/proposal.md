## Why

目前 `admin`、`hr`、`designer` 等角色 / role assignments 都靠 `OrgFixture` seed 寫死在程式裡。新進員工要加角色、員工離職要拔角色、新增 admin 帳號 → 都得改 code + 重啟服務。客戶 IT 不可能接受。

這個 change 在 `bpm-admin-ui` 加一個「Users & Roles」頁面，讓 admin 透過 UI 指派 / 撤銷角色，後端 API 補上對應的寫入端點 + audit。

不另開 `Site Settings → 設定管理員` 這個概念（如 Jason 原話 #4 提到）— 因為「指派 admin 角色」就是「Users & Roles」頁面上的一個 role assignment，沒必要分開做。

非目標：

- 不創角色（角色定義仍由 seed / migration 控管，避免 admin 不小心刪掉 system role）
- 不改權限模型（沒新增 RBAC 細節，只是現有 RoleAssignment 的 CRUD）
- 不導入 group 管理 UI（Group / GroupMember 表雖然存在，本 change 只動 RoleAssignment）
- 不做大量 import / sync（CSV import 走 `add-hr-sync-csv` 那個 change）

## What Changes

### Backend — Roles & Assignments API

- `GET /api/admin/roles` — 列出所有 system role + 描述 + 已指派的 user 數
- `GET /api/admin/users?q=&page=&pageSize=&roleCode=` — 分頁列出 users，支援搜尋（name / email）+ filter by role
- `GET /api/admin/users/{id}` — 單一 user 詳情：profile + 所有 role assignments（系統 + tenant scope）
- `POST /api/admin/users/{userId}/roles` body `{ roleCode, scope?, scopeRef? }` — 指派 role
- `DELETE /api/admin/users/{userId}/roles/{assignmentId}` — 撤銷 role
- 所有 endpoint require `admin` role
- 寫入操作 audit：寫一筆 `RoleAssignmentChange` row（actor, target user, role code, action: Assign/Revoke, timestamp）

### Backend — Guard

- 不能撤銷自己最後一個 `admin` role assignment（避免 lock-out）
- 不能撤銷整個 tenant 最後一個 admin 的 admin role
- 撤銷時若會違反上述規則 → 409 with explicit message

### Frontend (in bpm-admin-ui sidebar — depends on `add-admin-ui-split`)

- `Users & Roles` 頁面（`screens/admin/UsersRoles.tsx`）
  - 左側：搜尋欄 + role filter chip group + 分頁 user list
  - 右側：選定 user 的 detail panel
    - User profile（read-only：name, email, dept, manager, isActive）
    - "Assigned Roles" 區塊：表格列出當前 assignments + 每行 X 按鈕 revoke
    - "Add Role" 按鈕：開 modal 選 role code + （未來）scope/scopeRef
  - 操作後：refetch detail + toast

### UX 細節

- 撤銷 admin role 時跳 confirm dialog（「This will remove admin privileges from <name>」）
- 自我撤銷 admin → confirm 顯示警告，並後端拒絕
- "Assigned Roles" 顯示 role 描述 + 指派時間 + 指派人
- 整個 user list 預設按 last activity desc 排，方便找剛新增的人

## Impact

- Affected specs: NEW `bpm-admin-roles-ui`
- Affected code:
  - `bpm-svc/src/Application/Admin/IRoleAdminService.cs` (NEW)
  - `bpm-svc/src/Persistence/Admin/RoleAdminService.cs` (NEW)
  - `bpm-svc/src/Api/Admin/RolesAdminController.cs` (NEW)
  - `bpm-svc/src/Domain/Entities/Authz/RoleAssignmentChange.cs` (NEW; audit row)
  - Migration `AddRoleAssignmentAudit`
  - `bpm-admin-ui/src/screens/admin/UsersRoles.tsx` (NEW)
  - `bpm-admin-ui/src/lib/api/adminRoles.ts` (NEW)
- Existing OrgFixture seed unchanged — still seeds initial roles + the bootstrap admin / hr assignments

### Dependencies

- **Hard dependency on `add-admin-ui-split`** — the page lives in `bpm-admin-ui` which doesn't exist until that change ships
- Soft dependency on `add-user-impersonation` for proper audit (admin creating role assignment while impersonating gets recorded correctly via the shared interceptor pattern)
