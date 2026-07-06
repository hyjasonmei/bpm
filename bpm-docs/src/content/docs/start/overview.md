---
title: flowcook 是什麼
description: flowcook 的定位、兩大核心能力與導入方式總覽。
---

flowcook 是一套 **AI 驅動的企業流程（BPM）平台**：說得出的流程（請假、採購、差旅、報到離職……），用 AI Kitchen 對談就能設計出來，經 Sandbox 驗收後上線，員工每天在前台送單、簽核。

![AI Kitchen — 已上線的流程一覽與員工 launcher 預覽](../../../assets/screens/ai-kitchen.png)

## 兩大核心能力

1. **AI Kitchen 對談式流程設計** — AI 逐步訪談、即時生成問卷，產出完整的流程規格，再自動生成可運行的流程程式。設計流程不用寫 code，也不用畫 BPMN。
2. **無痛上線驗收** — Sandbox：攔信信箱、persona 切換、時間快轉、狀態重置。一位驗收員就能自己跑完整套 UAT，不用真的寄信、不用等七天假期真的過完。

## 一套堆疊、兩個入口

| 入口 | 誰在用 | 做什麼 |
|---|---|---|
| **前台**（員工端） | 全體員工、主管 | 送單、收件匣簽核、委任代理人、查詢案件 |
| **後台**（管理端） | 管理員（導入期含 flowcook 顧問） | AI Kitchen 設計流程、User & Role 組織維護、Sandbox 驗收、稽核與站台設定 |

部署是 **per-customer**（每家公司一套獨立堆疊，無 multi-tenant），可自 host 或由 flowcook 代管。

## 接下來讀什麼

- 想先看架構 → [系統全貌](/start/system/)
- 看員工每天用的東西 → [前台功能介紹](/frontend/home/)
- 準備開始導入 → [導入總覽](/onboarding/playbook/)
