# AI Kitchen Demo 腳本 — 「左邊聊天 → 右邊即時更新 → 產出 bundle」

> 周五 demo 主秀。情境設計成「7 步都用得到」、每步配一句可直接貼上的 prompt，
> 驗證會過、最後能 GO LIVE 打包 bundle。

## ✅ 本機彩排已驗證（2026-06-10，用 cli backend 免費跑）

整套照這腳本實跑過一遍，**全綠**：5 個 AI 步驟（SOURCE / FORMS / DECISIONS / APPROVERS / NOTIFY）每步左邊聊天都即時反映到右邊 canvas，AI 每次回「✓ 已套用到右邊 canvas」；最後 `Download bundle` 回 **HTTP 200 + `TRAINING_v1.zip`（10 KB，application/zip）**。產出的 BPMN 正確：start → 填單 → 主管審核 → `gateway_cost`（`cost > 10000` / `cost <= 10000`）→ 部門經理 / HR → end。

彩排抓到的幾個要點（已併入下方步驟）：
1. **flowCode / 流程名在「Cook new flow」那個 modal 先填**（FLOW CODE=TRAINING、DISPLAY NAME=教育訓練申請），不是在 SOURCE prompt 講。
2. **聊天框要真實打字**（React controlled input）—— 你手打沒問題，貼上後按送出鍵或 Enter 即可。
3. **ACCESS**：點「加 principal」→ 切「Dept」分頁 → 選 **Acme Corp**（全公司＝全體員工）→ Select。
4. **送出鈕文字**：FORMS 的 emit_form_fields 工具**不會改 action 的 label**（仍顯示「送出」），AI 會把「送出申請」寫成給 chef 的 note。若 demo 想當場讓按鈕顯示「送出申請」，到右邊 Actions 區的 LABEL 欄手動改即可（非必要）。
5. AI 很聰明：FORMS 設「訓練費用」時，AI 主動標註「此值會用於閘道條件 cost > 10000」，把欄位和 gateway 綁起來。

## ⚠️ 先搞清楚:7 步裡哪幾步「聊天會更新右邊」

AI Kitchen 目前**可見 7 步**,但只有 **5 步掛了 AI 工具**(左聊天→右畫布即時更新):

| # | 步驟 | 有 AI tool? | 聊天觸發的工具 | demo 怎麼演 |
|---|---|---|---|---|
| 1 | **SOURCE 來源** | ✅ | `emit_flow_skeleton` | 用一句話描述整個流程 → 右邊**自動畫出 BPMN 骨架** |
| 2 | **FORMS 表單** | ✅ | `emit_form_fields` | 描述要哪些欄位 → 右邊**表單預覽即時長出欄位** |
| 3 | **ACCESS 存取** | ❌ 手動 | （無） | 點 picker 選「誰可啟動」,口頭帶過 |
| 4 | **DECISIONS 決策** | ✅ | `emit_decision_rules` | 描述 gateway 分流條件 → 右邊**分支條件填上** |
| 5 | **APPROVERS 審核者** | ✅ | `emit_approver_config` | 描述每關審核者 → 右邊**審核者規則 + 按鈕填上** |
| 6 | **NOTIFY 通知** | ✅ | `emit_notifications` | 描述何時通知誰 → 右邊**通知清單長出** |
| 7 | **INTEGRATIONS 整合** | ❌ 手動 | （無） | 純內部流程 → 留空,口頭帶過 |

👉 demo 節奏:**聊天主秀放在 SOURCE / FORMS**(視覺衝擊最大),DECISIONS / APPROVERS / NOTIFY 各示範一句,ACCESS / INTEGRATIONS 用講的快速帶過。

---

## 情境:教育訓練申請（flowCode: TRAINING）

**對觀眾的一句話故事**:
> 員工想報名外部研習課程,線上填單、主管核准;**訓練費超過 1 萬要再加一關部門經理**;核准後自動通知 HR 登記、通知員工結果。

挑這個情境的理由:天生有一個 **金額 gateway**(展示 DECISIONS)+ **兩關審核**(展示 APPROVERS 的不同 actor)+ **多個通知**(展示 NOTIFY),五個 AI 步驟全用得到,又是每家公司都懂的場景。

> 想換產業?把「教育訓練」換成對方熟的場景即可(設備借用、樣品申請、用印申請…),只要保留「一個金額/類別 gateway + 兩關審核」這個骨架,五步就都演得到。

---

## 逐步 prompt（可直接複製貼到左邊聊天）

### Step 1 — SOURCE 來源
> 先確認在 SOURCE 步驟,左下聊天框輸入:

```
幫我設計一個「教育訓練申請」流程，flowCode 用 TRAINING。
流程是：員工填寫訓練申請表送出 → 直屬主管審核 → 系統判斷訓練費用是否超過 10000 元
→ 超過就再給部門經理審核、沒超過就直接通過 → 最後 HR 登記訓練紀錄 → 結束。
```
**右邊會發生**:BPMN 骨架自動畫出 —— 起點 → 填單(userTask) → 主管審核(approval) → gateway(費用判斷) → 部門經理審核(approval) → HR 登記(userTask) → 終點。
> 若骨架不完美,補一句:「把『部門經理審核』放在 gateway『超過一萬』那條分支上」。

### Step 2 — FORMS 表單
> 先在右邊點選「員工填寫訓練申請」那個 user task,再到左邊聊天:

```
這張訓練申請表單要這些欄位：
課程名稱（必填文字）、主辦單位（文字）、訓練期間（日期區間，必填）、
訓練類型（下拉：內部訓練 / 外部訓練）、訓練費用（數字，必填，新台幣）、
申請理由（多行文字，必填）、報名簡章（檔案上傳）。
送出按鈕叫「送出申請」。
```
**右邊會發生**:表單預覽即時長出這 7 個欄位 + 「送出申請」按鈕。
> （可選）再點「HR 登記」那張 task,聊天:「這張只要一個『登記備註』多行文字欄位,按鈕叫『完成登記』。」

### Step 3 — ACCESS 存取（手動，無 AI）
點「指定可啟動者」picker → 選 **全體員工**(或某部門/角色)。
口播:「這裡決定**誰能發起**這個流程 —— 我選全體員工。」

### Step 4 — DECISIONS 決策
```
那個費用判斷的 gateway：當「訓練費用」大於 10000 就走「需部門經理加簽」這條，
否則走「直接通過」。
```
**右邊會發生**:gateway 兩條分支條件填上(`費用 > 10000` / 預設)。

### Step 5 — APPROVERS 審核者
```
審核者設定：
第一關「主管審核」給申請人的直屬主管；
第二關「部門經理審核」給申請人所屬部門的主管。
兩關都要有「核准」和「退回」兩個按鈕。
```
**右邊會發生**:approver 規則填上(`submitter.manager` / `submitter.department.head`),每關 actions = 核准 + 退回。

### Step 6 — NOTIFY 通知
```
通知設定：
流程最後核准完成時，寄 email 通知申請人「訓練申請已核准」，同時通知 HR 這筆要登記；
被退回時，通知申請人退回原因。
```
**右邊會發生**:通知清單長出 3 筆(完成→申請人、完成→HR、退回→申請人)。

### Step 7 — INTEGRATIONS 整合（手動，可空）
口播:「這是純內部流程,不需要串外部系統,留空。」→ 直接往 GO LIVE。

### GO LIVE → 產出 bundle
驗證全綠後按 **GO LIVE**,打包成 Flow Library bundle (zip)。

---

## 驗證會過的檢查點（事先確認，避免 demo 卡關）

bundle 能打包,每步 validator 要綠:
- **SOURCE**: 有 flowName + flowCode + 起點/終點 ✓（prompt 已給）
- **FORMS**: 每個 user task 至少 1 個必填欄位 + 至少 1 個按鈕 ✓
- **ACCESS**: `launchableBy` 不可空 → **記得手動選可啟動者**（最容易忘）
- **DECISIONS**: 每個 gateway 都要有規則 ✓
- **APPROVERS**: 每個 approval 要有 approver + approve/reject 按鈕 ✓
- **NOTIFY / INTEGRATIONS**: 可空,不擋

approver 路徑 `submitter.manager` / `submitter.department.head` / `role:HR` 都能在內建 sample-org 解出來(4 人 1 部門:員工/主管/VP/HR),所以 bundle 驗證會過。

---

## 本機免費彩排（用 CLI backend，不燒 API key）

admin-svc **預設就是 cli backend**(`BPM_AI_BACKEND` 不設 = cli),會借你本機 Claude Code 訂閱額度跑,**不花 API 錢**。

1. 確認 `claude` CLI 已登入(`claude` 能跑就行)
2. 起後端:`cd bpm-admin-svc/src/Bpm.Admin.Api && dotnet run --launch-profile http`（:5266，log 會印 `AI backend: cli`）
3. 起前端:`cd bpm-admin-ui && npm run dev`（:5174）
4. 開 localhost:5174 → 登入(jack@acme.example / flowcook2026)→ AI Kitchen → Cook new flow → 照上面 7 步跑一遍
5. 確認每步右邊有更新、最後能 GO LIVE 出 bundle

> 注意:cli backend **不支援圖片輸入**(SOURCE 上傳流程圖那種),純文字描述的這套腳本不受影響。雲端(Azure)才是 api backend(燒 key),所以**彩排在本機跑、正式 demo 再上雲**最省。
