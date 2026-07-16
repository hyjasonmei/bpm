## Why

bpm-ui 目前是純桌面版:app shell 無行動版導航、Home 儀表板寫死兩欄 grid、四處寬表格只能橫向捲動,手機上簽核(產品最高頻的行動情境)體驗不可用。同時站台不可安裝、無靜態快取,行動裝置每次都是冷載入。客戶 demo 反饋與實際使用都指向「手機上要能收單、填單、簽核」,現在是補上 RWD 與 PWA shell 的時機。

## What Changes

- **RWD 核心動線**(Phase 1):AppLayout 導航加行動版選單(hamburger)、Home 儀表板兩欄改響應式單欄、Home 收件匣表格在窄螢幕改卡片式、FormShell 申請人摘要區改響應式;共用 form primitives(Field/Input/Select/Textarea/ActionFooter/SectionCard)確保 touch-friendly,20 支流程表單一次受惠。
- **RWD 次要頁**(Phase 2):Search 結果表格、Attendance 兩個表格在窄螢幕改卡片式或精簡欄位;CreateIndex 與其餘頁面補齊斷點。
- **PWA shell**(Phase 3):導入 vite-plugin-pwa — web manifest、app icons、theme-color/apple-touch-icon meta、service worker precache 靜態資源。**明確排除離線簽核/離線資料同步**;API 請求一律 network-only,不快取。
- 不動 bpm-admin-ui(桌面工具,不在本次範圍)。
- 不動 BPMN viewer 的 reflow(modal 內維持 pan/zoom 即可)。

## Capabilities

### New Capabilities
- `responsive-shell`: app shell 響應式 — 頂部導航在窄螢幕收合為行動版選單,main 容器與各頁在行動裝置單欄呈現,無橫向溢出。
- `responsive-inbox`: Home 儀表板/收件匣響應式 — 待簽核與我的案件在窄螢幕以卡片式呈現,可完成「看單 → 進 CaseDetail → 簽核」全動線。
- `responsive-forms`: 表單動線響應式 — FormShell 與共用 form primitives 在行動裝置單欄、touch-friendly,涵蓋 20 支 feature 表單的填寫與 CaseDetail 簽核操作。
- `responsive-tables`: 次要頁表格響應式 — Search 與 Attendance 的寬表格在窄螢幕改為卡片式或精簡欄位,不依賴橫向捲動完成主要操作。
- `pwa-shell`: 可安裝 PWA — web manifest、icons、service worker 靜態資源 precache 與更新策略;API 與 auth 流量不經快取。

### Modified Capabilities
(無 — openspec/specs/ 目前為空,無既有 spec 需要修改)

## Impact

- **bpm-ui only**;不動 bpm-svc、bpm-admin-*。
- 主要改動檔案:`src/components/AppLayout.tsx`、`src/screens/Home.tsx`、`src/screens/forms/FormShell.tsx`、`src/components/ui/form.tsx`、`src/components/ui/action-footer/ActionFooter.tsx`、`src/screens/Search.tsx`、`src/screens/Attendance.tsx`、`index.html`、`vite.config.ts`、`public/`(icons、manifest)。
- 新依賴:`vite-plugin-pwa`(dev dependency)。
- **chef 相容性約束**:`features/<CODE>/V<N>/` 內的 chef 產出檔案不得手改;RWD 必須透過共用 primitives 與 FormShell 達成。既有 chef 產出的 `md:grid-cols-4` 讀取面板維持不動。
- 部署:Azure SWA 的 `staticwebapp.config.json` navigationFallback 需與 service worker precache 對齊;手動 build 仍需帶 `VITE_BPM_SVC_URL`。
- 風險:service worker 快取到舊 bundle 導致更新不即時 — 以 autoUpdate 註冊策略處理。
