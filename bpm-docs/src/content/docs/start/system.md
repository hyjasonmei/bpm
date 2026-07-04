---
title: 系統全貌
description: 給導入員的高層架構（非原始碼細節）。
---

flowcook 是 per-customer 部署（無 multi-tenant），一套堆疊由四個面組成：

- **前台**（bpm-ui）：員工/主管每天用的客戶端 runtime。
- **後台**（bpm-admin-ui）：管理員的 AI Kitchen / User & Role / Sandbox / 站台設定。
- **AI 廚房 pipeline**：把 spec 煮成 per-flow 程式。
- **官網**（bpm-www）：對外行銷站。

（細節待補。）
