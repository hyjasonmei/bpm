---
title: 首頁／儀表板
description: 員工登入後的第一頁 — 待辦、案件狀態、快速開單。
sidebar:
  order: 1
---

員工登入前台後的首頁。所有「今天要處理什麼」都在這一頁。

![前台首頁 — 主管視角，有一件待簽核的請假案](../../../assets/screens/home-dashboard.png)

## 版面分區

| 區塊 | 內容 |
|---|---|
| **頂部統計卡** | 待我處理 / 進行中 / 已完成 / 總送件數，一眼掌握個人負載 |
| **Pending My Action** | 等這個人動作的案件（簽核、補件），點 **Open** 直接進案件 |
| **My Recent Cases** | 自己送出的案件與最新狀態 |
| **Quick Actions** | 已上線流程的快速開單入口（清單由後台 AI Kitchen 的 launcher 設定控制，可分組、排序、換圖示） |
| **Activity Feed** | 近期跟自己有關的案件動態 |

## 常見操作

- **簽核**：Pending My Action → Open → 案件詳情底部的核准/退件，見[簽核（含並簽）](/frontend/approval/)
- **開新申請**：Quick Actions 點流程，或上方 **Create** 進完整表單目錄
- **找舊案**：上方 **Search** 依關鍵字 / 流程 / 狀態查詢
