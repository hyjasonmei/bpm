## Why

Demo / POC 簽客戶階段，主管 / 業務希望同一份系統能換不同客戶的牌子（白標）— 帶 ACME 看時左上是 ACME 的 logo，帶廉誠看時是廉誠的 logo。目前 logo 是寫死「BPM」紅底方塊 + "BPM System" 文字，要換得改 code 重 deploy，沒法臨場切。

這個 change 讓 **管理員透過 admin UI 上傳 logo + 改顏色 / 文字**，前端兩個 UI（員工 + admin）同步反映。零 code 改動就能換 brand，業務 demo 前 5 分鐘自己改完即可。

非目標：

- 不做完整 theme 系統（accent color / dark mode / typography）— 只動 logo 區塊
- 不做 favicon 動態切換（瀏覽器 tab icon）
- 不做 email template 套用 logo（屬 `add-notification-engine` 範圍）
- 不做 PDF report 套用 logo（屬 `add-pdf-export` 範圍）
- 不做 multi-tenant 各自 brand（POC single-tenant，整個 instance 共用同一份）
- 不做歷史版本（換了就換了，不保留 brand 變更歷史）
- 不做使用者個人化 brand（永遠是 tenant 層）

## What Changes

### Tenant 層 branding 設定

`TenantSettings` 既有實體擴增：

| 欄位 | 型別 | 預設 | 說明 |
|---|---|---|---|
| `BrandLogoBase64` | text, nullable | null | 上傳的 logo 圖片（base64-encoded） |
| `BrandLogoMimeType` | string(50), nullable | null | `image/png` / `image/svg+xml` / `image/jpeg` / `image/webp` |
| `BrandSystemName` | string(50), nullable | null | logo 旁邊那行文字（"ACME 流程管理"）|

Logo 渲染優先序：
1. 有 `BrandLogoBase64` → 顯示上傳圖
2. 否則 → 顯示前端 bundle 內的 **預設 logo asset**（`/assets/bpm-default-logo.svg`，永遠不空白，看起來像產品就有的樣子）

**2026-05-10 簡化決定**：原規劃的 8-色 swatch + `BrandLogoColor` + `BrandLogoText` 全砍。客戶就兩種：上傳自己的 logo，或顯示我們乾淨的預設 logo。色彩控制 / 文字 fallback 都不再暴露給客戶。

### Backend API

- `GET /api/branding` — 公開（不需 auth），兩個 UI 在 boot 時呼叫
  - 回 `{ logoDataUrl?: string, systemName?: string }`
  - logoDataUrl 為 `data:<mime>;base64,<data>` 完整字串（前端直接套 `<img src>`）
  - 兩欄都 null = 前端 fallback 走 bundle 預設 asset
- `PUT /api/branding` — `[Authorize(Roles="admin")]`
  - body: `{ logoBase64?, logoMimeType?, systemName?, removeLogo? }`
  - `removeLogo: true` 清除 BrandLogoBase64 + MimeType（前端回到預設 asset）
  - 寫入 + audit toggle action（同 sandbox 模式：哪個 admin 在何時改了 brand）

### 上傳大小限制

- 圖片 base64 最大 200 KB（解碼後 ~150 KB），超過拒絕 413
- MIME 白名單：`image/png` / `image/svg+xml` / `image/jpeg` / `image/webp`
- 服務端驗證 magic bytes 防止 MIME 偽造（PNG 開頭 `89 50 4E 47`、JPEG `FF D8 FF`、SVG `<?xml` 或 `<svg`、WebP `RIFF...WEBP`）

### Admin UI — Site Settings 加 Branding 區塊

位置：`bpm-admin-ui/src/screens/SiteSettings.tsx`，原有 Sandbox 區塊**之後**新增 Branding 區塊。

Branding 區塊內容：

- **Logo 預覽**：左側顯示當前 logo（上傳圖 or bundle 預設 asset），即時反映 form 變更
- **Upload Logo**：file input + drop zone，accept image/*，上傳即顯示預覽
  - 顯示「Remove」按鈕清除上傳，回到 bundle 預設 asset
- **System Name**：input box（最多 50 字元），會出現在 logo 右邊
- **Reset to defaults**：清空 logo 上傳 + system name
- **Save** / **Discard**

UI 風格參考既有 `Sandbox Mode` 區塊。

### 兩個 UI 都吃 branding

- `bpm-ui/src/components/AppLayout.tsx` — header 左上的 BPM logo button：
  - 在 mount 時 fetch `/api/branding`
  - 渲染 `<img>` 或彩色方塊根據結果
  - System Name 用 branding 值
- `bpm-admin-ui/src/components/AdminLayout.tsx` — 同樣處理（注意 admin UI 的視覺底色不同，logo 顯示要保留可讀性）

### Caching

前端載入 branding 後 cache 在 sessionStorage，避免每頁 reload 都打 /api/branding。Admin 改 brand 儲存後，UI 主動刷新 sessionStorage cache（以及 broadcast `bpm:branding-changed` 事件讓其他開著的分頁也更新）。

## Impact

- Affected specs: NEW `bpm-tenant-branding`
- Affected code:
  - `bpm-svc/src/Domain/Entities/Sandbox/TenantSettings.cs` — 5 個新欄位
  - `bpm-svc/src/Persistence/Configurations/Sandbox/TenantSettingsConfiguration.cs` — 字段長度限制
  - 新 Migration `AddBranding`（schema migration of TenantSettings）
  - `bpm-svc/src/Application/Branding/IBrandingService.cs` (NEW)
  - `bpm-svc/src/Persistence/Branding/BrandingService.cs` (NEW)
  - `bpm-svc/src/Api/Branding/BrandingController.cs` (NEW)
  - `bpm-admin-ui/src/screens/SiteSettings.tsx` — 加 Branding 區塊
  - `bpm-admin-ui/src/lib/api/branding.ts` (NEW)
  - `bpm-admin-ui/src/types/branding.ts` (NEW)
  - `bpm-admin-ui/src/components/AdminLayout.tsx` — logo 區塊讀 branding
  - `bpm-ui/src/lib/api/branding.ts` (NEW)
  - `bpm-ui/src/types/branding.ts` (NEW)
  - `bpm-ui/src/components/AppLayout.tsx` — logo 區塊讀 branding

### Backwards compatibility

- 所有 brand 欄位 nullable + 有 default fallback：未設定 = 顯示原本的 BPM 紅底方塊 + "BPM System"
- 舊資料庫升 migration 後 brand 欄位都是 null，UI 行為不變
- 若 brand 設了之後想還原，admin 點「Reset to defaults」一鍵清空

### Coexistence with sandbox mode

Branding 跟 Sandbox 共用 TenantSettings 表但邏輯獨立：開 sandbox 不影響 brand，改 brand 不影響 sandbox。Sandbox banner 仍正常顯示在 brand logo 上方。
