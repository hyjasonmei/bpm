---
title: User & Role
description: 使用者、部門、群組與角色 — 簽核路由的資料來源。
sidebar:
  order: 2
---

User & Role 維護組織的 canonical 資料：**使用者 / 部門 / 群組**（principals）與**角色**。流程裡「送給主管」「送給財務」的簽核對象，就是從這裡解析出來的。

![User & Role — principal 清單與詳情編輯](../../../assets/screens/user-role.png)

## Principals

- 左欄列全部 principal，可依 **Users / Depts / Groups** 篩選，點一筆右欄編輯
- 使用者掛部門（主部門決定成本中心）、設直屬主管
- 部門可設**部門主管**（dept head），群組用於跨部門編組（如 Security Committee）
- 停用離職者用 **Soft-delete**（保留歷史案件的簽核紀錄）

## Roles

- 角色有 **Code**（如 `HR_MANAGER`，流程路由認這個）與顯示名稱
- 角色可指派給個人，也可掛在部門或群組上——成員**自動繼承**
- 哪些角色沒有在職成員（流程會卡），[Doctor](/backend/doctor/) 會主動點名

## 資料哪裡來

小組織直接在這頁維護；已有 HR 系統的客戶走 [OData 組織資料匯入](/api/org-crud/)自動同步。
