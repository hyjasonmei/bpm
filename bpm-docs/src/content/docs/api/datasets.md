---
title: 自訂資料集動態表
description: /odata-ds — 每個資料集一張 OData 表，讀取與推送。
sidebar:
  order: 5
---

後台建的每個[資料集](/backend/datasets/)都自動變成一張 OData 表，掛在 **`/odata-ds`** 下。

## 端點

| 端點 | 做什麼 |
|---|---|
| `GET /odata-ds/$metadata` | CSDL schema — 每個資料集一張表，欄位型別齊全，iPaaS connector 靠這個自動產生欄位對應 |
| `GET /odata-ds/{dataset}` | 讀資料列，支援 `$filter` / `$select` / `$orderby` / `$top` / `$count` |

```bash
# 讀出「台灣行政區劃」北部的鄉鎮，只取名稱欄
curl -u "$USER:$PASS" \
  "https://<admin-svc>/odata-ds/tw-districts?\$filter=region eq '北部'&\$select=name"
```

## 典型用法

- **採購系統推供應商主檔** → 表單下拉自動更新，flowcook 端零維護
- **BI 拉參考資料**對齊報表維度

Power Automate 逐步接法（建 connection → 選表 → 對應欄位）之後補充。
