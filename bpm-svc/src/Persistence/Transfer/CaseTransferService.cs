using System.Text.RegularExpressions;
using Bpm.Application.Common.Abstractions;
using Bpm.Application.Common.Authorization;
using Bpm.Application.Notifications;
using Bpm.Application.Transfer;
using Bpm.Domain.Entities.Transfer;
using Bpm.Persistence.SharedIdentity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace Bpm.Persistence.Transfer;

/// <summary>
/// Shared end-user transfer primitive (轉簽). Discovers model-B case tables by
/// reflection (same convention as <see cref="Doctor.DoctorService"/>) but
/// mutates through EF Find/SaveChanges — no raw SQL (root CLAUDE.md DB rule
/// #1) — so SQLite and Postgres behave identically. Validation order mirrors
/// the spec (docs/superpowers/specs/2026-07-19-case-transfer-design.md);
/// first failure wins. Requires the stage guards to authorize against
/// <c>CurrentAssignee*</c>, which is the flow-service convention.
/// </summary>
public sealed class CaseTransferService(
    AppDbContext db,
    IActorAuthorizer auth,
    IClock clock,
    INotifyDispatcher notify,
    ILogger<CaseTransferService> logger) : ICaseTransferService
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
        var versions = CaseTables()
            .Where(x => string.Equals(x.Code, flowCode, StringComparison.OrdinalIgnoreCase))
            .OrderByDescending(x => x.Version)
            .ToList();
        if (versions.Count == 0) return new TransferResult(false, "unknown_flow");

        // The caseId is unique per table; probe each version of the flow.
        CaseTable? t = null;
        object? entity = null;
        foreach (var v in versions)
        {
            entity = await db.FindAsync(v.Clr, new object[] { caseId }, ct).AsTask();
            if (entity is not null) { t = v; break; }
        }
        if (t is null || entity is null || Prop(entity, "CompletedAt") is not null)
            return new TransferResult(false, "not_found_or_closed");

        if (Prop(entity, "CurrentAssigneeRoleCode") is string role && !string.IsNullOrEmpty(role))
            return new TransferResult(false, "role_stage_not_transferable");

        if (Prop(entity, "CurrentAssigneeUserId") is not Guid current)
            return new TransferResult(false, "no_current_assignee");

        if (!await auth.CanActAsync(current, null, actorUserId, ct))
            return new TransferResult(false, "not_current_assignee");

        var target = await db.SharedPrincipals.AsNoTracking().FirstOrDefaultAsync(
            p => p.Id == toUserId && p.Type == SharedPrincipalType.User
                && p.Active && p.DeletedAt == null, ct);
        if (target is null) return new TransferResult(false, "target_not_active");

        if (toUserId == current) return new TransferResult(false, "target_is_current");

        if (string.IsNullOrWhiteSpace(reason)) return new TransferResult(false, "reason_required");

        var now = clock.UtcNow;
        SetIfExists(entity, "CurrentAssigneeUserId", (Guid?)toUserId);
        SetIfExists(entity, "LastActivityAt", now);

        db.CaseTransferLogs.Add(new CaseTransferLog
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

        // The transfer is already committed; a notification failure must not
        // surface as a 500 (the caller would retry a done transfer). Best-effort.
        try
        {
            await NotifyAsync(t, caseId, entity, current, target, actorUserId, reason.Trim(), ct);
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex,
                "Transfer of {Flow} case {CaseId} committed but notification failed",
                t.Code, caseId);
        }
        return new TransferResult(true);
    }

    private async Task NotifyAsync(
        CaseTable t, Guid caseId, object entity, Guid fromUserId,
        SharedPrincipal target, Guid actorUserId, string reason, CancellationToken ct)
    {
        var fromName = await db.SharedPrincipals.AsNoTracking()
            .Where(p => p.Id == fromUserId).Select(p => p.DisplayName)
            .FirstOrDefaultAsync(ct) ?? "前任簽核人";

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
