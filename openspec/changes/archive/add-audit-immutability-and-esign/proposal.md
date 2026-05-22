## Why

「合規即用」是平台第三大賣點，但要打到醫材 / 注射筆客戶（FDA Class II，賣美國，受 21 CFR Part 11 規範），現況差太遠：

- **應用層 append-only 攔截已有**（EF interceptor），但 DBA 走 SQL CLI 直接 UPDATE 完全擋不住
- **沒有 hash chain**（tamper-evident audit）— 即使 audit 紀錄被竄改，沒辦法偵測
- **沒有電子簽章**（Part 11 §11.50 / §11.70）— 每筆 approval 缺 `signatureMeaning`、缺 user-bound signature hash
- **時間戳沒有 trusted source 依據**（Part 11 §11.10(e)）— 系統時間不可信，沒記 NTP sync 狀態

這個 change 補齊四個剛性技術要求，讓系統在「**framework-ready**」層級符合醫材 / IATF 16949 嚴格稽核，同時補強對主流製造業的「合規即用」賣點。

非目標：

- 不寫客戶 QA 的 CSV / IQ-OQ-PQ 文件 — 客戶有專人處理
- 不解 15+ 年資料保存的儲存策略 — 客戶 IT 處理 backup / archive
- 不接 RFC 3161 Time-Stamp Authority（先用 NTP，TSA 是 enterprise add-on，未來開 `add-tsa-integration`）
- 不做 PKI 等級的電子簽章（先用 HMAC tied to user session，未來客戶要憑證等級再開 `add-pki-esignature`）
- 不做數位戳記法律有效性（不是法律 / 公證 SaaS）
- 不做完整 Part 11 認證（驗證是客戶 QA 的責任，我們提供「framework」）

## What Changes

### 1. Hash chain on audit tables

四張 audit 表（`HrFlowAction`, `ActorResolutionAudit`, `BrandingChange`, `RoleAssignmentChange`, `SandboxRedirect`, `ImpersonationSession` — 加 future `TaskHistory`）每筆新增：

- `PrevRowHash` (string, 64 chars hex) — 此表內前一筆 row 的 RowHash（同 InstanceId / 同 entity scope）
- `RowHash` (string, 64 chars hex) — 此筆 row 的 SHA-256 hash（包含所有業務欄位 + PrevRowHash）

寫入時 interceptor 自動計算：
1. 查同一 chain（按 entity 定義 chain key）的最後一筆 RowHash → PrevRowHash
2. 對所有業務欄位 + PrevRowHash 做 deterministic JSON 序列化（sorted keys）→ SHA-256 → RowHash
3. 兩個欄位一起 INSERT

提供 verify endpoint：

- `GET /api/audit/verify-chain?table=<name>&since=<date>&until=<date>` (admin only)
- 後端逐筆重算 RowHash 比對；任何 mismatch → 報「chain broken at row X」+ 哪筆有問題
- 用於稽核員臨場 demo 證明 audit 未被竄改

### 2. DB-level append-only enforcement

對所有 audit table 建 BEFORE UPDATE / BEFORE DELETE trigger：

```sql
CREATE TRIGGER prevent_hr_flow_actions_update
BEFORE UPDATE ON HrFlowActions
BEGIN
    SELECT RAISE(FAIL, 'audit table is append-only; UPDATE not permitted');
END;

CREATE TRIGGER prevent_hr_flow_actions_delete
BEFORE DELETE ON HrFlowActions
BEGIN
    SELECT RAISE(FAIL, 'audit table is append-only; DELETE not permitted');
END;
```

SQLite + Postgres 兩個 db engine 都支援。Migration `AddAuditTriggers` 統一加上去。

效果：
- DBA 用 `sqlite3 bpm.db "UPDATE HrFlowActions ..."` → SQL error
- ORM 走 EF interceptor → app-level 例外（已有）
- 雙層防禦

### 3. Electronic signature on approvals

擴充既有 approval 機制（HrFlowAction、未來的 ProcessTask）：

- `SignatureMeaning` (enum, required for Approve/Return actions): `Approved`, `Reviewed`, `Witnessed`, `Returned`, `Acknowledged`
- `SignerName` (string, snapshot of user's FullName at sign time) — 即使 user 之後改名，紀錄保留簽當下的名字
- `SignerEmail` (string, snapshot)
- `SignedAtUtc` (DateTime) — UTC 時間戳，使用 `IClock`（已有抽象）
- `SignatureHash` (string, 64 chars hex) — HMAC-SHA256(`SignerId|SignedAtUtc|SignatureMeaning|RecordId|Comment`, server_secret)
- `NtpSyncedAt` (DateTime?) — 後端啟動最後一次成功 NTP sync 的時間
- `NtpSyncDeltaMs` (int?) — sync 時的偏差（毫秒）

UI 端：
- Approve / Return modal 下方加「Signature meaning」select（依 action 限制可選項）
- 顯示 `Signed by Wilson You · 2026-05-08 10:32:15 UTC · Approved` 在簽核成功 toast 和 history view

API 拒絕缺少 `signatureMeaning` 的 approve / return 請求（400）。

### 4. NTP-synced time + delta logging

後端啟動時：

- 對配置的 NTP server pool（預設 `pool.ntp.org` / `time.google.com`）發 query
- 記錄成功 sync 時間 + delta
- 暴露 `GET /api/system/clock-status` (admin only) 顯示：
  - Last NTP sync timestamp
  - Last NTP delta (ms)
  - Sync server used
  - Time since last sync

每筆 audit row 寫入時 stamp 當下的 `NtpSyncDeltaMs`。如果 delta > 1000ms → log warning + UI 顯示「⚠️ system clock may be out of sync」banner（admin only）。

排程器每 6 小時重新 sync 一次。

不採用 RFC 3161 TSA（time-stamp authority）— 那是 legal-grade，需要訂閱外部服務 + token 驗證 + cost。POC + framework-ready 階段 NTP 足夠應付 Part 11 audit。

## 21 CFR Part 11 對照表

| Part 11 條款 | 要求 | 我們的機制 |
|---|---|---|
| §11.10(a) | 系統能 generate accurate copies of records in human + electronic form | ✅ Existing API + DB export, BPMN view |
| §11.10(b) | 系統能 protect records throughout retention period | ✅ Append-only DB triggers + app interceptor |
| §11.10(c) | 系統限制只有 authorized users 操作 | ✅ JWT + RoleAssignment + admin/HR/manager 角色 |
| §11.10(d) | 操作者唯一識別（unique to one individual） | ✅ User table，每筆 audit 帶 UserId |
| §11.10(e) | **Audit trail 包含時間戳，獨立於操作者** | ✅ NTP-synced timestamp + interceptor 寫，操作者改不了 |
| §11.10(f) | Operational system checks（順序強制） | ✅ State machine in HrFlowService |
| §11.10(g) | Authority checks（誰能簽什麼） | ✅ Role-based + ResolvedManager 邏輯 |
| §11.10(h) | Device checks（適用 IoT，不適用我們）| ⚠️ N/A |
| §11.10(i) | 訓練紀錄（操作者受訓） | ❌ 客戶 HR 系統紀錄 |
| §11.10(j) | 政策文件（操作 SOP） | ❌ 客戶 QA 寫 |
| §11.10(k) | 系統文件 control + audit trail of changes | ✅ Spec snapshot + this change |
| §11.30 | 開放系統 — 不適用，我們是 closed system | ✅ N/A |
| §11.50 | **電子簽章 components**（name + time + meaning）| ✅ This change adds SignatureMeaning + SignerName + SignedAtUtc |
| §11.70 | 簽章 + record 不可分（cannot be excised） | ✅ SignatureHash 綁 record + chain hash |
| §11.100 | 唯一性 | ✅ UserId Guid 唯一 |
| §11.200(a) | 雙因素 / 重簽章對 sequential signings | ⚠️ 目前一次簽，未來如有需求加 step-up auth |
| §11.300 | Password / 控制 | ✅ 走 SSO（roadmap）/ JWT |

對客戶 QA：給他們這張對照表 → 他們做 traceability matrix → 跟自己 SOP 對齊 → 寫 CSV。

## Impact

- Affected specs: NEW `bpm-compliance-audit`
- Affected code:
  - `bpm-svc/src/Domain/Common/IHashChained.cs` (NEW interface — entity in audit chain)
  - All audit entities implement `IHashChained` + add `PrevRowHash`, `RowHash`
  - `bpm-svc/src/Application/Common/Abstractions/IClock.cs` — extend with `NtpSyncedAt`, `NtpSyncDeltaMs` properties
  - `bpm-svc/src/Persistence/Common/SystemClock.cs` → `NtpClock.cs` (NEW; runs sync at startup + every 6h)
  - `bpm-svc/src/Persistence/Interceptors/AuditSaveChangesInterceptor.cs` — extend to compute hash chain on insert
  - `bpm-svc/src/Domain/Entities/HrFlows/HrFlowAction.cs` — add `SignatureMeaning`, `SignerName`, `SignerEmail`, `SignedAtUtc`, `SignatureHash`, `NtpSyncedAt`, `NtpSyncDeltaMs`
  - `bpm-svc/src/Persistence/HrFlows/HrFlowService.cs` — populate signature fields on Approve / Return
  - `bpm-svc/src/Api/Audit/AuditVerifyController.cs` (NEW) — `GET /api/audit/verify-chain`
  - `bpm-svc/src/Api/System/ClockStatusController.cs` (NEW) — `GET /api/system/clock-status`
  - Migrations:
    - `AddHashChain` (cols on each audit table)
    - `AddSignatureFields` (cols on HrFlowAction)
    - `AddAuditTriggers` (DB triggers preventing UPDATE / DELETE)
  - `bpm-ui/src/screens/forms/ResignForm.tsx` / `DeptxForm.tsx` — Approve modal adds Signature meaning select
  - `bpm-admin-ui/src/screens/AuditLogs.tsx` — replace placeholder with verify-chain UI
  - Optional: clock-status banner component when delta > 1000ms

### Coexistence

- 既有 audit data（hash chain 引入前的 row）`PrevRowHash` / `RowHash` 留 NULL
- Verify-chain endpoint 從第一筆 non-null row 開始驗
- Trigger 加上後，舊 row 仍可被 SELECT，只是無法 UPDATE / DELETE

### Backwards compatibility

- 既有 approve / return 流程在沒帶 `signatureMeaning` 時要不要 reject？方案：
  - **採方案：**Migration 套用後，前端必須帶 signatureMeaning，否則 API 400
  - 但前端有 fallback default：approve → "Approved", return → "Returned" — 客戶在 UI 看不到大改動
  - 配合本 change 的前端任務同步上線

### Out of scope

- Glossary 解釋（讓使用者理解 Approved vs Reviewed 差別）— 客戶 QA 教育員工
- BPM 流程設計時自定 SignatureMeaning enum — 第二期
- Cryptographic time-stamp authority (RFC 3161) — `add-tsa-integration` future change
- PKI / 客戶端憑證簽章 — `add-pki-esignature` future change
- Long-term archive 自動化 — 客戶 IT 處理
