namespace Bpm.Application.Features.FAD.V1;

/// <summary>Pure render functions for FAD V1 notifications.</summary>
public static class FAD_V1_NotificationTemplates
{
    public record Rendered(string Subject, string Body);

    public static Rendered RenderSubmitted(string caseUrl)
        => new("【已收到】您的資產處份申請已送出",
               "您的固定資產處份申請已送出，等待判別中。\n" + $"查看進度：{caseUrl}");

    public static Rendered RenderAssign(string applicantName, string summary, string caseUrl)
        => new($"【待判別】{applicantName} 的資產處份申請",
               $"申請人：{applicantName}\n摘要：{summary}\n\n請點此處理：{caseUrl}");

    public static Rendered RenderConfirm(string summary, string caseUrl)
        => new("【待領收確認】資產處份已核准",
               $"資產處份已核准，請完成領收確認。\n摘要：{summary}\n\n確認：{caseUrl}");
}
