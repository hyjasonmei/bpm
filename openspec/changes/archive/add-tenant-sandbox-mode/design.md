# Design — add-tenant-sandbox-mode

## 1. 為什麼是 tenant 層、不是 environment 層

**Alternative**：每個 tenant 跑兩個 instance（prod / sandbox），DB 分開、queue 分開、URL 分開。

**Rejected**：
- 客戶 IT 通常只願意付一份基礎設施錢
- prod/sandbox 兩份資料分叉後，想驗證「這張單在 prod 上會發生什麼」幾乎做不到
- 大企業才有「dev/staging/prod」分環境的習慣，中小企業不會

採用 tenant flag：**同一份資料、同一個 instance、只在 outbound 出口攔截**。代價：sandbox 模式下做的事是真的寫進 DB（送的單真的進 workflow runtime），但不會打擾真的人。

## 2. 為什麼是 outbound interception 而不是 dispatcher 層直接判斷

**Alternative**：每個 dispatcher 自己 check tenant.sandboxMode：

```csharp
// EmailDispatcher.cs
if (tenant.SandboxMode) { /* rewrite */ }
SendReal();
```

**Rejected**：
- 邏輯散在每個 dispatcher，加新出口（SMS / IM / Slack）就漏一次
- 測試覆蓋率難一致
- 改 sandbox 行為要改 N 個檔案

採用 **`IOutboundGate` 中介層**：dispatcher 只呼叫 `var gated = gate.Apply(message); SendReal(gated);`。Gate 集中所有 channel 的 rewrite 邏輯 + audit 寫入。新增 channel = 新增一個 case。

## 3. 為什麼 audit 不只記 metadata，還要保留 sample

每筆 redirect 記下：
- channel, original_targets, redirected_targets, dispatched_at
- `sample_subject` (email subject / webhook event_type / sms first 80 chars)
- 不存完整 body — 太大且可能含敏感資料

理由：
- 客戶 IT 想驗收「上週發了哪些信？」 — sample subject 足以辨識
- 完整內容可從測試收件人那邊看
- 省 DB 空間 + 減少 PII 風險

## 4. 為什麼 banner 不可隱藏

`🧪 SANDBOX MODE ACTIVE` 紅色橫條全站常駐。理由：
- Sandbox 開著時的所有操作都是「假的」（對外效果）—— 使用者必須無時無刻知道
- 過去出過事（其他系統）：使用者忘記在 sandbox，跟客戶展示時點到「發送」，以為發了結果沒發
- 反向：使用者真的在 prod 但忘了關 sandbox，以為發了結果只給測試人 — 兩種錯都很慘

選擇：banner 強制顯示。Admin 只能 dismiss 一次（接下頁面刷新就回來），不能 permanent dismiss。

## 5. 模式切換的時機

開 / 關 sandbox **立即生效**，不需要重啟服務。理由：
- Tenant flag 在 outbound gate 每次 dispatch 時才讀（cache 可有可無，POC 不 cache）
- 進行中的 in-flight email 不會被回頭重新攔（已經寄了就是寄了），audit 會反映當下狀態

代價：dispatch 高頻時每次查 DB 一次。POC 階段量小，問題不大；未來加 IMemoryCache + invalidation。

## 6. SandboxConfigJson 為什麼用 JSON 而不是欄位

**Alternative**：每個 channel 一個欄位（`SandboxEmailRecipients` text[], `SandboxWebhookUrl` string, ...）

**Rejected**：每加一個 channel 就要 migration。

採用 JSON：schema 定義在 DTO + 應用層 validator，DB 端不管結構。新增 channel 只動程式不動 DB。

## 7. 缺欄位的行為

如果 sandbox ON 但 `sandbox.emailRecipients` 為空：
- **DROP** 該封 email（不送、不轉）
- 寫一筆 `SandboxRedirect` audit，channel=email, redirected_targets=[], action=dropped

理由：「設定不完整就靜默送出去」是最糟糕的 fail mode。Drop + audit 讓問題顯化，admin 開 audit table 立刻看到有東西被 drop，可以補設定再 retry（手動，POC 階段）。

## 8. 與 add-user-impersonation 的互動

當 admin 用 impersonation 操作時，產生的 outbound 還是受 sandbox 攔截。Audit row 同時記 `actor_user_id` (被 impersonate 的人) + `impersonated_by_user_id`（admin），兩個 capability 互相不打架。

## 9. 與 prod release 的關係

Sandbox 模式不是「prod / dev 環境」概念，是 **per-tenant 開關**。實務上：
- 客戶剛上線 → 全 tenant 開 sandbox 跑 1 週 UAT
- UAT 過了 → 關 sandbox 切 prod
- 之後想加新流程，可以再開 sandbox 測試新流程，但缺點是 *所有* 流程的 outbound 都被攔（不只是新流程）

這個限制是接受的：要分流程級別的 sandbox 太複雜，POC 不做。客戶要更細粒度未來再加 `flowCode whitelist` 設定。
