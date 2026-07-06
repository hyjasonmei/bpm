---
title: 上線 checklist
description: 切正式前的最後一哩 — 逐項打勾。
sidebar:
  order: 6
---

## 資料

- [ ] 真實組織資料匯入完成（[組織資料匯入](/onboarding/org-import/)）
- [ ] 角色對應表全數對完，[Doctor](/backend/doctor/) 組織健檢**零紅字**
- [ ] 資料集（[Datasets](/backend/datasets/)）內容是正式資料，不是測試值

## 流程

- [ ] 所有要上線的流程 state = **Published**，launcher 分組排序設好（[Site Setting → Flow Groups](/backend/site-setting/)）
- [ ] 每隻流程 UAT 簽字完成（[Sandbox UAT](/onboarding/sandbox-uat/)）

## 環境

- [ ] **全域 sandbox 關閉**、逐流程攔信全關——通知要真的寄了
- [ ] Sandbox 測試資料已**全部清空**
- [ ] 白牌品牌設定完成（系統名稱 / Logo / Favicon）
- [ ] email 寄送網域驗證完成（SPF/DKIM）
- [ ] dev 登入已關（`BPM_AUTH_MODE=prod`），persona 切換不出現

## 人

- [ ] 管理員已拿到後台帳號，完成 User & Role 與 Doctor 的上手
- [ ] 員工端公告與登入方式發出
- [ ] 上線第一週的支援窗口與回報管道講好
