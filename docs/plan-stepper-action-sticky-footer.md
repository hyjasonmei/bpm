# Plan — Stepper 收斂、Action schema、sticky footer

合 ④ 加 action + 11→7 step + admin/bpm 兩處 sticky footer。Action 不開新 step，跟著節點走（FORMS 收 user task 的、APPROVERS 收 approval 的）。schema 收編但**不刪**，未來要回頭加回來只是切換顯示。

---

## 1. 範圍

| 區塊 | 動 / 不動 |
|---|---|
| bpm-admin-ui wizard (stepper、FORMS、APPROVERS、FormPreviewModal) | **主改** |
| chef skill docs (SKILL.md / conventions.md) | 補 actions → state machine 那節 |
| LEAVE V1 testbed (chef reference cook) | 對齊新 action schema |
| bpm-ui per-flow CaseDetail | 用新的共用 `<ActionFooter>` primitive |
| bpm-ui shell (`components/ui/`) | 新增 `<ActionFooter>` |
| bpm-svc | 不動 (actions 是 chef 在 Application 層自行 emit) |
| bpm-admin-svc | 不動 (spec.json 還是 passthrough，多帶 actions field) |

---

## 2. Stepper：11 → 7

### 拿掉但保留 schema 的 4 步

| 原 step | 拿掉理由 | schema 去處 |
|---|---|---|
| 8 VARIABLES | 多半可空；INTEGRATIONS 自動衍生 `${name}_BASE_URL` 等，已存在 `StepIntegrations:96-105` | `DraftSpec.variables: FlowVariable[]` 保留；改成 INTEGRATIONS 內部自動維護，UI 不暴露面板 |
| 9 SLA | 大多客戶第一年未治理 SLA；validator 本來就不擋 | `DraftSpec.sla.perNode` 保留；power user 走 chat 「emit_sla_config」工具 |
| 10 TRANSLATION | 內部流程 80%+ 只跑 zh-TW | `DraftSpec.labels` 保留；wizard 出口時自動帶 zh-TW，缺其他語系 chef 不會炸 |
| 11 NOTES | 自由文字，不該佔一個 step | `DraftSpec.notes` 保留；改成右上角 sticky「📝 給 chef 的備註」按鈕，隨時點開寫 |

### 留下的 7 step

1. **SOURCE** — 流程骨架
2. **FORMS** — user task 欄位 + 排版 + **actions ⬅ ④**
3. **ACCESS** — 誰可啟動
4. **DECISIONS** — gateway 條件
5. **APPROVERS** — 簽核者 + **actions ⬅ ④**
6. **NOTIFY** — 通知
7. **INTEGRATIONS** — 對外 API

### 進階面板入口（未來）

**本版不做**。VARIABLES / SLA / TRANSLATION 三個 step 雖然從 stepper 拿掉，schema 與 AI tools 都還在，未來真要回頭加入口（譬如進階抽屜或 power-user toggle）隨時可開。

### 影響檔案

- `bpm-admin-ui/src/lib/onboarding.ts` — `ONBOARDING_STEPS` 從 11 刪到 7（順序也順手調，FORMS 前移已經做過了）
- `bpm-admin-ui/src/screens/onboarding/Onboarding.tsx` — `renderCanvas` switch 刪掉四個 case；stepper bar 自動跟著 ONBOARDING_STEPS 收縮
- 四個 StepXxx.tsx (`StepVariables` / `StepSla` / `StepTranslation` / `StepNotes`) **不刪檔**，schema 還在所以 AI tools / 進階抽屜還能用
- `bpm-admin-ui/src/lib/onboardingTools.ts` — `emit_variables` / `emit_sla_config` / `emit_translation_labels` / `emit_notes` tools 保留，AI 還能改

### NOTES sticky button

新元件 `bpm-admin-ui/src/components/notes-sticky/NotesSticky.tsx`：
- 位置：AI Kitchen 頁面**右上角** floating button（topbar 右側、Alice 頭像旁的位置；不擋 footer）
- 點開 → 浮窗 textarea 收 `draft.notes`（沿用 `NoteEditorModal` 樣式）
- icon: `<StickyNote />`，有內容時加紅點

---

## 3. Action schema (④)

### kind enum (7 個)

```ts
type TaskActionKind =
  | 'submit'       // userTask → 推進
  | 'save_draft'   // userTask → 不推進，回 inbox
  | 'approve'      // approval → 走預設成功 edge
  | 'reject'       // approval → target 是 endEvent = 永久駁回；target 是 userTask = 退回補件
  | 'complete'     // 末端 userTask 結案
  | 'cancel'       // 棄案
  | 'custom'       // 自由命名 + targetEdgeId 必填

interface TaskAction {
  id: string
  kind: TaskActionKind
  /** 至少一個語系有值即可（不強制雙語）。zh-TW 為主、en 可選。 */
  label: { 'zh-TW'?: string; en?: string }
  /** 多 outgoing edge 時必填，單 outgoing 可省略 (chef 自動推) */
  targetEdgeId?: string
  /** CEL — 不滿足時按鈕 disable */
  guard?: string
  /** 棄案 / 駁回 之類需 confirm modal */
  confirm?: boolean
  /** 點擊後 modal 收 comment 再送 */
  promptComment?: boolean
}
```

> **與 §plan-admin-form-fixes.md 差異**：原本有 8 種 kind，本版**移掉 `request_changes`**，其語意改由 `reject` + `targetEdgeId` 推（target endEvent = 終結；target userTask = 退回）。一個 enum 少一個概念，UI 入口少一格 dropdown。

### Schema 掛點

- `UserTask` 多 `actions: TaskAction[]`
- `Approval` 多 `actions: TaskAction[]`

### migrateDraft 預設

- userTask 缺 actions → 補 `[{ kind: 'submit', label: { 'zh-TW': '送出' } }]`
- approval 缺 actions → 補 `[{ kind: 'approve', label: { 'zh-TW': '核准' } }, { kind: 'reject', label: { 'zh-TW': '駁回 / 退回' } }]`

### Validator 連動

- `validators.forms` 加：每個 userTask `actions.length >= 1` 且每個 action 至少有一個語系 label
- `validators.approvers` 加：每個 approval `actions.length >= 1`，且至少要有一個 `approve` kind 與一個 `reject` kind
- 多 outgoing edge 時，所有非 default edge 必須被 `targetEdgeId` 覆蓋（提示哪條 edge 還沒對應 action）

### UI 卡片

#### FORMS step
在 `StepForms.tsx` 的 Layout 區塊**下面**新增「Actions」區塊，每張卡：
- kind dropdown（submit / save_draft / complete / cancel / custom）
- label (zh-TW + en optional)
- targetEdgeId 下拉（過濾此 task 的 outgoing edge；單 outgoing 顯示 「自動: e1 → endEvent」）
- guard CEL（用既有 `<ExpressionEditorModal>`）
- confirm checkbox
- promptComment checkbox
- 預設按鈕：「依節點類型補預設」一發

#### APPROVERS step
在 `StepApprovers.tsx` 每個 approval 編輯區下面同樣加 Actions 區塊，kind 限於 approve / reject / custom。

reject 的 UI 提示：根據 targetEdgeId 自動 hint「此 reject = 永久駁回」or「此 reject = 退回補件」。

---

## 4. FormPreviewModal 動態按鈕

`bpm-admin-ui/src/components/form-preview/FormPreviewModal.tsx:49-51` 寫死的「送出（預覽不可點）」改成：

```tsx
{task.actions?.map(a => (
  <Button
    key={a.id}
    variant={primaryFor(a.kind)}
    onClick={() => { /* 預覽 no-op */ }}
    title="預覽僅顯示樣式"
  >
    {labelOf(a)}
  </Button>
))}
```

`primaryFor(kind)`：submit/approve/complete → primary；reject/cancel → destructive；save_draft → secondary；custom → secondary。

`labelOf(a)`：`a.label['zh-TW'] || a.label.en || a.kind`（兩個語系都沒填就退到 kind 當 fallback）。

按鈕**可點但不觸發任何 onSubmit**，純樣式預覽。

---

## 5. admin sticky footer（Back / Next + validator）

`bpm-admin-ui/src/screens/onboarding/Onboarding.tsx:288-311` 的 footer：
- 從 in-flow 元素改成 `position: sticky; bottom: 0`，加 `border-t`、`bg-card`、`backdrop-blur`
- main 容器加 `pb-20` 預留 footer 高度
- 確保 wizard body 在 footer 之上可獨立滾動，不論 stepper 有沒有展開
- mobile：footer 高度緊湊（不要 wrap）

實作方式建議：footer 改用 `sticky` 而非 `fixed`，避免遮 sidebar / topbar 互動。

---

## 6. bpm sticky footer（per-flow CaseDetail actions）

新增共用元件 `bpm-ui/src/components/ui/action-footer/ActionFooter.tsx`：

```tsx
interface ActionFooterProps {
  actions: Array<{
    id: string
    label: string
    variant?: 'primary' | 'secondary' | 'destructive'
    disabled?: boolean
    onClick: () => void
  }>
  hint?: ReactNode  // 顯示在按鈕左邊的提示文字（譬如 SLA 倒數）
}
```

- 位置：`sticky bottom-0`，main 容器 `pb-24`
- 樣式對齊 admin sticky footer，視覺一致
- 過 `Button` 既有 variant

### chef per-flow 改用

`bpm-ui/src/features/LEAVE/V1/LEAVE_V1_CaseDetail.tsx:213-242` 的 `ApprovalActions` 把 SectionCard 內的 flex 按鈕區拆出來，改用 `<ActionFooter actions={...}/>` mount 在頁面底部。Comment textarea 留在頁中央 SectionCard（user 先寫 comment、底部按鈕送出）。

> chef skill conventions 加一條：「per-flow CaseDetail 的決策按鈕**必須**用 `<ActionFooter>` 而不是 inline 按鈕」

`hint` prop 留著但**本版不接資料**（SLA 倒數等資訊未來真要時再串）。

---

## 7. chef + bpm 同步

### chef/skill/SKILL.md

- §spec 欄位表（line 134-150）：補一節「optional / advanced」標示 VARIABLES / SLA / labels / notes，說明 wizard 不再主動問，缺即用 default
- 新增 §「actions → state machine transitions」：
  - 每個 action.kind → 一個 service method 名稱對照表
    (submit → Submit / approve → ApproveByXxx / reject → RejectByXxx / complete → Complete / cancel → Cancel / save_draft → SaveDraft / custom → 用 label slug)
  - targetEdgeId 決定 state machine transition 寫哪一條
  - reject 的 endEvent vs userTask target 判斷規則
  - guard CEL 在 chef 端轉成 service method 的 precondition check
  - promptComment=true → controller 收 `commentRequired` param

### chef/skill/conventions.md

- 加一條「per-flow CaseDetail：action 按鈕必須走共用 `<ActionFooter>` (lead 提供)」
- LEAVE V1 testbed 改完當 reference

### LEAVE V1 testbed

- `userTasks[0].actions = [{ kind: 'submit', ... }]`
- `userTasks[1].actions = [{ kind: 'complete', ... }]` (HR 備案)
- `approvals[0].actions = [{ approve, reject(target=endEvent) }]`
- chef 重新跑、`LEAVE_V1_LeaveService` state machine 對齊
- `LEAVE_V1_CaseDetail.tsx` 用 `<ActionFooter>`

### bpm-svc

不動。每個 chef-cooked flow 自己的 service / controller 處理 actions，bpm-svc shell（unified inbox / case search / lifecycle）跟 actions 概念解耦。

### bpm-admin-svc

不動。`flow.specJson` 還是 passthrough，多帶 `actions[]` field。bundle builder 也不動（builder 直接複製 spec.json）。

---

## 8. PR 拆法

| PR | 範圍 | 估時 |
|---|---|---|
| **PR-S1** | admin & bpm sticky footer（兩 UI 各一）+ ActionFooter primitive | 1-2h |
| **PR-S2** | admin stepper 11→7（拿 4 step + NOTES sticky button + 不刪 schema / tools） | 2-3h |
| **PR-A1** | userTask actions：schema + migrate + StepForms UI + validator + FormPreviewModal 動態按鈕 | 4-6h |
| **PR-A2** | approval actions：schema + StepApprovers UI + validator + reject 語意推導 | 2-3h |
| **PR-A3** | chef skill docs 補 actions 一節 + LEAVE V1 testbed 對齊 actions + `<ActionFooter>` | 4-6h |
| **PR-A4**（之後）| NotifyTrigger enum → action.id reference | 半天 |

PR-S1 / S2 是純 UX，可獨立先 ship 看效果。PR-A1 / A2 / A3 順序動。

---

## 9. 拍板紀錄（2026-05-28）

| # | 議題 | 決策 |
|---|---|---|
| 1 | NOTES sticky button 位置 | **右上** topbar（Alice 頭像旁） |
| 2 | 進階抽屜 | **本版不做** |
| 3 | `custom` kind | **保留**（escape hatch） |
| 4 | action label 是否強制雙語 | **不強制**，zh-TW / en 至少一個有值即可，兩個都空就 fallback 顯示 kind |
| 5 | FormPreviewModal 按鈕互動 | **可點但 onClick no-op**（純樣式預覽） |
| 6 | bpm sticky footer 的 hint slot | **留 prop 不接資料**（SLA 將來再串） |

---

## 10. 時程估算

- PR-S1 + S2: 半天
- PR-A1 + A2: 1-1.5 天
- PR-A3 (chef + LEAVE V1): 半天-1 天

總計 **2.5-3 天** 可全部 land + LEAVE V1 reference 對齊。
