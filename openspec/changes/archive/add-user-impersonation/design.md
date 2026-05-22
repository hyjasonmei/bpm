# Design — add-user-impersonation

## 1. JWT 重簽 vs 旁路 claim

**Alternative**：admin JWT 不變，多帶一個 `acting_as` query/header 給後端，後端讀取後在當次 request 用該 user 視角操作。

**Rejected**：
- 必須改每個 controller / service / authorization handler 都認得 `acting_as`，散得到處
- 會議用 token 派發給前端後，前端要自己加 header — 漏一處就權限混淆
- 不能跟現有 `[Authorize]`、`User.IsInRole(...)` 等 ASP.NET 慣用語整合

採用 **重簽 JWT 把 `sub` 換成 target、加 `impersonated_by` claim**：
- 所有 ASP.NET 機制天然認得（`User.Identity.Name` = target user id）
- 後端任何地方拿 `User.IsInRole("hr")` 都會用 target 的 role — 自動正確
- audit 攔截器只要多讀一個 claim 就能填欄位

代價：admin 短時間內持有兩張 token（pre + impersonation），前端要管狀態。可接受 — 一個 helper module 包起來。

## 2. 30 分鐘自動過期

理由：
- 強迫 admin 重新確認意圖（過了還想做就重簽）
- 避免 admin 開了 session 走去開會回來忘記 → 改錯人
- 30 分鐘是「夠完成一個典型 UAT 操作 / 不夠長到忘記」的折衷

JWT exp 自然到期，前端拿 401 自動 swap 回 pre-impersonation JWT；後端 lazy 標 `EndedAt` + `EndReason=AutoExpired`（在下次 admin 操作或 audit 查詢時 backfill）。

不採用 cron job 主動標：POC 不需要、且 lazy approach 沒有「session 還在但 JWT 過期了」的灰色狀態 — 看 audit 就是「JWT 過期 ≈ 自動結束」。

## 3. Audit row 為什麼要每張表都加欄位

**Alternative**：開一張中央 `AuditEvent` 表，所有 audit 寫到那邊，加 `ImpersonatedByUserId`。

**Rejected**：
- 既有的 audit 已經分散在 HrFlowAction / ActorResolutionAudit / 未來的 TaskHistory，每張表 schema 不同（事件 payload 各異）
- 統一表會變成 polymorphic JSON blob，查詢痛苦
- migration 風險高

採用 **每張 audit 表加 nullable 欄位**：
- 改動小（只多一個 column）
- 既有查詢不變
- 舊資料直接 NULL（語意：那時候沒 impersonation 概念，正常）

寫入機制：`AuditSaveChangesInterceptor` 在 ApplyAudit 時，從 `ICurrentUser.ImpersonatedById` 讀，如果非 null 就寫入。每個 audit entity 必須實作 `IImpersonable` interface 暴露 `ImpersonatedByUserId` 欄位 setter；interceptor 用 reflection / 介面 dispatch 寫入。

## 4. 為什麼不允許 nested impersonation

Admin A impersonate Bob → Bob 去 impersonate Carol？不允許：
- Authorization 鏈會混亂（Carol 的 token 上 `impersonated_by` = Bob？但 Bob 不是 admin！）
- Audit 不可讀（誰真的做了動作？）
- 沒有業務需求

實作上：`POST /api/impersonation/start` 檢查 caller 的 JWT — 如果有 `impersonated_by` claim，return 409 "cannot start impersonation while already impersonating"。

## 5. Tenant 隔離

`POST /api/impersonation/start` 必須驗證：
- target user 存在
- target user 與 caller (admin) 同 tenant
- target user IsActive=true
- target user id != caller id（不能自己模擬自己）

Tenant 取得：POC 階段 single-tenant，先 skip 跨 tenant 檢查；當 tenant 概念落實（`add-tenant-sandbox-mode` 補上 Tenant 表時），這個檢查補上。

## 6. End reason 三種

| EndReason | 觸發 | 寫入時機 |
|---|---|---|
| `ManualExit` | admin 點 banner 上的 Exit 或呼叫 end endpoint | 立即同步 |
| `AutoExpired` | JWT 過期，前端拿 401 swap 回 pre-token | Lazy（next admin lookup） |
| `AdminRevoked` | 另一個 admin 在 admin UI 強制終止 | 立即同步 |

`AdminRevoked` 的價值：admin A 開了 session 結果 laptop 被偷或忘了關，admin B 可以強制 revoke。POC 階段先把 enum 留著，UI 不一定先做（v2）。

## 7. UI 上 Exit 按鈕的位置

紅色 banner 永遠在最頂端，Exit 按鈕在 banner 最右邊。**不能跟 sandbox banner 共用一條** — 兩個訊息語意不同：

```
┌────────────────────────────────────────────────────┐
│ 🧪 SANDBOX MODE ACTIVE — outbound redirected       │  ← orange/yellow
├────────────────────────────────────────────────────┤
│ ⚠️ ACTING AS Alice Chen · 28:42 left · [Exit]     │  ← red
├────────────────────────────────────────────────────┤
│  [BPM logo]  Home  Search  ...                     │  ← normal header
└────────────────────────────────────────────────────┘
```

## 8. 倒數計時的實作

Banner 顯示「28:42 left」 — 簡單做法：JWT exp claim 拿出來，前端 setInterval 每秒重算。最後 5 分鐘變橘色（`text-amber-600`），最後 1 分鐘變紅閃爍（不要太誇張，普通閃就好），到 0 時前端立刻主動 call end endpoint 並 swap token。

不採用 server-sent events / WebSocket：太重，POC 階段一個 client-side timer 就夠。

## 9. RoleSwitcher 上的入口（POC 階段）

POC 階段 admin UI 還沒拆出來，先把 impersonation 入口塞 RoleSwitcher dropdown 底部：

```
─────────────────
🎭 Act as another user...
```

點下去開 modal：
- User picker (search by name / email; 後端 endpoint `/api/org/users?q=...` 已存在或要加)
- Reason textarea (required, max 500)
- Confirm 按鈕

`add-admin-ui-split` 完成後這個入口移到 admin UI，RoleSwitcher 還原。

## 10. ICurrentUser 的擴充

```csharp
public interface ICurrentUser
{
    string? Id { get; }
    bool IsAuthenticated { get; }
    Guid? ImpersonatedById { get; }       // NEW: admin's id when this is an impersonation session
    Guid? ImpersonationSessionId { get; }  // NEW: for cross-table audit join
}
```

`HttpContextCurrentUser` 從 JWT claims 讀。`SystemCurrentUser`（背景作業用）回 null（系統不會 impersonate）。

audit interceptor：

```csharp
foreach (EntityEntry<IImpersonable> entry in context.ChangeTracker.Entries<IImpersonable>())
{
    if (entry.State == EntityState.Added && currentUser.ImpersonatedById is { } imp)
    {
        entry.Entity.ImpersonatedByUserId = imp;
    }
}
```

## 11. Banner / token swap 的競態

Edge case：admin 的 impersonation JWT 在 banner 倒數還沒到 0 時就過期了（系統時間漂移、或計算誤差）。前端拿 401 → swap 回 pre-token → reload。reload 後 banner 消失，因為 pre-token 沒有 impersonated 標記。看起來無感，admin 只會看到頁面 reload 一次。可接受。

反向 edge case：pre-token 也過期了。Swap 回去後第一次 API call 又拿 401，正常 401 流程觸發 dev-login 重新拿 admin token。可接受。
