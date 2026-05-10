## Why

UAT 階段第二大痛點：要驗證流程「對 Alice 而言看起來怎樣」需要拿 Alice 的密碼登入。客戶 IT 沒辦法逼員工交出密碼，又不能改員工密碼為已知值（污染、安全風險），結果只能：
- 找員工本人坐在電腦前操作（拖慢驗證進度）
- 或建假 user 模擬，但假 user 跟真 user 在組織圖上有差異，測不準

這個 change 加入 **admin 模擬登入（impersonation）**：admin 輸入目標 user id → 切換成該 user 的視角 → 任何操作都同時記錄「實際是 admin 在操作 Alice 的帳號」。配合大紅 banner 防止 admin 自己搞混。

非目標：

- 不允許跨 tenant impersonate
- 不允許看 / 改 password
- 不允許 escalate（不能 impersonate 比自己更高權限的角色 — 但 admin 已是頂層，所以 MVP 不限制）
- 不取代 SSO（impersonation 是 admin 特權通道，不是 user 替身用）
- 不替使用者收信、不轉送通知 — 純粹 session 視角切換

## What Changes

### Domain

- 新增 `ImpersonationSession` 實體：
  - `Id` (Guid)
  - `ImpersonatorUserId` (Guid) — admin 自己
  - `TargetUserId` (Guid) — 被冒用的人
  - `StartedAt` (UTC)
  - `EndedAt` (UTC, nullable) — null = active
  - `EndReason` (enum: `ManualExit`, `AutoExpired`, `AdminRevoked`)
  - `Reason` (string, max 500) — admin 開啟時必填的理由（給 audit）
- 新增 `audit table 擴充`（見 design §3）：所有 audit row 多 `ImpersonatedByUserId` 欄位（nullable），有值時表示這個動作是被 impersonate 的

### Auth flow

當 admin 起一個 impersonation session：

1. POST `/api/impersonation/start` body `{ targetUserId, reason }`
2. 後端驗證：呼叫者必須有 `admin` role；target 必須在同 tenant；不能 impersonate 自己；30 分鐘內同一 admin 只能有 1 個 active session（同時只能扮演一個人）
3. 後端開新 `ImpersonationSession` row + 簽一張新 JWT，claims：
   - `sub` = target user id
   - `impersonated_by` = admin user id
   - `roles` = target user's roles（**不是** admin 的）
   - `exp` = 30 分鐘
   - 新 claim `imp_session_id` = ImpersonationSession.Id
4. 回給 admin 那張 JWT；admin 前端把它塞到 localStorage 取代原本的 JWT；同時把原本 JWT 存到 `bpm_jwt_pre_impersonation`
5. UI 立刻 reload；header / role switcher / 所有後端呼叫都用新 JWT，看起來就是 target

當 admin 結束：

1. POST `/api/impersonation/end`
2. 後端標記 ImpersonationSession.EndedAt + EndReason=ManualExit
3. 前端把 `bpm_jwt_pre_impersonation` 還原到 `bpm_jwt`，刪除 pre-key
4. UI reload 回 admin 視角

當 30 分鐘到：

1. JWT 過期，下次 API call 拿 401
2. 前端攔 401 → 自動 swap 回 pre-impersonation JWT
3. 後端 cron / lazy check 把 active session 標 EndedAt + EndReason=AutoExpired

### API

- `POST /api/impersonation/start` — admin only
- `POST /api/impersonation/end` — must be in active session
- `GET /api/impersonation/status` — returns current session if any (前端用來 detect 狀態)
- `GET /api/impersonation/sessions?days=30` — admin only, audit log of all sessions

### Audit hook

任何動作（task submit / approval / form submit / 任何 controller 寫入）所產生的 audit row，**都要**自動填 `ImpersonatedByUserId`：
- 從 JWT 讀 `impersonated_by` claim
- 透過 `ICurrentUser` 額外暴露 `ImpersonatedById` (Guid?)
- 各 audit table（HrFlowAction, ProcessTaskHistory, ActorResolutionAudit）統一加這個欄位，由 SaveChangesInterceptor 填入

### UI surface

- **大紅 banner**：impersonation active 時，全站 header 上方常駐紅底白字 `⚠️ ACTING AS Alice Chen — started 14:32 by admin@acme · [Exit]`
- **不可隱藏**（同 sandbox banner）
- **Exit 按鈕**直接觸發 end + reload
- **倒數計時**：banner 顯示剩餘時間（最後 5 分鐘變橘色）
- **新進入 admin UI（`add-admin-ui-split` 完成後）**：Site Settings → Impersonation 頁
  - 輸入 target user id（或 user picker）
  - 必填 reason
  - 看歷史 sessions

### bpm-ui 上的入口

POC 階段在 RoleSwitcher dropdown 多一行 `🎭 Act as user...`（admin 看得到），點下去開 modal 輸入 target + reason。`add-admin-ui-split` 後遷到 admin UI，bpm-ui 上的入口移除。

## Impact

- Affected specs: NEW `bpm-impersonation`
- Affected code:
  - `bpm-svc/src/Domain/Entities/Impersonation/` (NEW folder)
  - `bpm-svc/src/Application/Impersonation/` (NEW)
  - `bpm-svc/src/Persistence/Impersonation/` (NEW)
  - `bpm-svc/src/Api/Impersonation/ImpersonationController.cs` (NEW)
  - `bpm-svc/src/Api/Auth/JwtTokenService.cs` (extend to mint impersonation JWT)
  - `bpm-svc/src/Application/Common/Abstractions/ICurrentUser.cs` (extend with `ImpersonatedById` and possibly `ImpersonationSessionId`)
  - `bpm-svc/src/Api/Common/HttpContextCurrentUser.cs` (read claim)
  - `bpm-svc/src/Persistence/Interceptors/AuditSaveChangesInterceptor.cs` (write into all audit rows)
  - **All existing audit-table entities**: add nullable `ImpersonatedByUserId` (HrFlowAction, ActorResolutionAudit, future TaskHistory)
  - Migration `AddImpersonation`
  - `bpm-ui/src/components/RoleSwitcher.tsx` (add Act-as menu)
  - `bpm-ui/src/components/ImpersonationBanner.tsx` (NEW)
  - `bpm-ui/src/lib/api/impersonation.ts` (NEW)
  - `bpm-ui/src/lib/apiFetch.ts` (extend 401 handler to swap back to pre-impersonation JWT)
- Migration impact on existing audit rows: backfill `ImpersonatedByUserId = null` (already nullable, no data change needed)

### Coexistence with sandbox mode

Sandbox + impersonation 可同時開。Banner 兩條都顯示（sandbox 在最上、impersonation 在第二條）。Audit row 會同時 carry `ImpersonatedByUserId` 和（如果有的話）`SandboxRedirect` 關聯。
