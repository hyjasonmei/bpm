using System.Globalization;
using Bpm.Application.Common.Identity;
using Bpm.Application.Common.Notifications;
using Bpm.Application.Common.Templates;
using Bpm.Domain.Cases;

namespace Bpm.Application.Travel.Services;

/// Emits the three notifications declared in spec.notifications[]:
///   notify_assign_approver (on_assign)   — to current_approver
///   notify_assign_admin    (on_assign)   — to role:Admin (when state enters PendingAdminBook)
///   notify_complete        (on_complete) — to submitter
public sealed class TravelNotificationEmitter(IIdentityProvider identity, INotificationSender sender)
{
    private const string SubjectAssignApprover =
        "【差旅待簽】{{applicant.name}} 申請 {{travel.destination}} {{travel.estimatedCost}} 元";
    private const string BodyAssignApprover =
        "申請人: {{applicant.name}}\n類型: {{travel.destinationType}}\n目的地: {{travel.destination}}\n期間: {{travel.depart}} - {{travel.return}}\n預估費用: {{travel.estimatedCost}} 元\n目的: {{travel.purpose}}\n\n請點此核准: {{caseUrl}}";

    private const string SubjectAssignAdmin =
        "【差旅待訂】{{applicant.name}} {{travel.destination}}";
    private const string BodyAssignAdmin =
        "案件已核准完畢，請協助訂票/訂房。\n申請人: {{applicant.name}}\n目的地: {{travel.destination}}\n期間: {{travel.depart}} - {{travel.return}}\n\n處理頁面: {{caseUrl}}";

    private const string SubjectComplete = "您的差旅申請已完成訂票";
    private const string BodyComplete =
        "您於 {{submitDate}} 申請的 {{travel.destination}} ({{travel.depart}} - {{travel.return}}) 已訂票完畢，票號: {{travel.ticketRef}}。";

    public async Task EmitOnAssignApproverAsync(TravelCase c, string caseUrl, CancellationToken ct = default)
    {
        if (string.IsNullOrEmpty(c.CurrentApproverUserId)) return;
        var applicant = await identity.FindByIdAsync(c.ApplicantUserId, ct);
        var approver = await identity.FindByIdAsync(c.CurrentApproverUserId, ct);
        if (approver is null) return;

        var values = ApplicantTravelValues(c, applicant);
        values["caseUrl"] = caseUrl;

        await sender.SendAsync(new NotificationMessage(
            Trigger: "on_assign",
            Channels: new[] { "email", "in_app" },
            Recipients: new[] { approver.Email },
            Subject: MustacheLite.Render(SubjectAssignApprover, values),
            Body: MustacheLite.Render(BodyAssignApprover, values)
        ), ct);
    }

    public async Task EmitOnAssignAdminAsync(TravelCase c, string caseUrl, CancellationToken ct = default)
    {
        var admin = await identity.FindByRoleAsync("Admin", ct);
        if (admin is null) return;
        var applicant = await identity.FindByIdAsync(c.ApplicantUserId, ct);

        var values = ApplicantTravelValues(c, applicant);
        values["caseUrl"] = caseUrl;

        await sender.SendAsync(new NotificationMessage(
            Trigger: "on_assign",
            Channels: new[] { "email", "in_app" },
            Recipients: new[] { admin.Email },
            Subject: MustacheLite.Render(SubjectAssignAdmin, values),
            Body: MustacheLite.Render(BodyAssignAdmin, values)
        ), ct);
    }

    public async Task EmitOnCompleteAsync(TravelCase c, CancellationToken ct = default)
    {
        var applicant = await identity.FindByIdAsync(c.ApplicantUserId, ct);
        if (applicant is null) return;

        var values = ApplicantTravelValues(c, applicant);
        values["submitDate"] = c.CreatedAt.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
        values["travel.ticketRef"] = c.TicketRef;

        await sender.SendAsync(new NotificationMessage(
            Trigger: "on_complete",
            Channels: new[] { "email" },
            Recipients: new[] { applicant.Email },
            Subject: MustacheLite.Render(SubjectComplete, values),
            Body: MustacheLite.Render(BodyComplete, values)
        ), ct);
    }

    private static Dictionary<string, string?> ApplicantTravelValues(TravelCase c, Employee? applicant) => new()
    {
        ["applicant.name"]          = applicant?.DisplayName ?? c.ApplicantUserId,
        ["travel.destinationType"]  = c.DestinationType,
        ["travel.destination"]      = c.Destination,
        ["travel.depart"]           = c.DepartDate.ToString("yyyy-MM-dd"),
        ["travel.return"]           = c.ReturnDate.ToString("yyyy-MM-dd"),
        ["travel.estimatedCost"]    = c.EstimatedCost.ToString("0", CultureInfo.InvariantCulture),
        ["travel.purpose"]          = c.Purpose,
    };
}
