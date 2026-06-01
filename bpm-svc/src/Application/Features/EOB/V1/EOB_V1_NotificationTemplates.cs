namespace Bpm.Application.Features.EOB.V1;

/// <summary>Pure render functions for EOB V1 notifications.</summary>
public static class EOB_V1_NotificationTemplates
{
    public record Rendered(string Subject, string Body);

    public static Rendered RenderSubmitted(string caseUrl)
        => new("【已收到】新進員工登入申請已送出",
               "新進員工登入申請已送出，等待主管核准中。\n" + $"查看進度：{caseUrl}");

    public static Rendered RenderAssign(string applicantName, string summary, string caseUrl)
        => new($"【待簽】{applicantName} 的新進員工登入申請",
               $"申請人：{applicantName}\n摘要：{summary}\n\n請點此處理：{caseUrl}");

    public static Rendered RenderSetup(string summary, string caseUrl)
        => new("【待設定】新進員工登入已核准，請完成基本設定",
               $"已核准，請完成新人基本設定（門禁卡 / 帳號 / 資產 / 座位 / Mentor）。\n摘要：{summary}\n\n設定：{caseUrl}");
}
