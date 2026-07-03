using Bpm.Application.Common.Directory;
using Bpm.Application.Inbox;
using Bpm.Domain.Features.ETM.V1;

namespace Bpm.Application.Features.ETM.V1;

/// <summary>Surface ETM V1 cases on the unified inbox.</summary>
public sealed class ETM_V1_InboxProvider(
    IETM_V1_CaseStore store,
    IPrincipalDirectory directory) : ITypedInboxProvider
{
    public string FlowCode => ETM_V1_TerminationService.FlowCode;
    public int FlowVersion => ETM_V1_TerminationService.FlowVersion;

    public async Task<IReadOnlyList<InboxRow>> GetMineAsync(Guid userId, CancellationToken ct)
    {
        var cases = await store.FindMineAsync(userId, ct);
        if (cases.Count == 0) return Array.Empty<InboxRow>();
        return cases.Select(c => new InboxRow(
            CaseId: c.Id, FlowCode: FlowCode, FlowVersion: FlowVersion,
            Title: $"員工離職 · {c.EmployeeName}",
            Status: ZhStatus(c.Status),
            Lifecycle: InboxLifecycle.FromStatusName(c.Status.ToString()),
            SubmittedAt: c.SubmittedAt, LastActivityAt: c.LastActivityAt,
            DetailUrl: $"/cases/etm/{c.Id}")).ToList();
    }

    public async Task<IReadOnlyList<InboxRow>> GetPendingAsync(Guid userId, CancellationToken ct)
    {
        var cases = await store.FindPendingAsync(userId, ct);
        if (cases.Count == 0) return Array.Empty<InboxRow>();
        var names = await directory.GetManyAsync(cases.Select(c => c.SubmitterUserId).Distinct().ToArray(), ct);
        return cases.Select(c =>
        {
            var who = names.GetValueOrDefault(c.SubmitterUserId)?.DisplayName ?? "—";
            var verb = c.Status == ETM_V1_CaseStatus.PendingHandover ? "交接" : "員工離職";
            return new InboxRow(
                CaseId: c.Id, FlowCode: FlowCode, FlowVersion: FlowVersion,
                Title: $"{who} {verb} · {c.EmployeeName}",
                Status: ZhStatus(c.Status),
                Lifecycle: InboxLifecycle.FromStatusName(c.Status.ToString()),
                SubmittedAt: c.SubmittedAt, LastActivityAt: c.LastActivityAt,
                DetailUrl: $"/cases/etm/{c.Id}");
        }).ToList();
    }

    private static string ZhStatus(ETM_V1_CaseStatus s) => s switch
    {
        ETM_V1_CaseStatus.PendingManager   => "待主管核准",
        ETM_V1_CaseStatus.PendingHandover  => "待交接",
        ETM_V1_CaseStatus.ResubmitRequired => "退回補件",
        ETM_V1_CaseStatus.Completed        => "已完成",
        ETM_V1_CaseStatus.Cancelled        => "已撤回",
        _ => s.ToString(),
    };
}
