---
title: 組織資料 CRUD
description: 使用者、部門（歸屬・階層・主管）、群組、角色與指派的完整屬性型別與 request / response 範例。
sidebar:
  order: 3
---

組織資料的整合端點，HR 系統同步的主戰場。十個 entity set 都掛在 `/odata` 下，[Basic auth](/api/auth/) 認證；權威 schema 可直接抓 `GET /odata/$metadata`（CSDL）。

通則：

- 屬性名稱為 **PascalCase**（與 $metadata、回應一致）
- 使用者 / 部門 / 群組的刪除是**軟刪除**（保留歷史簽核紀錄）；角色刪除會**連同其所有指派**一起清除；關聯資料列（歸屬、主管、階層、群組成員、角色指派）的刪除是移除該筆關聯。皆回 `204 No Content`
- 每一筆寫入都會記入[稽核](/backend/audit/)
- 列表查詢支援 `$filter` / `$select` / `$orderby` / `$top` / `$count`

---

## Users `/odata/Users`

| 屬性 | 型別 | 說明 |
|---|---|---|
| `Id` | Guid | **Key**，系統產生，建立時不用給 |
| `DisplayName` | string | **必填**，顯示名稱 |
| `Email` | string \| null | 登入帳號；`?upsert=true` 以此為比對鍵 |
| `Active` | bool | 在職狀態（停用 ≠ 刪除） |

### 建立 / Upsert

```http
POST /odata/Users?upsert=true
Content-Type: application/json

{ "DisplayName": "Bob Chen", "Email": "bob@example.com", "Active": true }
```

回應 `201 Created`（email 已存在且帶 upsert 時為**更新**，回 `204 No Content`）：

```json
{
  "@odata.context": "https://<admin-svc>/odata/$metadata#Users/$entity",
  "Id": "c6e84f6a-362a-4673-a268-ae4e9b94d626",
  "DisplayName": "Bob Chen",
  "Email": "bob@example.com",
  "Active": true
}
```

不帶 `?upsert=true` 時，email 重複回 `400`：`"Email already in use."`

### 查詢

```http
GET /odata/Users?$filter=Active eq true&$select=Id,DisplayName,Email&$count=true
```

```json
{
  "@odata.context": "https://<admin-svc>/odata/$metadata#Users(Id,DisplayName,Email)",
  "@odata.count": 13,
  "value": [
    { "Id": "c6e84f6a-…", "DisplayName": "Bob Chen", "Email": "bob@example.com" }
  ]
}
```

### 部分更新

```http
PATCH /odata/Users(c6e84f6a-362a-4673-a268-ae4e9b94d626)
Content-Type: application/json

{ "Active": false }
```

回應 `204 No Content`。

### 刪除（軟刪除）

```http
DELETE /odata/Users(c6e84f6a-362a-4673-a268-ae4e9b94d626)
```

回應 `204 No Content`。

### 設定密碼（bound action）

```http
POST /odata/Users(c6e84f6a-362a-4673-a268-ae4e9b94d626)/SetPassword
Content-Type: application/json

{ "password": "S3cure-Init-Pass!" }
```

回應 `204 No Content`。密碼不是 entity 屬性——不會出現在任何讀取與 `$metadata`（走 SSO 的話用不到這個 action）。

---

## Departments `/odata/Departments`

| 屬性 | 型別 | 說明 |
|---|---|---|
| `Id` | Guid | **Key** |
| `DisplayName` | string | **必填** |
| `Active` | bool | 啟用狀態 |

### 建立

```http
POST /odata/Departments
Content-Type: application/json

{ "DisplayName": "研發部" }
```

回應 `201 Created`：

```json
{
  "@odata.context": "https://<admin-svc>/odata/$metadata#Departments/$entity",
  "Id": "1f2a3b4c-…",
  "DisplayName": "研發部",
  "Active": true
}
```

:::caution
Departments 的 POST **沒有 upsert**（部門名稱不保證唯一）——同步排程請先 `GET` 比對再決定 POST 或 PATCH，避免建出重複部門。
:::

`PATCH /odata/Departments({id})`（可改 `DisplayName`、`Active`）→ `204`；`DELETE` → `204`。

---

## Groups `/odata/Groups`

跨部門編組（例：審議委員會）。模式與 Departments 相同。

| 屬性 | 型別 | 說明 |
|---|---|---|
| `Id` | Guid | **Key** |
| `DisplayName` | string | **必填** |
| `Active` | bool | 啟用狀態 |

### 建立

```http
POST /odata/Groups
Content-Type: application/json

{ "DisplayName": "採購審議委員會" }
```

回應 `201 Created`：

```json
{
  "@odata.context": "https://<admin-svc>/odata/$metadata#Groups/$entity",
  "Id": "019f3653-…",
  "DisplayName": "採購審議委員會",
  "Active": true
}
```

`PATCH /odata/Groups({id})`（可改 `DisplayName`、`Active`）→ `204`；`DELETE` → `204`（軟刪除）。POST 無 upsert（同 Departments 的注意事項）。

---

## GroupMembers `/odata/GroupMembers` — 群組成員

| 屬性 | 型別 | 說明 |
|---|---|---|
| `GroupId` | Guid | **複合 Key**：群組 Id |
| `MemberPrincipalId` | Guid | **複合 Key**：成員的 principal Id——可以是使用者、部門或另一個群組（巢狀） |
| `MemberType` | string | 唯讀，系統依成員 principal 自動判定（`User` / `Dept` / `Group`） |

成員語意：`User` 直接是群組成員；`Group` 為巢狀群組，其成員遞迴展開；`Dept` 表示**整個部門加入群組**——該部門的直屬成員都視為群組成員（掛在群組上的角色會套用到他們，簽核路由亦同）。

### 加入成員

```http
POST /odata/GroupMembers?upsert=true
Content-Type: application/json

{ "GroupId": "019f3653-…", "MemberPrincipalId": "c6e84f6a-…" }
```

回應 `201 Created`：

```json
{
  "@odata.context": "https://<admin-svc>/odata/$metadata#GroupMembers/$entity",
  "GroupId": "019f3653-…",
  "MemberPrincipalId": "c6e84f6a-…",
  "MemberType": "User"
}
```

帶 `?upsert=true` 時重複加入＝no-op 成功（回 `204`）；群組不可加入自己（回 `400`）。

### 移除成員

```http
DELETE /odata/GroupMembers(GroupId=019f3653-…,MemberPrincipalId=c6e84f6a-…)
```

回應 `204 No Content`。

---

## Roles `/odata/Roles`

| 屬性 | 型別 | 說明 |
|---|---|---|
| `Id` | Guid | **Key** |
| `Code` | string | **必填、全域唯一**，SCREAMING_SNAKE（例 `HR_MANAGER`）——流程簽核路由認這個；`?upsert=true` 以此為比對鍵 |
| `Name` | string | **必填**，顯示名稱 |
| `Description` | string \| null | 說明 |
| `IsSystem` | bool | 系統角色標記（整合請只建自訂角色） |

### 建立 / Upsert

```http
POST /odata/Roles?upsert=true
Content-Type: application/json

{ "Code": "LEGAL_REVIEWER", "Name": "法務審查", "Description": "合約審查流程的法務關" }
```

回應 `201 Created`（Code 已存在且帶 upsert 時**更新**該角色，回 `204 No Content`）：

```json
{
  "@odata.context": "https://<admin-svc>/odata/$metadata#Roles/$entity",
  "Id": "68fe7c39-…",
  "Code": "LEGAL_REVIEWER",
  "Name": "法務審查",
  "Description": "合約審查流程的法務關",
  "IsSystem": false
}
```

`PATCH` / `DELETE` 同上模式。

---

## Memberships `/odata/Memberships` — 角色指派

:::note
這張表是 **principal ↔ 角色**的指派（誰擁有什麼角色），**不是**「使用者屬於哪個部門」。PrincipalId 可以是使用者、部門或群組的 Id——掛在部門/群組上時搭配 `InheritToMembers` 讓成員自動繼承。
:::

| 屬性 | 型別 | 說明 |
|---|---|---|
| `PrincipalId` | Guid | **複合 Key**：使用者 / 部門 / 群組的 Id |
| `RoleId` | Guid | **複合 Key**：角色 Id |
| `InheritToMembers` | bool | 掛在部門/群組時，成員是否自動繼承此角色 |
| `IncludeSubDepts` | bool | 僅部門有意義：連同**所有子孫部門**的成員一起繼承（預設 false = 只及直接成員） |
| `AssignedAt` | DateTime | 指派時間（系統填，request 不用給） |

### 指派

```http
POST /odata/Memberships?upsert=true
Content-Type: application/json

{ "PrincipalId": "c6e84f6a-…", "RoleId": "68fe7c39-…", "InheritToMembers": false, "IncludeSubDepts": false }
```

回應 `201 Created`：

```json
{
  "@odata.context": "https://<admin-svc>/odata/$metadata#Memberships/$entity",
  "PrincipalId": "c6e84f6a-…",
  "RoleId": "68fe7c39-…",
  "InheritToMembers": false,
  "IncludeSubDepts": false,
  "AssignedAt": "2026-07-06T06:20:00Z"
}
```

帶 `?upsert=true` 時重複指派會**更新兩個 flag**（冪等）；不帶時回 `400`：`"Membership already exists."`

### 移除指派

```http
DELETE /odata/Memberships(PrincipalId=c6e84f6a-…,RoleId=68fe7c39-…)
```

回應 `204 No Content`。

---

## UserDepartments `/odata/UserDepartments` — 部門歸屬

:::note
「使用者屬於哪個部門」在這裡維護。一位使用者可以同時屬於多個部門，但**最多一個主部門**（`IsPrimary`）——主部門決定「部門主管」類簽核步驟找誰。
:::

| 屬性 | 型別 | 說明 |
|---|---|---|
| `UserId` | Guid | **複合 Key**：使用者 Id |
| `DeptId` | Guid | **複合 Key**：部門 Id |
| `IsPrimary` | bool | 是否為主部門。設 `true` 時系統自動把該使用者其他部門的主部門標記取消（一人恆一主部門） |

### 加入部門 / 更新主部門

```http
POST /odata/UserDepartments?upsert=true
Content-Type: application/json

{ "UserId": "c6e84f6a-…", "DeptId": "eca2fd8e-…", "IsPrimary": true }
```

回應 `201 Created`：

```json
{
  "@odata.context": "https://<admin-svc>/odata/$metadata#UserDepartments/$entity",
  "UserId": "c6e84f6a-…",
  "DeptId": "eca2fd8e-…",
  "IsPrimary": true
}
```

帶 `?upsert=true` 時，已存在的歸屬會**更新 `IsPrimary`**（回 `204`，冪等）；不帶時重複回 `400`：`"User is already in this department."`

### 移出部門

```http
DELETE /odata/UserDepartments(DeptId=eca2fd8e-…,UserId=c6e84f6a-…)
```

回應 `204 No Content`。

:::caution
複合 Key 的網址**順序固定為 `(DeptId=…,UserId=…)`**（依 $metadata 的 Key 順序）；寫成 `(UserId=…,DeptId=…)` 會比對不到路由。
:::

---

## Managers `/odata/Managers` — 直屬主管

:::note
「直屬主管」簽核步驟（例如請假單的主管核准）從這裡解析。每位使用者最多一位主管；沒有資料列＝沒有主管（匯報線頂端）。
:::

| 屬性 | 型別 | 說明 |
|---|---|---|
| `UserId` | Guid | **Key**：使用者 Id |
| `ManagerUserId` | Guid | 直屬主管的使用者 Id（不可是自己；不可造成匯報循環） |
| `AssignedAt` | DateTime | 指派時間（系統填，request 不用給） |

### 設定 / 更換主管

```http
POST /odata/Managers?upsert=true
Content-Type: application/json

{ "UserId": "c6e84f6a-…", "ManagerUserId": "d804cbde-…" }
```

回應 `201 Created`（已有主管時 `?upsert=true` 會**更換**，回 `204`）：

```json
{
  "@odata.context": "https://<admin-svc>/odata/$metadata#Managers/$entity",
  "UserId": "c6e84f6a-…",
  "ManagerUserId": "d804cbde-…",
  "AssignedAt": "2026-07-06T09:51:17Z"
}
```

防呆：自己當自己主管回 `400`；指派會沿主管鏈往上檢查，**形成循環**（A 的主管是 B、B 的主管又是 A）回 `400`：`"Assignment would create a reporting cycle."`

### 移除主管

```http
DELETE /odata/Managers(c6e84f6a-…)
```

回應 `204 No Content`。

---

## DepartmentHeads `/odata/DepartmentHeads` — 部門主管

:::note
「部門主管」簽核步驟從這裡解析：先取送單人的**主部門**，再查該部門的 head。每個部門最多一位主管；沒有資料列＝未設定（流程會走備援指派規則）。
:::

| 屬性 | 型別 | 說明 |
|---|---|---|
| `DeptId` | Guid | **Key**：部門 Id |
| `HeadUserId` | Guid | 部門主管的使用者 Id |
| `AssignedAt` | DateTime | 指派時間（系統填，request 不用給） |

### 設定 / 更換部門主管

```http
POST /odata/DepartmentHeads?upsert=true
Content-Type: application/json

{ "DeptId": "eca2fd8e-…", "HeadUserId": "d804cbde-…" }
```

回應 `201 Created`（已有主管時 `?upsert=true` 會**更換**，回 `204`）：

```json
{
  "@odata.context": "https://<admin-svc>/odata/$metadata#DepartmentHeads/$entity",
  "DeptId": "eca2fd8e-…",
  "HeadUserId": "d804cbde-…",
  "AssignedAt": "2026-07-06T09:51:17Z"
}
```

### 移除部門主管

```http
DELETE /odata/DepartmentHeads(eca2fd8e-…)
```

回應 `204 No Content`。

---

## DepartmentParents `/odata/DepartmentParents` — 部門階層

:::note
部門樹在這裡維護。每個部門最多一個上層部門；沒有資料列＝頂層部門。角色指派的 `IncludeSubDepts`（含子部門）就是沿這棵樹展開的。
:::

| 屬性 | 型別 | 說明 |
|---|---|---|
| `DeptId` | Guid | **Key**：部門 Id |
| `ParentDeptId` | Guid（可空） | 上層部門 Id（不可是自己；不可造成循環） |

### 設定 / 搬移上層部門

```http
POST /odata/DepartmentParents?upsert=true
Content-Type: application/json

{ "DeptId": "eca2fd8e-…", "ParentDeptId": "a0249e4d-…" }
```

回應 `201 Created`（已有上層時 `?upsert=true` 會**搬移**，回 `204`）：

```json
{
  "@odata.context": "https://<admin-svc>/odata/$metadata#DepartmentParents/$entity",
  "DeptId": "eca2fd8e-…",
  "ParentDeptId": "a0249e4d-…"
}
```

防呆：自己當自己上層回 `400`；指派會沿樹往上檢查，**形成循環**回 `400`：`"Assignment would create a cycle in the department tree."`

### 移除上層（變頂層部門）

```http
DELETE /odata/DepartmentParents(eca2fd8e-…)
```

回應 `204 No Content`。

---

## 同步排程建議

1. 先推 **Roles**（upsert by Code）與 **Departments / Groups** → 再推 **Users**（upsert by Email）→ 接著 **UserDepartments / DepartmentParents / Managers / DepartmentHeads** → 最後 **Memberships / GroupMembers**
2. 大批量用 [$batch](/api/batch/) 包裝；失敗筆修正後整批重推（upsert 冪等，重跑無害）
3. 推完跑一次 [Doctor](/backend/doctor/) 驗證沒有無人角色
