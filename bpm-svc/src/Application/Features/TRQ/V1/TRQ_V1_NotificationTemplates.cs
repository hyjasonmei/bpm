namespace Bpm.Application.Features.TRQ.V1;

/// <summary>
/// Pure render functions for the TRQ V1 notifications. The bundle
/// declares one event-bound template (<c>n_submit</c>, on_submit →
/// current_approver). We render that as <see cref="RenderAssign"/>
/// (sent to the resolved manager on submit / resubmit) and also emit a
/// submitted acknowledgement back to the applicant (<see
/// cref="RenderSubmitted"/>) for parity with the reference cook.
/// </summary>
public static class TRQ_V1_NotificationTemplates
{
    public record Rendered(string Subject, string Body);

    public static Rendered RenderSubmitted(string caseUrl)
    {
        var subject = "【已收到】您的差旅申請已送出";
        var body =
            "您的差旅申請已送出，等待主管核准中。\n" +
            $"查看進度：{caseUrl}";
        return new Rendered(subject, body);
    }

    public static Rendered RenderAssign(string applicantName, string summary, string caseUrl)
    {
        var subject = $"【待簽】{applicantName} 的差旅申請";
        var body =
            $"申請人：{applicantName}\n" +
            $"摘要：{summary}\n\n" +
            $"請點此核准：{caseUrl}";
        return new Rendered(subject, body);
    }
}
