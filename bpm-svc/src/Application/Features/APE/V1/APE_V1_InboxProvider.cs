using Bpm.Application.Common.Directory;
using Bpm.Application.Inbox;
using Bpm.Domain.Features.APE.V1;

namespace Bpm.Application.Features.APE.V1;

/// <summary>
/// Surface APE V1 cases on the unified inbox. "Mine" = cases the user
/// submitted; "Pending" = the manager approval step or a rejected case
/// bounced back to the submitter for resubmit.
/// </summary>
public sealed class APE_V1_InboxProvider(
    IAPE_V1_CaseStore store,
    IPrincipalDirectory directory) : ITypedInboxProvider
{
    public string FlowCode => APE_V1_AdvancePaymentService.FlowCode;
    public int FlowVersion => APE_V1_AdvancePaymentService.FlowVersion;

    public async Task<IReadOnlyList<InboxRow>> GetMineAsync(Guid userId, CancellationToken ct)
    {
        var cases = await store.FindMineAsync(userId, ct);
        if (cases.Count == 0) return Array.Empty<InboxRow>();
        return cases.Select(c => new InboxRow(
            CaseId: c.Id, FlowCode: FlowCode, FlowVersion: FlowVersion,
            Title: $"預支現金 · {c.Currency} {c.Amount:N0}",
            Status: ZhStatus(c.Status),
            SubmittedAt: c.SubmittedAt, LastActivityAt: c.LastActivityAt,
            DetailUrl: $"/cases/ape/{c.Id}")).ToList();
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
                Title: $"{who} 預支現金 · {c.Currency} {c.Amount:N0}",
                Status: ZhStatus(c.Status),
                SubmittedAt: c.SubmittedAt, LastActivityAt: c.LastActivityAt,
                DetailUrl: $"/cases/ape/{c.Id}");
        }).ToList();
    }

    private static string ZhStatus(APE_V1_CaseStatus s) => s switch
    {
        APE_V1_CaseStatus.PendingManager   => "待主管核准",
        APE_V1_CaseStatus.ResubmitRequired => "退回補件",
        APE_V1_CaseStatus.Completed        => "已核准",
        APE_V1_CaseStatus.Cancelled        => "已撤回",
        _ => s.ToString(),
    };
}
