# Design — add-admin-roles-ui

## 1. 為什麼不允許 UI 創 role

Role 定義（code + name + scope + permission set）應該由 platform owner 管，不是 tenant admin。理由：
- Code 是字串 key，UI 創容易拼錯（"hr" vs "HR" vs "Hr"）
- Permission 綁在 code 上，亂創 code 等於沒 permission
- Migration / sunset 路徑清楚（platform owner 控）

如果 tenant 真的需要客製角色，未來開 `add-tenant-custom-roles` change，限制名稱前綴 `tenant_` 之類。

## 2. 為什麼不能撤銷自己最後一個 admin role

不是技術限制，是 UX 安全網：
- Admin 不小心點 X → 自己變成普通 user → 沒人能再給他 admin → 找原廠 unlock
- 這種事每個 SaaS 都出過，全都加防呆

例外：可以撤銷別人的 admin role（包括另一個 admin）— 但前提是 tenant 內至少還有一個 admin（包含被撤銷後的剩餘人）。

## 3. 不能撤銷 tenant 最後一個 admin

兩階段檢查：
1. 計算當下 tenant 內 active admin user 數
2. 如果 = 1 且要撤的是這唯一一個 admin → 409

POC 階段 single-tenant，這個檢查就是「全系統 admin 數」。Tenant 多租戶上線後改成「同 tenant 內 admin 數」。

## 4. 為什麼 Role assignment audit 是新表而不是用 ActorResolutionAudit / 通用 audit

選項 A：通用 audit table，type column 區分

選項 B：每種 audit 一張表（現況：HrFlowAction, ActorResolutionAudit）

採用 B 路線（與既有一致）：
- 各表 schema 跟欄位專屬（RoleAssignment audit 有 RoleCode, AssignmentScope，跟其他無關）
- 查詢直觀（select * from RoleAssignmentChanges where actor=...）
- 不用 polymorphic JSON

統一機制：所有 audit 表都實作 `IImpersonable`（看 `add-user-impersonation`）讓 interceptor 自動寫 `ImpersonatedByUserId`。

## 5. UI 為什麼不放 Group / GroupMember

Groups 是流程定義引用的概念（`functional_members:hr`），管理 group 成員是另一個 admin 領域工具。為了 scope 收斂：
- 本 change 只動 RoleAssignment（控制誰能進 admin / 是 hr / 是 designer）
- Group 管理走未來 `add-admin-groups-ui` change
- 兩者拆開因為實際使用節奏不同（角色變動低頻、群組變動相對高頻）

## 6. 列表頁的搜尋 + 分頁

User 數量在中小企業典型 50–300 人，全部 load 進 client OK，但為了未來大企業考量，後端做分頁：
- 預設 pageSize = 50
- 搜尋 q on (FullName LIKE %q% OR Email LIKE %q%)
- Filter by roleCode（join RoleAssignment）

前端用簡單 list + pagination control，不用虛擬列表（POC 量小）。

## 7. Role detail panel — 為什麼分左右兩欄

UX：admin 在 user list 找人（需要看上下文：誰、最近活動），找到後不希望失去 list（要繼續看其他人）。

Master-detail 兩欄優於 push 詳情頁：
- 左 list 永遠在
- 右 detail 載入 selected user
- 操作 role 不離開 list 狀態

## 8. Add Role modal

- 從 `GET /api/admin/roles` 拿可用 role
- 顯示為 select dropdown（顯示 name + code）
- Scope 選擇：System / Tenant（根據 role 的 Scope 屬性自動鎖定，不讓 admin 亂選）
- ScopeRef：只有 Tenant scope role 需要（例如 `flow_admin:LEAVE` 形式）— POC 階段可暫時不開放，未來 spec 再延伸
- Confirm → POST → 後端寫 + audit

## 9. 不做的事

- ❌ Bulk assign（一次給多個 user 同 role）— 量小，未來再加
- ❌ Role-based view（「這 role 有哪些人」反向） — list 頁的 filter 已能達成同樣資訊
- ❌ CSV import（屬 `add-hr-sync-csv`）
- ❌ Role permission 編輯（permission 屬 platform-level，不開放）
