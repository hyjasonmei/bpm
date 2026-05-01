專案背景
Jason 正在開發一個商業 BPM 平台，兩人副業團隊（Jason 負責開發，夥伴負責業務導入）。夥伴從前公司帶回十幾個真實流程圖，涵蓋請假、採購、差旅、發公告等，作為 MVP 的需求基礎。

技術棧

前端：React 18 SPA + Tailwind + shadcn

後端：C# .NET Core Clean Architecture
EF Core
POC用SQL LITE即可 
專案分層
API - controllers
Application - Business Logic (Services, Handlers, Helpers)
Persistence - EF Core

流程引擎：自建 C# Workflow Engine

產品定義
目標客群： 中小企業（50-300人）為起點，之後再攻大企業，用成功案例敲門
MVP 功能範圍：

根據PDF生成POC，其中夥伴非常喜歡 "TREND BPM"的設計風格
只要頁面圖案有TREND BPM都可當作參考作為我們的網頁風格

最終目標: 賣出POC給潛在客戶並可跟微軟生態 (Entra ID, AD) 整合

現在進度: 尚未開始

當前目標: 建立一請假流程，都用c#寫，但使用者可透過bpmn圖形化檢視流程進度