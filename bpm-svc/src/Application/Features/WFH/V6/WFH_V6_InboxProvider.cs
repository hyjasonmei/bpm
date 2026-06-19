using Bpm.Application.Common.Directory;
using Bpm.Application.Inbox;
using Bpm.Domain.Features.WFH.V6;

namespace Bpm.Application.Features.WFH.V6;

/// <summary>
/// Surface WFH V6 cases on the unified inbox. "Mine" = cases the user
/// submitted; "Pending" = the manager / senior approval step or a
/// rejected case bounced back to the submitter for resubmit.
/// </summary>
public sealed class WFH_V6_InboxProvider(
    IWFH_V6_CaseStore store,
    IPrincipalDirectory directory) : ITypedInboxProvider
{
    public string FlowCode => WFH_V6_WfhService.FlowCode;
    public int FlowVersion => WFH_V6_WfhService.FlowVersion;

    public async Task<IReadOnlyList<InboxRow>> GetMineAsync(Guid userId, CancellationToken ct)
    {
        var cases = await store.FindMineAsync(userId, ct);
        if (cases.Count == 0) return Array.Empty<InboxRow>();
        return cases.Select(c => new InboxRow(
            CaseId: c.Id, FlowCode: FlowCode, FlowVersion: FlowVersion,
            Title: $"居家辦公 · {c.Days} 天",
            Status: ZhStatus(c.Status),
            SubmittedAt: c.SubmittedAt, LastActivityAt: c.LastActivityAt,
            DetailUrl: $"/cases/wfh/{c.Id}")).ToList();
    }

    public async Task<IReadOnlyList<InboxRow>> GetPendingAsync(Guid userId, CancellationToken ct)
    {
        var cases = await store.FindPendingAsync(userId, ct);
        if (cases.Count == 0) return Array.Empty<InboxRow>();
        var names = await directory.GetManyAsync(cases.Select(c => c.SubmitterUserId).Distinct().ToArray(), ct);
        return cases.Select(c =>
        {
            var who = names.GetValueOrDefault(c.SubmitterUserId)?.DisplayName ?? "—";
            return new InboxRow(
                CaseId: c.Id, FlowCode: FlowCode, FlowVersion: FlowVersion,
                Title: $"{who} 居家辦公 · {c.Days} 天",
                Status: ZhStatus(c.Status),
                SubmittedAt: c.SubmittedAt, LastActivityAt: c.LastActivityAt,
                DetailUrl: $"/cases/wfh/{c.Id}");
        }).ToList();
    }

    private static string ZhStatus(WFH_V6_CaseStatus s) => s switch
    {
        WFH_V6_CaseStatus.PendingManager   => "待主管核准",
        WFH_V6_CaseStatus.PendingSenior    => "待上級主管核准",
        WFH_V6_CaseStatus.ResubmitRequired => "退回補件",
        WFH_V6_CaseStatus.Completed        => "已核准",
        WFH_V6_CaseStatus.Cancelled        => "已撤回",
        _ => s.ToString(),
    };
}
