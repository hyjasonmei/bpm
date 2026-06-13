using Bpm.Application.Common.Abstractions;
using Bpm.Application.Common.Authorization;
using Bpm.Application.Common.Directory;
using Bpm.Application.Common.Exceptions;
using Bpm.Application.Notifications;
using Bpm.Application.Org;
using Bpm.Domain.Features.LEAVE.V1;
using FluentValidation.Results;
using Microsoft.Extensions.Logging;

namespace Bpm.Application.Features.LEAVE.V1;

/// <summary>
/// State machine for LEAVE V1. One transition per public method.
/// All persistence + notification calls happen inside the caller's
/// EF unit-of-work — controllers call <c>SaveChangesAsync</c> after
/// every action.
/// </summary>
public sealed class LEAVE_V1_LeaveService(
    ILEAVE_V1_CaseStore store,
    IOrgChartReader org,
    IClock clock,
    ILogger<LEAVE_V1_LeaveService> log,
    INotifyDispatcher notify,
    IActorAuthorizer auth,
    IPrincipalDirectory directory)
{
    public const string FlowCode = "LEAVE";
    public const int FlowVersion = 1;

    public const string DaysGateThreshold = "7";   // spec: days >= 7 → VP

    public sealed record SubmitInput(
        Guid SubmitterUserId,
        string LeaveType,
        DateOnly StartDate,
        DateOnly EndDate,
        string Reason,
        Guid? CertFileId);

    public async Task<LEAVE_V1_Case> SubmitAsync(SubmitInput input, CancellationToken ct)
    {
        if (input.EndDate < input.StartDate)
            throw Invalid(nameof(input.EndDate), "end_date must be on or after start_date");
        if (string.IsNullOrWhiteSpace(input.LeaveType))
            throw Invalid(nameof(input.LeaveType), "leave_type is required");
        if (string.IsNullOrWhiteSpace(input.Reason))
            throw Invalid(nameof(input.Reason), "reason is required");
        if (CertRequired(input.LeaveType) && input.CertFileId == null)
            throw Invalid(nameof(input.CertFileId), $"cert is required for leave_type={input.LeaveType}");

        var days = ComputeBusinessDays(input.StartDate, input.EndDate);

        var managerUserId = await ResolveManagerAsync(input.SubmitterUserId, ct);
        if (managerUserId == null)
            throw new ConflictException("submitter has no manager assigned; cannot route approval");

        var now = clock.UtcNow;
        var c = new LEAVE_V1_Case
        {
            Id = Guid.NewGuid(),
            SubmitterUserId = input.SubmitterUserId,
            LeaveType = input.LeaveType,
            StartDate = input.StartDate,
            EndDate = input.EndDate,
            Days = days,
            Reason = input.Reason,
            CertFileId = input.CertFileId,
            Status = LEAVE_V1_CaseStatus.PendingManager,
            ManagerUserId = managerUserId,
            CurrentAssigneeUserId = managerUserId,
            SubmittedAt = now,
            LastActivityAt = now,
        };
        store.Add(c);
        await store.SaveChangesAsync(ct);

        await NotifyAssignAsync(c, managerUserId.Value, ct);
        return c;
    }

    public async Task<LEAVE_V1_Case> ManagerDecisionAsync(Guid caseId, Guid actorUserId, bool approve, string? comment, CancellationToken ct)
    {
        var c = await store.FindByIdAsync(caseId, ct)
                ?? throw new NotFoundException("LEAVE_V1_Case", caseId);

        if (c.Status != LEAVE_V1_CaseStatus.PendingManager)
            throw new ConflictException($"case is in status {c.Status}, expected PendingManager");
        if (c.ManagerUserId is not { } mgr || !await auth.CanActAsync(mgr, actorUserId, ct))
            throw new ForbiddenException("only the assigned manager (or their active delegate) may act on this case");

        c.ManagerApproved = approve;
        c.ManagerComment = comment;
        c.ManagerDecisionAt = clock.UtcNow;
        c.LastActivityAt = clock.UtcNow;

        if (!approve)
        {
            c.Status = LEAVE_V1_CaseStatus.Rejected;
            c.CurrentAssigneeUserId = null;
            c.CompletedAt = clock.UtcNow;
            await store.SaveChangesAsync(ct);
            log.LogInformation("LEAVE/{CaseId}: manager rejected", c.Id);
            return c;
        }

        // Gateway: days >= 7 → VP, else → HR
        if (c.Days >= 7m)
        {
            var vpUserId = await ResolveVpApproverAsync(c.SubmitterUserId, ct);
            if (vpUserId == null)
                throw new ConflictException("no VP approver could be resolved (dept_head primary, role:VP fallback)");
            c.VpUserId = vpUserId;
            c.CurrentAssigneeUserId = vpUserId;
            c.Status = LEAVE_V1_CaseStatus.PendingVp;
            await store.SaveChangesAsync(ct);
            await NotifyAssignAsync(c, vpUserId.Value, ct);
        }
        else
        {
            var hrUserId = await ResolveFirstUserInRoleAsync("HR_MANAGER", ct);
            if (hrUserId == null)
                throw new ConflictException("no user assigned to role:HR_MANAGER; cannot route HR archive");
            c.HrUserId = hrUserId;
            c.CurrentAssigneeUserId = hrUserId;
            c.Status = LEAVE_V1_CaseStatus.PendingHr;
            await store.SaveChangesAsync(ct);
            await NotifyAssignAsync(c, hrUserId.Value, ct);
        }

        return c;
    }

    /// <summary>
    /// Submitter withdraws their own case. Allowed in any non-terminal
    /// state (PendingManager / PendingVp / PendingHr); once Completed,
    /// Rejected or already Cancelled it can no longer be withdrawn.
    /// Clears the assignee so the case drops out of every inbox.
    /// </summary>
    public async Task<LEAVE_V1_Case> CancelAsync(Guid caseId, Guid actorUserId, CancellationToken ct)
    {
        var c = await store.FindByIdAsync(caseId, ct)
                ?? throw new NotFoundException("LEAVE_V1_Case", caseId);
        if (c.SubmitterUserId != actorUserId)
            throw new ForbiddenException("only the original submitter may withdraw this case");
        if (c.Status is LEAVE_V1_CaseStatus.Completed or LEAVE_V1_CaseStatus.Rejected or LEAVE_V1_CaseStatus.Cancelled)
            throw new ConflictException($"case is in status {c.Status} and can no longer be withdrawn");

        c.Status = LEAVE_V1_CaseStatus.Cancelled;
        c.CurrentAssigneeUserId = null;
        c.CompletedAt = clock.UtcNow;
        c.LastActivityAt = clock.UtcNow;
        await store.SaveChangesAsync(ct);
        log.LogInformation("LEAVE/{CaseId}: withdrawn by submitter", c.Id);
        return c;
    }

    public async Task<LEAVE_V1_Case> VpDecisionAsync(Guid caseId, Guid actorUserId, bool approve, string? comment, CancellationToken ct)
    {
        var c = await store.FindByIdAsync(caseId, ct)
                ?? throw new NotFoundException("LEAVE_V1_Case", caseId);

        if (c.Status != LEAVE_V1_CaseStatus.PendingVp)
            throw new ConflictException($"case is in status {c.Status}, expected PendingVp");
        if (c.VpUserId is not { } vp || !await auth.CanActAsync(vp, actorUserId, ct))
            throw new ForbiddenException("only the assigned VP approver (or their active delegate) may act on this case");

        c.VpApproved = approve;
        c.VpComment = comment;
        c.VpDecisionAt = clock.UtcNow;
        c.LastActivityAt = clock.UtcNow;

        if (!approve)
        {
            c.Status = LEAVE_V1_CaseStatus.Rejected;
            c.CurrentAssigneeUserId = null;
            c.CompletedAt = clock.UtcNow;
            await store.SaveChangesAsync(ct);
            log.LogInformation("LEAVE/{CaseId}: VP rejected", c.Id);
            return c;
        }

        var hrUserId = await ResolveFirstUserInRoleAsync("HR_MANAGER", ct);
        if (hrUserId == null)
            throw new ConflictException("no user assigned to role:HR_MANAGER; cannot route HR archive");
        c.HrUserId = hrUserId;
        c.CurrentAssigneeUserId = hrUserId;
        c.Status = LEAVE_V1_CaseStatus.PendingHr;
        await store.SaveChangesAsync(ct);
        await NotifyAssignAsync(c, hrUserId.Value, ct);
        return c;
    }

    public async Task<LEAVE_V1_Case> HrArchiveAsync(Guid caseId, Guid actorUserId, string archiveNote, CancellationToken ct)
    {
        var c = await store.FindByIdAsync(caseId, ct)
                ?? throw new NotFoundException("LEAVE_V1_Case", caseId);

        if (c.Status != LEAVE_V1_CaseStatus.PendingHr)
            throw new ConflictException($"case is in status {c.Status}, expected PendingHr");
        if (c.HrUserId is not { } hr || !await auth.CanActAsync(hr, actorUserId, ct))
            throw new ForbiddenException("only the assigned HR (or their active delegate) may archive this case");
        if (string.IsNullOrWhiteSpace(archiveNote))
            throw Invalid("archiveNote", "archive_note is required");

        c.ArchiveNote = archiveNote;
        c.HrArchivedAt = clock.UtcNow;
        c.Status = LEAVE_V1_CaseStatus.Completed;
        c.CurrentAssigneeUserId = null;
        c.LastActivityAt = clock.UtcNow;
        c.CompletedAt = clock.UtcNow;
        await store.SaveChangesAsync(ct);

        await NotifyCompleteAsync(c, ct);
        return c;
    }

    // ------------------------------------------------------------
    // Helpers
    // ------------------------------------------------------------

    private static ValidationException Invalid(string field, string message)
        => new(new[] { new ValidationFailure(field, message) });

    public static bool CertRequired(string leaveType)
        => leaveType == "病假" || leaveType == "公假";

    /// <summary>
    /// Business days between start and end (inclusive), Mon-Fri only.
    /// Mirrors the TS-side businessDaysBetween in the form so client +
    /// server arrive at the same `days` value.
    /// </summary>
    public static decimal ComputeBusinessDays(DateOnly start, DateOnly end)
    {
        if (end < start) return 0m;
        int count = 0;
        for (var d = start; d <= end; d = d.AddDays(1))
        {
            var dow = d.DayOfWeek;
            if (dow != DayOfWeek.Saturday && dow != DayOfWeek.Sunday) count++;
        }
        return count;
    }

    public Task<Guid?> ResolveManagerAsync(Guid userId, CancellationToken ct)
        => org.GetManagerIdAsync(userId, ct);

    public async Task<Guid?> ResolveDeptHeadAsync(Guid submitterUserId, CancellationToken ct)
    {
        var deptId = await org.GetPrimaryDepartmentIdAsync(submitterUserId, ct);
        if (deptId == null) return null;

        var headId = await org.GetDepartmentHeadIdAsync(deptId.Value, ct);

        // Dept head can't approve their own leave — fall through.
        if (headId == submitterUserId) return null;
        return headId;
    }

    // Resolves the first active user holding role:<roleCode>, honouring
    // Dept / Group inheritance. Delegates to the shared directory so this
    // flow matches how every other flow resolves role assignees.
    public Task<Guid?> ResolveFirstUserInRoleAsync(string roleCode, CancellationToken ct)
        => directory.FindFirstUserInRoleAsync(roleCode, ct);

    /// <summary>
    /// VP-stage approver. Spec uses <c>submitter.department.head</c>
    /// with NL fallback "若部門主管不在，請走 VP 角色". Baked structurally
    /// as dept_head → first user in role:VP.
    /// </summary>
    public async Task<Guid?> ResolveVpApproverAsync(Guid submitterUserId, CancellationToken ct)
    {
        var head = await ResolveDeptHeadAsync(submitterUserId, ct);
        if (head != null) return head;
        return await ResolveFirstUserInRoleAsync("VP", ct);
    }

    private async Task NotifyAssignAsync(LEAVE_V1_Case c, Guid recipientUserId, CancellationToken ct)
    {
        var applicantName = await ResolveDisplayNameAsync(c.SubmitterUserId, ct);
        var recipientEmail = await ResolveEmailAsync(recipientUserId, ct);
        var recipientName  = await ResolveDisplayNameAsync(recipientUserId, ct);
        var rendered = LEAVE_V1_NotificationTemplates.RenderAssignManager(
            applicantName: applicantName,
            days: c.Days,
            leaveType: c.LeaveType,
            start: c.StartDate,
            end: c.EndDate,
            reason: c.Reason,
            caseUrl: $"/cases/leave/{c.Id}");
        await notify.DispatchAsync(new NotifyMessage(
            SourceId:   $"LEAVE_V1.notify_assign_manager",
            Subject:    rendered.Subject,
            Body:       rendered.Body,
            Channels:   new[] { "email", "in_app" },
            Recipients: new[] { new NotifyRecipient(recipientUserId, recipientEmail, recipientName) },
            Context:    new Dictionary<string, string?>
            {
                ["caseId"]      = c.Id.ToString(),
                ["flowCode"]    = FlowCode,
                ["flowVersion"] = FlowVersion.ToString(),
                ["stage"]       = c.Status.ToString(),
            }
        ), ct);
    }

    private async Task NotifyCompleteAsync(LEAVE_V1_Case c, CancellationToken ct)
    {
        var recipientEmail = await ResolveEmailAsync(c.SubmitterUserId, ct);
        var recipientName  = await ResolveDisplayNameAsync(c.SubmitterUserId, ct);
        var rendered = LEAVE_V1_NotificationTemplates.RenderComplete(
            submittedAt: c.SubmittedAt,
            days: c.Days,
            leaveType: c.LeaveType);
        await notify.DispatchAsync(new NotifyMessage(
            SourceId:   $"LEAVE_V1.notify_complete",
            Subject:    rendered.Subject,
            Body:       rendered.Body,
            Channels:   new[] { "email" },
            Recipients: new[] { new NotifyRecipient(c.SubmitterUserId, recipientEmail, recipientName) },
            Context:    new Dictionary<string, string?>
            {
                ["caseId"]      = c.Id.ToString(),
                ["flowCode"]    = FlowCode,
                ["flowVersion"] = FlowVersion.ToString(),
                ["stage"]       = c.Status.ToString(),
            }
        ), ct);
    }

    private async Task<string> ResolveDisplayNameAsync(Guid userId, CancellationToken ct)
    {
        var info = await directory.GetByIdAsync(userId, ct);
        var name = info?.DisplayName;
        return string.IsNullOrWhiteSpace(name) ? userId.ToString("N").Substring(0, 8) : name;
    }

    private async Task<string?> ResolveEmailAsync(Guid userId, CancellationToken ct)
        => (await directory.GetByIdAsync(userId, ct))?.Email;
}
