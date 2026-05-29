using System.Globalization;

namespace Bpm.Persistence.Features.LEAVE.V1;

/// <summary>
/// Pure render functions for the two spec-declared notification templates.
/// Body tokens that match the spec's <c>variables</c> list are substituted;
/// the wizard occasionally emits stray <c>{{token}}</c> placeholders that
/// don't correspond to any variable — those are stripped quietly.
/// </summary>
public static class LEAVE_V1_NotificationTemplates
{
    public record Rendered(string Subject, string Body);

    public static Rendered RenderAssignManager(
        string applicantName,
        decimal days,
        string leaveType,
        DateOnly start,
        DateOnly end,
        string reason,
        string caseUrl)
    {
        var subject = $"【請假待簽】{applicantName} 申請 {FormatDays(days)} 天 {leaveType}";
        var body =
            $"申請人: {applicantName}\n" +
            $"假別: {leaveType}\n" +
            $"期間: {start:yyyy-MM-dd} - {end:yyyy-MM-dd}\n" +
            $"事由: {reason}\n\n" +
            $"請點此核准: {caseUrl}";
        return new Rendered(subject, body);
    }

    public static Rendered RenderComplete(
        DateTime submittedAt,
        decimal days,
        string leaveType)
    {
        var subject = "您的請假已備案";
        var body = $"您於 {submittedAt:yyyy-MM-dd} 申請的 {FormatDays(days)} 天 {leaveType} 已完成備案。";
        return new Rendered(subject, body);
    }

    private static string FormatDays(decimal d)
        => d == Math.Floor(d)
            ? d.ToString("0", CultureInfo.InvariantCulture)
            : d.ToString("0.0", CultureInfo.InvariantCulture);
}
