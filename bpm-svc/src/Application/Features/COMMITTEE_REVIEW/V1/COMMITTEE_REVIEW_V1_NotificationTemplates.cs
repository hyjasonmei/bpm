namespace Bpm.Application.Features.COMMITTEE_REVIEW.V1;

/// <summary>Pure render functions for COMMITTEE_REVIEW V1 notifications (quorum 2/3).</summary>
public static class COMMITTEE_REVIEW_V1_NotificationTemplates
{
    public record Rendered(string Subject, string Body);

    public static Rendered RenderSubmitted(string title, string caseUrl)
        => new("【已送出】您的委員會審議已送出",
            $"案由「{title}」已送出委員會（財務 / 法務 / 採購），任 2 位委員核准即通過。\n查看進度：{caseUrl}");

    public static Rendered RenderParallelAssign(string applicantName, string title, string caseUrl)
        => new($"【待審議】{applicantName} 的委員會案：{title}",
            $"申請人：{applicantName}\n案由：{title}\n\n此案送委員會審議（門檻 2/3），請點此處理：{caseUrl}");

    public static Rendered RenderCompleted(string title, string caseUrl)
        => new("【已通過】委員會審議通過",
            $"案由「{title}」已達委員會門檻（2/3）、審議通過。\n查看：{caseUrl}");

    public static Rendered RenderRejected(string title, string caseUrl)
        => new("【已退件】委員會審議被退回",
            $"案由「{title}」被委員退件（任一委員退件即整案退回）。\n查看：{caseUrl}");
}
