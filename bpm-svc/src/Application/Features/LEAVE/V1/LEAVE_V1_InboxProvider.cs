using System.Globalization;
using Bpm.Application.Common.Directory;
using Bpm.Application.Inbox;
using Bpm.Domain.Features.LEAVE.V1;

namespace Bpm.Application.Features.LEAVE.V1;

/// <summary>
/// Surface LEAVE V1 cases on the unified inbox. "Mine" lists cases the
/// user submitted; "Pending" lists cases waiting on the user (manager
/// approval / VP approval / HR archive).
/// </summary>
public sealed class LEAVE_V1_InboxProvider(
    ILEAVE_V1_CaseStore store,
    IPrincipalDirectory directory) : ITypedInboxProvider
{
    public string FlowCode => LEAVE_V1_LeaveService.FlowCode;
    public int FlowVersion => LEAVE_V1_LeaveService.FlowVersion;

    public async Task<IReadOnlyList<InboxRow>> GetMineAsync(Guid userId, CancellationToken ct)
    {
        var cases = await store.FindMineAsync(userId, ct);
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
        var cases = await store.FindPendingAsync(userId, ct);
        if (cases.Count == 0) return Array.Empty<InboxRow>();

        var submitterIds = cases.Select(c => c.SubmitterUserId).Distinct().ToArray();
        var names = await directory.GetManyAsync(submitterIds, ct);

        return cases.Select(c =>
        {
            var who = names.TryGetValue(c.SubmitterUserId, out var info) ? info.DisplayName : "—";
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
