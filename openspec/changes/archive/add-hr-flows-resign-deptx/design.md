# Design — add-hr-flows-resign-deptx

## 1. 為什麼不等 add-process-runtime 做完才做這個

夥伴下個月要 demo，但 process-runtime 是大工程（snapshot、actor resolver、delegation、CEL gateway、TaskHistory append-only 等）短期內做不完。POC 的價值在「給客戶看到完整流程跑起來」，所以選擇：

- 寫一個只支援 `RESIGN` + `DEPTX` 兩個寫死 specCode 的最小後端
- 用同樣的 status / step / action 概念，但不要可配置性
- 在 spec 明文寫 sunset 條款，等 process-runtime 上線即遷移

成本：未來會有一次性遷移工作（swap `IHrFlowService` → `IProcessRuntime`，DB 資料 backfill 至 `ProcessInstance` / `ProcessTask` / `TaskHistory`）。值得，因為換來幾週後就能 demo。

## 2. 為什麼後端用 status enum 而不是節點圖

兩個流程都是直線 3 步、沒分支、沒回圈、沒 gateway。用 `Status enum` + `CurrentStep enum` 寫死狀態機可以一眼看懂；硬套 BPMN node graph 是過度工程。等 process-runtime 接手才會用 graph。

狀態轉換表：

| Current Status | Action | Actor | New Status | New Step |
|---|---|---|---|---|
| (none) | Start | Initiator | PendingManager | ManagerApprove |
| PendingManager | Approve | Resolved manager | PendingHr | HrApprove |
| PendingManager | Return | Resolved manager | Returned | Apply |
| PendingHr | Approve | Any HR user | Completed | Closed |
| Returned | Resubmit | Initiator | PendingManager | ManagerApprove |
| (any non-Completed) | Cancel | Initiator | Cancelled | (unchanged) |

任何其他組合（HR 想 Return、Manager 想跳到 HR、別人想 Approve）→ 拋 `ForbiddenException` 或 `InvalidOperationException`。

## 3. Manager 解析時機 — at start vs at action

**選擇**：at start，cache 在 `ResolvedManagerUserId` 欄位

理由：
- 申請當下 manager 是誰就是誰；如果 case 還在審但組織異動了，不應該突然換人簽。
- 若異動主管後想要新主管簽，是商業流程（要明示 hand-over），不是預設行為。

代價：申請當下沒主管會失敗（spec 明文要求 — initiator 必須有 manager 才能起案）。

## 4. HR 是 first-come-first-served

PendingHr 狀態下，ANY 一個 hr-role 使用者都能 Approve。第一個 approve 的人 wins，其他人這個 case 就從 todo list 消失。沒有「鎖定 / claim」階段，因為：
- HR 通常 1-3 個人，搶單機率低
- 簡化 POC，未來 process-runtime 的 Task.ClaimedAt 才處理

並行衝突：兩個 HR 同時 approve →  DB row-level lock 自然序列化，第二個會在 status check 失敗（已不是 PendingHr）。

## 5. Form data 不版本化

Returned 後 initiator 修改 formData → 直接覆蓋 `FormDataJson`。歷史版本不保留。理由：POC，且 `HrFlowAction` 表已經有時序紀錄可以拼湊出「誰在何時做了什麼」。需要完整版本需等 process-runtime 的 `FormDataPatchJson` 模型。

## 6. 前端 — 與既有 form 螢幕的一致性

既有 LEAVE / GEE / GEV / TRQ / TEO 都用 `FormShell` 元件包外層、各 form 自己 render fields。本 change 兩個 form 沿用相同模式：

- `FormShell` 提供：標題列 / Stepper / 角色切換的審批 panel
- `ResignForm` / `DeptxForm` 只關心 fields + initial / approve handlers

審批 panel 的按鈕：
- `ManagerApprove` 步驟 + persona = manager → 顯示 `Approve` + `Return`
- `HrApprove` 步驟 + persona = hr → 顯示 `Approve`
- 其他狀況 → 唯讀 banner 說「等待 X 動作」

## 7. 為什麼 RESIGN 和 DEPTX 不共用 form component

雖然流程一樣，欄位差很多。共用會塞入 conditional rendering 變成義大利麵。POC 階段兩個各自獨立的 form file 反而清爽，重複的只是 FormShell 包裝（已抽出）+ 審批 panel（已抽出）。

## 8. 退回後的 audit trail

Manager Return 時，寫入一筆 `HrFlowAction { Action: Return, FromStep: ManagerApprove, ToStep: Apply, Comment: required }`；接著 initiator Resubmit 寫一筆 `Action: Submit, FromStep: Apply, ToStep: ManagerApprove`。歷史是平的 list，UI 顯示時間順序即可。允許多次 return → resubmit 來回。

## 9. Cancel 的權限

只有 initiator 能 cancel；只有狀態不是 Completed 才能 cancel。Manager / HR 沒有 cancel 權限（只能 Return 或不 Approve）。Tenant admin 不在這個 change 的 scope。
