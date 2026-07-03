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

定稿於 2026-07-03（與 Jason 逐段確認，並對整站 nav 做過 audit）。七個第一階章節：

**1. 開始**
- 1.1 flowcook 是什麼
- 1.2 系統全貌（給導入員的高層架構，非原始碼細節）
- 1.3 名詞速查

**2. 前台功能介紹**（bpm-ui，客戶端）
- 2.1 首頁／儀表板
- 2.2 流程與表單（開新申請＋填寫送件）
- 2.3 收件匣與案件詳情
- 2.4 簽核（含並簽；以 CONTRACT_REVIEW／COMMITTEE_REVIEW 當範例）
- 2.5 委任代理人
- 2.6 通知
- 2.7 打卡簽到
- 2.8 搜尋案件

**3. 後台功能介紹**（bpm-admin-ui，管理端；對齊實際 8-page nav，Reports 除外）
- 3.1 AI Kitchen（Prep／Cook／Serve 三階）
- 3.2 User & Role
- 3.3 資料集 Datasets
- 3.4 Sandbox 驗收（功能面）
- 3.5 稽核 Audit
- 3.6 Doctor 系統診斷
- 3.7 Site Setting（品牌 Branding／部署 Deploy／功能表 Feature Tables／流程分組 Flow Groups／重置 Reset）

**4. 導入指南**
- 4.1 導入總覽
- 4.2 用 AI Kitchen 產流程
- 4.3 流程設計原則
- 4.4 Sandbox UAT（怎麼帶驗收——使用面）
- 4.5 組織資料匯入與角色對應
- 4.6 上線 checklist
- 4.7 交付：代管 vs 自 host

**5. 使用案例（組織）**
- 5.1 預設角色介紹
- 5.2 怎麼設定角色（部門／角色／成員／繼承）

**6. 使用案例（流程）** — 預設 11 個流程
- 6.1 差旅假勤：請假申請、差旅申請、差旅費用核銷、遠距工作(WFH)
- 6.2 採購與資產：採購申請、廠商採購請款、資產採購、資產處分、預支現金
- 6.3 人事異動：新進員工報到、員工離職

**7. API 串接**
- 7.1 整合總覽
- 7.2 OData 認證（Basic auth 整合帳號 vs 使用者 JWT）
- 7.3 組織資料 CRUD（Users/Departments/Roles/Memberships、upsert、SetPassword）
- 7.4 批次 `$batch`（冪等再推、非交易性）
- 7.5 自訂資料集動態表（`/odata-ds`、`$metadata`、`$filter` 等）

### 結構決策紀錄（brainstorming 2026-07-03）

- **功能 vs 案例分家**：§2/§3 是「功能怎麼運作」（能力面）；§5/§6 是「拿我們預設組織/流程當範例」（案例面）。因為客戶的組織與角色名稱不一定跟我們 demo 一樣，故案例章用**通用角色**（申請人／審核者／管理員）+ 明講「以 demo 示範，操作一致」。
- **前台/後台各自成第一階**（不合併在單一「功能介紹」下）。
- **AI Kitchen 內部走 Prep／Cook／Serve 三幕**，對齊官網既有隱喻（備料規格→烹調開發→上菜驗收部署）；Prep 底下的設計細項（流程/表單、簽核路由與並簽、通知）併進各 phase 頁，不再往下鑽。
- **Sandbox 出現兩次、角度不同**：§3.4 講「這功能是什麼」、§4.4 講「導入時怎麼帶驗收」。Sandbox 在真實 nav 是獨立頁（非 AI Kitchen 子項）。
- **Reports 移除**（model-A 遺留、待產品決策，先不寫進手冊）。
- **CONTRACT_REVIEW／COMMITTEE_REVIEW 不算客戶預設流程**，是並簽示範，放 §2.4 當範例，不列進 §6。
- **預設 11 流程** = 10 隻正規商務流程 + WFH（實際 live 有 13 隻，另 2 隻為上述 REVIEW 示範）。

## Video Hosting

- **YouTube unlisted** 起手：零成本、零基建、MDX 直接嵌 iframe。
- 提醒：unlisted **不是**真的存取控制（有連結就能看）。含敏感內容的影片再單獨升級到 Vimeo（限網域嵌入）或 Cloudflare Stream / Bunny（signed URL）。POC 先不處理。
- 自 host mp4 放 SWA **不採用**（無 adaptive streaming + egress 費）。

## Deploy

- 自己的 **Azure Static Web App**（Free tier，跟 bpm-ui / admin-ui / www 一樣）。
- 命名沿用慣例：`${ENV_PREFIX}-flowcook-docs`（POC → `poc-flowcook-docs`）。
- 接進 `infra/azure` 既有流程：`00-config.sh`（`DOCS_SWA` / `DOCS_DIR`）、`01-provision.sh`（建 SWA）、`03-deploy.sh`（`deploy_swa` build + 部署）。
- SWA **Free tier 不會被停機**（跟 www 一樣），所以手冊常在。
- **自訂網域 `guide.flowcook.ai`**（Jason 2026-07-03 決定）：GoDaddy 加 CNAME `guide` → docs SWA default hostname，`az staticwebapp hostname set` 綁定，Azure 自動簽 managed TLS。
- `staticwebapp.config.json`：`navigationFallback`（deep-link/reload 不 404）+ `X-Robots-Tag: noindex` header。
- **不從 flowcook.ai 官網連進去**——只有拿到 `guide.flowcook.ai` 網址的人（我們 + 導入客戶）會進來。
- ⚠️ 取捨：好記的子網域 = **知道網址就能開**（不像亂數 hostname 難猜）。但 `noindex` + robots disallow 讓它搜不到，內容又是非機密 enablement，故 POC 階段可接受。之後要真鎖，SWA 內建 auth 可加 Basic / 串 Entra（YAGNI，先不做）。
- 實際 Azure provision/deploy + DNS 由 Jason 觸發（Azure/DNS 操作向來 Jason 手動；stack 目前停機）。本專案只把 script 接好 + 本機 build 驗過。

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
