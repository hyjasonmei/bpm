namespace Bpm.Application.Features.CONTRACT_REVIEW.V1;

/// <summary>
/// Pure render functions for CONTRACT_REVIEW V1 notifications. Parallel-approval
/// variant: on submit, every concurrent approver is notified (待並簽); the
/// submitter gets a submitted ack and a completed / rejected result.
/// </summary>
public static class CONTRACT_REVIEW_V1_NotificationTemplates
{
    public record Rendered(string Subject, string Body);

    public static Rendered RenderSubmitted(string title, string caseUrl)
        => new(
            "【已送出】您的合約審查已送出",
            $"合約「{title}」已送出，法務與財務並簽中。\n查看進度：{caseUrl}");

    public static Rendered RenderParallelAssign(string applicantName, string title, string caseUrl)
        => new(
            $"【待並簽】{applicantName} 的合約審查：{title}",
            $"申請人：{applicantName}\n合約：{title}\n\n此案需要您（法務 / 財務）並簽，請點此處理：{caseUrl}");

    public static Rendered RenderCompleted(string title, string caseUrl)
        => new(
            "【已完成】合約審查通過",
            $"合約「{title}」已完成法務與財務並簽、審查通過。\n查看：{caseUrl}");

    public static Rendered RenderRejected(string title, string caseUrl)
        => new(
            "【已退件】合約審查被退回",
            $"合約「{title}」在並簽中被退件（任一方退件即整案退回）。\n查看：{caseUrl}");
}
