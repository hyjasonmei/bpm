## Why

導入期 / UAT 階段最大的痛點是「不小心送真的東西出去」：客戶 IT 在驗證流程時，notification 引擎會把 mail 發給真的員工、webhook 會打到真的下游系統。導致客戶不敢全鏈路測試，要嘛改 prod 的收件人欄位（污染），要嘛停掉通知（測不出 SLA），要嘛偷改程式（破壞品質）。

這個 change 引入 **tenant 層的 Sandbox Mode**，讓 admin 一個 toggle 就把所有對外通訊的出口攔截到測試收件人 / URL，body / payload 開頭加註原本要送給誰，UAT 通過後 toggle 回去切到 prod。

非目標：

- 不影響流程引擎本身的執行（task 還是會 spawn、approval 還是會被簽、SLA 還是會跑）
- 不攔截「使用者主動觸發」的下載、列印、報表（這些是讀取，無外部副作用）
- 不取代壓測或負載測試環境（sandbox 是 functional UAT 工具，不是 perf 工具）
- 不限制 sandbox 模式下的資料寫入（DB 還是真寫，避免測試資料和 prod schema drift）

## What Changes

### Tenant flag

- `Tenant` 新增欄位 `SandboxMode` (bool, default false)
- `Tenant` 新增 `SandboxConfigJson` (text, nullable) — 結構：
  ```json
  {
    "emailRecipients": ["uat@acme.com", "qa@acme.com"],
    "webhookUrl": "https://webhook.site/<uuid>",
    "smsRecipients": ["+886912345678"]
  }
  ```
- 缺欄位 = 該出口不重定向（fall back to nothing — DROP，並寫一筆 audit）

### Outbound interception

所有「對外送東西」的 dispatcher 都要在 send 前過一個 `IOutboundGate`：

- `EmailDispatcher.SendAsync(EmailMessage)` → 進來時先檢查 tenant.sandboxMode
- `WebhookDispatcher.PostAsync(WebhookDelivery)` → 同上
- `SmsDispatcher` (未來) → 同上

`IOutboundGate.Apply(message)` 行為：
- Sandbox OFF → pass through unchanged
- Sandbox ON →
  - email: 改寫 `to`、`cc`、`bcc` 全部清掉並改成 `sandbox.emailRecipients`；body 開頭注入 banner（HTML + plaintext 兩版）：
    ```
    [SANDBOX MODE] Original recipients: alice@acme.com, bob@acme.com (cc: ceo@acme.com)
    Tenant: acme · Sandbox triggered at 2026-05-08T14:22Z
    ----
    <原本 body>
    ```
  - webhook: target URL 改成 `sandbox.webhookUrl`；保留原 payload；HTTP header 加 `X-BPM-Sandbox-Original-Url: <原本 URL>`
  - sms: 改寫 `to` 為 `sandbox.smsRecipients`；body 前綴 `[SANDBOX → originally to: +886900000000]`

### Audit

- 每次 sandbox 改寫寫一筆 `SandboxRedirect` audit row：tenant_id, channel (email/webhook/sms), original_targets[], redirected_targets[], dispatched_at, message_subject_or_event_type
- 給 admin 在 UI 上看「過去 30 天 sandbox 攔了哪些」 → 信心源

### UI surface (initially in bpm-ui Site Settings; will move to bpm-admin-ui later)

- Toggle on/off
- 編輯 `emailRecipients` / `webhookUrl` / `smsRecipients`
- Recent redirects table (近 30 天)
- 開啟 / 關閉時要 require admin role + confirm dialog（紅字「This affects ALL outbound communication」）

### Visual indicator

- 當 tenant 在 sandbox mode → 全站 header 加紅色 banner `🧪 SANDBOX MODE ACTIVE — outbound emails / webhooks are being redirected to test recipients`
- 不可隱藏；admin 能 dismiss 但下一頁刷新會回來

## Impact

- Affected specs: NEW `bpm-sandbox`
- Affected code:
  - `bpm-svc/src/Domain/Entities/Org/Tenant.cs` (add columns; if no Tenant entity exists yet, scope this change to add it minimal)
  - `bpm-svc/src/Application/Sandbox/IOutboundGate.cs` (NEW)
  - `bpm-svc/src/Persistence/Sandbox/OutboundGate.cs` (NEW)
  - `bpm-svc/src/Application/Notifications/EmailDispatcher.cs` (wrap with gate; depends on `add-notification-engine`)
  - Future: WebhookDispatcher, SmsDispatcher
  - `bpm-svc/src/Domain/Entities/Sandbox/SandboxRedirect.cs` (NEW)
  - `bpm-svc/src/Api/Sandbox/SandboxController.cs` (NEW; toggle + read recent redirects)
  - Migration `AddSandboxMode`
  - `bpm-ui/src/screens/SiteSettings.tsx` (interim location; will move to admin UI per `add-admin-ui-split`)

### Dependency note

`bpm-sandbox` capability **depends on `add-notification-engine`** for the email dispatcher hookup. If notification engine isn't implemented yet, this change ships:
1. The flag, audit table, UI toggle (above)
2. The `IOutboundGate` interface
3. A no-op email gate that asserts in tests but doesn't actually short-circuit (because there's nothing to short-circuit yet)

When notification engine ships, it MUST call `IOutboundGate.Apply()` before sending. Adding that call is a one-line addition documented in `add-notification-engine`'s tasks.md.
