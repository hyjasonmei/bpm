using Bpm.Application.Common.Directory;
using Bpm.Application.Inbox;
using Bpm.Domain.Features.TRQ.V1;

namespace Bpm.Application.Features.TRQ.V1;

/// <summary>
/// Surface TRQ V1 cases on the unified inbox. "Mine" lists cases the
/// user submitted; "Pending" lists cases waiting on the user — either
/// the manager approval step or a rejected case bounced back to the
/// submitter for resubmit.
/// </summary>
public sealed class TRQ_V1_InboxProvider(
    ITRQ_V1_CaseStore store,
    IPrincipalDirectory directory) : ITypedInboxProvider
{
    public string FlowCode => TRQ_V1_TravelRequestService.FlowCode;
    public int FlowVersion => TRQ_V1_TravelRequestService.FlowVersion;

    public async Task<IReadOnlyList<InboxRow>> GetMineAsync(Guid userId, CancellationToken ct)
    {
        var cases = await store.FindMineAsync(userId, ct);
        if (cases.Count == 0) return Array.Empty<InboxRow>();

        return cases.Select(c => new InboxRow(
            CaseId: c.Id,
            FlowCode: FlowCode,
            FlowVersion: FlowVersion,
            Title: TitleForOwner(c),
            Status: ZhStatus(c.Status),
            SubmittedAt: c.SubmittedAt,
            LastActivityAt: c.LastActivityAt,
            DetailUrl: $"/cases/trq/{c.Id}")).ToList();
    }

    public async Task<IReadOnlyList<InboxRow>> GetPendingAsync(Guid userId, CancellationToken ct)
    {
        var cases = await store.FindPendingAsync(userId, ct);
        if (cases.Count == 0) return Array.Empty<InboxRow>();

        var submitterIds = cases.Select(c => c.SubmitterUserId).Distinct().ToArray();
        var names = await directory.GetManyAsync(submitterIds, ct);

        return cases.Select(c =>
        {
            var who = names.GetValueOrDefault(c.SubmitterUserId)?.DisplayName ?? "—";
            return new InboxRow(
                CaseId: c.Id,
                FlowCode: FlowCode,
                FlowVersion: FlowVersion,
                Title: TitleForOther(who, c),
                Status: ZhStatus(c.Status),
                SubmittedAt: c.SubmittedAt,
                LastActivityAt: c.LastActivityAt,
                DetailUrl: $"/cases/trq/{c.Id}");
        }).ToList();
    }

    private static string TitleForOwner(TRQ_V1_Case c)
        => $"差旅申請 · {c.DepartureCity} → {c.DestinationCity}";

    private static string TitleForOther(string applicantName, TRQ_V1_Case c)
        => $"{applicantName} 差旅申請 · {c.DepartureCity} → {c.DestinationCity}";

    private static string ZhStatus(TRQ_V1_CaseStatus s) => s switch
    {
        TRQ_V1_CaseStatus.PendingManager   => "待主管核准",
        TRQ_V1_CaseStatus.ResubmitRequired => "退回補件",
        TRQ_V1_CaseStatus.Completed        => "已核准",
        TRQ_V1_CaseStatus.Cancelled        => "已撤回",
        _ => s.ToString(),
    };
}
