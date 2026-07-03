# Shared Role Queue — 角色共享待辦佇列 設計

**Status:** Draft for review (Jason, Telegram 2026-07-01). 由 demo 反饋 #4a 觸發（`docs/demo-feedback-2026-06-30.md` §1.5）。
**Owner layer:** lead（共用授權 primitive + 各流程角色關卡）。

## 問題

角色制簽核關卡（`role:FINANCE`、`role:PROCUREMENT` 等）目前的行為：

1. 案件進到角色關卡時，用 `IPrincipalDirectory.FindFirstUserInRoleAsync(role)` 從該角色成員裡挑 **DisplayName 字母序第一個人**，把案件指派給**單一一個人**（`CurrentAssigneeUserId`）。
2. 授權 `ActorAuthorizer.CanActAsync(requiredUserId, caller)` 只認「caller == 被指派人 或其代理人」——**完全不看角色**。
3. → 同角色的其他人**看不到也不能簽**。

客戶（demo 現場）預期的是「**有該角色就能簽**」＝企業一般的「角色共享佇列」。目前未實作。這也是「一人多職」無法成立的根因（給一人多角色，他也只在字母序剛好第一時才收得到）。

## 目標（本 spec）

- 角色制關卡從「pending 某個人」→「pending 某角色」：**持該角色的任何人都能在待辦看到、都能簽**（含代理人）。
- 主管關（org-chart 單一主管）**維持不變**——那本來就該是特定人。
- 「一人多職」自然成立：一人持多角色 → 他的每個角色佇列都看得到。

## 已定案的設計決定（Jason 2026-07-01）

- **D1＝純角色**：角色關卡 `CurrentAssigneeUserId = null`、`CurrentAssigneeRoleCode = <role>`；授權與佇列都認角色；畫面「目前指派給」顯示「待 [角色名]」而非單一人名。
- **D2＝一次做完 4 隻**有角色關的流程：VENDOR_EXPENSE（採購）、TEO（財務）、PURCHASE_REQUEST（財務）、LEAVE（VP + HR）。共用 primitive 是主要工，各流程轉換是機械式。
- 主管關（manager，走 org-chart）不動。
- 代理人（delegation）：人對人的代理仍適用於「單人關卡」；角色關卡本身就對全角色開放，不需要角色層代理。

## 非目標（YAGNI）

- **並簽 / 會簽（parallel / quorum，多人同時都要簽）**——那是「多待簽人 + 完成條件」的另一件事（demo 反饋 §1.6）。本 spec 是「單一決定、但任一角色成員可認領」，仍是 one-decision-advances。兩者同屬「多人簽核」家族但分開做；本件是其基礎件之一。
- 「認領鎖定 (claim/lock)」：本階段不做「A 認領後 B 就不能簽」的搶鎖；角色關卡由任一成員簽一次即前進（誰先簽算誰），簡單且符合多數需求。日後要 claim-lock 再加。

## 元件與變更

### 1. 角色成員查詢 port（共用，新增）

`bpm-svc/src/Application/Common/Directory/IPrincipalDirectory.cs` 加：
```csharp
/// 該使用者「有效持有」的所有角色 Code（直接授予 + dept/group 繼承展開）。
Task<IReadOnlySet<string>> GetRoleCodesForUserAsync(Guid userId, CancellationToken ct = default);
```
Persistence 實作 `PrincipalDirectory` 複用 `FindFirstUserInRoleAsync` 既有的展開邏輯（direct grant + `InheritToMembers` 的 dept/group 成員）反向做：給 userId → 找出他透過直接/繼承持有的所有 role Code。

### 2. 案件 schema（4 隻流程，各加一欄 + migration）

每隻流程的 `<CODE>_V<N>_Case` 加 nullable 欄位：
```csharp
public string? CurrentAssigneeRoleCode { get; set; }   // 非 null = 此關卡對「持此角色者」開放
```
- EF config 加 `.HasMaxLength(60)`，加 index `(CurrentAssigneeRoleCode, LastActivityAt)`。
- 進角色關卡：`CurrentAssigneeUserId = null; CurrentAssigneeRoleCode = <role>;`
- 離開角色關卡（核准往下 / 退件 / 結案）：`CurrentAssigneeRoleCode = null;`（回到單人關卡則照舊設 `CurrentAssigneeUserId`）。
- 4 個 EF migration（bpm-svc 自有表，真 migration）。

### 3. 授權 role-aware overload

`IActorAuthorizer` 加：
```csharp
Task<bool> CanActAsync(Guid? requiredUserId, string? requiredRoleCode, Guid callerUserId, CancellationToken ct = default);
```
`ActorAuthorizer` 實作：
```csharp
public async Task<bool> CanActAsync(Guid? requiredUserId, string? requiredRoleCode, Guid caller, CancellationToken ct = default)
{
    if (requiredUserId is { } u) {                       // 單人關卡：本人或代理人
        if (u == caller) return true;
        if (await delegation.GetActiveDelegateAsync(u, clock.UtcNow, ct) == caller) return true;
    }
    if (requiredRoleCode is { } role)                    // 角色關卡：持該角色即可
        return (await directory.GetRoleCodesForUserAsync(caller, ct)).Contains(role);
    return false;
}
```
（保留舊的 `CanActAsync(Guid, Guid)` overload 給單人關卡；新 overload 給角色關卡。各流程角色關卡的 decision method 改呼叫新 overload，傳 `requiredRoleCode`。）

### 4. Pending 佇列（4 隻流程的 inbox provider）

各 `*_InboxProvider.GetPendingAsync(userId)`：角色關卡的案子，對「持該角色的所有人」顯示。做法：provider 先取 `GetRoleCodesForUserAsync(userId)`，其 case store `FindPendingAsync` 改成回傳「`CurrentAssigneeUserId == userId`（單人關卡） **或** `CurrentAssigneeRoleCode ∈ 我的角色`（角色關卡）」的案子。
- 各流程的 `I<CODE>_V<N>_CaseStore.FindPendingAsync` 簽名擴充為吃 `(Guid userId, IReadOnlySet<string> myRoleCodes)`，query 加 `|| myRoleCodes.Contains(c.CurrentAssigneeRoleCode)`。

### 5. 通知 + 顯示

- **通知**：進角色關卡時，通知「該角色所有現任成員」（用既有 `IOrgChartReader.GetRoleAssigneesAsync` 或新 port 展開）——而非單一人。
- **case-detail / DTO**：`CurrentAssigneeUserId` 為 null 時，「目前指派給」顯示「待 [角色顯示名]」（角色 Code → 顯示名可查 `SharedRole.Name`）。DTO 加 `CurrentAssigneeRoleCode` / `CurrentAssigneeRoleName`。inbox `InboxRow` 已有 `Status`（在地化）夠用；額外的 assignee 顯示在 case-detail 處理。

## DB / 相容性

- EF migration（bpm-svc 自有 4 表）；純新增 nullable 欄位，無破壞。角色檢查在記憶體（`GetRoleCodesForUserAsync` 回 set 後 `.Contains`），符合 portable 規則（no JSON-path、no SQLite 特有）。
- **既有在途案件**：目前停在角色關卡的案件其 `CurrentAssigneeUserId` 已是某人、`CurrentAssigneeRoleCode` 為 null → 部署後仍以單人關卡運作（該人仍簽得掉），不會壞；新進角色關卡的案件才走共享佇列。可接受。

## 測試

- **單元**（`IActorAuthorizer` + 一隻代表流程，如 TEO）：
  - 角色關卡：持該角色的 A 可簽、持該角色的 B 也可簽、不持該角色的 C 被擋（403）。
  - 單人關卡（主管關）：本人可簽、代理人可簽、他人被擋（回歸，不可壞）。
  - Pending：角色關卡案子對「持該角色的多個人」都出現在 pending。
- **smoke（雲端/本機）**：TEO 財務關可被「任一 FINANCE 成員」簽（種第二個 FINANCE 人驗證）；VENDOR 採購關同理；主管關仍只本人可簽；既有 11 隻流程 happy path 全綠。

## Phase-1 交付物

1. `IPrincipalDirectory.GetRoleCodesForUserAsync` + Persistence 實作 + 單元測試。
2. `IActorAuthorizer` role-aware overload + `ActorAuthorizer` 實作 + 單元測試。
3. 4 隻流程：case 加 `CurrentAssigneeRoleCode` + EF config + migration；service 角色關卡改「設角色 / 清角色 + 用 role-aware CanAct」；case store `FindPendingAsync` role-aware；inbox provider role-aware pending；通知改通知全角色；case-detail DTO/顯示「待 [角色]」。
4. 一人多職即自然成立（一人持多角色 → 多佇列可見）；順帶 seed 一個雙職示範帳號驗證。
5. 測試 + smoke 全綠。

## 已定案（Jason review 2026-07-01）

1. **進角色關卡通知全角色成員**（in_app；email 選配）。
2. **inbox 標題不加「· 待 [角色]」後綴**——維持各 provider 既有 Title；角色資訊只在 case-detail 顯示「待 [角色]」。
