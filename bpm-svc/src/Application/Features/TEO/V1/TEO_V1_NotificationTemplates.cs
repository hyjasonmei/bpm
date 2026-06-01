namespace Bpm.Application.Features.TEO.V1;

/// <summary>Pure render functions for TEO V1 notifications.</summary>
public static class TEO_V1_NotificationTemplates
{
    public record Rendered(string Subject, string Body);

    public static Rendered RenderSubmitted(string caseUrl)
        => new("【已收到】您的差旅費用核銷已送出",
               "您的差旅費用核銷已送出，等待主管核准中。\n" + $"查看進度：{caseUrl}");

    public static Rendered RenderAssign(string applicantName, string summary, string caseUrl)
        => new($"【待簽】{applicantName} 的差旅費用核銷",
               $"申請人：{applicantName}\n摘要：{summary}\n\n請點此核准：{caseUrl}");
}
