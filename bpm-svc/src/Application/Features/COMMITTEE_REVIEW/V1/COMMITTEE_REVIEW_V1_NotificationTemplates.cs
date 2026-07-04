namespace Bpm.Application.Features.COMMITTEE_REVIEW.V1;

/// <summary>
/// Pure render functions for COMMITTEE_REVIEW V1 notifications, mirroring the six
/// spec templates. Parallel-quorum variant: on submit / resubmit every committee
/// member (財務 / 採購 / 資訊) is notified; the submitter gets a 退回 / 核定 / 否決
/// result; the CEO gets a 待裁決 hand-off once the 2/3 門檻 is met.
/// </summary>
public static class COMMITTEE_REVIEW_V1_NotificationTemplates
{
    public sealed record Rendered(string Subject, string Body);

    /// <summary>spec: notify_committee_on_submit — to role:Finance + Procurement + IT.</summary>
    public static Rendered RenderSubmitCommittee(
        string caseTitle, string categoryLabel, string amount, string execPeriod, string caseUrl)
        => new(
            $"【委員會審議】新審議案待審：{caseTitle}",
            $"您好，\n\n" +
            $"一份新的審議案已提交，請於期限內完成審議。\n\n" +
            $"• 案由：{caseTitle}\n" +
            $"• 審議類別：{categoryLabel}\n" +
            $"• 申請金額：{amount}\n" +
            $"• 執行期間：{execPeriod}\n\n" +
            $"請點選以下連結進行審議：\n{caseUrl}\n\n" +
            $"如有疑問請聯繫申請人。");

    /// <summary>spec: notify_committee_on_resubmit — to role:Finance + Procurement + IT.</summary>
    public static Rendered RenderResubmitCommittee(
        string caseTitle, string categoryLabel, string amount, string execPeriod, string revisionNote, string caseUrl)
        => new(
            $"【委員會審議】修改後重新送審：{caseTitle}",
            $"您好，\n\n" +
            $"申請人已依退回意見完成修改，審議案重新提交，請再次進行審議。\n\n" +
            $"• 案由：{caseTitle}\n" +
            $"• 審議類別：{categoryLabel}\n" +
            $"• 申請金額：{amount}\n" +
            $"• 執行期間：{execPeriod}\n" +
            $"• 修改說明：{revisionNote}\n\n" +
            $"請點選以下連結進行審議：\n{caseUrl}\n\n" +
            $"謝謝。");

    /// <summary>spec: notify_return_to_applicant — to the submitter (任一委員退回).</summary>
    public static Rendered RenderReturnApplicant(
        string caseTitle, string amount, string rejectionComment, string caseUrl)
        => new(
            $"【委員會審議】您的審議案已被退回：{caseTitle}",
            $"您好，\n\n" +
            $"您提交的審議案經委員審議後，已被退回，請依退回意見修改後重新送審。\n\n" +
            $"• 案由：{caseTitle}\n" +
            $"• 申請金額：{amount}\n" +
            $"• 退回意見：{rejectionComment}\n\n" +
            $"請點選以下連結修改並重新送審：\n{caseUrl}\n\n" +
            $"謝謝。");

    /// <summary>spec: notify_ceo_on_assign — to role:CEO (門檻通過待裁決).</summary>
    public static Rendered RenderCeoAssign(
        string caseTitle, string categoryLabel, string amount, string execPeriod, int approvedCount, string caseUrl)
        => new(
            $"【委員會審議】審議案待您裁決：{caseTitle}",
            $"執行長您好，\n\n" +
            $"一份審議案已通過委員門檻審議（≥2 位委員核准），現需您進行最終裁決。\n\n" +
            $"• 案由：{caseTitle}\n" +
            $"• 審議類別：{categoryLabel}\n" +
            $"• 申請金額：{amount}\n" +
            $"• 執行期間：{execPeriod}\n" +
            $"• 委員核准票數：{approvedCount} / 3\n\n" +
            $"請點選以下連結進行裁決：\n{caseUrl}\n\n" +
            $"謝謝。");

    /// <summary>spec: notify_approved_to_applicant — to the submitter (執行長核定).</summary>
    public static Rendered RenderApprovedApplicant(
        string caseTitle, string categoryLabel, string amount, string execPeriod, string approvalComment, string caseUrl)
        => new(
            $"【委員會審議】恭喜！您的審議案已核定：{caseTitle}",
            $"您好，\n\n" +
            $"您提交的審議案已通過執行長最終裁決，正式核定。\n\n" +
            $"• 案由：{caseTitle}\n" +
            $"• 審議類別：{categoryLabel}\n" +
            $"• 核定金額：{amount}\n" +
            $"• 執行期間：{execPeriod}\n" +
            $"• 核定備註：{approvalComment}\n\n" +
            $"請依核定內容推進後續執行事宜。\n{caseUrl}\n\n" +
            $"謝謝。");

    /// <summary>spec: notify_rejected_to_applicant — to the submitter (執行長否決終局).</summary>
    public static Rendered RenderRejectedApplicant(
        string caseTitle, string categoryLabel, string amount, string rejectionReason, string caseUrl)
        => new(
            $"【委員會審議】您的審議案已否決（終局）：{caseTitle}",
            $"您好，\n\n" +
            $"您提交的審議案經執行長最終裁決後，已予否決，本案正式結案。\n\n" +
            $"• 案由：{caseTitle}\n" +
            $"• 審議類別：{categoryLabel}\n" +
            $"• 申請金額：{amount}\n" +
            $"• 否決原因：{rejectionReason}\n\n" +
            $"若有任何疑問，請洽主辦單位。\n{caseUrl}\n\n" +
            $"謝謝。");
}
