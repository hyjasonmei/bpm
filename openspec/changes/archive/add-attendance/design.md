# Design — add-attendance

## 1. 為什麼用 event-sourced punches 而不是 daily record

**選項 A**：`AttendanceRecord` 一天一筆，欄位 `CheckInAt` + `CheckOutAt`

**選項 B（採用）**：`AttendancePunch` 單筆事件，每次打卡一列

採用 B 的理由：
1. **多次打卡天然支援**：Jason 確認一天可以多次 in/out。選項 A 要嘛存 array（破壞關聯模型），要嘛多開一張子表（複雜化）。
2. **補打卡同表**：補打卡是「事件」概念，落在 punches 表只差 `Source = Correction` + 一個審核欄位（未來新增）。選項 A 要嘛改 record 的 in/out（無法 audit），要嘛開新表。
3. **歷史查詢自然**：`GROUP BY LocalDate` 即可導出 daily summary，無 in/out 配對的 NULL 處理。
4. **Audit 簡單**：純 append-only，無 update 路徑，符合 BPM 平台整體「事件不可變」的設計風格（對齊 TaskHistory）。

成本：每次顯示「今日工時」要 aggregate 一次。punches/天 量級小（個位數），SQLite 上完全 OK。

## 2. 工時計算規則（in/out 配對）

純記錄不下判斷，但前端要顯示「累計工時」，所以仍需配對邏輯：

1. 取今日該 user 的所有 punches，按 `PunchAt` 升序
2. 從第一個 `In` 開始配對：下一個 `Out` 結束一段；若連續兩個 `In`，視為前一個沒 out（前段忽略不計）
3. 若最後落單一個 `In`（還在上班中），用「現在」當 out 計算當段時長，標註 `inProgress = true`
4. 加總所有完整段 → 累計工時

邊界：
- 只有 `Out`、沒有任何 `In`：累計 = 0，狀態 = "Check-out without check-in"（前端顯示警告但不擋）
- 連續兩個 `Out`：第二個 `Out` 視為無對應 `In`，跳過
- 跨午夜（不在範圍內）：不處理，當作各自獨立日

實作位置：`AttendanceService.ComputeWorkHoursForDay(punches)` — 純函數，可單元測試。

## 3. Today 狀態判定

```
status =
  no punches today                            → 'NotCheckedIn'
  last punch is In, no later Out              → 'OnDuty'
  last punch is Out                           → 'OffDuty'
```

按鈕顯示：
- `NotCheckedIn` → `Check in`
- `OnDuty` → `Check out`
- `OffDuty` → `Check in again`（允許再上工）

## 4. Tenant + UserId 取得

所有 endpoint 從 `IUserContext`（既有抽象）取 `TenantId` + `UserId`，**不接受**從 body / query 傳入 user。後端 controller 不暴露任何「指定別人」的參數路徑——這是 MVP「主管不看下屬」的剛性實現。

## 5. Timezone

`LocalDate` 用使用者 tenant 的 timezone（先寫死 `Asia/Taipei`，未來 tenant config 拉出）。`PunchAt` 永遠存 UTC。日切點 = 該 timezone 的 00:00。

## 6. 為什麼不在 BPM Process Runtime 內

打卡不是流程（無審核、無流轉、無多步驟）。硬塞進 BPM runtime 會：
- 每次打卡產生 ProcessInstance + Task + TaskHistory 三筆，IO 放大
- 流程查詢面板會被打卡訊息淹沒
- 失去「BPM 流程」的純粹語意

獨立 capability `bpm-attendance` 保持兩者解耦。未來若要加「打卡異常 → 開立補打卡流程」，再讓 attendance 觸發 BPM Runtime 的 StartInstance。

## 7. 前端 state 管理

- 進 Attendance 頁時呼叫 `getToday()` + `getHistory(30)` 並行
- Check-in / out 後 optimistic update：先把按鈕鎖住、insert 一筆 punch 到 local state，API 回來再 reconcile
- 失敗 → revert + toast

## 8. 後續擴充預留

- `Source` 欄位現在只用 `Manual`，預留 `Correction`（補打卡）、`Auto`（未來自動推導）
- daily summary 查詢未來可拉出主管視角 endpoint，但 controller route 設計上獨立（`/api/attendance/team/:userId`），避免本 endpoint 被改寫
- 累計工時函數獨立，未來加「扣除午休 1hr」「最低 8hr 才算正常」等規則時，只動這個函數
