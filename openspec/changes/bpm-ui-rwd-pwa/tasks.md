## 1. Phase 1 — App shell 與導航(responsive-shell)

- [x] 1.1 AppLayout:`<md` 收合主導航為 hamburger 選單(Radix),右側保留 Notifications + AccountMenu;選單項點擊高度 ≥44px
- [x] 1.2 AppLayout:main 容器與 header 在窄螢幕的 padding/間距調整,確認 360–767px 無頁面級橫向捲動(Attendance 標題列加 flex-wrap 修掉 405px 溢出;CaseToolbar overlay 手機隱藏)
- [x] 1.3 以 chrome-devtools 390×844 截圖驗證:登入頁 + Home + Create + Search + Attendance 的 shell 呈現與 hamburger 動線(全頁 scrollWidth=390,選單項 44px)

## 2. Phase 1 — Home 收件匣(responsive-inbox)

- [x] 2.1 Home:`grid-cols-[1fr_320px]` → `grid-cols-1 md:grid-cols-[1fr_320px]`,窄螢幕收件匣在前、側欄在後
- [x] 2.2 建輕量卡片列表樣式(共用),Home 待處理/我的案件兩表在 `<md` 改卡片渲染,與 table 共用資料與導航 handler;卡片全欄位顯示(Type chip + Status 頂列、Title 主行、Case ID + 時間 meta 行),整卡可點
- [x] 2.3 行動驗證:390px 從 Home 卡片 → CaseDetail → 核准(confirm modal)→ 返回 Home 案件消失(bob 送 LEAVE 457e6cf5 → alice 核准 → pending 9→8)

## 3. Phase 1 — 表單動線(responsive-forms)

- [x] 3.1 form.tsx primitives(Field/Input/Select/Textarea):`w-full` + 行動 touch target ≥44px,桌面以 `md:` 還原既有尺寸
- [x] 3.2 FormShell:申請人摘要 `grid-cols-2 divide-x` → `grid-cols-1 md:grid-cols-2`
- [x] 3.3 ActionFooter:`<md` sticky bottom + 按鈕全寬排列,confirm modal 機制不動
- [x] 3.4 驗證 chef 產出零改動:`git diff --stat` 確認 `bpm-ui/src/features/` 無變更(僅先前 session 的未追蹤 ADDR/,非本次改動)
- [x] 3.5 行動驗證:390px 開 /apply/LEAVE 填單送出、開一筆 CaseDetail 簽核,全程無橫向溢出
- [x] 3.6 桌面回歸:1280px 截圖比對 LEAVE、PURCHASE_REQUEST、WFH 表單與改動前一致;tsc 過、smoke 36/38(2 fail 為本機 DB 種子漂移:jack 多 CEO role、published 13/15,與本次前端改動無關)

## 4. Phase 2 — 次要頁表格(responsive-tables)

- [x] 4.1 Search:結果表格 `<md` 改卡片(標題/流程/狀態/日期),搜尋條件區單欄堆疊(filters grid 的 col-span-3 改 md:,修掉單欄下撐爆 grid 的 bug)
- [x] 4.2 Attendance:補登記錄與每日彙總 `<md` 改卡片、今日 tiles 單欄堆疊;桌面表格不變
- [x] 4.3 CreateIndex(已有 grid-cols-2 md:grid-cols-3)與 AttendanceCorrectionReview(2 欄 read fields 手機可讀)掃過無需改
- [x] 4.4 行動驗證:390px 搜尋結果卡片點入案件 OK、Attendance 無溢出;卡片與表格共用同一 rows(pageRows/corrections/history)與 handler,僅 presentation 分支

## 5. Phase 3 — PWA shell(pwa-shell)

- [x] 5.1 從 favicon.svg 產 192/512 PNG icons(含 maskable)與 apple-touch-icon,放 public/(用 Chrome canvas 轉檔;maskable 白底 62% 安全區)
- [x] 5.2 導入 vite-plugin-pwa:`registerType: 'autoUpdate'`、generateSW、manifest(standalone、theme #1e293b / bg #f8fafc)、navigateFallback `/index.html`、API 不設 runtime cache(跨 origin 直通)、Google Fonts stale-while-revalidate、`devOptions.enabled: false`
- [x] 5.3 index.html 補 theme-color meta + apple-touch-icon link
- [x] 5.4 本機 build + preview(port 5175,CORS 白名單內)驗證:SW activated、manifest 200、斷網重開 shell 完整渲染且 API 顯示 Failed to fetch(證明 network-only)、深連結 /attendance reload 正常、cache storage 僅 precache + google-fonts;autoUpdate 的「新版部署生效」留待 6.2 雲端實測
- [x] 5.5 staticwebapp.config.json:exclude 清單補 `webmanifest` 副檔名(否則 manifest.webmanifest 會被 SWA rewrite 成 index.html);sw.js/workbox-*.js 走既有 `/*.js` exclude

## 6. 部署與驗收

- [x] 6.1 各 phase 獨立 commit 於 feat/rwd-pwa(bc07834 / 501498d / 1e93540),交 Jason 以 GitKraken push
- [ ] 6.2 部署(手動 build 帶 VITE_BPM_SVC_URL)後線上驗證:手機實機安裝 A2HS、核心簽核動線、版本更新生效(bump 一版確認 autoUpdate)
- [ ] 6.3 Jason 手機實測反饋 → 收件匣卡片欄位取捨與間距微調
