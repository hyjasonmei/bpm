---
title: 名詞速查
description: 常見術語一句話解釋。
---

| 術語 | 意思 |
|---|---|
| 流程 / Flow | 一條可送件的業務流程（如請假） |
| 案件 / Case | 一次流程的執行實例 |
| 關卡 | 流程中的一個節點（送件/簽核/歸檔） |
| 角色 / Role | 授權單位（可掛部門或群組，成員繼承） |
| 委任 / Delegation | 把某人的簽核權暫時交給代理人 |
| Persona | dev 模式下快速切換身分 |
| Sandbox | 驗收沙盒（mail capture / persona 切換 / 時間快轉 / reset） |
| spec bundle | AI Kitchen 產出的流程規格 zip |

## 案件狀態速查

| 狀態 | 意思 |
|---|---|
| `Pending…`（PendingManager / PendingFinance / PendingHr⋯） | 等某一關處理中，字尾就是在等誰 |
| `PendingParallelReview` | 並簽審查中（多位審核者同關） |
| `ResubmitRequired` | **退回補件** — 申請人修正後可重新送出（見[退回、重送與撤回](/frontend/resubmit/)） |
| `Completed` | 走完全部關卡，正常結案 |
| `Cancelled` | 申請人自己撤回 |
| `Rejected` | 審核者終局否決（僅部分流程使用；多數流程退件走 ResubmitRequired 迴圈） |
