using Bpm.Application.Common.Directory;
using Bpm.Application.Inbox;
using Bpm.Domain.Features.FAP.V1;

namespace Bpm.Application.Features.FAP.V1;

/// <summary>Surface FAP V1 cases on the unified inbox.</summary>
public sealed class FAP_V1_InboxProvider(
    IFAP_V1_CaseStore store,
    IPrincipalDirectory directory) : ITypedInboxProvider
{
    public string FlowCode => FAP_V1_PurchaseService.FlowCode;
    public int FlowVersion => FAP_V1_PurchaseService.FlowVersion;

    public async Task<IReadOnlyList<InboxRow>> GetMineAsync(Guid userId, CancellationToken ct)
    {
        var cases = await store.FindMineAsync(userId, ct);
        if (cases.Count == 0) return Array.Empty<InboxRow>();
        return cases.Select(c => new InboxRow(
            CaseId: c.Id, FlowCode: FlowCode, FlowVersion: FlowVersion,
            Title: $"資產採購 · {c.PurchaseItems.Count} 項",
            Status: ZhStatus(c.Status),
            Lifecycle: InboxLifecycle.FromStatusName(c.Status.ToString()),
            SubmittedAt: c.SubmittedAt, LastActivityAt: c.LastActivityAt,
            DetailUrl: $"/cases/fap/{c.Id}")).ToList();
    }

    public async Task<IReadOnlyList<InboxRow>> GetPendingAsync(Guid userId, CancellationToken ct)
    {
        var cases = await store.FindPendingAsync(userId, ct);
        if (cases.Count == 0) return Array.Empty<InboxRow>();
        var names = await directory.GetManyAsync(cases.Select(c => c.SubmitterUserId).Distinct().ToArray(), ct);
        return cases.Select(c =>
        {
            var who = names.GetValueOrDefault(c.SubmitterUserId)?.DisplayName ?? "—";
            var verb = c.Status == FAP_V1_CaseStatus.PendingVerification ? "驗收" : "資產採購";
            return new InboxRow(
                CaseId: c.Id, FlowCode: FlowCode, FlowVersion: FlowVersion,
                Title: $"{who} {verb} · {c.PurchaseItems.Count} 項",
                Status: ZhStatus(c.Status),
                Lifecycle: InboxLifecycle.FromStatusName(c.Status.ToString()),
                SubmittedAt: c.SubmittedAt, LastActivityAt: c.LastActivityAt,
                DetailUrl: $"/cases/fap/{c.Id}");
        }).ToList();
    }

    private static string ZhStatus(FAP_V1_CaseStatus s) => s switch
    {
        FAP_V1_CaseStatus.PendingManager      => "待主管核准",
        FAP_V1_CaseStatus.PendingVerification => "待驗收",
        FAP_V1_CaseStatus.ResubmitRequired    => "退回補件",
        FAP_V1_CaseStatus.Completed           => "已入帳",
        FAP_V1_CaseStatus.Cancelled           => "已撤回",
        _ => s.ToString(),
    };
}
