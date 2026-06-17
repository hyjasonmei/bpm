using Bpm.Domain.Features.WFH.V1;

namespace Bpm.Application.Features.WFH.V1;

/// <summary>
/// Pure render functions for the three WFH V1 notification templates
/// declared in the bundle:
/// <list type="bullet">
///   <item><c>notify_sgjz</c> (on_submit → submitter): <see cref="RenderSubmitted"/></item>
///   <item><c>notify_shnv</c> (on_assign → current_approver): <see cref="RenderAssign"/></item>
///   <item><c>notify_slfp</c> (on_reject → submitter): <see cref="RenderReject"/></item>
/// </list>
/// Subjects + bodies mirror the bundle's template text verbatim, with the
/// <c>{{caseUrl}}</c> / <c>{{applicant.name}}</c> / <c>{{summary}}</c> /
/// <c>{{rejectReason}}</c> tokens substituted.
/// </summary>
public static class WFH_V1_NotificationTemplates
{
    public record Rendered(string Subject, string Body);

    /// <summary>notify_sgjz — submitted acknowledgement to the applicant.</summary>
    public static Rendered RenderSubmitted(string caseUrl)
        => new(
            "【已收到】您的申請已送出",
            "您的申請已送出，等待主管核准中。\n" + $"查看進度：{caseUrl}");

    /// <summary>notify_shnv — "please sign" to the current approver.</summary>
    public static Rendered RenderAssign(string applicantName, string summary, string caseUrl)
        => new(
            $"【待簽】{applicantName} 的申請",
            $"申請人：{applicantName}\n摘要：{summary}\n\n請點此核准：{caseUrl}");

    /// <summary>notify_slfp — send-back / reject notice to the applicant.</summary>
    public static Rendered RenderReject(string rejectReason, string caseUrl)
        => new(
            "【被駁回】您的申請需修改",
            $"駁回原因：{rejectReason}\n請修正後重新送件：{caseUrl}");

    /// <summary>
    /// One-line case summary used in the assign template body — e.g.
    /// "居家辦公 — 5 天（2026/07/01 ~ 2026/07/05）".
    /// </summary>
    public static string BuildSummary(WFH_V1_Case c)
        => $"居家辦公 — {c.Days} 天（{c.StartDate:yyyy/MM/dd} ~ {c.EndDate:yyyy/MM/dd}）";
}
