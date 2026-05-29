using Bpm.Application.Common.Directory;
using Bpm.Application.Inbox;
using Bpm.Domain.Features.PURCHASE_REQUEST.V1;

namespace Bpm.Application.Features.PURCHASE_REQUEST.V1;

/// <summary>
/// Surface PURCHASE_REQUEST V1 cases on the unified inbox.
/// "Mine" lists cases the user submitted; "Pending" lists cases waiting
/// on the user — either an approval step (dept-head / finance) or a
/// rejected case bounced back to the submitter for resubmit.
/// </summary>
public sealed class PURCHASE_REQUEST_V1_InboxProvider(
    IPURCHASE_REQUEST_V1_CaseStore store,
    IPrincipalDirectory directory) : ITypedInboxProvider
{
    public string FlowCode => PURCHASE_REQUEST_V1_PurchaseRequestService.FlowCode;
    public int FlowVersion => PURCHASE_REQUEST_V1_PurchaseRequestService.FlowVersion;

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
            DetailUrl: $"/cases/purchase-request/{c.Id}")).ToList();
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
                DetailUrl: $"/cases/purchase-request/{c.Id}");
        }).ToList();
    }

    private static string TitleForOwner(PURCHASE_REQUEST_V1_Case c)
        => $"採購申請 · {c.Invoices.Count} {(c.Invoices.Count == 1 ? "invoice" : "invoices")}";

    private static string TitleForOther(string applicantName, PURCHASE_REQUEST_V1_Case c)
        => $"{applicantName} 採購申請 · {c.Invoices.Count} {(c.Invoices.Count == 1 ? "invoice" : "invoices")}";

    private static string ZhStatus(PURCHASE_REQUEST_V1_CaseStatus s) => s switch
    {
        PURCHASE_REQUEST_V1_CaseStatus.PendingDeptHead   => "待主管核准",
        PURCHASE_REQUEST_V1_CaseStatus.PendingFinance    => "待財務核准",
        PURCHASE_REQUEST_V1_CaseStatus.ResubmitRequired  => "退回補件",
        PURCHASE_REQUEST_V1_CaseStatus.Completed         => "已核准",
        _ => s.ToString(),
    };
}
