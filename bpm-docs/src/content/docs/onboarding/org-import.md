---
title: 組織資料匯入與角色對應
description: 把貴公司的真實組織接進來 — 手動維護或 OData 同步。
sidebar:
  order: 5
---

流程驗收完成後，最後一塊拼圖是**真實組織**：人、部門、主管鏈、角色指派。

## 兩條路

| 方式 | 適合 | 做法 |
|---|---|---|
| **後台手動維護** | ≤ 幾十人、異動少 | [User & Role](/backend/user-role/) 逐筆建 |
| **OData 匯入/同步** | 已有 HR 系統 / AD | 用貴公司的 iPaaS（Power Automate 等）打 [OData 端點](/api/org-crud/)，`?upsert=true` 冪等推送，之後排程同步 |

## 匯入順序

1. **角色**（upsert by Code）與**部門**
2. **使用者**（email 為 key，upsert 冪等）
3. **角色指派**（role code 對應：貴公司的「課長」對應到流程裡的哪個角色？）
4. 設定初始密碼（bound action `SetPassword`）或走 SSO

:::note
部門歸屬、直屬主管、部門主管、群組目前不在 OData 上（見[組織資料 CRUD](/api/org-crud/)的說明）——這幾項在後台 [User & Role](/backend/user-role/) 維護，或導入期由 flowcook 顧問協助批次建立。
:::

## 角色對應

拿一張表把**流程裡用到的每個角色**列出來，逐一對應到貴公司的真人/部門/群組（導入期 flowcook 顧問會和你們一起對）。對完跑一次 [Doctor](/backend/doctor/)——沒有在職成員的角色會被點名，上線前必須清零。
