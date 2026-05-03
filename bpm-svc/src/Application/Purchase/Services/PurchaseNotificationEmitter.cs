using System.Globalization;
using Bpm.Application.Common.Identity;
using Bpm.Application.Common.Notifications;
using Bpm.Application.Common.Templates;
using Bpm.Domain.Cases;

namespace Bpm.Application.Purchase.Services;

/// Emits the three notifications declared in spec.notifications[]:
///   notify_assign_approver (on_assign)   — to current_approver  (manager / finance / ceo)
///   notify_assign_purchase (on_assign)   — to role:Purchase     (when state enters PendingPurchaseExec)
///   notify_complete        (on_complete) — to submitter
public sealed class PurchaseNotificationEmitter(IIdentityProvider identity, INotificationSender sender)
{
    private const string SubjectAssignApprover =
        "【採購待簽】{{applicant.name}} 申請 {{purchase.amount}} 元 ({{purchase.vendor}})";
    private const string BodyAssignApprover =
        "申請人: {{applicant.name}}\n供應商: {{purchase.vendor}}\n金額: {{purchase.amount}} 元\n類別: {{purchase.category}}\n理由: {{purchase.justification}}\n\n請點此核准: {{caseUrl}}";

    private const string SubjectAssignPurchase =
        "【採購待處理】{{purchase.vendor}} - {{purchase.amount}} 元";
    private const string BodyAssignPurchase =
        "案件已核准完畢，請開立 PO。\n供應商: {{purchase.vendor}}\n金額: {{purchase.amount}} 元\n\n處理頁面: {{caseUrl}}";

    private const string SubjectComplete = "您的採購申請已完成";
    private const string BodyComplete =
        "您於 {{submitDate}} 申請的 {{purchase.vendor}} {{purchase.amount}} 元已開立 PO ({{purchase.poNumber}})，預計 {{purchase.expectedDelivery}} 到貨。";

    public async Task EmitOnAssignApproverAsync(PurchaseCase c, string caseUrl, CancellationToken ct = default)
    {
        if (string.IsNullOrEmpty(c.CurrentApproverUserId)) return;
        var applicant = await identity.FindByIdAsync(c.ApplicantUserId, ct);
        var approver = await identity.FindByIdAsync(c.CurrentApproverUserId, ct);
        if (approver is null) return;

        var values = ApplicantPurchaseValues(c, applicant);
        values["caseUrl"] = caseUrl;

        await sender.SendAsync(new NotificationMessage(
            Trigger: "on_assign",
            Channels: new[] { "email", "in_app" },
            Recipients: new[] { approver.Email },
            Subject: MustacheLite.Render(SubjectAssignApprover, values),
            Body: MustacheLite.Render(BodyAssignApprover, values)
        ), ct);
    }

    public async Task EmitOnAssignPurchaseAsync(PurchaseCase c, string caseUrl, CancellationToken ct = default)
    {
        var purchase = await identity.FindByRoleAsync("Purchase", ct);
        if (purchase is null) return;
        var applicant = await identity.FindByIdAsync(c.ApplicantUserId, ct);

        var values = ApplicantPurchaseValues(c, applicant);
        values["caseUrl"] = caseUrl;

        await sender.SendAsync(new NotificationMessage(
            Trigger: "on_assign",
            Channels: new[] { "email", "in_app" },
            Recipients: new[] { purchase.Email },
            Subject: MustacheLite.Render(SubjectAssignPurchase, values),
            Body: MustacheLite.Render(BodyAssignPurchase, values)
        ), ct);
    }

    public async Task EmitOnCompleteAsync(PurchaseCase c, CancellationToken ct = default)
    {
        var applicant = await identity.FindByIdAsync(c.ApplicantUserId, ct);
        if (applicant is null) return;

        var values = ApplicantPurchaseValues(c, applicant);
        values["submitDate"] = c.CreatedAt.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
        values["purchase.poNumber"] = c.PoNumber;
        values["purchase.expectedDelivery"] = c.ExpectedDelivery?.ToString("yyyy-MM-dd");

        await sender.SendAsync(new NotificationMessage(
            Trigger: "on_complete",
            Channels: new[] { "email" },
            Recipients: new[] { applicant.Email },
            Subject: MustacheLite.Render(SubjectComplete, values),
            Body: MustacheLite.Render(BodyComplete, values)
        ), ct);
    }

    private static Dictionary<string, string?> ApplicantPurchaseValues(PurchaseCase c, Employee? applicant) => new()
    {
        ["applicant.name"]      = applicant?.DisplayName ?? c.ApplicantUserId,
        ["purchase.amount"]     = c.Amount.ToString("0", CultureInfo.InvariantCulture),
        ["purchase.vendor"]     = c.Vendor,
        ["purchase.category"]   = c.Category,
        ["purchase.justification"] = c.Justification,
    };
}
