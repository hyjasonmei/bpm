using Bpm.Application.Common.Directory;
using Bpm.Application.Inbox;
using Bpm.Domain.Features.FAD.V1;

namespace Bpm.Application.Features.FAD.V1;

/// <summary>Surface FAD V1 cases on the unified inbox.</summary>
public sealed class FAD_V1_InboxProvider(
    IFAD_V1_CaseStore store,
    IPrincipalDirectory directory) : ITypedInboxProvider
{
    public string FlowCode => FAD_V1_DisposalService.FlowCode;
    public int FlowVersion => FAD_V1_DisposalService.FlowVersion;

    public async Task<IReadOnlyList<InboxRow>> GetMineAsync(Guid userId, CancellationToken ct)
    {
        var cases = await store.FindMineAsync(userId, ct);
        if (cases.Count == 0) return Array.Empty<InboxRow>();
        return cases.Select(c => new InboxRow(
            CaseId: c.Id, FlowCode: FlowCode, FlowVersion: FlowVersion,
            Title: $"資產處份 · {c.AssetName}",
            Status: ZhStatus(c.Status),
            SubmittedAt: c.SubmittedAt, LastActivityAt: c.LastActivityAt,
            DetailUrl: $"/cases/fad/{c.Id}")).ToList();
    }

    public async Task<IReadOnlyList<InboxRow>> GetPendingAsync(Guid userId, CancellationToken ct)
    {
        var cases = await store.FindPendingAsync(userId, ct);
        if (cases.Count == 0) return Array.Empty<InboxRow>();
        var names = await directory.GetManyAsync(cases.Select(c => c.SubmitterUserId).Distinct().ToArray(), ct);
        return cases.Select(c =>
        {
            var who = names.GetValueOrDefault(c.SubmitterUserId)?.DisplayName ?? "—";
            var verb = c.Status == FAD_V1_CaseStatus.PendingConfirm ? "領收確認" : "資產處份";
            return new InboxRow(
                CaseId: c.Id, FlowCode: FlowCode, FlowVersion: FlowVersion,
                Title: $"{who} {verb} · {c.AssetName}",
                Status: ZhStatus(c.Status),
                SubmittedAt: c.SubmittedAt, LastActivityAt: c.LastActivityAt,
                DetailUrl: $"/cases/fad/{c.Id}");
        }).ToList();
    }

    private static string ZhStatus(FAD_V1_CaseStatus s) => s switch
    {
        FAD_V1_CaseStatus.PendingManager   => "待判別",
        FAD_V1_CaseStatus.PendingConfirm   => "待領收確認",
        FAD_V1_CaseStatus.ResubmitRequired => "退回補件",
        FAD_V1_CaseStatus.Completed        => "已處份",
        FAD_V1_CaseStatus.Cancelled        => "已撤回",
        _ => s.ToString(),
    };
}
