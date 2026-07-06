---
title: User & Role
description: 使用者、部門、群組與角色 — 簽核路由的資料來源。
sidebar:
  order: 2
---

User & Role 維護組織的 canonical 資料：**使用者 / 部門 / 群組**（principals）與**角色**。流程裡「送給主管」「送給財務」的簽核對象，就是從這裡解析出來的。

![User & Role — principal 清單與詳情編輯](../../../assets/screens/user-role.png)

## Principals

- 左欄列全部 principal，可依 **Users / Depts / Groups** 篩選並建立，點一筆右欄編輯
- **使用者**：部門歸屬（可多個，星號標**主部門**）、**直屬主管**（Direct manager，「送給主管」步驟找的人）、角色指派、委任代理（可代使用者建立/取消委任）
- **部門**：上層部門（組織階層）、**部門主管**（Dept head，「送給部門主管」步驟找的人）、角色掛載；**群組**：跨部門編組（如 Security Committee），成員可加任意類型
- 停用離職者用 **Soft-delete**（保留歷史案件的簽核紀錄）

## Roles

- 角色有 **Code**（如 `HR_MANAGER`，流程路由認這個）與顯示名稱；系統角色唯讀，自訂角色可增刪改，並顯示指派人數
- 角色可指派給個人，也可掛在部門或群組上（開「成員繼承」）——成員**自動繼承**；部門指派可再勾「**包含子部門**」讓子孫部門的成員一併繼承（不勾＝只及該部門直接成員）
- 右欄的 **Effective roles** 顯示解析後的最終角色（直接 vs 繼承），驗路由最好用
- 哪些角色沒有在職成員（流程會卡），[Doctor](/backend/doctor/) 會主動點名

## 資料哪裡來

小組織直接在這頁維護（含直屬主管、部門主管）；已有 HR 系統的話，走 [OData 組織資料匯入](/api/org-crud/)自動同步——組織的每一項（含部門歸屬、主管、部門主管、部門階層）都有對應端點。缺漏由 [Doctor](/backend/doctor/) 檢查。
