namespace Bpm.Application.Features.VENDOR_EXPENSE.V1;

/// <summary>
/// Pure render functions for the two event-bound notification templates
/// declared in the bundle, both addressed to the submitter:
///
///  - <c>notify_qkje</c> (on_submit)  → "【已收到】您的申請已送出"
///  - <c>notify_qs19</c> (on_reject)  → "【被駁回】您的申請需修改"
///
/// The spec declares no on_assign template, so approvers are not
/// emailed — the unified inbox surfaces pending cases instead.
/// Template text + the <c>{{caseUrl}}</c> / <c>{{rejectReason}}</c>
/// tokens are copied verbatim from the bundle.
/// </summary>
public static class VENDOR_EXPENSE_V1_NotificationTemplates
{
    public record Rendered(string Subject, string Body);

    /// <summary>notify_qkje — fired to the submitter on submit.</summary>
    public static Rendered RenderSubmitted(string caseUrl)
    {
        var subject = "【已收到】您的申請已送出";
        var body =
            "您的申請已送出，等待主管核准中。\n" +
            $"查看進度：{caseUrl}";
        return new Rendered(subject, body);
    }

    /// <summary>
    /// Shared-role-queue assign — in_app only, fired to every holder of the
    /// role a case is now pending on (procurement). The bundle declares no
    /// on_assign email template, so this is an inbox-only nudge.
    /// </summary>
    public static Rendered RenderRoleAssign(string applicantName, string summary, string caseUrl)
    {
        var subject = $"【待審定】{applicantName} 的採購申請";
        var body =
            $"{summary}\n" +
            $"前往審定：{caseUrl}";
        return new Rendered(subject, body);
    }

    /// <summary>notify_qs19 — fired to the submitter on any reject.</summary>
    public static Rendered RenderRejected(string rejectReason, string caseUrl)
    {
        var subject = "【被駁回】您的申請需修改";
        var body =
            $"駁回原因：{rejectReason}\n" +
            $"請修正後重新送件：{caseUrl}";
        return new Rendered(subject, body);
    }
}
