namespace Bpm.Application.Features.ETM.V1;

/// <summary>Pure render functions for ETM V1 notifications.</summary>
public static class ETM_V1_NotificationTemplates
{
    public record Rendered(string Subject, string Body);

    public static Rendered RenderSubmitted(string caseUrl)
        => new("【已收到】員工離職申請已送出",
               "員工離職申請已送出，等待主管核准中。\n" + $"查看進度：{caseUrl}");

    public static Rendered RenderAssign(string applicantName, string summary, string caseUrl)
        => new($"【待簽】{applicantName} 的員工離職申請",
               $"申請人：{applicantName}\n摘要：{summary}\n\n請點此處理：{caseUrl}");

    public static Rendered RenderHandover(string summary, string caseUrl)
        => new("【待交接】離職已核准，請完成交接",
               $"已核准，請完成交接（物品返還 / 帳號處理 / 欠款）。\n摘要：{summary}\n\n交接：{caseUrl}");
}
