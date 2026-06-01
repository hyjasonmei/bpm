using Bpm.Application.Common.Directory;
using Bpm.Application.Inbox;
using Bpm.Domain.Features.EOB.V1;

namespace Bpm.Application.Features.EOB.V1;

/// <summary>Surface EOB V1 cases on the unified inbox.</summary>
public sealed class EOB_V1_InboxProvider(
    IEOB_V1_CaseStore store,
    IPrincipalDirectory directory) : ITypedInboxProvider
{
    public string FlowCode => EOB_V1_OnboardingService.FlowCode;
    public int FlowVersion => EOB_V1_OnboardingService.FlowVersion;

    public async Task<IReadOnlyList<InboxRow>> GetMineAsync(Guid userId, CancellationToken ct)
    {
        var cases = await store.FindMineAsync(userId, ct);
        if (cases.Count == 0) return Array.Empty<InboxRow>();
        return cases.Select(c => new InboxRow(
            CaseId: c.Id, FlowCode: FlowCode, FlowVersion: FlowVersion,
            Title: $"新進員工登入 · {c.FirstName} {c.LastName}",
            Status: ZhStatus(c.Status),
            SubmittedAt: c.SubmittedAt, LastActivityAt: c.LastActivityAt,
            DetailUrl: $"/cases/eob/{c.Id}")).ToList();
    }

    public async Task<IReadOnlyList<InboxRow>> GetPendingAsync(Guid userId, CancellationToken ct)
    {
        var cases = await store.FindPendingAsync(userId, ct);
        if (cases.Count == 0) return Array.Empty<InboxRow>();
        var names = await directory.GetManyAsync(cases.Select(c => c.SubmitterUserId).Distinct().ToArray(), ct);
        return cases.Select(c =>
        {
            var who = names.GetValueOrDefault(c.SubmitterUserId)?.DisplayName ?? "—";
            var verb = c.Status == EOB_V1_CaseStatus.PendingSetup ? "基本設定" : "新進員工登入";
            return new InboxRow(
                CaseId: c.Id, FlowCode: FlowCode, FlowVersion: FlowVersion,
                Title: $"{who} {verb} · {c.FirstName} {c.LastName}",
                Status: ZhStatus(c.Status),
                SubmittedAt: c.SubmittedAt, LastActivityAt: c.LastActivityAt,
                DetailUrl: $"/cases/eob/{c.Id}");
        }).ToList();
    }

    private static string ZhStatus(EOB_V1_CaseStatus s) => s switch
    {
        EOB_V1_CaseStatus.PendingManager   => "待主管核准",
        EOB_V1_CaseStatus.PendingSetup     => "待基本設定",
        EOB_V1_CaseStatus.ResubmitRequired => "退回補件",
        EOB_V1_CaseStatus.Completed        => "已完成",
        _ => s.ToString(),
    };
}
