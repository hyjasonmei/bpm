# Design — add-tenant-branding

## 1. 為什麼 base64 in DB，不接 file storage

**Alternative**：上傳到 S3 / Azure Blob，DB 只存 URL

**Rejected for now**：
- `add-file-storage` 還沒 ship
- 需要簽 storage 服務、credentials、CORS、CDN、生命週期…
- POC 階段每 tenant 一張 logo 圖，量少（< 200KB × N 個 tenant）
- base64 in DB 簡單到極致：upload → base64 → INSERT，下載 → SELECT → render

成本：
- DB row 大小（200KB × few tenants）— 完全可接受
- LIST 查詢時不該 SELECT logo 欄位（Configuration 用 SQL Server / Postgres column projection 控制）
- 跨 client cache 由 sessionStorage 負責，不會每頁 reload 都拉 200KB

未來 `add-file-storage` 落地後，這個 change 會被 `extend-tenant-branding-with-file-storage` 之類的 change 取代 — 把 base64 移到對象存儲，DB 改存 URL。屆時 schema 加一個 `BrandLogoUrl` 欄位、留 `BrandLogoBase64` 為 nullable 過渡，新上傳走 URL 路徑、舊資料 lazy migrate。**那是另一張 ticket，不在這個 change 的範圍。**

## 2. 為什麼 logo 文字限 8 字元

CSS：彩色方塊預設 `h-7 w-7` (28px × 28px)，要塞 8 個半形字符（如 "BPM"）已經有點擠，超過 8 字元視覺上爆掉。

對中文：4 個全形字符 ≈ 8 個半形寬，所以 8 字元上限對中文 / 英文都合理。

實作上前端 input maxLength=8，後端字段 `string(8)` 嚴格擋。客戶要更長的字 → 用 BrandSystemName（在 logo 旁邊）。

## 3. 預設色 swatch 為什麼選這 8 個

Tailwind built-in palette：red / blue / green / amber / slate / indigo / violet / rose

- 涵蓋常見企業色（紅、藍、綠是 80% 客戶會選的）
- 包含 neutral（slate 灰，給保守風格客戶）
- 限制成 swatch 而非色票輸入：
  - 客戶不會挑出「紫不紫紅不紅」的尷尬色
  - Tailwind classes 可以靜態確定（不用 dynamic style）
  - 白色 / 黑色不在內，因為 contrast 對 header 反白文字不適合

Tailwind class 對應：

```ts
const SWATCHES = {
  red:    'bg-red-500',
  blue:   'bg-blue-600',
  green:  'bg-green-600',
  amber:  'bg-amber-500',
  slate:  'bg-slate-700',
  indigo: 'bg-indigo-600',
  violet: 'bg-violet-600',
  rose:   'bg-rose-600',
}
```

如果客戶非要自定 hex（demo 簽很大的單），未來開 `add-tenant-branding-custom-color` 加 hex picker。本 change 不做。

## 4. 為什麼 GET /api/branding 公開（無 auth）

理由：
- Logo 是「公開資訊」（任何人造訪 `/app/` 看得到 logo）— 不該因為使用者沒登入就看不到
- 兩個 UI 在 mount 時就要 fetch，這時 dev-login auto-mint JWT 還在 race
- 公開 endpoint 反而簡化 race condition 問題

風險：未授權者可以查 logo — 完全不是安全議題（logo 本來就是公開的牌面）。

## 5. SVG 安全 — 不允許 `<script>`

SVG 是 XML，可以含 `<script>` tag → XSS 風險。

實作上後端在驗證 MIME 後，對 `image/svg+xml` 做：
1. 解析 SVG 文字
2. 拒絕含以下 tag / attribute 的：
   - `<script>` 任意位置
   - 任何 `on*` event handler 屬性（onclick, onerror 等）
   - `xlink:href` / `href` 指向 `javascript:` URL
3. 通過 → 接受
4. 否則 → 422 with detail "SVG contains disallowed elements"

前端則不解析 SVG，純 `<img src="data:image/svg+xml;base64,...">` 套用 — 瀏覽器處理 SVG image content 時會 sandboxed（不執行 script），但如果用 `<object>` 或 `<svg>` inline 就有風險。所以**前端只用 `<img>`**。

## 6. Cache invalidation

兩個策略：

**Server-side**：每次 GET /api/branding 都從 DB 讀（不 cache server-side），DB 命中極快、流量不會大。
**Client-side**：sessionStorage 存 logo data URL + system name + 1 個 timestamp。下次 mount 直接讀 cache（< 100ms 載入）。

Invalidation：
- Admin 點 Save 後，前端立即 setItem `bpm_branding_cache` + dispatch `bpm:branding-changed` (window event)
- 其他 component（兩個 UI 的 layout）listen 到 event → reload
- 同一瀏覽器其他分頁靠 `storage` event 自動 sync（現代瀏覽器原生 support）

POC 接受 stale cache 的「幾秒」延遲（其他分頁 / 別人的瀏覽器）— branding 不是即時要求。

## 7. Audit

每次 `PUT /api/branding` 寫一筆 audit row 到既有的 audit table 或新建一個 `BrandingChange`：

| Field | 說明 |
|---|---|
| ActorUserId | 改的人 |
| ChangedFieldsJson | 哪幾個欄位變了 |
| OldValuesSnapshot | 變更前的 snapshot |
| NewValuesSnapshot | 變更後的 snapshot |
| CreatedAt | When |

選擇放 `BrandingChange` 新表還是塞進現有的 audit 通用表？採新表（與 sandbox / role assignment 一致），保持 schema 清晰、查詢方便。

## 8. 不做歷史版本回退

「3 個月前的 brand 是什麼？」— 走 audit log（OldValuesSnapshot 內含完整 old state）。不另做「version 1, 2, 3 列表 + 一鍵還原 v1」。

理由：實際使用上，brand 改了就改了，沒人會有「我想看 3 個月前的 brand 然後還原」的需求 — 大不了重 upload。

## 9. 為什麼放在 SiteSettings 而不是獨立頁

Site Settings 是 admin 「整個系統設定」的歸屬，目前已含 Sandbox。Branding 在語意上屬同類（系統層面的 demo / display 設定）。塞同一頁減少 sidebar 浪費 + 減少跳轉。

未來 Site Settings 內容變多（10+ 子區塊）再考慮拆 tab。POC 階段 Sandbox + Branding 兩個區塊用 SectionCard 隔開夠清楚。

## 10. 為什麼 admin UI 的 Logo 也要跟著切

Demo 流程：業務切到 admin UI 給客戶 IT 看「你以後管理員介面長這樣」— 如果 admin UI 還是 BPM logo，違和。

統一兩個 UI 都吃同一份 brand 設定 → 客戶感覺「整套產品都是我的牌」。實作上兩個 UI 各自呼叫 `/api/branding` 即可（沒 shared component 也無妨）。
