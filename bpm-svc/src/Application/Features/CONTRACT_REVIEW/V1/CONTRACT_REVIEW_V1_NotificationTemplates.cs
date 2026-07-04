namespace Bpm.Application.Features.CONTRACT_REVIEW.V1;

/// <summary>
/// Pure render functions for CONTRACT_REVIEW V1 notifications, mirroring the five
/// spec templates. Parallel-approval variant: on submit / resubmit every
/// concurrent reviewer (LEGAL + FINANCE) is notified; the submitter gets a
/// reject / completed result; the LEGAL_MANAGER gets a 待定案歸檔 hand-off once both
/// reviews pass.
/// </summary>
public static class CONTRACT_REVIEW_V1_NotificationTemplates
{
    public sealed record Rendered(string Subject, string Body);

    /// <summary>spec: notify_on_submit_reviewers — to role:Legal + role:Finance.</summary>
    public static Rendered RenderSubmitReviewers(
        string submitterName, string counterparty, string subject, string amount, string caseUrl)
        => new(
            $"【合約待審】{counterparty} 合約送審通知",
            $"您好，\n\n" +
            $"申請人 {submitterName} 已提交一份合約審查申請，請您盡快完成審查。\n\n" +
            $"• 對方公司：{counterparty}\n" +
            $"• 合約主旨：{subject}\n" +
            $"• 合約金額：{amount}\n" +
            $"• 案件連結：{caseUrl}\n\n" +
            $"請登入系統進行審查，謝謝。");

    /// <summary>spec: notify_on_reject_submitter — to the submitter.</summary>
    public static Rendered RenderRejectSubmitter(
        string submitterName, string counterparty, string approverName, string rejectReason, string caseUrl)
        => new(
            "【合約退回】您的合約審查申請已被退回",
            $"您好 {submitterName}，\n\n" +
            $"您提交的合約審查申請（對方公司：{counterparty}）已被退回。\n\n" +
            $"• 退回審查者：{approverName}\n" +
            $"• 退回意見：{rejectReason}\n" +
            $"• 案件連結：{caseUrl}\n\n" +
            $"請依退回意見修改後重新送審，謝謝。");

    /// <summary>spec: notify_on_approve_legal_mgr — to role:LegalManager.</summary>
    public static Rendered RenderApproveLegalMgr(
        string counterparty, string subject, string amount, string caseUrl)
        => new(
            $"【待定案歸檔】{counterparty} 合約已通過審查",
            $"您好，\n\n" +
            $"以下合約已通過法務與財務雙邊審查，請進行定案歸檔。\n\n" +
            $"• 對方公司：{counterparty}\n" +
            $"• 合約主旨：{subject}\n" +
            $"• 合約金額：{amount}\n" +
            $"• 案件連結：{caseUrl}\n\n" +
            $"請登入系統完成歸檔作業，謝謝。");

    /// <summary>spec: notify_on_complete_submitter — to the submitter.</summary>
    public static Rendered RenderCompleteSubmitter(
        string submitterName, string counterparty, string subject, string amount, string completedAt, string caseUrl)
        => new(
            "【審查完成】您的合約已完成定案歸檔",
            $"您好 {submitterName}，\n\n" +
            $"您提交的合約審查申請已完成全部流程，合約已正式定案歸檔。\n\n" +
            $"• 對方公司：{counterparty}\n" +
            $"• 合約主旨：{subject}\n" +
            $"• 合約金額：{amount}\n" +
            $"• 完成時間：{completedAt}\n" +
            $"• 案件連結：{caseUrl}\n\n" +
            $"感謝您的耐心配合！");

    /// <summary>spec: notify_on_resubmit_reviewers — to role:Legal + role:Finance.</summary>
    public static Rendered RenderResubmitReviewers(
        string submitterName, string counterparty, string subject, string amount, string revisionNote, string caseUrl)
        => new(
            $"【重新送審】{counterparty} 合約已修改，請重新審查",
            $"您好，\n\n" +
            $"申請人 {submitterName} 已針對退回意見完成修改，並重新提交合約審查申請，請您重新進行審查。\n\n" +
            $"• 對方公司：{counterparty}\n" +
            $"• 合約主旨：{subject}\n" +
            $"• 合約金額：{amount}\n" +
            $"• 修改說明：{revisionNote}\n" +
            $"• 案件連結：{caseUrl}\n\n" +
            $"請登入系統進行審查，謝謝。");
}
