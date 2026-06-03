using Bpm.Application.Common.Directory;
using Bpm.Application.Inbox;
using Bpm.Domain.Features.TEO.V1;

namespace Bpm.Application.Features.TEO.V1;

/// <summary>Surface TEO V1 cases on the unified inbox.</summary>
public sealed class TEO_V1_InboxProvider(
    ITEO_V1_CaseStore store,
    IPrincipalDirectory directory) : ITypedInboxProvider
{
    public string FlowCode => TEO_V1_TravelExpenseService.FlowCode;
    public int FlowVersion => TEO_V1_TravelExpenseService.FlowVersion;

    public async Task<IReadOnlyList<InboxRow>> GetMineAsync(Guid userId, CancellationToken ct)
    {
        var cases = await store.FindMineAsync(userId, ct);
        if (cases.Count == 0) return Array.Empty<InboxRow>();
        return cases.Select(c => new InboxRow(
            CaseId: c.Id, FlowCode: FlowCode, FlowVersion: FlowVersion,
            Title: $"差旅費用核銷 · {c.ExpenseItems.Count} 筆",
            Status: ZhStatus(c.Status),
            SubmittedAt: c.SubmittedAt, LastActivityAt: c.LastActivityAt,
            DetailUrl: $"/cases/teo/{c.Id}")).ToList();
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
                Title: $"{who} 差旅費用核銷 · {c.ExpenseItems.Count} 筆",
                Status: ZhStatus(c.Status),
                SubmittedAt: c.SubmittedAt, LastActivityAt: c.LastActivityAt,
                DetailUrl: $"/cases/teo/{c.Id}");
        }).ToList();
    }

    private static string ZhStatus(TEO_V1_CaseStatus s) => s switch
    {
        TEO_V1_CaseStatus.PendingManager   => "待主管核准",
        TEO_V1_CaseStatus.PendingFinance   => "待財務核准",
        TEO_V1_CaseStatus.ResubmitRequired => "退回補件",
        TEO_V1_CaseStatus.Completed        => "已核准",
        TEO_V1_CaseStatus.Cancelled        => "已撤回",
        _ => s.ToString(),
    };
}
