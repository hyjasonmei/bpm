# Design — add-audit-immutability-and-esign

## 1. Hash chain — chain key 怎麼定義

每張 audit table 的「chain」是**全表單一 chain**還是 per-instance / per-entity chain？

| Strategy | Pros | Cons |
|---|---|---|
| **Per-table single chain** | 驗一個 chain 就行 | 高頻寫入時鎖爭用嚴重；驗證一筆要回頭重算整表 |
| **Per-entity (per-Instance) chain** | 鎖粒度小、驗證 scope 小 | Chain 多、entity 之間的時序關係需另外追蹤 |
| **Per-day chain** | 鎖粒度小、驗證 scope 一天 | 跨日鏈接需 special handling |

**採用：per-entity chain**（HrFlowAction 用 InstanceId 分；BrandingChange 不分（單一全表 chain，因為量小）；ImpersonationSession 不分；SandboxRedirect 用 TenantCode 分）。

每張表的 chain key 列在 entity 配置：

```csharp
[ChainKey(nameof(HrFlowAction.InstanceId))]
public sealed class HrFlowAction : ... { ... }

[ChainKey] // no key → single chain
public sealed class BrandingChange : ... { ... }
```

Interceptor 用 reflection 讀 attribute → 找同 chain key 最後一筆 → 串。

## 2. Hash 計算 — 哪些欄位進去

要 deterministic（同樣 input → 同樣 hash），又不能讓「修改不影響 hash」。

**入計算**：
- 所有業務欄位（ActorUserId, Action, Comment, ...）
- ImpersonatedByUserId（如果有）
- CreatedAt（精確到 100ns）
- PrevRowHash

**不入**：
- Id（GUID newID 每次不同，但不算「狀態」）— 等等，Id 也應入，否則同樣內容的兩筆會產生同 hash。**入。**
- UpdatedAt（audit row 不該被 update，這欄無意義；忽略）

序列化：JSON sorted by key + UTC ISO8601 timestamps + Guid stringified lowercase。

```csharp
string ComputeRowHash(IHashChained row, string? prevHash) {
    var dict = new SortedDictionary<string, object?> {
        ["id"] = row.Id.ToString("D").ToLowerInvariant(),
        ["createdAt"] = row.CreatedAt.ToString("O"),
        ["prevRowHash"] = prevHash ?? "",
        // ... all business fields
    };
    var json = JsonSerializer.Serialize(dict, new JsonSerializerOptions { WriteIndented = false });
    using var sha = SHA256.Create();
    return Convert.ToHexString(sha.ComputeHash(Encoding.UTF8.GetBytes(json))).ToLowerInvariant();
}
```

## 3. Verify endpoint — 演算法

```
GET /api/audit/verify-chain?table=HrFlowActions&chainKey=<instanceId>&since=...&until=...

For each row in chain order (by CreatedAt asc):
    expectedHash = ComputeRowHash(row, prev_row.RowHash)
    if expectedHash != row.RowHash → ERROR (row id, expected, actual)
    if row.PrevRowHash != prev_row.RowHash → ERROR (chain broken)
    prev_row = row

return { ok: true, rowsChecked: N }
or { ok: false, brokenAt: rowId, expected: ..., actual: ..., reason: ... }
```

UI 上的「verify chain」按鈕：admin only，跑 ~1s 對 1000 筆，秀結果。

## 4. DB trigger — 為什麼不只 SQL trigger 也保留 app interceptor

兩層防禦原則：

- **DB trigger**：擋直接 SQL（DBA、誤用 raw ADO.NET）— 強制
- **App interceptor**：擋 ORM 呼叫（業務開發者意外寫錯 code）— 早期報錯，比 DB exception 訊息友善

兩者並存，trigger 是底線、interceptor 是 dev experience。

## 5. NTP — 為什麼不用 RFC 3161 TSA

**Alternative**：每筆 audit 接 RFC 3161 Time-Stamp Authority（如 DigiCert、freeTSA）取得 cryptographic timestamp token

**Rejected for now**：
- 每筆都接 TSA → cost + latency（HTTP round-trip ~500ms）
- 多數 audit 不需要法律 / 公證等級時間戳
- Part 11 §11.10(e) 字面要求是「include time stamp」 + 證明 system clock accuracy — NTP-synced + delta logging 滿足

升級路徑：未來 enterprise 客戶要 TSA → `add-tsa-integration` change，加 `TsaTokenBlob` 欄位、批次接 TSA、`SignatureHash` 帶 TSA token。

## 6. Electronic signature — HMAC vs PKI

**Alternative A**：客戶端拿憑證做 PKCS#7 簽章 → 上傳給後端

**Rejected**：
- 中小企業沒 PKI 基礎建設
- 客戶端憑證部署 = 死亡螺旋
- Part 11 §11.300 要求簽章不可被竊用 → 但 token-based / password-based 也可滿足（NIST 800-63B）

**採用：HMAC-SHA256(server_secret, signing_payload)**：
- Server-side secret（同 JWT secret 或獨立）
- Payload = `SignerId|SignedAtUtc|SignatureMeaning|RecordId|Comment`
- 簽章 = HMAC hex
- 驗證：給定 record + secret 重算
- Server compromised → 全簽章失效（同樣 risk model 跟 JWT 一致）

升級路徑：客戶要憑證等級 → `add-pki-esignature`，加 `SignatureCertThumbprint` + `SignatureBlob` (PKCS#7)，HMAC 仍保留作 fallback。

## 7. SignatureMeaning enum — 為什麼這 5 個

| Meaning | 場景 |
|---|---|
| `Approved` | 主管 approve / HR approve 標準路徑 |
| `Reviewed` | 看過、不需 approve（適用 future review-only step） |
| `Witnessed` | 第三者見證（適用 future co-sign step） |
| `Returned` | 退回（manager return） |
| `Acknowledged` | 收到通知（適用 future notify-with-ack step） |

來源：Part 11 typical practice + ISO 13485 常見 sign-off types。其他 like `Authored`, `Verified` 留 future 加。

不開放客戶自定 meaning 字串：理由
- 自由欄位 → 客戶 QA 寫 traceability 痛苦
- enum + clear semantics 比 free string 容易稽核
- 5 個夠 90% 場景，缺再加

## 8. 為什麼 SignerName / SignerEmail snapshot 要存

User 之後改名 / 改 email → audit 紀錄保留簽當下的身分。
- 即使 User 軟刪除（離職），紀錄還能還原「Wilson You 在 2024-03-12 簽了這張」
- 不只存 UserId（雖然 UserId 不變，display 時找不到 user 會很尷尬）
- 兩個都存：UserId 給 join、Name / Email 給直接 display

## 9. Migration 順序

要確保不破壞既有資料：

1. **AddHashChain**：所有 audit table 加 `PrevRowHash` + `RowHash` 欄位（nullable，舊資料 NULL）
2. **AddSignatureFields**：HrFlowAction 加 SignatureMeaning + SignerName + SignerEmail + SignedAtUtc + SignatureHash + NtpSyncedAt + NtpSyncDeltaMs（前 5 個 nullable，舊資料 NULL）
3. **AddAuditTriggers**：建 BEFORE UPDATE / DELETE triggers
4. App layer：interceptor 升級邏輯（同一 release 上線）

順序很重要：先 schema 再 trigger，否則 trigger 上線時 schema 還沒擴。

## 10. Verify chain UX

Admin UI 的 Audit Logs 頁面（目前是 placeholder）：

```
[Verify Chain]
  Table:   [HrFlowAction ▼]
  Filter:  Instance: [<guid>]   Date: [2026-05-01] to [2026-05-08]

  [Run]

  Result:
    ✓ 247 rows checked, chain integrity intact (verified at 2026-05-08T10:32:15 UTC)
    [Download verification report]
```

Bad case：
```
✗ Chain broken at row 9b3c... (expected hash abc..., actual def...)
  Adjacent rows: ...
  [Download forensic report]
```

「forensic report」是 JSON dump，給客戶 QA 寫 deviation report 用。

## 11. Clock-status UI

`/api/system/clock-status` 回：

```json
{
  "now": "2026-05-08T10:32:15.123Z",
  "lastNtpSync": "2026-05-08T08:00:00.000Z",
  "lastNtpDeltaMs": 12,
  "ntpServer": "time.google.com",
  "syncIntervalHours": 6,
  "warningThresholdMs": 1000
}
```

Admin UI 一個 sticky banner：當 delta > 1000ms 顯示橘色警告「⚠️ System clock drift X ms — last sync Y hours ago」。

Banner 位置：admin layout 的 SandboxBanner 上方（compliance 警示優先）。

## 12. 不做的（明確 out of scope）

| 項目 | 為什麼不做 |
|---|---|
| RFC 3161 TSA | 看 §5 — 留 add-tsa-integration |
| PKI 客戶端憑證 | 看 §6 — 留 add-pki-esignature |
| Customizable SignatureMeaning | 看 §7 — 留 future |
| Audit table data export | 已可走 EF query → CSV，不用特製 |
| 自動 archive 到 S3 / Object Lock | 客戶 IT 處理 |
| Long-term retention（15 年） | 同上 |
| 多重簽章 (re-sign sequential) | Part 11 §11.200(a) 提到，但醫材標準 SOP 中是少數情境，留 future |
| 客戶端 audit log 顯示（給員工自己看） | 員工只看自己 case 的 history（已有），不需要 raw audit |
