# flowcook 導入/使用手冊（Enablement Docs）— Design

**Date:** 2026-07-03
**Status:** Approved (Jason, Telegram 2026-07-03)

## Purpose

一個 wiki 式的 **內部導入/使用手冊**。主要讀者是**我們內部**（怎麼帶客戶導入、產品怎麼用），**導入中的客戶想學也能拿連結看**。

明確**不是**：
- 公開行銷站（那是 `bpm-www` / flowcook.ai 的工作）
- 機密內部原始碼架構文件（Clean Arch 分層、SharedIdentity、chef pipeline 那類**不放**此站；要寫留在 repo `docs/`）

「設計」一詞在本站指 **導入方法 + 使用指引**（怎麼設計/建模流程、表單、簽核路由），**不是**系統原始碼架構。

## Audience & Access

- 單一讀者群：「正在學怎麼跑 flowcook 的人」——內部優先、客戶可看。
- 因為全是 enablement 內容（非機密），**不需要**「可分享 vs 團隊限定」的硬牆。
- **不對外索引**：`noindex` + `robots disallow`，且**不從公開官網連進去**。網址私下丟給導入中客戶。
- POC 階段以「不公開連結」為存取控制即可。之後真要鎖，再加 Basic auth / Entra（YAGNI，先不做）。

## Tech

- **Astro + Starlight**（沿用 `bpm-www` 的 Astro 技術棧）。Starlight 是 Astro 官方 docs 框架，內建 sidebar 導覽、全文搜尋、MDX、上下頁、深淺色。
- 內容寫 **Markdown / MDX**，版控在 repo（新 top-level 專案 `bpm-docs/`，與 `bpm-www/` 平行）。
- 品牌對齊 flowcook：slate / blue / amber + DM Sans + Noto Sans TC（Starlight theming override）。

## Content Structure（sidebar 分區）

1. **開始** — flowcook 是什麼、給導入員的高層架構總覽（非原始碼細節）
2. **功能介紹** — 每個功能一頁，**嵌 YouTube 解說影片** + 文字說明
3. **導入指南** — 怎麼帶客戶跑 AI Kitchen → sandbox 驗收 → 上線 checklist
4. **使用教學** — 各角色操作（員工申請 / 主管簽核 / 委任 / User & Role / sandbox）
5. **API 串接** — OData（org data CRUD、`$batch`、upsert、自訂資料集 `/odata-ds`）、auth（Basic / JWT）、curl / Power Automate 範例

## Video Hosting

- **YouTube unlisted** 起手：零成本、零基建、MDX 直接嵌 iframe。
- 提醒：unlisted **不是**真的存取控制（有連結就能看）。含敏感內容的影片再單獨升級到 Vimeo（限網域嵌入）或 Cloudflare Stream / Bunny（signed URL）。POC 先不處理。
- 自 host mp4 放 SWA **不採用**（無 adaptive streaming + egress 費）。

## Deploy

- 自己的 **Azure Static Web App**（Free tier，跟 bpm-ui / admin-ui / www 一樣）。
- 命名沿用慣例：`${ENV_PREFIX}-flowcook-docs`（POC → `poc-flowcook-docs`）。
- 接進 `infra/azure` 既有流程：`00-config.sh`（`DOCS_SWA` / `DOCS_DIR`）、`01-provision.sh`（建 SWA）、`03-deploy.sh`（`deploy_swa` build + 部署）。
- SWA **Free tier 不會被停機**（跟 www 一樣），所以手冊常在。
- `staticwebapp.config.json`：`navigationFallback`（deep-link/reload 不 404）+ `X-Robots-Tag: noindex` header。
- **不掛自訂網域**、**不從 flowcook.ai 連**——用 Azure default hostname。
- 實際 Azure provision/deploy 由 Jason 觸發（Azure 操作向來 Jason 手動；stack 目前停機）。本專案只把 script 接好 + 本機 build 驗過。

## Out of Scope (YAGNI)

- 公開行銷 Product Tour（Jason 明確沒要）
- 多語 i18n
- CMS / 後台編輯（內容就是 repo 裡的 Markdown）
- 留言 / 使用者帳號 / 真存取控制（之後需要再加）
- 內部原始碼架構文件（留在 repo `docs/`，不放此站）

## Verification

靜態內容站，無單元 TDD。驗收 gate：
- `npm run build`（含 `astro check`）綠
- dev server 起得來、五個分區都渲染、sidebar 導覽正常
- 影片嵌入元件在頁面上正確顯示（Chrome 目視）
- `noindex` meta + `robots.txt` disallow 存在
- deep-link reload 不白頁（`navigationFallback`）
