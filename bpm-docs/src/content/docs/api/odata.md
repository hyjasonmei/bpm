---
title: OData 整合
description: 用 OData 把組織資料與自訂資料集接進 flowcook。
---

flowcook 透過 OData 提供組織資料的整合介面，客戶端 iPaaS（Power Automate / Azure Data Factory 等）可推入或讀取。

## 認證

OData 端點用 **Basic auth**（整合專用帳號），與一般使用者 JWT 分離。

```bash
curl -u "$USER:$PASS" https://<admin-svc>/odata/Users
```

## 組織資料（CRUD）

- `GET/POST/PATCH/DELETE /odata/Users`（可 `?upsert=true` 以 email 冪等）
- `/odata/Departments`、`/odata/Roles`（`?upsert=true` 以 code）、`/odata/Memberships`
- 設定密碼：bound action `SetPassword`

## 批次

- `POST /odata/$batch`：一次 request 推多筆。逐筆各自成敗（非交易性）。

## 自訂資料集（動態表）

- `GET /odata-ds/$metadata`：CSDL，每個資料集一張表
- `GET /odata-ds/{dataset}`：資料列，支援 `$filter` / `$select` / `$orderby` / `$top` / `$count`

（Power Automate 逐步接法待補。）
