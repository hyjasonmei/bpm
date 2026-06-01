namespace Bpm.Application.Features.FAP.V1;

/// <summary>Pure render functions for FAP V1 notifications.</summary>
public static class FAP_V1_NotificationTemplates
{
    public record Rendered(string Subject, string Body);

    public static Rendered RenderSubmitted(string caseUrl)
        => new("【已收到】您的資產採購申請已送出",
               "您的固定資產採購申請已送出，等待主管核准中。\n" + $"查看進度：{caseUrl}");

    public static Rendered RenderAssign(string applicantName, string summary, string caseUrl)
        => new($"【待簽】{applicantName} 的資產採購申請",
               $"申請人：{applicantName}\n摘要：{summary}\n\n請點此處理：{caseUrl}");

    public static Rendered RenderVerify(string summary, string caseUrl)
        => new("【待驗收】資產採購已核准，請驗收",
               $"採購單已成立，貨品到位後請完成驗收。\n摘要：{summary}\n\n驗收：{caseUrl}");
}
