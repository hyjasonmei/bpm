---
title: 資料集 Datasets
description: 表單下拉選單背後的參考資料表。
sidebar:
  order: 3
---

資料集是**參考資料表**：行政區、幣別、供應商清單……表單裡的下拉選單、連動選擇從這裡取值，改資料不用改流程。

![資料集 — 台灣行政區劃資料表](../../../assets/screens/datasets.png)

## 特性

- 每個資料集是一張獨立資料表，欄位結構自訂
- 後台直接編輯內容，前台表單即時取用
- 也開放 [OData 動態表端點](/api/datasets/)（`/odata-ds`），客戶的 iPaaS 可自動推送更新——供應商清單交給採購系統維護，flowcook 只讀
