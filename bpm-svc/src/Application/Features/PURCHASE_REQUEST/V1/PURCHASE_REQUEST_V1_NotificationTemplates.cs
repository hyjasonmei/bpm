namespace Bpm.Application.Features.PURCHASE_REQUEST.V1;

/// <summary>
/// Pure render functions for the two event-bound notification templates
/// declared in the bundle (<c>notify_ckqd</c> on_submit → submitter,
/// <c>notify_crme</c> on_assign → current_approver). The third bundle
/// template <c>notify_d3c1</c> (on_approve → role:HR) is intentionally
/// not rendered — it targets a role name that doesn't exist in admin
/// and was dropped during the chef planning round.
/// </summary>
public static class PURCHASE_REQUEST_V1_NotificationTemplates
{
    public record Rendered(string Subject, string Body);

    public static Rendered RenderSubmitted(string caseUrl)
    {
        var subject = "【已收到】您的申請已送出";
        var body =
            "您的申請已送出，等待主管核准中。\n" +
            $"查看進度：{caseUrl}";
        return new Rendered(subject, body);
    }

    public static Rendered RenderAssign(string applicantName, string summary, string caseUrl)
    {
        var subject = $"【待簽】{applicantName} 的申請";
        var body =
            $"申請人：{applicantName}\n" +
            $"摘要：{summary}\n\n" +
            $"請點此核准：{caseUrl}";
        return new Rendered(subject, body);
    }
}
