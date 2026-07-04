---
title: 系統全貌
description: 給導入員的高層架構（非原始碼細節）。
---

flowcook 是 **per-customer 部署**（無 multi-tenant）：每個客戶一套獨立堆疊，可代管或自 host（見[交付](/onboarding/delivery/)）。一套堆疊由四個面組成：

| 面 | 誰在用 | 做什麼 |
|---|---|---|
| **前台** | 全體員工、主管 | 每天的送單、收件匣簽核、打卡、搜尋（[前台功能介紹](/frontend/home/)） |
| **後台** | 導入員、客戶管理員 | AI Kitchen 設計流程、User & Role 組織維護、Sandbox 驗收、稽核與站台設定（[後台功能介紹](/backend/ai-kitchen/)） |
| **AI 廚房 pipeline** | 我們（導入期） | 把對談產出的 spec 煮成可運行的 per-flow 程式 |
| **整合介面** | 客戶 IT / iPaaS | OData 組織資料與資料集串接（[API 串接](/api/overview/)） |

## 幾個關鍵設計

- **組織資料單一來源**：後台維護的 User & Role 是 canonical，前台簽核路由直接讀同一份——不會有兩邊對不上的問題
- **流程是編譯出來的，不是設定出來的**：每隻流程煮成獨立程式，跑起來快、行為可預期；改規則走版本演進（v1 → v2），舊案照舊版走完
- **驗收環境內建**：Sandbox 不是另一套環境，是同一套系統的安全模式——驗完關掉即正式
- **通知雙管道**：站內鈴鐺 + email，模板 per-flow 客製

## 名詞先修

流程 / 案件 / 關卡 / 角色這些詞的精確定義，見[名詞速查](/start/glossary/)。
