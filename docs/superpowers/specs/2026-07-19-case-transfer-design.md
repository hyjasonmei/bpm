# Case Transfer（轉簽）— Design

2026-07-19。競品缺口路線圖（`docs/plan-competitive-gap-roadmap.md` §2.1）
第一項。TG 定案：

- **只有當前簽核人本人**（含其已接受的代理人）能轉自己手上的單。
  管理員救火走既有 Process Doctor，前台不重複。
- **角色共享佇列關卡不開放轉簽**——整組都能簽，沒有「不歸我管」
  的問題。轉簽按鈕只在「個人指派」關卡出現。
- 轉給任何在職使用者（不能轉給自己）；理由必填；通知新簽核人
  與申請人。

## Non-goals

- 前 / 後加簽、平行加簽（路線圖 §2.4 另案）
- 管理員前台代轉（Doctor 已涵蓋）
- 角色佇列關卡轉簽
- case detail 顯示轉簽歷程 timeline（log 先落 DB，UI 呈現 v2）

## 設計總覽

方案一（TG 核准）：**lead 共用 primitive + guard 統一化**。
一個共用 `CaseTransferService` + 一個通用端點 + CaseDetail 一顆
共用按鈕，14 隻流程一次生效；chef conventions 補一條讓新 cook
天生支援。

### 1. Guard 統一化（前置，行為不變）

現況：各 flow service 的關卡授權混用兩種寫法——

- `CanActAsync(mgr, actorUserId)`：對 stage 專屬欄位
  （`c.ManagerUserId` 等）判斷。**26 處**（含 WFH V1–V6 舊版）。
- `CanActAsync(c.CurrentAssigneeUserId, c.CurrentAssigneeRoleCode,
  actorUserId)`：role-aware，對當前指派判斷。

已查證各流程在每次狀態轉移都同步維護 `CurrentAssigneeUserId`
（stage 欄位與其同值），所以把前者機械替換為後者**行為不變**，
並讓「改 CurrentAssigneeUserId」成為唯一正確的改派手段。

副作用（good）：修掉 Process Doctor 既有 bug——Doctor reassign 只
改 `CurrentAssigneeUserId`，對讀 stage 欄位的關卡（如 LEAVE
manager）轉了也不能簽；統一化後 Doctor reassign 真正生效。

替換前逐關卡確認 stage 欄位與 `CurrentAssigneeUserId` 同步設定
（實作時逐流程檢查 submit / 各 decision 的賦值）；既有 432 隻
單元測試全綠為回歸底線。

### 2. `CaseTransferService`（lead，共用 primitive）

- 介面 `ICaseTransferService` 在 **Application**
  （`Application/Common/Transfer/`）；實作在 **Persistence**
  （需要反射掃 case tables——沿用 / 抽出 DoctorService 的
  `CaseTables()` 反射基礎）。
- **不用 raw SQL**（root CLAUDE.md DB 規則 #1；Doctor 的 raw
  UPDATE 是它自己的歷史包袱）：以 `CaseTables()` 拿到 CLR type →
  `db.FindAsync(clrType, caseId)` → 反射讀欄位驗證 → 反射寫
  `CurrentAssigneeUserId` / `LastActivityAt` → `SaveChangesAsync`。
  SQLite / Postgres 皆安全。
- 驗證規則（依序，全部過才轉）：
  1. flowCode 存在於 case tables（否則 `unknown_flow`）
  2. case 存在且 `CompletedAt == null`（否則 `not_found_or_closed`）
  3. `CurrentAssigneeRoleCode == null`（角色佇列關卡 →
     `role_stage_not_transferable`）
  4. `auth.CanActAsync(CurrentAssigneeUserId, null, actorUserId)`
     （本人或已接受代理人；否則 `not_current_assignee`）
  5. 目標使用者存在、`Active`、未刪除（`target_not_active`）
  6. 目標 ≠ 現任簽核人（`target_is_current`）
  7. `reason` trim 後非空（`reason_required`）
- 成功後：
  - 寫 `CaseTransferLog`（新表 + migration）：Id / FlowCode /
    CaseId / FromUserId / ToUserId / OperatorUserId（=actor，可能
    是代理人）/ Reason / CreatedAt。**不共用 DoctorActionLog**——
    管理員救火與使用者自助要分開稽核。
  - 通知（走既有 `INotifyDispatcher`，in-app + email 由既有
    dispatcher 鏈處理，sandbox capture 自動生效）：
    - 新簽核人：「有一張〈流程名〉案件轉簽給您」+ case deep link
    - 申請人：「您的案件已由 A 轉簽給 B」

### 3. API（lead）

- `POST /api/case-transfer/{flowCode}/{caseId}`
  body `{ toUserId, reason }` → 200 `{ ok }` / 4xx `{ error }`
  （error code 對應上面驗證規則）
- `GET /api/case-transfer/candidates?q=` → 在職使用者搜尋
  （DisplayName / Email like，Take 20）——與 Doctor candidates
  同 query 形狀，但**獨立端點**（Doctor 的掛在 admin 授權下）。
  一般登入者即可呼叫。

### 4. UI（lead 共用元件 + 各 CaseDetail 兩行接線）

- `components/TransferButton.tsx`（或 hook `useTransferAction`）：
  輸出一個 `ActionFooterItem`，只在
  `canAct && currentAssigneeRoleCode == null && case open` 時出現
- Modal 遵守 ActionFooter styled-confirm-modal 慣例
  （禁 `window.confirm`）：選人器（搜尋 candidates、顯示
  DisplayName + email）+ 必填理由 textarea + 確認
- 成功後 invalidate case query + toast；因指派已變，footer 動作
  自然消失
- 各 flow 的 `<CODE>_V<N>_CaseDetail.tsx` 在 `footerActions` 加一
  行接線（機械改，含 WFH 多版本）

### 5. chef conventions（`chef/skill/conventions.md`）

補「Case transfer」段：

- 關卡授權 guard 一律
  `CanActAsync(c.CurrentAssigneeUserId, c.CurrentAssigneeRoleCode, actorUserId)`
- 每次狀態轉移必同步 `CurrentAssigneeUserId` / `CurrentAssigneeRoleCode`
- CaseDetail `footerActions` 接 transfer 一行（附 snippet）

### 6. 測試

- `CaseTransferServiceTests`（新）：七條驗證規則各一 + 成功案例
  （斷言 assignee 變更、log 落表、通知發出）
- 一條端到端流程整合測試（用 OVERTIME：submit → manager 轉簽給
  另一人 → 原 manager 再動作 403 → 新簽核人 approve 成功走完）
- Guard 統一化回歸：既有測試全綠（432+）
- 前端：tsc `-p tsconfig.app.json` + 本機 boot 實測
  （persona 切換驗證按鈕出現/隱藏條件）

## 風險與緩解

- **stage 欄位與 CurrentAssignee 不同步的漏網關卡**：實作時逐
  流程過一遍賦值點；發現不同步就補賦值（屬 bug fix）。
- **舊版 WFH V1–V5 有無 open cases**：guard 替換一併做（機械），
  測試靠既有 per-flow 測試涵蓋。
- **代理人可轉簽**：規則 4 沿用 CanActAsync 語意（代理人可動作
  即可轉）。log 的 OperatorUserId 記實際動作者，可稽核。
