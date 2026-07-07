using Bpm.Application.Inbox;
using Bpm.Domain.Entities.Attendance;

namespace Bpm.Application.Attendance;

/// <summary>
/// Surface attendance corrections on the unified inbox. "Mine" = my own
/// requests; "Pending" = requests from my direct reports awaiting my call.
/// Not a chef flow — DetailUrl points at the attendance review screen, not a
/// /cases route.
/// </summary>
public sealed class AttendanceCorrectionInboxProvider(
    IAttendanceCorrectionService corrections) : ITypedInboxProvider
{
    public string FlowCode => "ATTENDANCE_CORRECTION";
    public int FlowVersion => 1;

    public async Task<IReadOnlyList<InboxRow>> GetMineAsync(Guid userId, CancellationToken ct)
    {
        var rows = await corrections.MineAsync(userId, ct);
        return rows.Select(c => Row(c, $"補打卡：{Describe(c)}")).ToList();
    }

    public async Task<IReadOnlyList<InboxRow>> GetPendingAsync(Guid userId, CancellationToken ct)
    {
        var rows = await corrections.PendingForApproverAsync(userId, ct);
        return rows.Select(c => Row(c, $"{c.UserName} 補打卡：{Describe(c)}")).ToList();
    }

    private InboxRow Row(CorrectionDto c, string title) => new(
        CaseId: c.Id, FlowCode: FlowCode, FlowVersion: FlowVersion,
        Title: title,
        Status: ZhStatus(c.Status),
        Lifecycle: c.Status switch
        {
            CorrectionStatus.Pending => InboxLifecycle.Open,
            CorrectionStatus.Approved => InboxLifecycle.Completed,
            _ => InboxLifecycle.Rejected,
        },
        SubmittedAt: c.SubmittedAt, LastActivityAt: c.DecidedAt ?? c.SubmittedAt,
        DetailUrl: $"/attendance/corrections/{c.Id}");

    private static string Describe(CorrectionDto c)
        => $"{c.Date:MM/dd} {(c.PunchType == PunchType.In ? "上班卡" : "下班卡")}";

    private static string ZhStatus(CorrectionStatus s) => s switch
    {
        CorrectionStatus.Pending  => "待主管核准",
        CorrectionStatus.Approved => "已核准補卡",
        CorrectionStatus.Rejected => "已駁回",
        _ => s.ToString(),
    };
}
