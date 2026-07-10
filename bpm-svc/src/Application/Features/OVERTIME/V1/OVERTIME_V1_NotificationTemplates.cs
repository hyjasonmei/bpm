using System.Globalization;

namespace Bpm.Application.Features.OVERTIME.V1;

/// <summary>
/// Pure render functions for the OVERTIME V1 notification templates declared in
/// the bundle. Five distinct messages are rendered:
/// <list type="bullet">
///   <item><see cref="RenderAssignManager"/> — on_submit → 直屬主管 (notif_on_submit_manager).</item>
///   <item><see cref="RenderManagerApproved"/> — on_approve → 申請人, when the manager
///     approves and the case routes on to HR review (notif_on_approve_submitter).</item>
///   <item><see cref="RenderManagerRejected"/> — 主管退回 → 申請人 (notif_node_reject_manager).</item>
///   <item><see cref="RenderHrRejected"/> — 人資退回 → 申請人 (notif_node_reject_hr).</item>
///   <item><see cref="RenderFullyApproved"/> — 全程核准 → 申請人 + 人資 (notif_node_approve).</item>
/// </list>
/// When the manager approves a case whose monthly hours are under the threshold,
/// the case auto-completes and only <see cref="RenderFullyApproved"/> fires (the
/// intermediate manager-approved ack is skipped to avoid a duplicate).
/// </summary>
public static class OVERTIME_V1_NotificationTemplates
{
    public record Rendered(string Subject, string Body);

    public static string Hours(decimal? h)
        => (h ?? 0m).ToString("0.##", CultureInfo.InvariantCulture);

    private static string Date(DateOnly d) => d.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);

    public static Rendered RenderAssignManager(
        string applicantName, DateTime submitTime, DateOnly overtimeDate, decimal? estimatedHours, string reason)
    {
        var subject = $"【加班申請】{applicantName} 提交了加班申請，請審核";
        var body =
            $"您好，\n\n" +
            $"{applicantName} 於 {submitTime:yyyy-MM-dd HH:mm} 提交了加班申請，詳細資訊如下：\n\n" +
            $"・加班日期：{Date(overtimeDate)}\n" +
            $"・預估時數：{Hours(estimatedHours)} 小時\n" +
            $"・事由：{reason}\n\n" +
            $"請登入系統進行審核。";
        return new Rendered(subject, body);
    }

    public static Rendered RenderManagerApproved(
        string applicantName, DateTime submitTime, DateOnly overtimeDate, decimal? estimatedHours)
    {
        var subject = "【加班申請】您的加班申請已核准";
        var body =
            $"您好，{applicantName}，\n\n" +
            $"您於 {submitTime:yyyy-MM-dd HH:mm} 提交的加班申請（加班日期：{Date(overtimeDate)}，" +
            $"預估時數：{Hours(estimatedHours)} 小時）已獲主管核准。\n\n" +
            $"如有疑問，請聯繫您的直屬主管。";
        return new Rendered(subject, body);
    }

    public static Rendered RenderManagerRejected(
        string applicantName, DateTime submitTime, DateOnly overtimeDate, decimal? estimatedHours, string? rejectReason)
    {
        var subject = "【加班申請】您的加班申請已被主管退回";
        var body =
            $"您好，{applicantName}，\n\n" +
            $"您於 {submitTime:yyyy-MM-dd HH:mm} 提交的加班申請（加班日期：{Date(overtimeDate)}，" +
            $"預估時數：{Hours(estimatedHours)} 小時）已被直屬主管退回。\n\n" +
            $"退回原因：{ReasonOrDash(rejectReason)}\n\n" +
            $"如需重新申請，請登入系統操作。";
        return new Rendered(subject, body);
    }

    public static Rendered RenderHrRejected(
        string applicantName, DateTime submitTime, DateOnly overtimeDate, decimal? estimatedHours, string? rejectReason)
    {
        var subject = "【加班申請】您的加班申請已被人資退回";
        var body =
            $"您好，{applicantName}，\n\n" +
            $"您於 {submitTime:yyyy-MM-dd HH:mm} 提交的加班申請（加班日期：{Date(overtimeDate)}，" +
            $"預估時數：{Hours(estimatedHours)} 小時）已被人資部門退回。\n\n" +
            $"退回原因：{ReasonOrDash(rejectReason)}\n\n" +
            $"如需重新申請，請登入系統操作。";
        return new Rendered(subject, body);
    }

    public static Rendered RenderFullyApproved(
        string applicantName, DateOnly overtimeDate, decimal? estimatedHours, string reason)
    {
        var subject = $"【加班申請】{applicantName} 的加班申請已全程核准";
        var body =
            $"您好，\n\n" +
            $"{applicantName} 的加班申請已完成全部審核流程並獲核准，詳情如下：\n\n" +
            $"・加班日期：{Date(overtimeDate)}\n" +
            $"・核准時數：{Hours(estimatedHours)} 小時\n" +
            $"・事由：{reason}\n\n" +
            $"人資部門請依規定完成後續薪資計算作業。";
        return new Rendered(subject, body);
    }

    private static string ReasonOrDash(string? reason)
        => string.IsNullOrWhiteSpace(reason) ? "（未填寫）" : reason.Trim();
}
