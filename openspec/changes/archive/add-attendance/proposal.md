## Why

夥伴 dogfood 後第一個明確的需求：員工每天上下班要打卡。BPM 平台目前只處理「流程」（請假、採購、差旅等），缺一個讓員工每天最常用的高頻動作 — 打卡。沒有打卡，員工進系統的理由就少一個；有了打卡，BPM 變成員工每天必開的工作面板，後面的流程曝光率自然提升。

這個 change 的範圍刻意限縮為「員工自己看自己的打卡」：純記錄、無規則判斷、無主管視角、不跟其他流程連動。後續若要加遲到/早退規則、主管報表、HR 月報，都另外開 change。

非目標（明確排除）：

- 主管看下屬打卡（個人自看 only）
- 與 Leave 流程連動扣抵
- 跨午夜班次處理（夜班員工目前不適用）
- 遲到/早退自動判定（純記錄時間，不下結論）
- 自動補打卡推導（補打卡是獨立頁面、人工申請）
- 地理圍欄、IP 限制、人臉辨識（POC 不需要）

## What Changes

### Frontend (NEW capability `bpm-attendance-ui`)

**Header 入口**

- AppLayout 右側 Notification 按鈕左邊新增 NavBtn，icon = `Clock`，label = `Attendance`
- 點擊進入 Attendance 頁

**Screen — `screens/Attendance.tsx`**

- 大型狀態卡：今日狀態（未打卡 / 上班中 / 已下班）、累計工時、最近一次 Check-in / Check-out 時間
- 主按鈕：依今日狀態切換
  - 未打卡 → `Check in`
  - 上班中（已 check-in，尚未 check-out）→ `Check out`
  - 已下班 → `Check in again`（允許多次打卡）
- 下方表格：近 30 天打卡紀錄（日期、首次 in、最後 out、累計工時、打卡次數）
- 補打卡入口：頁面右上角 `Request Correction` 按鈕（連去補打卡頁，本 change 先放 placeholder）

**Screen union 擴充**

- `Screen` 增加 `{ kind: 'attendance' }`

**API client**

- `lib/api/attendance.ts`：`checkIn()`, `checkOut()`, `getToday()`, `getHistory(days)`

### Backend (NEW capability `bpm-attendance`)

**Domain entities**

- `AttendancePunch`（單筆打卡事件）
  - `Id` (Guid)
  - `TenantId` (Guid)
  - `UserId` (Guid)
  - `PunchType` (enum: `In` / `Out`)
  - `PunchAt` (UTC datetime)
  - `LocalDate` (date，用使用者 timezone 計算，索引用)
  - `Source` (enum: `Manual` / `Correction`，本 change 只有 `Manual`，`Correction` 留給未來補打卡)
  - `CreatedAt` (UTC)

設計重點：以「事件」為單位儲存，而不是「日記錄」（一筆 record 含 in/out）。理由：
1. 同一天可多次打卡（Jason 已確認 OK）
2. 補打卡是事件型操作，自然落在同一個 table
3. 統計面統一從 punches aggregate 出來，避免 in/out 配對的邊界問題

**Application services**

- `IAttendanceService`
  - `CheckInAsync(userId)` — 寫入 PunchType=In
  - `CheckOutAsync(userId)` — 寫入 PunchType=Out
  - `GetTodayAsync(userId)` — 回傳今日狀態 + 所有 punches + 累計工時
  - `GetHistoryAsync(userId, days)` — 回傳近 N 天的 daily summary
- 累計工時計算：把今天所有 in/out 配對（時間排序，配成 pairs，落單的 in 用「現在」當 out），加總時長
- 不限制連續兩個 In 或兩個 Out（純記錄）

**API endpoints**

- `POST /api/attendance/checkin` → 201 with punch
- `POST /api/attendance/checkout` → 201 with punch
- `GET /api/attendance/today` → today summary
- `GET /api/attendance/history?days=30` → daily summaries

所有 endpoint 都用當前登入者的 UserId（從 auth context），無 query param 傳 userId（防主管看下屬）。

**Persistence**

- EF Core entity + Configuration
- Index：`(TenantId, UserId, LocalDate)` 用於 today/history 查詢
- SQLite migration `AddAttendance`

## Impact

- Affected specs: NEW `bpm-attendance` (backend domain), NEW `bpm-attendance-ui` (frontend)
- Affected code: 
  - `bpm-svc/src/Domain/Entities/Attendance/`
  - `bpm-svc/src/Application/Attendance/`
  - `bpm-svc/src/Persistence/Configurations/AttendanceConfiguration.cs`
  - `bpm-svc/src/API/Controllers/AttendanceController.cs`
  - `bpm-ui/src/components/AppLayout.tsx` (nav button)
  - `bpm-ui/src/screens/Attendance.tsx` (NEW)
  - `bpm-ui/src/lib/api/attendance.ts` (NEW)
- 不影響現有流程（請假、採購等）的 spec 或 runtime
- DB schema 新增 1 張表，零修改既有表
