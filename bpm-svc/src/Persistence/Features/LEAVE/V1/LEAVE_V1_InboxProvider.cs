using System.Globalization;
using Bpm.Application.Inbox;
using Microsoft.EntityFrameworkCore;

namespace Bpm.Persistence.Features.LEAVE.V1;

/// <summary>
/// Surface LEAVE V1 cases on the unified inbox. "Mine" lists cases the
/// user submitted; "Pending" lists cases waiting on the user (manager
/// approval / VP approval / HR archive).
/// </summary>
public sealed class LEAVE_V1_InboxProvider(AppDbContext db) : ITypedInboxProvider
{
    public string FlowCode => LEAVE_V1_LeaveService.FlowCode;
    public int FlowVersion => LEAVE_V1_LeaveService.FlowVersion;

    public async Task<IReadOnlyList<InboxRow>> GetMineAsync(Guid userId, CancellationToken ct)
    {
        var cases = await db.LEAVE_V1_Cases.AsNoTracking()
            .Where(c => c.SubmitterUserId == userId)
            .OrderByDescending(c => c.LastActivityAt)
            .ToListAsync(ct);
        if (cases.Count == 0) return Array.Empty<InboxRow>();

        return cases.Select(c => new InboxRow(
            CaseId: c.Id,
            FlowCode: FlowCode,
            FlowVersion: FlowVersion,
            Title: $"{c.LeaveType} {FormatDays(c.Days)} 天",
            Status: ZhStatus(c.Status),
            SubmittedAt: c.SubmittedAt,
            LastActivityAt: c.LastActivityAt,
            DetailUrl: $"/cases/leave/{c.Id}")).ToList();
    }

    public async Task<IReadOnlyList<InboxRow>> GetPendingAsync(Guid userId, CancellationToken ct)
    {
        var cases = await db.LEAVE_V1_Cases.AsNoTracking()
            .Where(c => c.CurrentAssigneeUserId == userId
                        && c.Status != LEAVE_V1_CaseStatus.Completed
                        && c.Status != LEAVE_V1_CaseStatus.Rejected
                        && c.Status != LEAVE_V1_CaseStatus.Cancelled)
            .OrderByDescending(c => c.LastActivityAt)
            .ToListAsync(ct);
        if (cases.Count == 0) return Array.Empty<InboxRow>();

        var submitterIds = cases.Select(c => c.SubmitterUserId).Distinct().ToArray();
        var names = await db.SharedPrincipals.AsNoTracking()
            .Where(p => submitterIds.Contains(p.Id))
            .ToDictionaryAsync(p => p.Id, p => p.DisplayName, ct);

        return cases.Select(c =>
        {
            var who = names.GetValueOrDefault(c.SubmitterUserId, "—");
            return new InboxRow(
                CaseId: c.Id,
                FlowCode: FlowCode,
                FlowVersion: FlowVersion,
                Title: $"{who} 申請 {c.LeaveType} {FormatDays(c.Days)} 天",
                Status: ZhStatus(c.Status),
                SubmittedAt: c.SubmittedAt,
                LastActivityAt: c.LastActivityAt,
                DetailUrl: $"/cases/leave/{c.Id}");
        }).ToList();
    }

    private static string FormatDays(decimal d)
        => d == Math.Floor(d)
            ? d.ToString("0", CultureInfo.InvariantCulture)
            : d.ToString("0.0", CultureInfo.InvariantCulture);

    private static string ZhStatus(LEAVE_V1_CaseStatus s) => s switch
    {
        LEAVE_V1_CaseStatus.PendingManager => "待主管核准",
        LEAVE_V1_CaseStatus.PendingVp      => "待 VP 核准",
        LEAVE_V1_CaseStatus.PendingHr      => "待 HR 備案",
        LEAVE_V1_CaseStatus.Completed      => "已完成",
        LEAVE_V1_CaseStatus.Rejected       => "已退件",
        LEAVE_V1_CaseStatus.Cancelled      => "已取消",
        _ => s.ToString(),
    };
}
