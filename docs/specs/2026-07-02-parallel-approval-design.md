# 並簽 / 平行簽核（Parallel Approval）設計 spec

- 日期：2026-07-02
- 狀態：草案（待 Jason review）
- 範圍：全系統支援「同一關卡多人並行簽核」，走真正的 BPMN 平行 gateway，守 Model B（每流程手寫，不做通用執行引擎）

## 1. 問題

flowcook 目前**完全不支援**「同一關多人並行簽核」：

- **設計層**：AI Kitchen 的 gateway 只有 `exclusive`（決策分支，if 金額>50K 走 A），沒有 parallel(AND) 的 fork/join。
- **codegen 層**：16 隻已 cook 的流程全是「單人循序」（一個 `CurrentAssignee` 一關一關走），chef 沒有 fork/join 參考範本。
- **runtime 層**：inbox、狀態機、BPMN 高亮都只處理「一個當前節點 / 一個簽核人」。

這正是 demo 反饋裡「多人簽核支援度沒那麼高」的缺口。這是產品能力，要全系統補齊。

## 2. 目標 / 非目標

**v1 目標**
- 支援「一層」平行 gateway：`fork → N 個簽核 branch → join`。
- Join 政策：門檻 M/N（`AND = N/N`、`OR = 1/N` 都是特例）。
- 退件：任一 branch 退件 → 整關立刻退（AND 與門檻一致）。
- 同一案件同時進 N 位簽核人的 inbox；達標/退件後自動收掉其餘待辦。
- 前台 BPMN：多節點同時亮、依狀態換色；案件詳情顯示簽核清單。
- chef 能照參考範本 cook 出帶平行 gateway 的流程。
- AI Kitchen 能設計平行 gateway。

**非目標（v1 不做）**
- 巢狀平行（平行裡再平行）。
- 不同 branch 做「不同任務」（例：法務填表 A、財務填表 B 再 join）。v1 每個 branch 就是一個簽核 task。
- 「容忍部分退件的門檻」（門檻只要還可能達標就繼續）——v1 一律任一退即退，之後要再加。
- 通用 workflow 執行引擎（Model A 已證實失敗，不重蹈）。

## 3. 核心建模決定

**走真正的 BPMN 平行 gateway，但守 Model B。**

- 為何不是「一個 approval 節點多簽核人」：那樣 BPMN 上只會是一個框，亮不出「多節點同時亮」——直接違背需求；且不是 BPMN 標準。
- 為何不怕 Model A 的坑：Model A 是「一個通用引擎解釋任意 spec」。我們不做那個。每隻流程仍**手寫**自己的 fork/join 狀態追蹤，只是共用一個**單一職責**的「平行簽核」primitive（見 §5），不是通用 token 引擎。

## 4. Join / 退件語意

- **門檻 M/N**：累積到 M 個 branch 核准即達標，join 通過、往下一關。
  - `AND（全簽）` = 門檻 N/N。
  - `OR（任一）` = 門檻 1/N。
- **達標後**：其餘未簽 branch → `Skipped`，那些人的 inbox 待辦自動收掉。
- **退件**：任一 branch 退件 → 整關立刻 `Rejected`，其餘 branch → `Skipped`。
- 語意含義：門檻模式下「一個否決蓋過多數」。對「任一人可否決」的簽核是合理預設；日後可加「容忍退件的門檻」變體。

## 5. 資料模型（關鍵：shared primitive vs per-flow）

**建議：一個單一職責的共用 primitive（lead territory），而非每流程各寫一套。**

理由：讓「N 個 slot + join 判斷 + inbox 查詢」統一、把 chef 的錯誤面積降到最低；同時它只做「一關平行簽核」這件事，不是通用引擎，不違反 Model B 精神。

### 5.1 Shared 資料表（lead）
`ParallelApprovalGroup`（一個平行關卡一列）
- `Id`, `FlowCode`, `CaseId`, `GatewayNodeId`（spec 裡 fork gateway 的 node id）
- `Policy`：`Threshold`（int，M）, `TotalSlots`（int，N）
- `Status`：`Open` / `Approved` / `Rejected`
- `OpenedAt`, `ResolvedAt`

`ParallelApprovalSlot`（一個 branch = 一列，可查詢供 inbox 用）
- `Id`, `GroupId`
- `NodeId`（該 branch 的 user-task node id，供 BPMN 高亮）
- `AssigneeRoleCode`（string?）或 `AssigneeUserId`（Guid?）——沿用現有 shared-role-queue：填 RoleCode 則「該角色任一持有人可簽」
- `Decision`：`Pending` / `Approved` / `Rejected` / `Skipped`
- `Comment`（string?）, `DecisionByUserId`（Guid?）, `DecisionAt`（DateTime?）

> 註：這兩張表是「runtime」共用表，放 bpm-svc 端（非 admin identity）。若採 SharedIdentity 那種 admin 擁有的模式不適用——這是 bpm-svc 自己的執行資料。EF entity + configuration + migration 由 lead 寫。

### 5.2 Service（lead）
`IParallelApprovalService`
- `OpenAsync(flowCode, caseId, gatewayNodeId, slots[], threshold, ct)` → 建 group + N slots(Pending)。
- `DecideAsync(slotId, userId, approve, comment, ct)` → 更新 slot；重算 group：
  - 若有任一 `Rejected` → group `Rejected`、其餘 Pending → `Skipped`。
  - 否則若 `Approved` 數 ≥ threshold → group `Approved`、其餘 Pending → `Skipped`。
  - 回傳 group 結果（未定/通過/退件），呼叫端（flow 狀態機）據此推進自己的 Status。
- `GetGroupAsync(caseId, gatewayNodeId, ct)` → 供案件詳情/BPMN 顯示。
- 授權：沿用現有 `IActorAuthorizer`（含角色感知 + delegation）判斷這個 user 能不能簽這個 slot。

### 5.3 每流程手寫的部分（chef）
- flow 的 `Case` 狀態機加一個平行關卡狀態（例 `Status = PendingParallelReview`）。
- 進入該關時呼叫 `OpenAsync(...)`，slots 從 spec 的平行 branch 定義來。
- 各 branch 的簽核 API（controller action）呼叫 `DecideAsync`，拿回 group 結果：`Approved` → 推進到下一關；`Rejected` → 該 case `Rejected`。
- inbox provider：查 `ParallelApprovalSlot` 裡 `Decision=Pending` 且 assignee 命中本 user/角色 的 group，對應回自己的 case → 出現在該 user inbox。

## 6. Inbox（同一案進 N 人）

- 現有 inbox 是 per-flow provider 聚合。平行關卡時，一個 case 會對應「多個 Pending slot」；每個 slot 的 assignee 都要在自己的 inbox 看到這個 case。
- 作法：flow 的 inbox provider 除了原本循序邏輯，另查「本流程、本 user（或其角色）命中的 Pending slot」→ 帶出對應 case。達標/退件後 slot 轉 Skipped，就自動不再出現（不需額外收）。
- 沿用 shared-role-queue：slot 填 `AssigneeRoleCode` 時，該角色任一持有人可簽（且 delegation 代理人也可）。

## 7. 前台 BPMN + 案件詳情

（已於 mockup 對齊，Jason 已確認視覺方向）

**BPMN 高亮**
- `BpmnView`：`currentNode: string | null` → 擴成 `currentNodes: string[]`（`addMarker` 迴圈已支援多個）。
- 新增節點狀態 class：`bpm-completed`(綠)、`bpm-active`(黃/待簽)、`bpm-rejected`(紅)、`bpm-skipped`(灰/虛線)。
- 案件詳情計算每個 slot 的 nodeId → 狀態，餵給 `currentNodes` / completed / rejected / skipped 各集合。

**節點燈態**
- 🟡待簽（Pending）→ 🟢已核准（Approved）/ 🔴已退件（Rejected）/ ⚪無需·略過（Skipped）
- 情境1 全簽：N 個同時 🟡 → 逐一 🟢 → N/N 才 join。
- 情境2 門檻 M/N：達 M 個 🟢 即通過，其餘 ⚪。
- 情境3 任一退：退件者 🔴、其餘 ⚪、整關退件。

**案件詳情**
- 並簽區塊：政策（「需全部核准」/「門檻 M/N」）、進度條、每位簽核人一列（角色 / 姓名 / 狀態 / 意見 / 時間），與 BPMN 同步。

## 8. spec.json schema 變更（P2）

- gateway node 增加 `kind: 'exclusive' | 'parallel'`（現有 Decision 已有 `type` 欄位，延伸）。
- parallel gateway：需要對應的 join node（或以 `gatewayRef` 標示成對）。
- 每條 fork 出的 branch 對應一個 approval user-task（帶 assignee：role 或 user）。
- group 帶 `joinPolicy: { threshold: number }`（threshold=N 即 AND、=1 即 OR）。
- spec-extract 的 AI prompt 補：能從自然語言（「財務跟法務要一起簽」「五個裡面三個簽就過」）產出 parallel gateway + threshold。

## 9. 分階段計畫

### P1 — runtime + 參考 cook（先做，最高價值）
Lead：
- `ParallelApprovalGroup` / `Slot` entity + configuration + migration（bpm-svc）。
- `IParallelApprovalService` + impl（Persistence）+ 授權接 `IActorAuthorizer`。
- `BpmnView` 多節點高亮（`currentNodes[]` + 4 種狀態 class + CSS）。
- 案件詳情「並簽區塊」共用元件（bpm-ui）。
- inbox 共用 helper：依 Pending slot 帶出 case。

Chef 邊界內（這隻 demo 流程本身，當作參考範本）：
- 一隻 demo 流程（建議：**合約審查 `CONTRACT_REVIEW`**，法務 + 財務 兩方並簽，全簽才過；或沿用採購案例）——Domain/Application/Persistence/Api/UI。
- 該流程的 fork/join 狀態機 + inbox provider + case-detail + `.bpmn.xml`（含平行 gateway）。

產出：程式邏輯 + BPMN 多節點高亮 + 可 demo + 證明 runtime。

### P2 — chef codegen 支援
- spec.json approval/gateway schema 加 parallel（§8）。
- chef skill（SKILL.md / conventions.md / workflow.md）補「平行簽核」參考範本段落，指向 P1 的 demo 流程當範例。
- 之後任何流程 spec 有平行 gateway，chef 照抄即可 cook。

### P3 — AI Kitchen 設計支援
- onboarding wizard：能把某簽核關設成平行 gateway（加 N 個簽核人 + 設門檻）。
- spec-extract prompt 認得自然語言的並簽。
- BPMN 建模器能畫平行 gateway（若 modeler 需擴充）。

**順序**：P1 → P2 → P3。P1 先把技術風險打掉並交出 demo。

## 10. 觸及範圍

- P1：`bpm-svc`（Domain/Application/Persistence/Api：平行簽核 primitive + demo flow）、`bpm-ui`（BpmnView / 案件詳情 / inbox）
- P2：spec schema、`chef/skill/*`、`bpm-admin-svc` spec-extract prompt
- P3：`bpm-admin-ui` onboarding、`bpm-admin-svc` AI prompt

## 11. 測試

- 單元：`ParallelApprovalService` 的 join 判斷（門檻達標、未達標、任一退、全 Skipped 轉換）。
- 整合（in-memory SQLite，比照現有）：開 group → 多人 decide → 達標推進 / 退件退回；inbox 查詢命中/收掉。
- 授權：非該角色 / 已簽過 / 代理人 各情境（接 `IActorAuthorizer`）。
- E2E（smoke + chrome）：demo 流程走一遍，BPMN 多節點亮、達標前進、退件退回。
- 雲端 Postgres 實測（比照 SQLite/Postgres 坑的教訓）。

## 12. 已定 / 待確認

**已定**（Jason 已拍板）
- 真 BPMN 平行 gateway + Model B。
- 門檻 M/N 語意；任一退即退。
- 達標後剩餘轉 Skipped + 收 inbox。
- BPMN 多節點 + 案件詳情兩邊都顯示；視覺方向已確認。
- chef（P2）、AI Kitchen（P3）都在範圍內。

**待 Jason 確認**
1. 資料模型走「共用 primitive（§5，推薦）」還是「每流程各自手寫 slot」？
2. P1 的 demo 參考流程：新開一隻 `CONTRACT_REVIEW`（法務+財務並簽，推薦，乾淨的參考範本）還是擴充現有 `PURCHASE_REQUEST`？
3. 平行簽核的兩張 runtime 表放 bpm-svc（§5.1 註）確認 OK？
