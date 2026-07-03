# flowcook 導入/使用手冊（Enablement Docs）Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** 建一個 Astro + Starlight 的內部導入/使用手冊站（`bpm-docs/`），wiki 式導覽、可嵌 YouTube 解說影片、不對外索引，接進既有 Azure SWA 部署流程。

**Architecture:** 新 top-level 專案 `bpm-docs/`，與 `bpm-www/` 平行。Astro 5 + `@astrojs/starlight`；內容是 `src/content/docs/**` 的 Markdown/MDX，五個 sidebar 分區（開始 / 功能介紹 / 導入指南 / 使用教學 / API 串接）。品牌對齊 flowcook（slate/blue/amber + DM Sans + Noto Sans TC，自 host 字型）。`noindex` + `robots disallow`。部署接 `infra/azure` 既有 `deploy_swa` 流程。

**Tech Stack:** Astro 5、@astrojs/starlight、@fontsource（DM Sans / Noto Sans TC）、Azure Static Web Apps（Free tier）、swa CLI。

**驗收方式（非單元 TDD）：** 這是靜態內容站，沒有有意義的單元測試。每個 task 的 gate 是 `npm run build`（含 `astro check`）綠 + dev server 目視。這是刻意的取捨，符合 spec 的 Verification 一節。

**Spec:** `docs/superpowers/specs/2026-07-03-enablement-docs-design.md`

---

## File Structure

| 檔案 | 責任 |
|---|---|
| `bpm-docs/package.json` | 專案 + scripts + deps |
| `bpm-docs/astro.config.mjs` | Astro + Starlight 設定（title / sidebar / noindex head / customCss） |
| `bpm-docs/tsconfig.json` | Astro strict + `@/*` alias |
| `bpm-docs/src/content.config.ts` | Starlight docs collection（Astro 5 loader 形式） |
| `bpm-docs/src/styles/custom.css` | flowcook 品牌 token + 字型 import |
| `bpm-docs/src/components/YouTube.astro` | 可重用的 YouTube(-nocookie) 嵌入元件（16:9 RWD） |
| `bpm-docs/src/content/docs/index.mdx` | 首頁（splash） |
| `bpm-docs/src/content/docs/start/overview.md` | 「開始」分區範例頁 |
| `bpm-docs/src/content/docs/features/leave.mdx` | 「功能介紹」範例頁（含嵌影片） |
| `bpm-docs/src/content/docs/onboarding/playbook.md` | 「導入指南」範例頁 |
| `bpm-docs/src/content/docs/usage/roles.md` | 「使用教學」範例頁 |
| `bpm-docs/src/content/docs/api/odata.md` | 「API 串接」實質內容頁（OData） |
| `bpm-docs/public/robots.txt` | Disallow all |
| `bpm-docs/staticwebapp.config.json` | noindex header + navigationFallback |
| `infra/azure/00-config.sh` | 加 `DOCS_SWA` / `DOCS_DIR`（修改） |
| `infra/azure/01-provision.sh` | 建 docs SWA（修改） |
| `infra/azure/03-deploy.sh` | `deploy_swa` docs（修改） |

---

## Task 1: Scaffold `bpm-docs` Starlight 專案

**Files:**
- Create: `bpm-docs/package.json`
- Create: `bpm-docs/astro.config.mjs`
- Create: `bpm-docs/tsconfig.json`
- Create: `bpm-docs/src/content.config.ts`
- Create: `bpm-docs/.gitignore`

- [ ] **Step 1: 建 package.json**

`bpm-docs/package.json`:
```json
{
  "name": "bpm-docs",
  "type": "module",
  "version": "0.1.0",
  "description": "flowcook 內部導入/使用手冊 — Astro + Starlight. Internal enablement docs; noindex, not linked from the public site.",
  "scripts": {
    "dev": "astro dev",
    "start": "astro dev",
    "build": "astro check && astro build",
    "preview": "astro preview",
    "astro": "astro"
  },
  "dependencies": {
    "@astrojs/check": "^0.9.4",
    "@astrojs/starlight": "^0.34.0",
    "@fontsource-variable/noto-sans-tc": "^5.1.0",
    "@fontsource/dm-sans": "^5.1.0",
    "astro": "^5.6.0",
    "sharp": "^0.33.5",
    "typescript": "^5.7.0"
  }
}
```

- [ ] **Step 2: 建 astro.config.mjs**

`bpm-docs/astro.config.mjs`:
```js
import { defineConfig } from 'astro/config'
import starlight from '@astrojs/starlight'

// site 只影響 canonical/sitemap；此站 noindex，故用 default hostname 佔位即可。
export default defineConfig({
  site: 'https://poc-flowcook-docs.azurestaticapps.net',
  server: { port: 4331, host: 'localhost' },
  integrations: [
    starlight({
      title: 'flowcook 導入 / 使用手冊',
      // 對搜尋引擎隱藏（unlisted 內部站）
      head: [
        { tag: 'meta', attrs: { name: 'robots', content: 'noindex, nofollow' } },
      ],
      customCss: ['./src/styles/custom.css'],
      pagination: true,
      sidebar: [
        { label: '開始', items: [{ label: 'flowcook 是什麼', slug: 'start/overview' }] },
        { label: '功能介紹', autogenerate: { directory: 'features' } },
        { label: '導入指南', autogenerate: { directory: 'onboarding' } },
        { label: '使用教學', autogenerate: { directory: 'usage' } },
        { label: 'API 串接', autogenerate: { directory: 'api' } },
      ],
    }),
  ],
})
```

- [ ] **Step 3: 建 tsconfig.json**

`bpm-docs/tsconfig.json`:
```json
{
  "extends": "astro/tsconfigs/strict",
  "include": [".astro/types.d.ts", "**/*"],
  "exclude": ["dist"],
  "compilerOptions": {
    "baseUrl": ".",
    "paths": { "@/*": ["src/*"] }
  }
}
```

- [ ] **Step 4: 建 content.config.ts（Astro 5 loader 形式）**

`bpm-docs/src/content.config.ts`:
```ts
import { defineCollection } from 'astro:content'
import { docsLoader } from '@astrojs/starlight/loaders'
import { docsSchema } from '@astrojs/starlight/schema'

export const collections = {
  docs: defineCollection({ loader: docsLoader(), schema: docsSchema() }),
}
```

- [ ] **Step 5: 建 .gitignore**

`bpm-docs/.gitignore`:
```
node_modules/
dist/
.astro/
.DS_Store
```

- [ ] **Step 6: 安裝 deps**

Run: `cd bpm-docs && npm install`
Expected: 安裝成功，產生 `package-lock.json`、`node_modules/`。無 peer-dep error（Starlight 0.34 對應 Astro 5）。

- [ ] **Step 7: 先放一頁讓 build 能過**

`bpm-docs/src/content/docs/start/overview.md`:
```md
---
title: flowcook 是什麼
description: 給導入員的高層總覽
---

flowcook 是一套流程（BPM）平台。這頁稍後補齊——先確保站台建得起來。
```

- [ ] **Step 8: 驗證 build 綠**

Run: `cd bpm-docs && npm run build`
Expected: `astro check` 0 errors、`astro build` 成功、`dist/` 產出 `start/overview/index.html`。

- [ ] **Step 9: Commit**

```bash
git add bpm-docs/package.json bpm-docs/package-lock.json bpm-docs/astro.config.mjs bpm-docs/tsconfig.json bpm-docs/src/content.config.ts bpm-docs/.gitignore bpm-docs/src/content/docs/start/overview.md
git commit -m "feat(docs): scaffold bpm-docs Starlight enablement site"
```

---

## Task 2: flowcook 品牌對齊（配色 + 字型）

**Files:**
- Create: `bpm-docs/src/styles/custom.css`

- [ ] **Step 1: 建 custom.css**

`bpm-docs/src/styles/custom.css`（自 host 字型，CSP-safe；配色對齊 flowcook slate/blue/amber）:
```css
/* 自 host 字型 —— 不打外部 CDN */
@import '@fontsource/dm-sans/400.css';
@import '@fontsource/dm-sans/500.css';
@import '@fontsource/dm-sans/600.css';
@import '@fontsource/dm-sans/700.css';
@import '@fontsource-variable/noto-sans-tc';

:root {
  --sl-font: 'DM Sans', 'Noto Sans TC Variable', 'Noto Sans TC', system-ui, sans-serif;

  /* accent = flowcook blue */
  --sl-color-accent-low: #172554;
  --sl-color-accent: #2563eb;
  --sl-color-accent-high: #bfdbfe;

  /* 中性色偏 slate */
  --sl-color-gray-1: #e2e8f0;
  --sl-color-gray-2: #cbd5e1;
  --sl-color-gray-3: #94a3b8;
  --sl-color-gray-4: #475569;
  --sl-color-gray-5: #334155;
  --sl-color-gray-6: #1e293b;
  --sl-color-black: #0f172a;
}

/* 淺色模式 accent 微調 */
:root[data-theme='light'] {
  --sl-color-accent-low: #dbeafe;
  --sl-color-accent: #2563eb;
  --sl-color-accent-high: #1e3a8a;
}

/* amber 作為 tip/highlight 強調（Starlight aside） */
.starlight-aside--tip {
  --sl-color-asides-text-accent: #b45309;
  --sl-color-asides-border: #f59e0b;
}
```

- [ ] **Step 2: 驗證 build 綠 + 字型載入**

Run: `cd bpm-docs && npm run build`
Expected: build 成功；`dist/_astro/` 內含 dm-sans / noto-sans-tc 字型檔（`.woff2`）。

- [ ] **Step 3: 目視確認品牌**

Run: `cd bpm-docs && npm run dev`（起在 http://localhost:4331）
用 chrome-devtools 開 `http://localhost:4331/start/overview/`，截圖。
Expected: 字型是 DM Sans / Noto Sans TC，accent 是 flowcook 藍，非 Starlight 預設紫。

- [ ] **Step 4: Commit**

```bash
git add bpm-docs/src/styles/custom.css
git commit -m "feat(docs): align Starlight theme to flowcook brand (slate/blue/amber + DM Sans + Noto Sans TC)"
```

---

## Task 3: YouTube 嵌入元件

**Files:**
- Create: `bpm-docs/src/components/YouTube.astro`

- [ ] **Step 1: 建 YouTube.astro（youtube-nocookie + 16:9 RWD + lazy）**

`bpm-docs/src/components/YouTube.astro`:
```astro
---
interface Props {
  /** YouTube video id，例如 dQw4w9WgXcQ */
  id: string
  /** 無障礙標題 */
  title?: string
}
const { id, title = '解說影片' } = Astro.props
---
<div class="yt-embed">
  <iframe
    src={`https://www.youtube-nocookie.com/embed/${id}`}
    title={title}
    loading="lazy"
    referrerpolicy="strict-origin-when-cross-origin"
    allow="accelerometer; autoplay; clipboard-write; encrypted-media; gyroscope; picture-in-picture; web-share"
    allowfullscreen
  ></iframe>
</div>
<style>
  .yt-embed { position: relative; aspect-ratio: 16 / 9; margin: 1.5rem 0; }
  .yt-embed iframe {
    position: absolute; inset: 0; width: 100%; height: 100%;
    border: 0; border-radius: 0.5rem;
  }
</style>
```

- [ ] **Step 2: 驗證 build 綠（元件被下一 task 使用，先確認能編譯）**

Run: `cd bpm-docs && npm run build`
Expected: 成功（元件未被引用不影響 build）。

- [ ] **Step 3: Commit**

```bash
git add bpm-docs/src/components/YouTube.astro
git commit -m "feat(docs): reusable YouTube-nocookie embed component"
```

---

## Task 4: 五分區內容骨架 + 範例頁

**Files:**
- Create: `bpm-docs/src/content/docs/index.mdx`
- Create: `bpm-docs/src/content/docs/features/leave.mdx`
- Create: `bpm-docs/src/content/docs/onboarding/playbook.md`
- Create: `bpm-docs/src/content/docs/usage/roles.md`
- Create: `bpm-docs/src/content/docs/api/odata.md`
- (Task 1 已建 `start/overview.md`)

- [ ] **Step 1: 首頁 splash**

`bpm-docs/src/content/docs/index.mdx`:
```mdx
---
title: flowcook 導入 / 使用手冊
description: 內部導入與使用指引。導入中的客戶亦可參閱。
template: splash
hero:
  tagline: 怎麼帶客戶導入、怎麼用 flowcook。內部優先，客戶想學也能看。
  actions:
    - text: 從「開始」讀起
      link: /start/overview/
      icon: right-arrow
      variant: primary
    - text: API 串接
      link: /api/odata/
      icon: external
---

import { CardGrid, Card } from '@astrojs/starlight/components'

<CardGrid>
  <Card title="功能介紹" icon="open-book">每個功能一頁，附解說影片。</Card>
  <Card title="導入指南" icon="rocket">AI Kitchen → sandbox 驗收 → 上線 checklist。</Card>
  <Card title="使用教學" icon="pencil">各角色怎麼操作。</Card>
  <Card title="API 串接" icon="setting">OData 組織資料 / 自訂資料集整合。</Card>
</CardGrid>
```

- [ ] **Step 2: 功能介紹範例頁（含嵌影片）**

`bpm-docs/src/content/docs/features/leave.mdx`（相對路徑到元件：`features` → `docs` → `content` → `src`，共三層）:
```mdx
---
title: 請假流程（LEAVE）
description: 請假申請、主管簽核、HR 歸檔的完整流程。
---

import YouTube from '../../../components/YouTube.astro'

請假流程涵蓋員工申請、主管核准、（長假）VP 加簽、HR 歸檔。

<YouTube id="REPLACE_WITH_VIDEO_ID" title="請假流程解說" />

## 流程重點

- 一般假：員工送出 → 主管核准 → HR 歸檔 → 完成
- 長假（≥7 天）：主管核准後多一關 VP 加簽
- 病假：附證明欄位變必填

:::tip[影片先佔位]
`REPLACE_WITH_VIDEO_ID` 換成實際 YouTube unlisted 影片 id 即可。
:::
```

- [ ] **Step 3: 導入指南範例頁**

`bpm-docs/src/content/docs/onboarding/playbook.md`:
```md
---
title: 導入 Playbook
description: 怎麼帶一個新客戶從零到上線。
---

## 標準導入四步

1. **AI Kitchen**：跟客戶對談，即時生成問卷，產出 spec bundle。
2. **Sandbox 驗收**：mail capture / webhook redirect / persona 切換 / 時間快轉 / 狀態重置，一個驗收員跑完整 UAT。
3. **上線 checklist**：組織資料匯入（OData）、角色對應、通知設定。
4. **交付**：客戶可自 host 或我們代管。

（各步驟細節待補。）
```

- [ ] **Step 4: 使用教學範例頁**

`bpm-docs/src/content/docs/usage/roles.md`:
```md
---
title: 各角色怎麼用
description: 員工 / 主管 / 財務 / HR / 管理員的操作入口。
---

| 角色 | 主要操作 |
|---|---|
| 員工 | 送出申請、查自己的案件 |
| 主管 | 收件匣簽核、委任代理人 |
| 財務 / HR | 該關卡簽核 / 歸檔 |
| 管理員 | User & Role、AI Kitchen、Sandbox、Reset |

（逐項截圖與步驟待補。）
```

- [ ] **Step 5: API 串接實質頁（OData，內容我們已知）**

`bpm-docs/src/content/docs/api/odata.md`:
```md
---
title: OData 整合
description: 用 OData 把組織資料與自訂資料集接進 flowcook。
---

flowcook 透過 OData 提供組織資料的整合介面，客戶端 iPaaS（Power Automate / Azure Data Factory 等）可推入或讀取。

## 認證

OData 端點用 **Basic auth**（整合專用帳號），與一般使用者 JWT 分離。

```bash
curl -u "$USER:$PASS" https://<admin-svc>/odata/Users
```

## 組織資料（CRUD）

- `GET/POST/PATCH/DELETE /odata/Users`（可 `?upsert=true` 以 email 冪等）
- `/odata/Departments`、`/odata/Roles`（`?upsert=true` 以 code）、`/odata/Memberships`
- 設定密碼：bound action `SetPassword`

## 批次

- `POST /odata/$batch`：一次 request 推多筆。逐筆各自成敗（非交易性）。

## 自訂資料集（動態表）

- `GET /odata-ds/$metadata`：CSDL，每個資料集一張表
- `GET /odata-ds/{dataset}`：資料列，支援 `$filter` / `$select` / `$orderby` / `$top` / `$count`

（Power Automate 逐步接法待補。）
```

- [ ] **Step 6: 驗證 build 綠（含所有頁 + 元件引用）**

Run: `cd bpm-docs && npm run build`
Expected: `astro check` 0 errors、build 成功、`dist/` 含 `features/leave/index.html` 等所有頁。MDX 對元件的相對 import 解析成功。

- [ ] **Step 7: 目視確認 sidebar 五分區 + 影片框**

Run: `cd bpm-docs && npm run dev`
chrome-devtools 開 `http://localhost:4331/`，確認左側五分區；開 `http://localhost:4331/features/leave/`，確認 16:9 影片框渲染（佔位 id 會顯示 YouTube 錯誤畫面，正常）。截圖。

- [ ] **Step 8: Commit**

```bash
git add bpm-docs/src/content/docs
git commit -m "feat(docs): five-section content skeleton + exemplar pages (incl OData API page + embedded video)"
```

---

## Task 5: noindex + robots + SWA 設定

**Files:**
- Create: `bpm-docs/public/robots.txt`
- Create: `bpm-docs/staticwebapp.config.json`

- [ ] **Step 1: robots.txt（全站禁爬）**

`bpm-docs/public/robots.txt`:
```
User-agent: *
Disallow: /
```

- [ ] **Step 2: staticwebapp.config.json（noindex header + fallback）**

`bpm-docs/staticwebapp.config.json`:
```json
{
  "navigationFallback": {
    "rewrite": "/404.html",
    "exclude": ["/_astro/*", "/*.{css,js,woff,woff2,png,jpg,svg,ico,xml,txt}"]
  },
  "globalHeaders": {
    "X-Robots-Tag": "noindex, nofollow"
  },
  "responseOverrides": {
    "404": { "rewrite": "/404.html", "statusCode": 404 }
  }
}
```

- [ ] **Step 3: 確認 Astro 有產出 404.html**

Run: `cd bpm-docs && npm run build && ls dist/404.html`
Expected: `dist/404.html` 存在（Starlight 內建 404 頁）。若不存在，建 `bpm-docs/src/content/docs/404.md` 帶 `title: 找不到頁面`。

- [ ] **Step 4: 確認 robots + noindex meta 進了 dist**

Run: `cd bpm-docs && grep -r "noindex" dist/start/overview/index.html && cat dist/robots.txt`
Expected: HTML head 有 `<meta name="robots" content="noindex, nofollow">`；`robots.txt` 為 Disallow all。

- [ ] **Step 5: Commit**

```bash
git add bpm-docs/public/robots.txt bpm-docs/staticwebapp.config.json
git commit -m "feat(docs): noindex (meta + robots + X-Robots-Tag) and SWA navigation fallback"
```

---

## Task 6: 接進 infra/azure 部署流程

**Files:**
- Modify: `infra/azure/00-config.sh`
- Modify: `infra/azure/01-provision.sh`
- Modify: `infra/azure/03-deploy.sh`

- [ ] **Step 1: 00-config.sh 加 SWA 名稱**

在 `WWW_SWA="${WWW_SWA:-${ENV_PREFIX}-flowcook-www}"` 那行後面加：
```bash
DOCS_SWA="${DOCS_SWA:-${ENV_PREFIX}-flowcook-docs}"
```

- [ ] **Step 2: 00-config.sh 加 repo 路徑**

在 `WWW_DIR="$REPO_ROOT/bpm-www"` 那行後面加：
```bash
DOCS_DIR="$REPO_ROOT/bpm-docs"
```

- [ ] **Step 3: 01-provision.sh 建 docs SWA**

找到建 `WWW_SWA` 的那行（形如 `az staticwebapp create -n "$WWW_SWA" -g "$RG" -l "$SWA_LOCATION" -o none`，或透過 idempotent helper 包起來的呼叫）。緊接其後，用**同樣的形式**加一行建 `DOCS_SWA`。若該檔用 helper（例如 `ensure_swa "$WWW_SWA"`），就照 helper 形式加 `ensure_swa "$DOCS_SWA"`；若是直接 `az staticwebapp create`，就複製那行把 `$WWW_SWA` 換成 `$DOCS_SWA`。

- [ ] **Step 4: 03-deploy.sh 部署 docs**

找到部署 www 的那行（形如 `deploy_swa "$WWW_SWA" "$WWW_DIR" "dist"`）。緊接其後加：
```bash
deploy_swa "$DOCS_SWA" "$DOCS_DIR" "dist"
```
（docs 站不需要任何 `VITE_*` build 環境變數——它沒有後端呼叫。）

- [ ] **Step 5: 語法檢查三個 script**

Run: `bash -n infra/azure/00-config.sh && bash -n infra/azure/01-provision.sh && bash -n infra/azure/03-deploy.sh && echo OK`
Expected: `OK`（無語法錯）。

- [ ] **Step 6: Commit**

```bash
git add infra/azure/00-config.sh infra/azure/01-provision.sh infra/azure/03-deploy.sh
git commit -m "feat(infra): wire bpm-docs into Azure SWA provision + deploy (poc-flowcook-docs)"
```

> **Azure 實際 provision/deploy 由 Jason 觸發**（Azure 操作向來 Jason 手動、stack 目前停機）。本 task 只把 script 接好並過語法檢查；跑 `01-provision.sh` / `03-deploy.sh` 不在此 plan 的執行範圍。

---

## Task 7: 收尾驗證 + README

**Files:**
- Create: `bpm-docs/README.md`

- [ ] **Step 1: 建 README**

`bpm-docs/README.md`:
```md
# bpm-docs — flowcook 導入/使用手冊

內部 enablement docs（Astro + Starlight）。**不對外索引、不從公開官網連。** 導入中客戶可拿連結參閱。

## 跑

    npm install
    npm run dev      # http://localhost:4331
    npm run build    # astro check + build → dist/

## 內容

`src/content/docs/**` 的 Markdown/MDX，五分區：開始 / 功能介紹 / 導入指南 / 使用教學 / API 串接。
嵌影片：`import YouTube from '@/components/YouTube.astro'` 或相對路徑，`<YouTube id="..." />`（YouTube unlisted）。

## 部署

Azure SWA `poc-flowcook-docs`（Free tier）。接在 `infra/azure` 的 `03-deploy.sh`。noindex + robots disallow。
```

- [ ] **Step 2: 全站最終 build**

Run: `cd bpm-docs && npm run build`
Expected: 綠。

- [ ] **Step 3: Chrome 走查五分區**

Run: `cd bpm-docs && npm run dev`
chrome-devtools 依序開 `/`、`/start/overview/`、`/features/leave/`、`/onboarding/playbook/`、`/usage/roles/`、`/api/odata/`，各截圖。確認：品牌配色/字型、sidebar 五分區、搜尋框在、影片框在、深淺色切換正常。

- [ ] **Step 4: Commit**

```bash
git add bpm-docs/README.md
git commit -m "docs(bpm-docs): add README (run/build/deploy)"
```

---

## Self-Review Notes

- **Spec coverage：** 五分區（Task 1/4）、YouTube 嵌入（Task 3/4）、品牌對齊（Task 2）、noindex+robots（Task 5）、SWA 部署接線（Task 6）、驗收目視（Task 2/4/7）——皆對應 spec。內部架構文件刻意不放此站（spec Out of Scope），plan 亦未含。
- **YAGNI：** 無 i18n / CMS / 留言 / 真存取控制 / 公開行銷 tour。Basic auth gating 留待日後。
- **命名一致：** `DOCS_SWA` / `DOCS_DIR` / `bpm-docs` / `poc-flowcook-docs` 全 plan 一致；元件名 `YouTube.astro`、prop `id`/`title` 一致。
- **相對路徑：** `features/leave.mdx` → 元件為 `../../../components/YouTube.astro`（三層），已在 Task 4 Step 2 標明。
- **版本：** Starlight `^0.34` 對應 Astro `^5`；若安裝時 peer-dep 有出入，以 `npm install @astrojs/starlight astro` 取當時相容版並更新 package.json。
