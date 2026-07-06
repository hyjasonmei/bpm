---
title: 組織資料 CRUD
description: Users / Departments / Roles / Memberships 的讀寫與 upsert。
sidebar:
  order: 3
---

組織資料的整合端點，HR 系統同步的主戰場。

## 端點

| 資源 | 端點 | upsert key |
|---|---|---|
| 使用者 | `/odata/Users` | email（`?upsert=true`） |
| 部門 | `/odata/Departments` | — |
| 角色 | `/odata/Roles` | code（`?upsert=true`） |
| 成員關係 | `/odata/Memberships` | — |

標準動詞：`GET / POST / PATCH / DELETE`。

## Upsert（冪等推送）

同步排程的關鍵：`?upsert=true` 讓「存在就更新、不存在就建立」，重跑不會炸重複。

```bash
curl -u "$USER:$PASS" -X POST \
  "https://<admin-svc>/odata/Users?upsert=true" \
  -H "Content-Type: application/json" \
  -d '{ "email": "bob@acme.example", "name": "Bob", "dept": "BACKEND" }'
```

## 設定密碼

Bound action **`SetPassword`** 對單一使用者設初始密碼（走 SSO 的話用不到）。

## 匯入順序與驗證

先部門 → 再使用者 → 最後角色指派（詳見[組織資料匯入](/onboarding/org-import/)）。推完跑 [Doctor](/backend/doctor/) 驗證沒有無人角色。
