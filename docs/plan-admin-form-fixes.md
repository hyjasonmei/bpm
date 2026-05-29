# Plan — admin AI Kitchen 四件事

依「修小 bug → schema 設計」順序排，前三個 1-day 內可完，第四個 (Action schema) 需設計拍板再動。

---

## ① Blur bug：`key` 跟著被編輯的 id 跑

**根因**
- `FormLayoutEditor.tsx:683` `<ItemFieldRow key={f.id}>` + `:810` 的 `<input value={field.id} onChange={...id: e.target.value...}>` → 每按一鍵，`field.id` 變、key 變、React unmount/remount → input 失焦。
- `StepForms.tsx:196` `<FieldEditor key={field.id}>` + `:254` 一樣的 pattern。

**修法（最小侵入）**
1. `FormField` 多一個非語意 stable key — 兩條路擇一：
   - (A) 加 `_uid?: string`（非持久化、TS 標 optional、`onChange` 透傳保留），在 `Add Field` / `addItemField` / `migrateDraft` 補上預設值。優點：完全 stable。缺點：要動 schema 與 migration。
   - (B) 用「render-time map」— 在 component 內維持 `WeakMap<FormField, string>` 給 key。優點：不動 schema。缺點：欄位被 `{ ...f, ...patch }` 重建後是新 object，WeakMap 失效。所以**走 (A)**。
2. `_uid` 只活在 client，不送進 bundle。`StepForms.tsx`(`Add Field`) / `FormLayoutEditor.tsx`(`addItemField`) 產 field 時補 `_uid: crypto.randomUUID()`。
3. `migrateDraft` 在 walk userTasks 時，沒 `_uid` 的補一個。
4. 兩處 `<FieldEditor>` / `<ItemFieldRow>` 改用 `key={field._uid ?? field.id}`（fallback 保留是給尚未 migrate 的 in-memory draft）。
5. **驗證**：在 outer ID 輸入框連打 5 個字，焦點不該掉；repeater item ID 同樣測。

**檔案**
- `bpm-admin-ui/src/lib/onboarding.ts` — `FormField` 加 `_uid`、`migrateDraft` 補
- `bpm-admin-ui/src/screens/onboarding/steps/StepForms.tsx` — Add Field 補、key 改
- `bpm-admin-ui/src/components/form-layout-editor/FormLayoutEditor.tsx` — addItemField 補、key 改
- 把 `_uid` 從 bundle payload 過濾掉（搜 emit / save / pack 對應點）

---

## ② Validator 放寬：「至少一個必填」應該包含 repeater

**根因**
- `onboarding.ts:826` `validators.forms` 看 `ut.fields.some(f => f.required)`，repeater 內的 `itemFields` 不在 `ut.fields` 裡。
- `StepForms.tsx:93`/`:138` 的 `taskValid` / 提示文案是同條規則的鏡像。

**修法**
1. 在 `onboarding.ts` 加 helper `hasMeaningfulInput(ut: UserTask): boolean`：
   - 任一 outer field `required` 為 true，**或**
   - layout 內存在 repeater 且 `minCount >= 1`（minCount 預設 1 — 但目前 schema 是 optional，這條要明確：minCount undefined → 視為 0；只有顯式 `minCount >= 1` 才算「必須填」）
   - **或** 任一 repeater 內 `itemFields.some(f => f.required)` — 即「使用者每次加一筆都被迫填某欄」
2. `validators.forms` 改呼叫 helper、訊息也跟著改成「至少要有一個必填欄位或必填 repeater」。
3. `StepForms.tsx:93` `taskValid` 改成同 helper。
4. `StepForms.tsx:84` 那行提示文案、`:138` 的 `缺必填欄位` 也跟著改。
5. **驗證**：建一張只有 repeater (minCount=1) 的表 → 應該過。建一張無任何 required 的表 → 應該擋。

**檔案**
- `bpm-admin-ui/src/lib/onboarding.ts`（helper + validator）
- `bpm-admin-ui/src/screens/onboarding/steps/StepForms.tsx`（pill + 提示文案）

---

## ③ 排序拖曳：引入 `@dnd-kit/sortable`

**現況**
- 沒裝任何 DnD lib。`GripVertical` 圖示純裝飾。

**修法**
1. `npm i @dnd-kit/core @dnd-kit/sortable @dnd-kit/utilities` — 三件套都會用到。
2. 抽一個 `SortableList<T>` primitive（`bpm-admin-ui/src/components/sortable-list/SortableList.tsx`），約 50 行：包 `DndContext` + `SortableContext`，children 走 render-prop 拿到 `attributes` / `listeners` / `setNodeRef`。把 GripVertical 包成 drag handle（`{...listeners}`）。
3. 套到三個層級：
   - **Sections**（`FormLayoutEditor` 第 95–108 行的 `sections.map`）
   - **Section children**（第 236–292 的 `children.map`）— field/row/banner/repeater 混合排序
   - **Repeater itemFields**（第 681–688）
   - **Repeater itemLayout**（第 705–746）
   - **Row cells**（`RowCard` 內 `row.children.map`）
4. 每處需要的 id：fieldRef 用 `${id}-${idx}` (因為同 id 可能重複)、其他 child 有自己 id；itemFields 用 `_uid`（① 已加好）。
5. 重排只調 `children` array 的順序，其他都不動。
6. **驗證**：用 chrome-devtools 跑 demo flow，section / row / repeater 三層都試一次。

**檔案**
- `bpm-admin-ui/package.json` — add deps
- 新檔 `bpm-admin-ui/src/components/sortable-list/SortableList.tsx`
- `bpm-admin-ui/src/components/form-layout-editor/FormLayoutEditor.tsx` — 套五處
- `bpm-admin-ui/src/screens/onboarding/steps/StepForms.tsx` — 套 outer Fields list

**風險**
- 跟 `<select>` / `<input>` 的點擊衝突 — 解法是只在 GripVertical 上掛 `{...listeners}`，輸入框就不受影響。

---

## ④ Action schema — 需要設計拍板再動

這條最大，因為動 schema → 動 validator → 動 chef → 動 FormPreview → 動 generated forms。

### 4.1 設計問題

目前 schema：
- `UserTask` 沒有 action 概念，FormPreviewModal 寫死「送出」按鈕；
- `Approval` 只有 `approver`，approve/reject 是 chef 隱性生的；
- `NotifyTrigger` 有 `on_submit/on_approve/on_reject/on_complete/on_assign/on_sla_breach` — 反向掛勾，等於假設那些 action 已經存在。

### 4.2 提案 schema

```ts
type TaskActionKind =
  | 'submit'           // user task → 推進
  | 'save_draft'       // user task → 不推進，回 inbox
  | 'approve'          // approval → 走預設成功 edge
  | 'reject'           // approval → 走 fail edge（通常退案結束）
  | 'request_changes'  // approval → 走 back edge，退回 submitter
  | 'complete'         // 末端 user task 結案
  | 'cancel'           // 棄案
  | 'custom'           // 自由命名 + 指定 targetEdgeId

interface TaskAction {
  id: string
  kind: TaskActionKind
  label: { 'zh-TW': string; en?: string }
  /** 多 outgoing edge 時必填，單 outgoing 可省略 (chef 自動推) */
  targetEdgeId?: string
  /** CEL — 不滿足時按鈕 disable */
  guard?: string
  /** 退回 / 棄案 之類需確認 */
  confirm?: boolean
  /** 點擊後出現一個 modal 收備註再送出 */
  promptComment?: boolean
}
```

`UserTask` 與 `Approval` 都加 `actions: TaskAction[]`；空陣列時 chef 依 node type 套預設：
- userTask → `[{ kind: 'submit' }]`
- approval → `[{ kind: 'approve' }, { kind: 'reject' }]`

### 4.3 為什麼這樣設計

- **kind 而不只是 label**：chef / runtime / notification 都需要分類別才能掛 side effect（譬如 reject 要清空原表單欄位）。
- **targetEdgeId**：BPMN edge 才是真實路由，避免另開一個「狀態機」概念。多 outgoing 時必填強迫使用者想清楚對應關係，validator 可幫忙。
- **promptComment**：reject / request_changes 幾乎一定要收備註，與其 chef 自己決定，不如顯式宣告。
- **NotifyTrigger 改用 action.id**（後續一步）：目前 `on_approve` 只對應「approval 節點的 approve action」，未來若一個 approval 有多種 approve 行為（譬如「核准且發 PO」vs「核准但不發 PO」），用 action.id 才能精準掛通知。但本 PR 暫不動 NotifyTrigger 結構，先加 actions 欄位，相容期 NotifyTrigger 保留現有 enum。

### 4.4 UI 入口

`StepForms.tsx` 在「Layout」之後加一個 `Actions` 區塊：
- 卡片列表，每張卡：kind dropdown / label / targetEdgeId（多 outgoing 才顯示）/ guard CEL / confirm checkbox / promptComment checkbox。
- 旁邊顯示「此 task 的 outgoing edges」清單給使用者參考（節點 id → 目標 id + 條件）。
- 預設按鈕：依 node type 填一發 default action。

Approval 的 actions 要不要也放 StepForms？目前 StepForms 只列 userTask；建議在 `StepApprovers.tsx` 多開一格 actions（小範圍），維持「每個 step 對應一個關注點」的 wizard 心智。

### 4.5 連動

- `validators.forms` 與新的 `validators.approvers` 都要檢查：
  - actions 至少一個
  - 多 outgoing edge 時，所有 actions 的 `targetEdgeId` 要覆蓋住所有非 default edge
- `FormPreviewModal` 把寫死的「送出」換成 `task.actions.map(...)` 渲染。
- `migrateDraft` 對舊 draft 補 default actions（kind 依 node type 推）。
- chef skill 文件（`chef/skill/conventions.md`、`chef/skill/SKILL.md`）要加一節說明 actions → React buttons / state machine transitions / inbox provider 的對應；以及 reference cook (LEAVE V1) 怎麼用 actions。

### 4.6 拆 PR 建議

- **PR-A1**: schema + migration + StepForms UI（user task only）+ validator + FormPreviewModal — 純 admin-ui 內。
- **PR-A2**: Approval 也加 actions + StepApprovers UI + validator — 仍在 admin-ui。
- **PR-A3**: chef skill 文件更新 + LEAVE V1 testbed 改用 actions，產出 reference baseline。
- **PR-A4**（之後再說）: NotifyTrigger 從 hardcoded enum 換成 action.id reference。

### 4.7 需要拍板的決定

1. **kind 列表是否就這 8 個？** 漏了什麼？`request_changes` 與 `reject` 是否真的分開，還是合併成「退回」由 fallback 邏輯處理？
2. **Approval 是否真的不該收 user input？** 若加 approver 寫備註 / 加附件，等於 approval 變成「有 actions + minimal fields 的 user task」，schema 可以收斂為一個 node type — 但這牽動更大，本 plan 暫不動。
3. **第 4 件先停在設計，先把 ①②③ 修掉再說？** 還是 4 件一氣呵成？

---

## 時程估算

| 編號 | 內容 | 工時 |
|---|---|---|
| ① blur bug | 改 `_uid` + key | 20 min |
| ② validator 放寬 | helper + 文案 | 20 min |
| ③ DnD | dnd-kit + 5 處 sortable | 2–3 h |
| ④ actions | schema + UI + chef 文件 + LEAVE 對齊 | 1–2 天 |
