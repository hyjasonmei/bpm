---
title: 登入與帳號
description: 怎麼登入、忘記密碼、登出 — 員工的第一步。
sidebar:
  order: 1
---

前台登入頁輸入 **email + 密碼**。登入頁的系統名稱與 Logo 是白牌的——你們公司設定什麼就顯示什麼（[Site Setting → Branding](/backend/site-setting/)）。

## 常見問題

| 問題 | 處理 |
|---|---|
| 忘記密碼 | 登入頁下方有支援信箱連結，聯絡管理員重設（管理員可用 [SetPassword](/api/org-crud/) 重設） |
| 帳號哪來的 | 管理員在 [User & Role](/backend/user-role/) 建立，或由 HR 系統[自動同步](/api/org-crud/) |
| Microsoft 帳號登入 | 按鈕已在登入頁，**即將推出**（Entra ID / AD 整合） |
| 登出 | 右上角頭像選單 → Logout |

:::note[demo 環境]
demo 站帳密：`bob@acme.example` / `flowcook2026`（登入頁已預填）。人物清單見[預設角色介紹](/cases-org/default-roles/)。
:::
