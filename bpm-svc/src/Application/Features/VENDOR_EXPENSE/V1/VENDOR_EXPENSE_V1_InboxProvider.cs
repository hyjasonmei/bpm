using Bpm.Application.Common.Directory;
using Bpm.Application.Inbox;
using Bpm.Domain.Features.VENDOR_EXPENSE.V1;

namespace Bpm.Application.Features.VENDOR_EXPENSE.V1;

/// <summary>
/// Surface VENDOR_EXPENSE V1 cases on the unified inbox.
/// "Mine" lists cases the user submitted; "Pending" lists cases waiting
/// on the user — an approval step (supervisor / procurement / sign) or a
/// rejected case bounced back to the submitter for resubmit.
///
/// Auto-registered by the Application-assembly <c>ITypedInboxProvider</c>
/// scan in <c>Application/DependencyInjection.cs</c>.
/// </summary>
public sealed class VENDOR_EXPENSE_V1_InboxProvider(
    IVENDOR_EXPENSE_V1_CaseStore store,
    IPrincipalDirectory directory) : ITypedInboxProvider
{
    public string FlowCode => VENDOR_EXPENSE_V1_VendorExpenseService.FlowCode;
    public int FlowVersion => VENDOR_EXPENSE_V1_VendorExpenseService.FlowVersion;

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
            Lifecycle: InboxLifecycle.FromStatusName(c.Status.ToString()),
            SubmittedAt: c.SubmittedAt,
            LastActivityAt: c.LastActivityAt,
            DetailUrl: DetailUrl(c))).ToList();
    }

    public async Task<IReadOnlyList<InboxRow>> GetPendingAsync(Guid userId, CancellationToken ct)
    {
        var myRoles = await directory.GetRoleCodesForUserAsync(userId, ct);
        var cases = await store.FindPendingAsync(userId, myRoles, ct);
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
                Lifecycle: InboxLifecycle.FromStatusName(c.Status.ToString()),
                SubmittedAt: c.SubmittedAt,
                LastActivityAt: c.LastActivityAt,
                DetailUrl: DetailUrl(c));
        }).ToList();
    }

    // Underscore slug so the /cases/:flowCode router (which does
    // flowCode.toUpperCase()) resolves back to the FormCode "VENDOR_EXPENSE".
    private static string DetailUrl(VENDOR_EXPENSE_V1_Case c) => $"/cases/vendor_expense/{c.Id}";

    private static string Vendor(VENDOR_EXPENSE_V1_Case c)
        => string.IsNullOrWhiteSpace(c.Vendor) ? "採購申請" : c.Vendor!.Trim();

    private static string Count(VENDOR_EXPENSE_V1_Case c)
        => $"{c.Invoices.Count} {(c.Invoices.Count == 1 ? "invoice" : "invoices")}";

    private static string TitleForOwner(VENDOR_EXPENSE_V1_Case c)
        => $"{Vendor(c)} · {Count(c)}";

    private static string TitleForOther(string applicantName, VENDOR_EXPENSE_V1_Case c)
        => $"{applicantName} · {Vendor(c)} · {Count(c)}";

    private static string ZhStatus(VENDOR_EXPENSE_V1_CaseStatus s) => s switch
    {
        VENDOR_EXPENSE_V1_CaseStatus.PendingSupervisor  => "待主管審核",
        VENDOR_EXPENSE_V1_CaseStatus.PendingProcurement => "待採購審定",
        VENDOR_EXPENSE_V1_CaseStatus.PendingSign        => "待簽核",
        VENDOR_EXPENSE_V1_CaseStatus.ResubmitRequired   => "退回重填",
        VENDOR_EXPENSE_V1_CaseStatus.Completed          => "已核准",
        VENDOR_EXPENSE_V1_CaseStatus.Cancelled          => "已撤回",
        _ => s.ToString(),
    };
}
