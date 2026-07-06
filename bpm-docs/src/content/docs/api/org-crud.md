---
title: 組織資料 CRUD
description: Users / Departments / Roles / Memberships 完整屬性型別與 request / response 範例。
sidebar:
  order: 3
---

組織資料的整合端點，HR 系統同步的主戰場。四個 entity set 都掛在 `/odata` 下，[Basic auth](/api/auth/) 認證；權威 schema 可直接抓 `GET /odata/$metadata`（CSDL）。

通則：

- 屬性名稱為 **PascalCase**（與 $metadata、回應一致）
- 所有刪除都是**軟刪除**（保留歷史簽核紀錄），回 `204 No Content`
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

## 目前不在 OData 上的組織資料

以下幾項**尚未開放** OData 端點，導入期由後台 [User & Role](/backend/user-role/) 維護，或由 flowcook 顧問協助批次建立：

- **部門歸屬**（使用者 ↔ 部門、主部門）
- **直屬主管 / 部門主管**（簽核路由的 manager / dept-head 解析來源）
- **部門階層**（上層部門）

需要自動同步這幾項的話，請與 flowcook 討論。

## 同步排程建議

1. 先推 **Roles**（upsert by Code）與 **Departments / Groups** → 再推 **Users**（upsert by Email）→ 最後 **Memberships / GroupMembers**
2. 大批量用 [$batch](/api/batch/) 包裝；失敗筆修正後整批重推（upsert 冪等，重跑無害）
3. 推完跑一次 [Doctor](/backend/doctor/) 驗證沒有無人角色
