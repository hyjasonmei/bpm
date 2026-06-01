namespace Bpm.Application.Features.APE.V1;

/// <summary>
/// Pure render functions for the APE V1 notifications. The bundle
/// declares one event-bound template (<c>n_submit</c>, on_submit →
/// current_approver), rendered as <see cref="RenderAssign"/> to the
/// resolved manager; a submitted acknowledgement to the applicant is
/// emitted via <see cref="RenderSubmitted"/> for parity.
/// </summary>
public static class APE_V1_NotificationTemplates
{
    public record Rendered(string Subject, string Body);

    public static Rendered RenderSubmitted(string caseUrl)
        => new(
            "【已收到】您的預支申請已送出",
            "您的預支現金申請已送出，等待主管核准中。\n" + $"查看進度：{caseUrl}");

    public static Rendered RenderAssign(string applicantName, string summary, string caseUrl)
        => new(
            $"【待簽】{applicantName} 的預支申請",
            $"申請人：{applicantName}\n摘要：{summary}\n\n請點此核准：{caseUrl}");
}
