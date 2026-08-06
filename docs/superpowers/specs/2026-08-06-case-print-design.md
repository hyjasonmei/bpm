# 前台案件列印精修 — Design

2026-08-06。TG 定案：

- 用途 = **內部紙本歸檔／稽核佐證** ＋ **申請人自行留存憑證**。
  不做「印出來給人手簽蓋章」，所以不留簽名欄／章位。
- 範圍 = **案件詳情頁**（14 隻 chef-cooked flow 一次到位）。
  首頁清單列印、BPMN 圖列印都不在本次。
- 做法 = **方案 A：全域列印樣式層**（lead 範圍，chef code 零改動）。
  方案 B（專用列印檢視路由）排在 A 之後另案。
- **chef conventions 要納入列印**，讓新 cook 出來的流程天生印得好。

## 現況

`components/CaseToolbar.tsx` 的印表機 icon 只呼叫 `window.print()`；
`index.css` 有一段 20 行的 `@media print`：把 `.no-print` /
`[data-no-print]` 藏掉、`main` 去掉 padding、關掉 fade-in 動畫。
等於「螢幕版直接送印表機」。

查證出來的具體問題（對 `LEAVE_V1_CaseDetail.tsx` + 共用元件實查）：

| # | 問題 | 位置 |
|---|---|---|
| 1 | 「返回」「View BPMN」按鈕沒掛 `no-print`，會印在紙上 | 各 flow CaseDetail header（chef code，14 份） |
| 2 | 簽核意見 / 備案備註的 `Textarea` 沒藏，印出空白輸入框 | `ApprovalForm` / `HrArchiveForm`（chef code） |
| 3 | 沒有 break 控制，`SectionCard` 與時序表整列會被切在兩頁中間 | `components/ui/card.tsx` 使用處 |
| 4 | 沒有列印頁首／頁尾：案號、列印時間、列印人都不在紙上 | 無 |
| 5 | demo 用的 `FlowStateBanner` 照印 | 各 flow CaseDetail |
| 6 | `pb-24`（給 ActionFooter 讓位）在紙上留一段空白 | 各 flow CaseDetail 外層 |

## Non-goals

- 首頁清單（我的申請 / 待我簽核）列印 — 偏報表，另案
- BPMN 流程圖印進文件 — 圖在 modal 內，要另外把 SVG 拉出來，另案
- 後端產 PDF / 浮水印 / 存檔 — 跟「稽核 log 統一匯出」那條線一起做
- 手簽章位、騎縫章 — 用途 B 未選

## 設計

### 1. 邊界

全部落在 lead 範圍，chef 的 14 隻 CaseDetail **一行不動**：

| 檔案 | 動作 |
|---|---|
| `bpm-ui/src/index.css` | 擴充 `@media print` 樣式層 |
| `bpm-ui/src/router.tsx` | `FeatureCaseDetailRoute` 注入列印頁首／頁尾 |
| `bpm-ui/src/components/CasePrintChrome.tsx` | **新增** — 只在列印出現的文件頭尾 |
| `bpm-ui/src/components/ui/FilePicker.tsx` | `AuthedFileLink` 加 `data-print-keep` |
| `chef/skill/conventions.md` | 補「列印友善」規範段落 |
| `bpm-docs/.../frontend/inbox.md` | 客戶手冊同步（根 CLAUDE.md 硬規定） |

後端不動。

### 2. `CasePrintChrome`（新元件）

`FeatureCaseDetailRoute` 已經知道 `flowCode` / `version` / `caseId`，
在 `<Detail>` 前後各掛一段 `print-only`（螢幕 `display:none`，
`@media print` 才顯示）的區塊。

**頁首**

- 左：客戶 logo + 系統名 — 走現成的 `useBranding()`
  （`GET /api/branding` 回 `systemName` / `logoDataUri`），白牌自動生效；
  沒設定就只印系統名，沒有 logo 也不留空框
- 標題：`FORMS[code].zhLabel` +「案件單」（例：`請假申請 案件單`），
  副標 `<CODE> V<N>`
- meta 行：`案號 <caseId>` · `列印時間 <yyyy/MM/dd HH:mm>` ·
  `列印人 <full_name>`
  - 列印人 = `decodeJwt(getJwt()).full_name`，退回 `email`，再退回 `—`
  - 列印時間用 `beforeprint` 事件當下 stamp，不是頁面載入時間

**頁尾**

一行小字：`本文件由 <系統名> 於 <列印時間> 由 <列印人> 列印`。
歸檔稽核比頁碼有價值。

**「目前狀態」不放頁首**：狀態是 per-flow API 的資料，wrapper 拿不到
（chef 元件自己 fetch），要拿只能爬 DOM。而且各 flow 的
「狀態 / Status」卡本來就會印出來，重複無益 —— 故不放。

**每頁重複的頁首／頁尾**（2026-08-06 修訂，原本誤判為做不到）：
瀏覽器本來就會把 table 的 `thead` / `tfoot` 印在**每一頁**，而
`display: table-header-group` / `table-footer-group` 讓一個普通 div
取得同樣行為——**不需要**改成 `<table>` 標籤，螢幕 DOM 也不動。
所以 `@media print` 裡把 `.print-doc` 設成 `display: table`，
header → `table-header-group`、案件本體 → `table-row-group`、
footer → `table-footer-group`。頁碼仍交給列印對話框內建的頁首頁尾
（CSS 沒有跨瀏覽器可用的頁碼變數）。

### 3. 列印樣式層（`index.css`）

沿用現有 `@media print` 區塊往下長：

**隱藏**

- `button`, `input`, `textarea`, `select` 一律 `display:none`
  —— 一條規則同時解掉問題 1 與 2，chef 的 14 份 code 不用動
- 例外：`[data-print-keep]` 保留
- `.no-print` / `[data-no-print]`（既有）
- `FlowStateBanner` 掛 `no-print`（lead 元件，直接改）

**附件連結**

`AuthedFileLink` 實際上是 `<button>`（要帶 JWT fetch blob，不能用
`<a href>`），會被上面的規則誤殺 —— 紙上必須看得到「這案有附件」。
故：加 `data-print-keep`，列印時樣式降成純文字（無邊框、無色、
不帶 hover 底線）。

**分頁**

- `SectionCard` → `break-inside: avoid`
- 時序表的 `li`（`.divide-y` 的每一列）→ `break-inside: avoid`
- `SectionTitle` → `break-after: avoid`（標題不落單在頁尾）
- `h1` → `break-after: avoid`

**版面**

- `@page { margin: 12mm }`
- 卡片：`box-shadow: none`，邊框改細灰線（`#d4d4d8` 1px）
- `SectionTitle` 的 `bg-slate-50` 保留（`print-color-adjust: exact` 既有），
  灰階印表機仍可讀
- 去掉外層 `pb-24` 留白（`.print-doc .pb-24 { padding-bottom: 0 }`）
- **字級收斂：預設不做。** Tailwind 對每個元素都下了明確字級，改
  `body` font-size 無效；真的塞不下再對 `.print-doc` 下 `zoom: 0.92`，
  以視覺驗收結果決定（YAGNI）
- `Stepper` **保留**：紙上看得出案子走到哪一關是有價值的

### 4. chef conventions 補的規範

`chef/skill/conventions.md` 加一節「列印」：

- 互動元件（button / input / textarea / select）不需特別標記，
  全域列印樣式已藏
- **紙上要出現的內容不可以只存在於 modal / tooltip / 摺疊區**
- 區塊一律用 `SectionCard` 包 —— 自動有防切頁保護
- 該區塊不該印就掛 `no-print`
- 不要自己寫 `@media print`；有共用需求回報 lead 提到樣式層

## 驗收

1. `npx tsc -p tsconfig.app.json --noEmit` 全綠
2. 本機 boot（bpm-svc + bpm-ui），用 Chrome 列印預覽抽驗三種型態：
   - `LEAVE` 走完結案的案件（完整時序 + HR 備註）
   - `PURCHASE_REQUEST` pending 中的案件（會出現簽核意見輸入區 → 應消失）
   - `CONTRACT_REVIEW` 並簽案件（多 slot 時序表 → 檢查分頁不切列）
3. 檢查項：無按鈕、無空白輸入框、頁首頁尾正確、卡片不跨頁斷裂、
   附件欄仍顯示文字
4. 截圖回報
5. `bpm-docs` 的 `frontend/inbox.md`「小工具」段補上「印出來會長怎樣」，
   `npm run build` 過（根 CLAUDE.md：功能改動必同步客戶手冊）

## 後續（方案 B，另案）

專用列印檢視路由 `/cases/:code/:id/print`：重排成單欄正式表單版面，
可解掉「頁首只有第一頁」的限制、可插 BPMN 圖。需要 per-flow 的
列印元件或欄位描述 manifest，會動到 chef 邊界 —— A 上線後另開。
