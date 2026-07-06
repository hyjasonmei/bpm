---
title: 整合總覽
description: flowcook 對外整合的全貌 — OData、認證與典型串接情境。
sidebar:
  order: 1
---

flowcook 用 **OData** 做系統整合：標準協議，Power Automate、Azure Data Factory 等 iPaaS 都有現成 connector，貴公司 IT 不用學私有 API。

## 兩組端點

| 端點 | 做什麼 | 詳見 |
|---|---|---|
| `/odata/*` | **組織資料**：使用者 / 部門（歸屬・階層・主管）/ 群組 / 角色與指派 的讀寫 | [組織資料 CRUD](/api/org-crud/) |
| `/odata-ds/*` | **自訂資料集**：表單參考資料的動態表，讀取與推送 | [自訂資料集動態表](/api/datasets/) |

## 典型串接情境

1. **HR 系統 → flowcook 組織同步**：排程把人事異動 upsert 進來（[組織資料 CRUD](/api/org-crud/) + [$batch](/api/batch/)）
2. **採購系統 → 供應商清單**：供應商主檔推進資料集，表單下拉自動更新
3. **BI 讀取**：用 `$filter` / `$select` 拉資料進報表

## 先讀認證

所有整合端點走 **Basic auth 整合帳號**，與一般使用者 JWT 分離——見[OData 認證](/api/auth/)。
