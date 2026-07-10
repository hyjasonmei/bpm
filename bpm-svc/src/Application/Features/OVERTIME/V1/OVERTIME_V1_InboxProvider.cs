using Bpm.Application.Common.Directory;
using Bpm.Application.Inbox;
using Bpm.Domain.Features.OVERTIME.V1;

namespace Bpm.Application.Features.OVERTIME.V1;

/// <summary>
/// Surface OVERTIME V1 cases on the unified inbox. "Mine" lists cases the user
/// submitted; "Pending" lists cases waiting on the user — either the manager
/// step (direct assignee) or the HR review step (shared HR role queue).
/// </summary>
public sealed class OVERTIME_V1_InboxProvider(
    IOVERTIME_V1_CaseStore store,
    IPrincipalDirectory directory) : ITypedInboxProvider
{
    public string FlowCode => OVERTIME_V1_OvertimeService.FlowCode;
    public int FlowVersion => OVERTIME_V1_OvertimeService.FlowVersion;

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
            Lifecycle: Lifecycle(c.Status),
            SubmittedAt: c.SubmittedAt,
            LastActivityAt: c.LastActivityAt,
            DetailUrl: $"/cases/overtime/{c.Id}")).ToList();
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
                Lifecycle: Lifecycle(c.Status),
                SubmittedAt: c.SubmittedAt,
                LastActivityAt: c.LastActivityAt,
                DetailUrl: $"/cases/overtime/{c.Id}");
        }).ToList();
    }

    // Two distinct terminal reject states → both map to the canonical Rejected
    // lifecycle bucket (InboxLifecycle.FromStatusName only knows the literal
    // "Rejected", so this flow computes the bucket itself).
    private static string Lifecycle(OVERTIME_V1_CaseStatus s) => s switch
    {
        OVERTIME_V1_CaseStatus.Completed         => InboxLifecycle.Completed,
        OVERTIME_V1_CaseStatus.Cancelled         => InboxLifecycle.Cancelled,
        OVERTIME_V1_CaseStatus.RejectedByManager => InboxLifecycle.Rejected,
        OVERTIME_V1_CaseStatus.RejectedByHr      => InboxLifecycle.Rejected,
        _ => InboxLifecycle.Open,
    };

    private static string Hours(decimal? h) => OVERTIME_V1_NotificationTemplates.Hours(h);

    private static string TitleForOwner(OVERTIME_V1_Case c)
        => $"加班申請 · {c.OvertimeDate:yyyy-MM-dd} · {Hours(c.EstimatedHours)} 小時";

    private static string TitleForOther(string applicantName, OVERTIME_V1_Case c)
        => $"{applicantName} 申請加班 · {c.OvertimeDate:yyyy-MM-dd} · {Hours(c.EstimatedHours)} 小時";

    private static string ZhStatus(OVERTIME_V1_CaseStatus s) => s switch
    {
        OVERTIME_V1_CaseStatus.PendingManager    => "待主管審核",
        OVERTIME_V1_CaseStatus.PendingHr         => "待人資複核",
        OVERTIME_V1_CaseStatus.Completed         => "已核准",
        OVERTIME_V1_CaseStatus.RejectedByManager => "主管退回",
        OVERTIME_V1_CaseStatus.RejectedByHr      => "人資退回",
        OVERTIME_V1_CaseStatus.Cancelled         => "已撤回",
        _ => s.ToString(),
    };
}
