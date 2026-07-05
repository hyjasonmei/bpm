---
title: 組織資料匯入與角色對應
description: 把客戶的真實組織接進來 — 手動維護或 OData 同步。
sidebar:
  order: 5
---

流程驗完收，最後一塊拼圖是**真實組織**：人、部門、主管鏈、角色指派。

## 兩條路

| 方式 | 適合 | 做法 |
|---|---|---|
| **後台手動維護** | ≤ 幾十人、異動少 | [User & Role](/backend/user-role/) 逐筆建 |
| **OData 匯入/同步** | 已有 HR 系統 / AD | 客戶的 iPaaS（Power Automate 等）打 [OData 端點](/api/org-crud/)，`?upsert=true` 冪等推送，之後排程同步 |

## 匯入順序

1. **部門**（含部門主管）
2. **使用者**（email 為 key；掛主部門、設直屬主管）
3. **角色指派**（role code 對應：客戶的「課長」= 我們流程裡的哪個角色？）
4. 設定初始密碼（bound action `SetPassword`）或走 SSO

## 角色對應工作坊

拿一張表把**流程裡用到的每個角色**列出來，跟客戶一一對到真人/部門/群組。對完跑一次 [Doctor](/backend/doctor/)——沒有在職成員的角色會被點名，上線前必須清零。
