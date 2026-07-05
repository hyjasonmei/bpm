---
title: OData 認證
description: 整合帳號 Basic auth 與使用者 JWT 的分工。
sidebar:
  order: 2
---

flowcook 有兩套憑證，用途不同、不可混用：

| | **整合帳號（Basic auth）** | **使用者（JWT）** |
|---|---|---|
| 給誰用 | iPaaS / 排程 / 系統對接 | 人 — 前後台網頁登入 |
| 用在哪 | `/odata/*`、`/odata-ds/*` | 前台/後台的 app API |
| 生命週期 | 長期有效，專帳專用 | 登入簽發，短期有效 |

## Basic auth 用法

```bash
curl -u "$INTEGRATION_USER:$INTEGRATION_PASS" \
  "https://<admin-svc>/odata/Users"
```

## 實務建議

- 每個對接系統**各開一個整合帳號**（HR 同步一個、採購一個）——出事好追、好停用
- 憑證放客戶 iPaaS 的秘密管理（Key Vault / connection credential），不要寫死在 flow 定義裡
- 整合帳號的操作同樣進 [Audit](/backend/audit/)
