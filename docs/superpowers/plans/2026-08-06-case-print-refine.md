# 前台案件列印精修 Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** 把案件詳情頁的列印從「螢幕版直接送印」升級成可直接歸檔的案件單——去掉互動元件、加上文件頁首／頁尾、控制分頁，且 14 隻 chef-cooked flow 一次到位。

**Architecture:** 全部做在 lead 範圍的共用層。一段擴充的 `@media print`（`bpm-ui/src/index.css`）負責隱藏／扁平化／分頁；一個新的 `CasePrintChrome` 元件由 `/cases/:flowCode/:caseId` route 包住每一隻 detail 元件，注入只在列印出現的頁首頁尾。chef 的 per-flow CaseDetail 一行不動。

**Tech Stack:** React 18 + Vite + Tailwind v4（bpm-ui）。**沒有 JS test runner** —— 驗證靠 `tsc -p tsconfig.app.json --noEmit` ＋ 本機 boot ＋ Chrome 列印樣式截圖（下面每個任務都給了確切指令）。

Spec：`docs/superpowers/specs/2026-08-06-case-print-design.md`

---

## 為什麼沒有單元測試

`bpm-ui/CLAUDE.md`：「No JS test runner — rely on tsc + manual boot
(`npm run dev`, port 5173) + chrome-devtools screenshots」。本計畫改
的是 CSS 與一個 presentational 元件，沒有可斷言的邏輯分支；驗證方式
是 **Task 8 的視覺回歸**（三種型態的案件各截一張列印樣式圖）。
每個任務仍先寫下「預期看到什麼」再改，改完立刻驗。

**列印樣式怎麼在瀏覽器裡看：** chrome-devtools MCP 沒有 print media
emulation。用 CSSOM 把 `@media print` 就地改成 `screen`（Task 8 的
snippet），頁面即刻以列印樣式渲染，可直接 `take_screenshot`。

## File Structure

| 檔案 | 責任 | 動作 |
|---|---|---|
| `bpm-ui/src/index.css` | 列印樣式層（隱藏／扁平／分頁） | 改 §115–144 |
| `bpm-ui/src/components/CasePrintChrome.tsx` | 列印文件頁首＋頁尾，包住 detail | **新增** |
| `bpm-ui/src/router.tsx` | 在 case-detail route 掛上 CasePrintChrome | 改 `FeatureCaseDetailRoute` |
| `bpm-ui/src/components/ui/card.tsx` | 給 SectionCard / SectionTitle 穩定的列印 hook class | 加 class |
| `bpm-ui/src/components/ui/FilePicker.tsx` | 附件按鈕在列印時要活下來 | 加 `data-print-keep` |
| `bpm-ui/src/components/ui/flow-state-banner/FlowStateBanner.tsx` | demo 提示條不上紙 | 加 `no-print` |
| `chef/skill/conventions.md` | 新 cook 天生印得好 | 新增 `### 列印友善` |
| `bpm-docs/src/content/docs/frontend/inbox.md` | 客戶手冊同步（根 CLAUDE.md 硬規定） | 改「小工具」段 |

---

### Task 1: 列印 hook class（card.tsx）

先給共用卡片穩定的 class，後面的 CSS 才有東西可鉤——不要用
`[class*="rounded-lg"]` 這種脆弱選擇器。

**Files:**
- Modify: `bpm-ui/src/components/ui/card.tsx:4-12`

- [ ] **Step 1: 加上 hook class**

把 `SectionCard` / `SectionTitle` 改成：

```tsx
export const SectionCard = ({ className, ...props }: React.HTMLAttributes<HTMLDivElement>) => (
  <div className={cn('print-block overflow-hidden rounded-lg border border-rule bg-card', className)} {...props} />
)

export const SectionTitle = ({ className, children, right }: React.HTMLAttributes<HTMLDivElement> & { right?: React.ReactNode }) => (
  <div className={cn('print-title flex items-center justify-between border-b border-rule bg-slate-50 px-4 py-2.5 text-sm font-semibold text-ink', className)}>
    <span>{children}</span>
    {right}
  </div>
)
```

`print-block` / `print-title` 在螢幕上沒有任何樣式（Task 2 只在
`@media print` 裡定義），所以這步對畫面零影響。

- [ ] **Step 2: 型別檢查**

Run: `cd /Users/jason/claude/bpm/bpm-ui && npx tsc -p tsconfig.app.json --noEmit`
Expected: 無輸出（全綠）

- [ ] **Step 3: Commit**

```bash
cd /Users/jason/claude/bpm
git add bpm-ui/src/components/ui/card.tsx
git commit -m "refactor(ui): stamp print hook classes on SectionCard / SectionTitle"
```

---

### Task 2: 列印樣式層（index.css）

**Files:**
- Modify: `bpm-ui/src/index.css:115-144`（整段 `@media print` 換掉）

- [ ] **Step 1: 換掉整段列印區塊**

把 `index.css` 第 115 行 `/* ── Print mode ──…` 到第 144 行結尾的
`}` 整段，換成：

```css
/* ── Print mode ──────────────────────────────────────────────────────────
   The case-detail printer icon (components/CaseToolbar) calls
   window.print(); everything below turns the screen page into a
   file-able case sheet.
   Design: docs/superpowers/specs/2026-08-06-case-print-design.md

   Deliberately global rather than per-flow: the 14 chef-cooked
   CaseDetail components are feature code, so this shared layer has to
   cover all of them at once without editing any of them. */

/* Document head / foot injected by components/CasePrintChrome —
   invisible on screen, revealed on paper. */
.print-only { display: none; }

@media print {
  .print-only { display: block !important; }

  .no-print,
  [data-no-print] {
    display: none !important;
  }

  /* Interactive chrome never belongs on paper. One blanket rule kills
     the per-flow "返回" / "View BPMN" buttons and the decision
     textareas without touching any chef feature code. Anything that
     must survive opts in with `data-print-keep`. */
  button:not([data-print-keep]),
  input:not([data-print-keep]),
  textarea:not([data-print-keep]),
  select:not([data-print-keep]) {
    display: none !important;
  }

  /* Attachment links are <button> (they JWT-fetch a blob, so they
     cannot be a plain <a href>). Keep them, flattened to text — the
     paper record must still show that the case carries an attachment. */
  [data-print-keep] {
    display: inline !important;
    border: 0 !important;
    background: none !important;
    padding: 0 !important;
    color: inherit !important;
    text-decoration: none !important;
  }

  html, body, #root {
    height: auto !important;
    background: #fff !important;
  }
  body {
    -webkit-print-color-adjust: exact;
    print-color-adjust: exact;
  }

  @page { margin: 12mm; }

  /* Drop the screen layout's padding / width cap + entry animation so the
     content uses the full printable page. `main` is the AppLayout content
     slot that hosts the case detail. */
  main {
    max-width: none !important;
    padding: 0 !important;
  }
  .fade-in {
    animation: none !important;
  }
  /* That bottom spacer only exists to clear the sticky ActionFooter,
     which is itself `no-print`. Scoped to `.print-doc` (the wrapper
     CasePrintChrome puts around the case detail) so these loose
     element-level rules can't leak onto anything else that gets
     printed later. */
  .print-doc .pb-24 { padding-bottom: 0 !important; }

  /* Flat print frame, and keep a card whole on one page. `print-block`
     / `print-title` are stamped by components/ui/card.tsx. */
  .print-block {
    box-shadow: none !important;
    border-color: #d4d4d8 !important;
    break-inside: avoid;
  }
  .print-title { break-after: avoid; }
  .print-doc h1 { break-after: avoid; }
  /* Approval-timeline rows are per-flow markup, so key off the element:
     keeps a row's label / actor / comment / timestamp together. */
  .print-doc li { break-inside: avoid; }
}
```

- [ ] **Step 2: 確認 `pb-24` 是各 flow 共用的底部 spacer**

Run:
```bash
cd /Users/jason/claude/bpm
grep -rl "pb-24" bpm-ui/src/features | wc -l
grep -rL "pb-24" bpm-ui/src/features/*/V*/*CaseDetail.tsx
```
Expected: 第一行印出 ≥ 10；第二行列出「沒用 pb-24」的 CaseDetail。
若有漏網的（用了別的 spacer class，例如 `pb-20`／`pb-28`），把那個
class 一併加進上面 `.pb-24` 那條規則的選擇器，例如
`.pb-24, .pb-20 { padding-bottom: 0 !important; }`。

- [ ] **Step 3: 型別檢查（確認沒改壞 build）**

Run: `cd /Users/jason/claude/bpm/bpm-ui && npx tsc -p tsconfig.app.json --noEmit`
Expected: 無輸出

- [ ] **Step 4: Commit**

```bash
cd /Users/jason/claude/bpm
git add bpm-ui/src/index.css
git commit -m "feat(print): shared print layer — hide interactive chrome, flatten cards, control page breaks"
```

---

### Task 3: 附件按鈕在列印時活下來（FilePicker）

**Files:**
- Modify: `bpm-ui/src/components/ui/FilePicker.tsx:225-229`

- [ ] **Step 1: 加 `data-print-keep`**

把 `AuthedFileLink` 的 return 改成：

```tsx
  return (
    // data-print-keep: the shared print layer hides every <button>; this
    // one carries the "案件有附件" fact, so it survives and renders as
    // plain text on paper (see index.css @media print).
    <button type="button" data-print-keep onClick={open} disabled={busy} className={className}>
      {busy ? '開啟中…' : failed ? '開啟失敗，再試一次' : children}
    </button>
  )
```

- [ ] **Step 2: 型別檢查**

Run: `cd /Users/jason/claude/bpm/bpm-ui && npx tsc -p tsconfig.app.json --noEmit`
Expected: 無輸出（`data-print-keep` 是合法的 data attribute，React 直接透傳）

- [ ] **Step 3: Commit**

```bash
cd /Users/jason/claude/bpm
git add bpm-ui/src/components/ui/FilePicker.tsx
git commit -m "feat(print): keep attachment links on paper as plain text"
```

---

### Task 4: 下架提示條不上紙（FlowStateBanner）

**Files:**
- Modify: `bpm-ui/src/components/ui/flow-state-banner/FlowStateBanner.tsx:32-44`

- [ ] **Step 1: 兩個 return 都包 `no-print`**

把最後兩個 return 改成：

```tsx
  if (entry.version !== flowVersion) {
    return (
      <div className="no-print">
        <InfoBanner>
          此案件使用的 <span className="font-mono">{flowCode} v{flowVersion}</span> 已下架。最新版本為 v{entry.version}（state = {entry.state}）。新案件請使用最新版。
        </InfoBanner>
      </div>
    )
  }
  return (
    <div className="no-print">
      <InfoBanner>
        此流程 <span className="font-mono">{flowCode} v{flowVersion}</span> 已下架，新案件無法建立。本案件仍可繼續處理至結束。
      </InfoBanner>
    </div>
  )
```

（包一層 div 而不是給 `InfoBanner` 加 `className` prop —— `InfoBanner`
是被 6 個以上地方共用的 primitive，為了單一使用者改它的簽名不划算。）

- [ ] **Step 2: 型別檢查**

Run: `cd /Users/jason/claude/bpm/bpm-ui && npx tsc -p tsconfig.app.json --noEmit`
Expected: 無輸出

- [ ] **Step 3: Commit**

```bash
cd /Users/jason/claude/bpm
git add bpm-ui/src/components/ui/flow-state-banner/FlowStateBanner.tsx
git commit -m "feat(print): keep the retired-flow advisory off the printed sheet"
```

---

### Task 5: `CasePrintChrome` 元件

**Files:**
- Create: `bpm-ui/src/components/CasePrintChrome.tsx`

- [ ] **Step 1: 建檔**

完整內容：

```tsx
import { useEffect, useRef, type ReactNode } from 'react'

import { getJwt } from '@/lib/apiFetch'
import { useBranding } from '@/lib/branding'
import { decodeJwt } from '@/lib/jwt'
import { FORMS, type FormCode } from '@/lib/workflow'

interface Props {
  flowCode: FormCode
  flowVersion: number
  caseId: string
  children: ReactNode
}

/**
 * Document head + foot for the printed case sheet.
 *
 * Both blocks carry `print-only` — invisible on screen, revealed by the
 * `@media print` layer in index.css. Wrapping happens once in the shared
 * `/cases/:flowCode/:caseId` route, so all 14 chef-cooked CaseDetail
 * components inherit the printed chrome with no per-flow edit.
 *
 * The timestamp is written straight into the DOM on `beforeprint` rather
 * than through React state: Chrome snapshots the page as soon as the
 * event handlers return, which is before React would flush a state
 * update — a re-render could miss the paint.
 *
 * "目前狀態" deliberately does NOT appear here: it lives in the per-flow
 * API response the chef component fetches, and every flow already prints
 * its own 狀態 / Status card.
 */
export function CasePrintChrome({ flowCode, flowVersion, caseId, children }: Props) {
  const branding = useBranding()
  const systemName = branding.systemName ?? 'BPM System'
  const flowLabel = FORMS[flowCode]?.zhLabel ?? flowCode
  const printedBy = viewerName()
  const stamps = useRef<(HTMLSpanElement | null)[]>([])

  useEffect(() => {
    const sync = () => {
      const now = formatNow()
      for (const el of stamps.current) if (el) el.textContent = now
    }
    sync()
    window.addEventListener('beforeprint', sync)
    return () => window.removeEventListener('beforeprint', sync)
  }, [])

  return (
    <div className="print-doc">
      <header className="print-only mb-4 border-b-2 border-ink pb-3">
        <div className="flex items-start justify-between gap-4">
          <div className="flex items-center gap-2">
            {branding.logoDataUri && (
              <img src={branding.logoDataUri} alt="" className="h-8 w-auto max-w-[140px] object-contain" />
            )}
            <span className="text-sm font-semibold text-ink">{systemName}</span>
          </div>
          <div className="text-right">
            <h1 className="text-lg font-bold text-ink">{flowLabel} 案件單</h1>
            <p className="font-mono text-[11px] text-ink-muted">{flowCode} V{flowVersion}</p>
          </div>
        </div>
        <p className="mt-2 text-[11px] text-ink-muted">
          案號 <span className="font-mono">{caseId}</span>
          {' · '}列印時間 <span ref={el => { stamps.current[0] = el }} />
          {' · '}列印人 {printedBy}
        </p>
      </header>

      {children}

      <footer className="print-only mt-4 border-t border-rule pt-2 text-[10px] text-ink-muted">
        本文件由 {systemName} 於 <span ref={el => { stamps.current[1] = el }} /> 由 {printedBy} 列印
      </footer>
    </div>
  )
}

/** Printer identity = the JWT the page is already authenticated with. */
function viewerName(): string {
  const tok = getJwt()
  if (!tok) return '—'
  const d = decodeJwt(tok)
  return d?.full_name ?? d?.email ?? '—'
}

function formatNow(): string {
  const d = new Date()
  const pad = (n: number) => String(n).padStart(2, '0')
  return `${d.getFullYear()}/${pad(d.getMonth() + 1)}/${pad(d.getDate())} ${pad(d.getHours())}:${pad(d.getMinutes())}`
}
```

- [ ] **Step 2: 型別檢查**

Run: `cd /Users/jason/claude/bpm/bpm-ui && npx tsc -p tsconfig.app.json --noEmit`
Expected: 無輸出。若報 `getJwt` 不存在，確認它確實由 `@/lib/apiFetch`
export（`bpm-ui/src/features/LEAVE/V1/LEAVE_V1_CaseDetail.tsx:14` 就是
這樣 import 的）。

- [ ] **Step 3: Commit**

```bash
cd /Users/jason/claude/bpm
git add bpm-ui/src/components/CasePrintChrome.tsx
git commit -m "feat(print): CasePrintChrome — document head/foot for the printed case sheet"
```

---

### Task 6: 掛進 case-detail route

**Files:**
- Modify: `bpm-ui/src/router.tsx`（import 區 + `FeatureCaseDetailRoute` 的 return，約 §92–112）

- [ ] **Step 1: 加 import**

在 `import { CaseToolbar } from '@/components/CaseToolbar'` 下面加一行：

```tsx
import { CasePrintChrome } from '@/components/CasePrintChrome'
```

- [ ] **Step 2: 包住既有的 return**

`FeatureCaseDetailRoute` 目前 return 一個 `<div className="relative">`。
改成用 `CasePrintChrome` 包起來：

```tsx
  const Detail = manifest.detailComponent
  return (
    <CasePrintChrome
      flowCode={normalizedCode as FormCode}
      flowVersion={manifest.version}
      caseId={caseId}
    >
      <div className="relative">
        {/* Copy-link + Print overlaid onto the per-flow header row, just left
            of its "View BPMN" button. Overlaid (not in normal flow) so chef's
            CaseDetail is untouched; the container mirrors the detail's
            `max-w-screen-lg` + `p-6` so the right edge lines up, and the
            ~132px right offset clears the View BPMN button + gap (every flow
            cribs the same header from LEAVE V1). pointer-events-none lets
            clicks fall through to the detail (incl. View BPMN) everywhere
            except the toolbar itself. */}
        <div className="pointer-events-none absolute inset-x-0 top-0 z-10 hidden md:block">
          <div className="mx-auto flex max-w-screen-lg justify-end pl-6 pr-[156px] pt-[30px]">
            <CaseToolbar />
          </div>
        </div>
        <Detail caseId={caseId} persona={persona} />
      </div>
    </CasePrintChrome>
  )
```

- [ ] **Step 3: 型別檢查**

Run: `cd /Users/jason/claude/bpm/bpm-ui && npx tsc -p tsconfig.app.json --noEmit`
Expected: 無輸出

- [ ] **Step 4: 螢幕沒有回歸（跑起來看一眼）**

Run: `cd /Users/jason/claude/bpm/bpm-ui && npm run dev`
開 `http://localhost:5173`，登入後隨便進一張案件頁。
Expected: 畫面**跟改之前一模一樣** —— `print-only` 的頁首頁尾在螢幕
上不顯示，`print-doc` 只是個沒有樣式的 wrapper div。

- [ ] **Step 5: Commit**

```bash
cd /Users/jason/claude/bpm
git add bpm-ui/src/router.tsx
git commit -m "feat(print): wrap every chef case-detail with CasePrintChrome"
```

---

### Task 7: chef conventions 補列印規範

**Files:**
- Modify: `chef/skill/conventions.md`（在 `### Retired-flow banner` 之後、`## BPMN passthrough` 之前插入）

- [ ] **Step 1: 插入新小節**

```markdown
### 列印友善（Print）

案件詳情頁是可以被列印歸檔的（案件頁右上角印表機 icon →
`window.print()`）。列印規則由 lead 的共用層一次處理
（`bpm-ui/src/index.css` 的 `@media print` ＋
`components/CasePrintChrome`），chef **不需要也不應該**自己寫
`@media print`。要配合的只有四條：

1. **互動元件不用特別標記** —— 共用層已把所有 `button` / `input` /
   `textarea` / `select` 藏起來，所以「返回」「View BPMN」「簽核意見
   輸入框」不會印在紙上。
2. **紙上要出現的內容，不可以只存在於 modal / tooltip / 摺疊區。**
   案件的業務欄位、簽核時序、決策意見都要在頁面主體直接可見。
3. **區塊一律用 `SectionCard` 包**（`@/components/ui/card`）——
   它帶著 `print-block`，自動有「不被切在兩頁中間」的保護。
4. **不該印的區塊掛 `no-print`**（例如純提示性的 banner）。

唯讀狀態不要用 disabled `<input>` / `<checkbox>` 呈現——那些在列印
時會整個消失。已決定的值請直接渲染成文字（例：`是` / `否`）。

有跨流程的共用列印需求（例如要在紙上加一塊制式欄位），回報 lead
加進共用層，不要在 feature 資料夾裡自己寫。
```

- [ ] **Step 2: Commit**

```bash
cd /Users/jason/claude/bpm
git add chef/skill/conventions.md
git commit -m "docs(chef): print-friendly conventions for case-detail pages"
```

---

### Task 8: 視覺驗證（三種型態的案件）

沒有 JS test runner，這一步就是本計畫的回歸測試。**做完要留截圖。**

**Files:** 無（只驗證）

- [ ] **Step 1: 起本機 stack**

```bash
cd /Users/jason/claude/bpm/bpm-svc && dotnet run --project src/Api
```
另一個 shell：
```bash
cd /Users/jason/claude/bpm/bpm-ui && npm run dev
```
（本機跑的是本地 Postgres:5432，不是 SQLite —— 見
`reference_bpm_local_postgres` 記憶。`db/bpm.db` 是廢棄舊檔。）

- [ ] **Step 2: 準備三張案件**

用 seed 帳號登入（`alice@acme.example` / `flowcook2026`，或 persona
快切）。需要的三種型態：

| 型態 | 為什麼要驗 |
|---|---|
| `LEAVE` 已結案（Completed） | 完整簽核時序 + HR 備註，驗頁首頁尾與長文件分頁 |
| `PURCHASE_REQUEST` pending 中 | 會出現「您的決定」輸入區 → 列印時必須整塊消失 |
| `CONTRACT_REVIEW` 並簽中 | 多 slot 時序表 → 驗 `li` 的 break-inside 沒被切列 |

沒有現成案件就先用前台送一張出來。

- [ ] **Step 3: 用 CSSOM 把 print 樣式打到螢幕上**

chrome-devtools MCP 沒有 print media emulation，所以就地把
`@media print` 改成 `@media screen`。在案件頁上 `evaluate_script`：

```js
() => {
  let swapped = 0
  for (const sheet of document.styleSheets) {
    let rules
    try { rules = sheet.cssRules } catch { continue }
    for (const rule of rules) {
      if (rule.constructor.name === 'CSSMediaRule' && rule.conditionText.includes('print')) {
        rule.media.mediaText = 'screen'
        swapped++
      }
    }
  }
  return swapped
}
```
Expected: 回傳 ≥ 1。畫面立刻變成列印樣式（頂部 nav 消失、頁首出現）。
恢復：重新整理頁面即可。

- [ ] **Step 4: 逐項檢查並截圖**

`take_screenshot`（預設 `fullPage=true`）每一張案件各一張。逐項核對：

- [ ] 頁首有：logo（若客戶有設）／系統名／`<流程中文名> 案件單`／`<CODE> V<N>`
- [ ] 頁首 meta 行有：案號、列印時間（今天此刻）、列印人（登入者姓名）
- [ ] 頁尾那行「本文件由 … 列印」有出現且時間與頁首一致
- [ ] 沒有任何按鈕（尤其「返回」「View BPMN」「複製連結」「印表機」）
- [ ] 沒有空白輸入框（PURCHASE_REQUEST 那張的「您的決定」整塊消失）
- [ ] 附件欄仍看得到文字（LEAVE 若該案有證明文件）
- [ ] 卡片是細線框、沒有陰影
- [ ] 底部沒有 ActionFooter 留下的大片空白
- [ ] 簽核時序每一列的「關卡／處理人／意見／時間」沒被拆到兩頁

任一項不符 → 回對應 Task 修 CSS，重跑本步。

- [ ] **Step 5: 真列印預覽最終確認**

在同一張案件頁按 `Cmd+P` 開 Chrome 列印預覽（這是真的分頁引擎，
CSSOM 那招看不到分頁行為）。確認：
- 卡片沒有跨頁斷裂
- 標題沒有落單在頁尾
- 一般長度的案件在 1–2 頁內

若某張明顯塞不下，加一條收斂字級的規則進 `index.css` 的
`@media print`（放在 `.print-block` 規則之前）：

```css
  /* Tailwind sets an explicit size on each element, so changing the body
     font-size does nothing — scale the whole document instead. */
  .print-doc { zoom: 0.92; }
```
加了要重跑 Step 4 + Step 5。**沒塞不下就不要加**（YAGNI）。

- [ ] **Step 6: 截圖回傳 Telegram**

把三張截圖用 reply tool 的 `files` 參數傳給 Jason。

---

### Task 9: 客戶手冊同步（根 CLAUDE.md 硬規定）

**Files:**
- Modify: `bpm-docs/src/content/docs/frontend/inbox.md:25-29`（「## 小工具」段）

- [ ] **Step 1: 改寫列印那條**

把第 27 行那條 bullet：

```markdown
- 案件頁右上角可**複製本案連結**（貼給同事直接開）與**列印此案件**
```

換成：

```markdown
- 案件頁右上角可**複製本案連結**（貼給同事直接開）與**列印此案件**
  - 列印出來是一份可直接歸檔的案件單：頁首帶貴公司名稱／標誌、流程名稱、案號、列印時間與列印人，頁尾另有一行列印紀錄
  - 畫面上的按鈕、輸入框、系統提示都不會印出來；附件欄位會以文字保留，讓紙本看得出該案有附件
```

- [ ] **Step 2: build 確認**

Run: `cd /Users/jason/claude/bpm/bpm-docs && npm run build`
Expected: build 成功（`Complete!`），無 broken-link 警告

- [ ] **Step 3: Commit**

```bash
cd /Users/jason/claude/bpm
git add bpm-docs/src/content/docs/frontend/inbox.md
git commit -m "docs(guide): describe what the printed case sheet contains"
```

- [ ] **Step 4: 重佈手冊站（Jason 確認後才做）**

```bash
cd /Users/jason/claude/bpm/bpm-docs
npm run build
swa deploy ./dist --deployment-token $(az staticwebapp secrets list -n poc-flowcook-docs -g rg-poc --query properties.apiKey -o tsv) --env production
```

---

## 完成後

- branch `feat/case-print-refine`，push 由 Jason 用 GitKraken 處理
- 方案 B（專用列印檢視路由 `/cases/:code/:id/print`）另案，見 spec 末段
