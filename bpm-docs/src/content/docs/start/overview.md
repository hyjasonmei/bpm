---
title: flowcook 是什麼
description: 給導入員的高層總覽 — flowcook 的定位、兩大賣點與一條龍導入方式。
---

flowcook 是一套 **AI 驅動的企業流程（BPM）平台**：客戶說得出的流程（請假、採購、差旅、報到離職……），用 AI Kitchen 對談就能設計出來，經 Sandbox 驗收後上線，員工每天在前台送單、簽核。

![AI Kitchen — 已上線的流程一覽與員工 launcher 預覽](../../../assets/screens/ai-kitchen.png)

## 兩大賣點

1. **AI Kitchen onboarding** — 對談式設計：AI 逐步訪談、即時生成問卷，產出完整的流程規格（spec bundle），再煮成可運行的流程程式。導入員不用寫 code，也不用畫 BPMN。
2. **無痛上線驗收** — Sandbox：攔信信箱、persona 切換、時間快轉、狀態重置。一位驗收員就能自己跑完整套 UAT，不用真的寄信、不用等七天假期真的過完。

## 一套堆疊、兩個入口

| 入口 | 誰在用 | 做什麼 |
|---|---|---|
| **前台**（員工端） | 全體員工、主管 | 送單、收件匣簽核、委任代理人、查詢案件 |
| **後台**（管理端） | 導入員、客戶管理員 | AI Kitchen 設計流程、User & Role 組織維護、Sandbox 驗收、稽核與站台設定 |

部署是 **per-customer**（每個客戶一套獨立堆疊，無 multi-tenant），可由我們代管或客戶自 host。

## 接下來讀什麼

- 想先看架構 → [系統全貌](/start/system/)
- 看員工每天用的東西 → [前台功能介紹](/frontend/home/)
- 準備帶客戶導入 → [導入 Playbook](/onboarding/playbook/)
