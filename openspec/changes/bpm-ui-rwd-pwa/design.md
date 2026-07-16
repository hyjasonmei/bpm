## Context

bpm-ui 為 Vite + React 18 + Tailwind CSS v4(CSS-native config,無 tailwind.config)+ shadcn 式 utility stack。現況:

- viewport meta 已在 `index.html`;layout 用 flex + `max-w-screen-*`,非死 px,行動裝置上「可捲不可用」。
- 全 codebase 僅 66 處響應式斷點,絕大多數是 chef 產出的 `md:grid-cols-4` 讀取面板;app shell(`AppLayout.tsx`)零斷點、無行動選單。
- 20 支 feature 表單(15 codes)全部經 `FormShell` + 共用 primitives(`components/ui/form.tsx` 的 Field/Input/Select/Textarea、SectionCard、ActionFooter、ConfirmDialog、FilePicker);表單 body 已是單欄直排,無 grid-cols。
- 寬表格四處:`Search.tsx`(6 欄)、`Attendance.tsx`(6+5 欄)、`Home.tsx` 收件匣兩表(位於寫死的 `grid-cols-[1fr_320px]` 內)。
- PWA 零基礎:無 manifest、無 SW、無 icons(public/ 僅 favicon.svg、icons.svg、staticwebapp.config.json)。
- Auth 為 localStorage JWT(`bpm_jwt`),`apiFetch` 注入 Bearer,401 清 token 發 window event;無 cookie/redirect flow。
- 部署於 Azure SWA,SPA fallback 靠 `staticwebapp.config.json` navigationFallback;手動 build 需帶 `VITE_BPM_SVC_URL`。

約束:chef 產出檔(`features/<CODE>/V<N>/`)不得手改 — RWD 效果必須從共用 primitives 與 FormShell 傳導。

## Goals / Non-Goals

**Goals:**
- 手機(≥360px 寬)可完成核心動線:登入 → Home 收件匣 → 開單/填單 → CaseDetail 簽核,無橫向溢出、touch target 足夠。
- 次要頁(Search、Attendance)在手機可完成主要操作。
- 站台可安裝(A2HS)、靜態資源 precache、更新自動生效。
- 改動對 20 支既有表單零逐支修改 — 全靠共用層。

**Non-Goals:**
- 離線簽核 / 離線資料同步 / background sync — 明確排除,API 一律 network-only。
- 推播通知(Phase 4 另案評估,需後端 push service)。
- bpm-admin-ui 的 RWD。
- BPMN viewer 的行動版 reflow(modal 內 pan/zoom 即可)。
- 平板專屬 layout(桌面斷點沿用即可)。

## Decisions

**D1. 斷點策略:mobile-first 補丁,以 `md`(768px)為主分界**
現有 code 是 desktop-first 寫法,全面重寫成 mobile-first 成本過高且會碰 chef 產出。做法:桌面樣式維持,對窄螢幕以「預設(mobile)先寫、`md:` 還原桌面」只改動刀的幾個檔。單一分界(md)而非多級,降低測試矩陣。
*替代方案:全面 mobile-first 重構 — 否決,碰面太廣、與 chef 產出衝突。*

**D2. 行動導航:同一 header 內收合為 hamburger + 下拉 sheet,不做 bottom tab bar**
`AppLayout.tsx` 的文字導航(Home/Create/Search/Attendance)在 `<md` 收進 hamburger 選單(Radix DropdownMenu 或簡單 disclosure,沿用既有 Radix 依賴);右側僅保留 Notifications + AccountMenu。
*替代方案:bottom tab bar — 更 app-like,但多一個常駐元件、與 ActionFooter(表單底部動作列)衝突,否決。*

**D3. 表格 → 卡片:以共用 `ResponsiveList`/卡片樣式處理,`md` 以上維持 table**
Search、Attendance、Home 收件匣的表格在 `<md` 改渲染卡片(每列一卡,主欄位為標題、次欄位為 meta 行)。實作為各 screen 內的條件渲染(table 隱藏、card list 顯示),共用一個輕量卡片樣式元件;不引入 tanstack table。
*替代方案:CSS-only(table 變 block)— 髒且 a11y 差;橫向捲動維持現狀 — 收件匣是核心動線,不可接受。*

**D4. Home layout:`grid-cols-[1fr_320px]` → `grid-cols-1 md:grid-cols-[1fr_320px]`**
320px 側欄在 `<md` 落到主欄下方。收件匣(核心)在上、側欄資訊在下。

**D5. 表單傳導層:只改 `form.tsx` primitives、`FormShell`、`ActionFooter`**
- Field/Input/Select/Textarea:確保 `w-full`、touch target ≥44px 高(調 padding,桌面觀感不變或微調)。
- FormShell 申請人摘要:`grid-cols-2 divide-x` → `grid-cols-1 md:grid-cols-2`,label/value 的 `grid-cols-[110px_1fr]` 維持(110px 標籤在手機可接受)。
- ActionFooter:在 `<md` sticky bottom、按鈕全寬排列,confirm modal 既有機制不動。
- chef 產出的 `md:grid-cols-4` 讀取面板本來就會在 `<md` 收成 2 欄,不需動。

**D6. PWA:`vite-plugin-pwa`,`registerType: 'autoUpdate'`,`generateSW` 模式**
- precache:build 產物(js/css/html/svg/fonts 自 host 部分);`navigateFallback: '/index.html'`,與 SWA config 的 fallback 語意一致。
- runtime caching:**不設 API route 的快取**。`VITE_BPM_SVC_URL` 為跨 origin,SW 預設不攔;明確在 workbox config 排除任何 API pattern,Google Fonts 用 stale-while-revalidate。
- `autoUpdate` 避免「舊 bundle 卡快取」— 新版部署後下次導航自動接管。不做「有新版」toast(過度設計)。
- manifest:`display: 'standalone'`、theme/背景色用既有 slate/blue token;icons 由 favicon.svg 產 192/512 PNG + maskable。
*替代方案:`injectManifest` 自寫 SW — 彈性高但維護成本高,本次無自訂 SW 邏輯需求,否決。*

**D7. 驗收工具:chrome-devtools MCP 以行動 viewport(390×844)截圖驗證,納入 tasks 的每階段驗收**
既有 bpm-smoke-test 流程照跑(桌面),行動驗收另以 emulate + screenshot 走核心動線。

## Risks / Trade-offs

- [SW 快取舊版導致改版不生效] → `autoUpdate` + build 產物 hash;部署後以 hard reload 驗證;緊急時 SWA 端可下 `Clear-Site-Data` 或 bump SW 版本。
- [改共用 primitives 影響 20 支表單桌面觀感] → 桌面樣式以 `md:` 還原為原值;改完跑 bpm-smoke-test + 抽 3 支代表表單(LEAVE、PURCHASE_REQUEST、WFH V6)桌面截圖比對。
- [卡片式與 table 雙渲染造成資料/操作不同步] → 卡片與 table 共用同一 row 資料與 handler,僅 presentation 分支。
- [iOS Safari PWA 限制(無 install prompt、快取上限)] → 接受;iOS 走「加入主畫面」,manifest + apple-touch-icon 使其正常 standalone。
- [SWA navigationFallback 與 SW navigateFallback 打架] → 兩者語意相同(fallback 到 index.html);SW 先攔、未命中時 SWA 兜底,部署後實測深連結。
- [dev 模式 SW 干擾本機開發] → `devOptions.enabled: false`(預設),SW 只在 build 產物生效。

## Migration Plan

1. Phase 1(responsive-shell / responsive-inbox / responsive-forms)→ 本機驗證 + 部署 → Jason 手機實測。
2. Phase 2(responsive-tables)→ 同上。
3. Phase 3(pwa-shell)最後上,避免 RWD 迭代期間 SW 快取干擾。
4. 回滾:各 phase 獨立 commit;PWA 出問題可單獨 revert vite-plugin-pwa 導入 commit(SW 需再部署一版 self-destroying 或靠 autoUpdate 拉回)。

## Open Questions

(皆已決議 — 2026-07-16 Jason 拍板)

- ~~icons 視覺~~ → 先用現有 favicon.svg 產生,品牌 icon 之後有再換。
- ~~收件匣卡片欄位取捨~~ → **全欄位顯示**:卡片版面為 Type chip + Status(頂列)、Title(主行)、Case ID + 時間(meta 行),不砍欄位;收件匣卡片整卡可點(取代 Open 按鈕),MyCases 卡片同樣含 Started + Last activity。Jason 手機實測後再微調排法。
