# Case Transfer（轉簽）Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** 簽核人把手上「個人指派」關卡的單轉給另一位在職使用者——共用 primitive 一次覆蓋全部 model-B flows。

**Architecture:** lead 共用 `CaseTransferService`（反射掃 case tables + EF Find/SaveChanges，無 raw SQL）+ 通用 REST 端點 + 共用 UI hook/modal，各 flow CaseDetail 三行接線。前置：26 處關卡授權 guard 統一為 `CanActAsync(CurrentAssignee*)`（行為不變，順修 Doctor reassign 既有 bug）。

**Tech Stack:** .NET 10 / EF Core (SQLite→Postgres-safe) / xUnit / React 18 + Vite + Tailwind。

**Spec:** `docs/superpowers/specs/2026-07-19-case-transfer-design.md`（驗證規則順序、error codes 以 spec 為準）

---

### Task 1: Domain entity + EF config + migration

**Files:**
- Create: `bpm-svc/src/Domain/Entities/Transfer/CaseTransferLog.cs`
- Create: `bpm-svc/src/Persistence/Configurations/Transfer/CaseTransferLogConfiguration.cs`
- Create (generated): `bpm-svc/src/Persistence/Migrations/<ts>_CaseTransferLog.cs` + snapshot 重生

- [ ] **Step 1: entity**（形狀對齊 `Domain/Entities/Doctor/DoctorActionLog.cs`）

```csharp
namespace Bpm.Domain.Entities.Transfer;

/// <summary>
/// End-user case transfer (轉簽) audit trail — distinct from
/// <c>DoctorActionLog</c>, which records admin remediation.
/// </summary>
public sealed class CaseTransferLog
{
    public Guid Id { get; set; }
    public string FlowCode { get; set; } = "";
    public int FlowVersion { get; set; }
    public Guid CaseId { get; set; }
    public Guid FromUserId { get; set; }
    public Guid ToUserId { get; set; }
    /// <summary>Actual actor — differs from <see cref="FromUserId"/> when an accepted delegate transfers.</summary>
    public Guid OperatorUserId { get; set; }
    public string Reason { get; set; } = "";
    public DateTime CreatedAt { get; set; }
}
```

- [ ] **Step 2: EF configuration**（先開 `Configurations/Audit/DoctorActionLogConfiguration.cs`（或同資料夾同型檔）對齊 table naming / index 慣例；`AppDbContext` 用 assembly scan 就不用改，若是顯式 DbSet 慣例則補一行）

```csharp
using Bpm.Domain.Entities.Transfer;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Bpm.Persistence.Configurations.Transfer;

public sealed class CaseTransferLogConfiguration : IEntityTypeConfiguration<CaseTransferLog>
{
    public void Configure(EntityTypeBuilder<CaseTransferLog> b)
    {
        b.ToTable("CaseTransferLogs");
        b.HasKey(x => x.Id);
        b.Property(x => x.FlowCode).HasMaxLength(64).IsRequired();
        b.Property(x => x.Reason).HasMaxLength(2000).IsRequired();
        b.HasIndex(x => x.CaseId);
        b.HasIndex(x => x.OperatorUserId);
        b.HasIndex(x => x.CreatedAt);
    }
}
```

- [ ] **Step 3: migration**

Run: `cd bpm-svc && dotnet ef migrations add CaseTransferLog --project src/Persistence --startup-project src/Api`
（若 README 記的 flags 不同以 README 為準）
Expected: 新 migration 建 `CaseTransferLogs` 表；`git diff --stat` 只有 migration + snapshot + 新檔。

- [ ] **Step 4: build + 既有測試綠**

Run: `cd bpm-svc && dotnet build && dotnet test`
Expected: PASS（432+ 全綠）

- [ ] **Step 5: Commit** `feat(transfer): CaseTransferLog entity + migration`

---

### Task 2: Application 契約

**Files:**
- Create: `bpm-svc/src/Application/Transfer/ICaseTransferService.cs`

- [ ] **Step 1: interface + records**（Application 層無 EF 依賴）

```csharp
namespace Bpm.Application.Transfer;

/// <summary>
/// End-user case transfer (轉簽): the current personal-stage assignee
/// (or their accepted delegate) hands the case to another active user.
/// Role-queue stages are not transferable. See
/// docs/superpowers/specs/2026-07-19-case-transfer-design.md.
/// </summary>
public interface ICaseTransferService
{
    Task<TransferResult> TransferAsync(
        string flowCode, Guid caseId, Guid actorUserId, Guid toUserId,
        string? reason, CancellationToken ct = default);

    /// <summary>Active-user picker search（DisplayName / Email like，Take 20）。</summary>
    Task<IReadOnlyList<TransferCandidate>> CandidatesAsync(string? q, CancellationToken ct = default);
}

/// <param name="Error">null on success; otherwise one of:
/// unknown_flow / not_found_or_closed / role_stage_not_transferable /
/// no_current_assignee / not_current_assignee / target_not_active /
/// target_is_current / reason_required</param>
public sealed record TransferResult(bool Ok, string? Error = null);

public sealed record TransferCandidate(Guid UserId, string Name, string? Email);
```

- [ ] **Step 2: build**（`dotnet build`）→ PASS
- [ ] **Step 3: Commit** `feat(transfer): ICaseTransferService contract`

---

### Task 3: CaseTransferService（TDD）

**Files:**
- Create: `bpm-svc/tests/Bpm.Tests/Transfer/CaseTransferServiceTests.cs`
- Create: `bpm-svc/src/Persistence/Transfer/CaseTransferService.cs`
- Modify: `bpm-svc/src/Persistence/DependencyInjection.cs`（AddScoped 一行）

測試 bootstrap 抄 `tests/Bpm.Tests/Parallel/ParallelApprovalServiceTests.cs`
（in-memory SQLite + `EnsureCreated` + `StubAuthorizer`）；另需 stub
`INotifyDispatcher`（收集 `NotifyMessage` 的 list sink）與 seed 一筆
`SharedPrincipal`（照 `Bpm.Tests.Common` 既有 helper；沒有就 new 直插）。
用 **OVERTIME_V1_Case** 當測試載具（個人指派關卡、欄位齊全）。

- [ ] **Step 1: 失敗測試——驗證規則 7 條 + 成功 2 條**

```csharp
// 名稱即規格（Arrange 各自 seed 一筆 OVERTIME_V1_Case + principals）：
[Fact] Transfer_unknown_flow_fails()                 // "NOPE" → unknown_flow
[Fact] Transfer_closed_case_fails()                  // CompletedAt != null → not_found_or_closed
[Fact] Transfer_role_stage_fails()                   // CurrentAssigneeRoleCode = "HR_MANAGER" → role_stage_not_transferable
[Fact] Transfer_by_non_assignee_fails()              // actor ≠ assignee 且 StubAuthorizer deny → not_current_assignee
[Fact] Transfer_to_inactive_target_fails()           // target principal Active=false → target_not_active
[Fact] Transfer_to_current_assignee_fails()          // to == assignee → target_is_current
[Fact] Transfer_without_reason_fails()               // reason "  " → reason_required
[Fact] Transfer_success_updates_assignee_and_logs()  // Ok；重讀 case 斷言 CurrentAssigneeUserId==to、LastActivityAt 更新；CaseTransferLogs 一筆（From/To/Operator/Reason 正確）
[Fact] Transfer_success_notifies_target_and_submitter() // stub sink 收到 1 則，Recipients 含 to + submitter，Context 有 caseId/flowCode/flowVersion
```

- [ ] **Step 2: 跑測試確認失敗**（type 不存在 → compile error 即失敗態）
- [ ] **Step 3: 實作**

```csharp
using System.Reflection;
using System.Text.RegularExpressions;
using Bpm.Application.Common.Abstractions;
using Bpm.Application.Common.Authorization;
using Bpm.Application.Notifications;
using Bpm.Application.Transfer;
using Bpm.Domain.Entities.Transfer;
using Bpm.Persistence.SharedIdentity;
using Microsoft.EntityFrameworkCore;

namespace Bpm.Persistence.Transfer;

/// <summary>
/// Shared end-user transfer primitive. Discovers model-B case tables by
/// reflection (like DoctorService) but mutates through EF Find/SaveChanges —
/// no raw SQL (root CLAUDE.md DB rule #1) — so SQLite and Postgres behave
/// identically. Validation order mirrors the spec; first failure wins.
/// </summary>
public sealed class CaseTransferService(
    AppDbContext db,
    IActorAuthorizer auth,
    IClock clock,
    INotifyDispatcher notify) : ICaseTransferService
{
    private static readonly Regex CaseTypeRe = new(@"^(?<code>.+)_V(?<ver>\d+)_Case$", RegexOptions.Compiled);

    private sealed record CaseTable(string Code, int Version, Type Clr);

    private IReadOnlyList<CaseTable> CaseTables()
        => db.Model.GetEntityTypes()
            .Select(e => new { e, m = CaseTypeRe.Match(e.ClrType.Name) })
            .Where(x => x.m.Success
                && x.e.ClrType.GetProperty("CurrentAssigneeUserId") is not null
                && x.e.ClrType.GetProperty("SubmitterUserId") is not null
                && x.e.ClrType.GetProperty("CompletedAt") is not null)
            .Select(x => new CaseTable(
                x.m.Groups["code"].Value,
                int.Parse(x.m.Groups["ver"].Value),
                x.e.ClrType))
            .ToList();

    private static object? Prop(object row, string name)
        => row.GetType().GetProperty(name)?.GetValue(row);

    private static void SetIfExists(object row, string name, object? value)
        => row.GetType().GetProperty(name)?.SetValue(row, value);

    public async Task<TransferResult> TransferAsync(
        string flowCode, Guid caseId, Guid actorUserId, Guid toUserId,
        string? reason, CancellationToken ct = default)
    {
        var t = CaseTables().Where(x => string.Equals(x.Code, flowCode, StringComparison.OrdinalIgnoreCase))
            .OrderByDescending(x => x.Version).FirstOrDefault();
        if (t is null) return new TransferResult(false, "unknown_flow");

        // Try every version of the flow — caseId is globally unique per table.
        object? entity = null;
        foreach (var v in CaseTables().Where(x => x.Code.Equals(t.Code, StringComparison.OrdinalIgnoreCase)))
        {
            entity = await db.FindAsync(v.Clr, new object[] { caseId }, ct).AsTask();
            if (entity is not null) { t = v; break; }
        }
        if (entity is null || Prop(entity, "CompletedAt") is not null)
            return new TransferResult(false, "not_found_or_closed");

        if (Prop(entity, "CurrentAssigneeRoleCode") is string role && !string.IsNullOrEmpty(role))
            return new TransferResult(false, "role_stage_not_transferable");

        if (Prop(entity, "CurrentAssigneeUserId") is not Guid current)
            return new TransferResult(false, "no_current_assignee");

        if (!await auth.CanActAsync(current, null, actorUserId, ct))
            return new TransferResult(false, "not_current_assignee");

        var target = await db.SharedPrincipals.AsNoTracking().FirstOrDefaultAsync(
            p => p.Id == toUserId && p.Type == SharedPrincipalType.User && p.Active && p.DeletedAt == null, ct);
        if (target is null) return new TransferResult(false, "target_not_active");

        if (toUserId == current) return new TransferResult(false, "target_is_current");

        if (string.IsNullOrWhiteSpace(reason)) return new TransferResult(false, "reason_required");

        var now = clock.UtcNow;
        SetIfExists(entity, "CurrentAssigneeUserId", toUserId);
        SetIfExists(entity, "LastActivityAt", now);

        db.Set<CaseTransferLog>().Add(new CaseTransferLog
        {
            Id = Guid.NewGuid(),
            FlowCode = t.Code,
            FlowVersion = t.Version,
            CaseId = caseId,
            FromUserId = current,
            ToUserId = toUserId,
            OperatorUserId = actorUserId,
            Reason = reason.Trim(),
            CreatedAt = now,
        });
        await db.SaveChangesAsync(ct);

        await NotifyAsync(t, caseId, entity, current, target, actorUserId, reason.Trim(), ct);
        return new TransferResult(true);
    }

    private async Task NotifyAsync(
        CaseTable t, Guid caseId, object entity, Guid fromUserId,
        SharedPrincipal target, Guid actorUserId, string reason, CancellationToken ct)
    {
        var fromName = await NameAsync(fromUserId, ct) ?? "前任簽核人";
        var recipients = new List<NotifyRecipient>
        {
            new(target.Id, target.Email, target.DisplayName),
        };
        if (Prop(entity, "SubmitterUserId") is Guid submitter && submitter != target.Id)
        {
            var p = await db.SharedPrincipals.AsNoTracking()
                .FirstOrDefaultAsync(x => x.Id == submitter, ct);
            if (p is not null) recipients.Add(new NotifyRecipient(p.Id, p.Email, p.DisplayName));
        }

        var subject = $"【轉簽】{fromName} 將一張 {t.Code} 案件轉簽給 {target.DisplayName}";
        var body =
            $"您好，\n\n{fromName} 已將案件轉簽給 {target.DisplayName}，理由：\n{reason}\n\n" +
            "請登入系統檢視並處理。";
        await notify.DispatchAsync(new NotifyMessage(
            SourceId: $"{t.Code}_V{t.Version}.notify_transfer",
            Subject: subject,
            Body: body,
            Channels: new[] { "in_app", "email" },
            Recipients: recipients,
            Context: new Dictionary<string, string?>
            {
                ["caseId"] = caseId.ToString(),
                ["flowCode"] = t.Code,
                ["flowVersion"] = t.Version.ToString(),
                ["actorUserId"] = actorUserId.ToString(),
            }), ct);
    }

    private async Task<string?> NameAsync(Guid id, CancellationToken ct)
        => await db.SharedPrincipals.AsNoTracking()
            .Where(p => p.Id == id).Select(p => p.DisplayName).FirstOrDefaultAsync(ct);

    public async Task<IReadOnlyList<TransferCandidate>> CandidatesAsync(string? q, CancellationToken ct = default)
    {
        var query = db.SharedPrincipals.AsNoTracking()
            .Where(p => p.Type == SharedPrincipalType.User && p.Active && p.DeletedAt == null);
        if (!string.IsNullOrWhiteSpace(q))
        {
            var s = q.Trim();
            query = query.Where(p => EF.Functions.Like(p.DisplayName, "%" + s + "%")
                || (p.Email != null && EF.Functions.Like(p.Email, "%" + s + "%")));
        }
        return await query.OrderBy(p => p.DisplayName).Take(20)
            .Select(p => new TransferCandidate(p.Id, p.DisplayName, p.Email))
            .ToListAsync(ct);
    }
}
```

註：`SharedPrincipalType` / `Active` / `DeletedAt` / `Email` /
`DisplayName` 的確切欄位名以 `Persistence/SharedIdentity/SharedPrincipal.cs`
為準（DoctorService `CandidatesAsync` 已用同組欄位，可對照）。
`notify_transfer` 的通知模板字串就放 service（generic primitive，
不屬於任何單一 flow 的 templates 檔）。

- [ ] **Step 4: DI 註冊**（`Persistence/DependencyInjection.cs`，放
  Doctor 註冊附近）：`services.AddScoped<ICaseTransferService, CaseTransferService>();`
- [ ] **Step 5: 跑新測試** `dotnet test --filter CaseTransferServiceTests` → 9 條全 PASS
- [ ] **Step 6: 全套測試** `dotnet test` → 全綠
- [ ] **Step 7: Commit** `feat(transfer): shared CaseTransferService + tests`

---

### Task 4: API controller

**Files:**
- Create: `bpm-svc/src/Api/Transfer/CaseTransferController.cs`

- [ ] **Step 1: controller**（base class 與 `RequireUserId()` 同
  `Api/Features/OVERTIME/V1/OVERTIME_V1_Controller.cs` 的 `BpmControllerBase`）

```csharp
using Bpm.Application.Transfer;
using Microsoft.AspNetCore.Mvc;

namespace Bpm.Api.Transfer;

[ApiController]
[Route("api/case-transfer")]
public sealed class CaseTransferController(ICaseTransferService transfers) : BpmControllerBase
{
    public sealed record TransferRequest(Guid ToUserId, string? Reason);
    public sealed record TransferResponse(bool Ok, string? Error);

    [HttpPost("{flowCode}/{caseId:guid}")]
    public async Task<ActionResult<TransferResponse>> Transfer(
        string flowCode, Guid caseId, [FromBody] TransferRequest req, CancellationToken ct)
    {
        var userId = RequireUserId();
        var r = await transfers.TransferAsync(flowCode, caseId, userId, req.ToUserId, req.Reason, ct);
        if (r.Ok) return Ok(new TransferResponse(true, null));
        return r.Error switch
        {
            "unknown_flow" or "not_found_or_closed" => NotFound(new TransferResponse(false, r.Error)),
            "not_current_assignee" => StatusCode(403, new TransferResponse(false, r.Error)),
            _ => BadRequest(new TransferResponse(false, r.Error)),
        };
    }

    [HttpGet("candidates")]
    public async Task<IReadOnlyList<TransferCandidate>> Candidates([FromQuery] string? q, CancellationToken ct)
    {
        RequireUserId();
        return await transfers.CandidatesAsync(q, ct);
    }
}
```

（`BpmControllerBase` 的 namespace / auth attribute 慣例照抄任一
feature controller；若 controller 需要 `[Authorize]` 顯式標註，同步加。）

- [ ] **Step 2: build + 手動 curl**（本機 boot：README 的 run 指令）

```bash
# dev login 取 token 後：
curl -s -X POST http://localhost:5000/api/case-transfer/OVERTIME/<caseId> \
  -H "Authorization: Bearer $TOKEN" -H "Content-Type: application/json" \
  -d '{"toUserId":"<guid>","reason":"測試"}'
```
Expected: 對真 case 200 `{"ok":true}`；亂 flowCode 404。

- [ ] **Step 3: Commit** `feat(transfer): case-transfer REST endpoints`

---

### Task 5: Guard 統一化（26 處）

**Files:**
- Modify: 19 個 `bpm-svc/src/Application/Features/*/V*/​*Service.cs`
  （APE, EOB, ETM, FAD, FAP, LEAVE, OVERTIME, PURCHASE_REQUEST, TEO,
  TRQ, VENDOR_EXPENSE, WFH V1–V6；COMMITTEE/CONTRACT 已是 role-aware 不動）

- [ ] **Step 1: 逐檔替換**。Pattern（單一形狀，26 處都長這樣）：

```csharp
// BEFORE（stage 欄位名各異：ManagerUserId / VerifierUserId / SignerUserId …）
if (c.ManagerUserId is not { } mgr || !await auth.CanActAsync(mgr, actorUserId, ct))
    throw new ForbiddenException(...);

// AFTER
if (!await auth.CanActAsync(c.CurrentAssigneeUserId, c.CurrentAssigneeRoleCode, actorUserId, ct))
    throw new ForbiddenException(...);
```

逐處注意：
1. 若綁定變數（`mgr` 等）在 guard 之後還有使用，保留一行
   `var mgr = c.ManagerUserId!.Value;`（或等價）在 guard 後。
2. 若該 flow 的 Case entity **沒有** `CurrentAssigneeRoleCode` 欄位
   （純個人指派流程），改用雙參 overload：
   `!await auth.CanActAsync(c.CurrentAssigneeUserId ?? Guid.Empty, actorUserId, ct)`
   ——先 `grep CurrentAssigneeRoleCode` 該 entity 確認。
3. 替換前先確認該 stage 的上一個轉移有把 `CurrentAssigneeUserId`
   設成同一個 stage 欄位值（`grep CurrentAssigneeUserId` 該 service
   逐賦值點對照）；發現不同步＝既有 bug，補賦值並在 commit
   message 註明。

- [ ] **Step 2: 全套測試** `dotnet test` → 全綠（回歸底線 432+）
- [ ] **Step 3: Commit** `refactor(flows): normalize stage guards to CurrentAssignee* — makes transfer/doctor reassign effective`

---

### Task 6: UI — api client + 共用 hook/modal

**Files:**
- Create: `bpm-ui/src/lib/api/transfer.ts`
- Create: `bpm-ui/src/components/CaseTransfer.tsx`

- [ ] **Step 1: api client**（fetch wrapper 用 `lib/apiFetch.ts` 既有慣例，
  形狀對照 `lib/api/delegation.ts`）

```ts
import { apiFetch } from '@/lib/apiFetch'

export type TransferCandidate = { userId: string; name: string; email: string | null }

export async function searchTransferCandidates(q: string): Promise<TransferCandidate[]> {
  return apiFetch(`/api/case-transfer/candidates?q=${encodeURIComponent(q)}`)
}

export async function transferCase(
  flowCode: string, caseId: string, toUserId: string, reason: string,
): Promise<{ ok: boolean; error: string | null }> {
  return apiFetch(`/api/case-transfer/${flowCode}/${caseId}`, {
    method: 'POST',
    body: JSON.stringify({ toUserId, reason }),
  })
}
```

- [ ] **Step 2: 共用 hook + modal**（`components/CaseTransfer.tsx`）。
  Modal 殼用 `components/ui/modal.tsx`（body 記得自帶 padding——
  慣例是 caller 包 `p-5`）；人選搜尋 UX 對照 `DelegationButton.tsx`
  （query state + debounce + 結果列表）；按鈕文案「轉簽」。

```tsx
import { useMemo, useState } from 'react'
import type { ActionFooterItem } from '@/components/ui/action-footer/ActionFooter'
import { searchTransferCandidates, transferCase, type TransferCandidate } from '@/lib/api/transfer'

export type UseCaseTransferArgs = {
  flowCode: string
  caseId: string
  /** case 還在跑（per-flow lifecycle 判斷後傳入） */
  isOpen: boolean
  currentAssigneeUserId: string | null
  currentAssigneeRoleCode: string | null
  viewerUserId: string | null
  delegatedFor: string[]
  /** 成功後 refetch case */
  onTransferred: () => void
}

/**
 * Shared 轉簽 action：只在「個人指派關卡 + 我（或我代理的人）是
 * 當前簽核人 + 案件未結」時回傳 ActionFooterItem。用法：
 *   const transfer = useCaseTransfer({...})
 *   ...footerActions 加 ...(transfer.action ? [transfer.action] : [])
 *   JSX 尾端渲染 {transfer.modal}
 */
export function useCaseTransfer(args: UseCaseTransferArgs): {
  action: ActionFooterItem | null
  modal: React.ReactNode
} {
  const [open, setOpen] = useState(false)
  const canTransfer =
    args.isOpen &&
    !args.currentAssigneeRoleCode &&
    !!args.currentAssigneeUserId &&
    !!args.viewerUserId &&
    (args.currentAssigneeUserId === args.viewerUserId ||
      args.delegatedFor.includes(args.currentAssigneeUserId))

  const action: ActionFooterItem | null = canTransfer
    ? { id: 'transfer', label: '轉簽', variant: 'secondary', onClick: () => setOpen(true) }
    : null

  const modal = open ? (
    <TransferModal
      flowCode={args.flowCode}
      caseId={args.caseId}
      onClose={() => setOpen(false)}
      onDone={() => { setOpen(false); args.onTransferred() }}
    />
  ) : null

  return useMemo(() => ({ action, modal }), [action, modal])
}
```

`TransferModal`（同檔下方）：搜尋框（onChange debounce 300ms →
`searchTransferCandidates`）、結果列（DisplayName + email，點選
highlight）、必填理由 textarea、「確認轉簽」primary +「取消」。
送出：`transferCase(...)`；error code → 中文訊息 map
（`role_stage_not_transferable`:「此關卡為角色佇列，不可轉簽」、
`target_not_active`:「對象已停用」、`reason_required`:「請填寫理由」、
`not_current_assignee`:「您不是目前簽核人」、其餘顯 generic）。
`variant: 'secondary'` 若 ActionFooterItem 無此 variant，用現有
非 destructive/primary 的一種（開 `ActionFooter.tsx` 對）。

- [ ] **Step 3: type-check** `cd bpm-ui && npx tsc -p tsconfig.app.json --noEmit` → 0 errors
- [ ] **Step 4: Commit** `feat(transfer-ui): shared useCaseTransfer hook + modal`

---

### Task 7: CaseDetail 接線（全部 model-B detail）

**Files:**
- Modify: `bpm-ui/src/features/<CODE>/V<N>/<CODE>_V<N>_CaseDetail.tsx`
  ×（APE, EOB, ETM, FAD, FAP, LEAVE, OVERTIME, PURCHASE_REQUEST, TEO,
  TRQ, VENDOR_EXPENSE, WFH V1–V6）＝19 檔；COMMITTEE / CONTRACT
  為 parallel/role 流程可跳過（沒有個人關卡就不會出現按鈕，
  接了也無害——為一致性照接）。

- [ ] **Step 1: 逐檔三行**（以 OVERTIME 為例；各檔的 lifecycle/
  viewer/delegatedFor 變數名就用該檔既有的）

```tsx
const transfer = useCaseTransfer({
  flowCode: 'OVERTIME', caseId: caseId!, isOpen: data?.status === 'PendingManager' || data?.status === 'PendingHr',
  currentAssigneeUserId: data?.currentAssigneeUserId ?? null,
  currentAssigneeRoleCode: data?.currentAssigneeRoleCode ?? null,
  viewerUserId, delegatedFor, onTransferred: refetch,
})
// footerActions useMemo 內：
if (transfer.action) actions.push(transfer.action)
// JSX <ActionFooter …/> 旁：
{transfer.modal}
```

（`isOpen` 用該 flow「非終態」的既有判斷；deps array 記得補
`transfer.action`。）

- [ ] **Step 2: type-check** `npx tsc -p tsconfig.app.json --noEmit` → 0 errors
- [ ] **Step 3: Commit** `feat(transfer-ui): wire transfer action into all case details`

---

### Task 8: chef conventions

**Files:**
- Modify: `chef/skill/conventions.md`

- [ ] **Step 1: 新增「Case transfer（轉簽）」段**（放 primitive 對照表附近）：

```markdown
### Case transfer（轉簽）— shared primitive，chef 只需三件事

1. 關卡授權 guard 一律寫
   `!await auth.CanActAsync(c.CurrentAssigneeUserId, c.CurrentAssigneeRoleCode, actorUserId, ct)`
   ——不要對 stage 專屬欄位（ManagerUserId 等）做授權判斷。
2. 每次狀態轉移必同步 `CurrentAssigneeUserId`（個人關卡）與
   `CurrentAssigneeRoleCode`（角色佇列關卡；個人關卡設 null）。
3. CaseDetail 接共用 hook（三行）：

    const transfer = useCaseTransfer({ flowCode: '<CODE>', caseId, isOpen: <非終態判斷>,
      currentAssigneeUserId: data?.currentAssigneeUserId ?? null,
      currentAssigneeRoleCode: data?.currentAssigneeRoleCode ?? null,
      viewerUserId, delegatedFor, onTransferred: refetch })
    // footerActions: if (transfer.action) actions.push(transfer.action)
    // JSX: {transfer.modal}

轉簽的驗證 / 軌跡（CaseTransferLogs）/ 通知全在 lead 的
`CaseTransferService`，chef 不用寫任何 per-flow transfer code。
```

- [ ] **Step 2: Commit** `docs(chef): case-transfer conventions`

---

### Task 9: 端到端整合測試（OVERTIME）

**Files:**
- Create: `bpm-svc/tests/Bpm.Tests/Transfer/CaseTransferFlowTests.cs`

- [ ] **Step 1: 失敗測試**（bootstrap 照 `Features/OVERTIME/V1/` 既有
  flow tests 的 fixture 慣例；真 `OVERTIME_V1_OvertimeService` +
  真 `ActorAuthorizer`（或既有 flow-test 用的 authorizer 佈線））

```csharp
[Fact]
public async Task Manager_transfers_then_new_assignee_approves_and_old_cannot()
{
    // 1. bob submit → PendingManager, CurrentAssignee = alice(manager)
    // 2. alice transfer → carol（reason 必填）via CaseTransferService
    // 3. alice ApproveByManagerAsync → ForbiddenException
    // 4. carol ApproveByManagerAsync → 成功推進（PendingHr 或 Completed 依時數）
    // 5. CaseTransferLogs 恰一筆 from=alice to=carol
}
```

- [ ] **Step 2: 跑它確認紅**（步驟 3 在 guard 統一化後才會 403——
  若紅的原因不是預期的，回頭查 Task 5 該 stage 的賦值同步）
- [ ] **Step 3: 修到綠 + 全套測試綠**
- [ ] **Step 4: Commit** `test(transfer): OVERTIME end-to-end transfer flow`

---

### Task 10: 本機實測 + 收尾

- [ ] **Step 1: boot 本機 stack**（bpm-svc + bpm-ui，照 README；
  參 memory：只需 bpm-svc + bpm-ui，persona 切換要 reload）
- [ ] **Step 2: Chrome 實測**（chrome-devtools，截圖 fullPage）：
  1. bob 開 OVERTIME 單 → alice（manager）開 case → 「轉簽」按鈕在
  2. 轉給 carol：搜尋選人、空理由被擋、填理由成功 → toast + footer 動作消失
  3. carol 收 in-app 鈴鐺通知、bob 也收到；carol 開 case 可核准
  4. alice 重開 case → 無動作按鈕
  5. 角色佇列關卡 case（LEAVE 進 VP/HR 關）→ 無「轉簽」按鈕
- [ ] **Step 3: 全套** `dotnet test` + `tsc -p tsconfig.app.json` 最後一輪
- [ ] **Step 4: Commit**（若實測有修）`fix(transfer): …`
- [ ] **Step 5: 回報 Jason**（TG）：完成摘要 + 截圖 + 「push 請用
  GitKraken；要不要部署雲端」
