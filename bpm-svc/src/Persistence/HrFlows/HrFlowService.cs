using System.Text.Json;
using Bpm.Application.Common.Abstractions;
using Bpm.Application.Common.Exceptions;
using Bpm.Application.HrFlows;
using Bpm.Application.HrFlows.Dtos;
using Bpm.Domain.Entities.Authz;
using Bpm.Domain.Entities.HrFlows;
using Bpm.Domain.Entities.Org;
using Microsoft.EntityFrameworkCore;

namespace Bpm.Persistence.HrFlows;

// Interim service. Sunset when add-process-runtime ships.
// See openspec/changes/add-hr-flows-resign-deptx/proposal.md.
public sealed class HrFlowService(AppDbContext db, IClock clock) : IHrFlowService
{
    private const string HrRoleCode = "hr";

    public async Task<HrFlowInstanceDto> StartAsync(HrFlowSpecCode specCode, JsonElement formData, Guid initiatorUserId, CancellationToken ct = default)
    {
        var initiator = await db.Users.FirstOrDefaultAsync(u => u.Id == initiatorUserId, ct)
            ?? throw new NotFoundException("User", initiatorUserId);

        if (initiator.ManagerId is null)
            throw new ConflictException("no manager assigned; cannot start HR flow");

        var now = clock.UtcNow;
        var instance = new HrFlowInstance
        {
            SpecCode = specCode,
            InitiatorUserId = initiatorUserId,
            ResolvedManagerUserId = initiator.ManagerId.Value,
            Status = HrFlowStatus.PendingManager,
            CurrentStep = HrFlowStep.ManagerApprove,
            FormDataJson = formData.GetRawText(),
            StartedAt = now,
            LastActivityAt = now,
        };

        db.HrFlowInstances.Add(instance);
        AddAction(instance.Id, initiatorUserId, HrFlowActionType.Submit, HrFlowStep.Apply, HrFlowStep.ManagerApprove, null, now);

        await db.SaveChangesAsync(ct);
        return await ToDto(instance.Id, ct);
    }

    public async Task<HrFlowInstanceDto> GetByIdAsync(Guid instanceId, Guid requesterUserId, CancellationToken ct = default)
    {
        var instance = await db.HrFlowInstances.FirstOrDefaultAsync(i => i.Id == instanceId, ct)
            ?? throw new NotFoundException("HrFlowInstance", instanceId);

        await EnsureCanRead(instance, requesterUserId, ct);
        return await ToDto(instance.Id, ct);
    }

    public async Task<IReadOnlyList<HrFlowSummaryDto>> GetMineAsync(Guid userId, CancellationToken ct = default)
    {
        var rows = await (
            from i in db.HrFlowInstances
            where i.InitiatorUserId == userId
            join u in db.Users on i.InitiatorUserId equals u.Id
            orderby i.LastActivityAt descending
            select new HrFlowSummaryDto(
                i.Id, i.SpecCode, i.InitiatorUserId, u.FullName,
                i.Status, i.CurrentStep, i.StartedAt, i.LastActivityAt))
            .ToListAsync(ct);
        return rows;
    }

    public async Task<IReadOnlyList<HrFlowSummaryDto>> GetMyTodoAsync(Guid userId, CancellationToken ct = default)
    {
        var isHr = await IsHr(userId, ct);

        var query =
            from i in db.HrFlowInstances
            join u in db.Users on i.InitiatorUserId equals u.Id
            where (i.Status == HrFlowStatus.PendingManager && i.ResolvedManagerUserId == userId)
               || (isHr && i.Status == HrFlowStatus.PendingHr)
            orderby i.LastActivityAt descending
            select new HrFlowSummaryDto(
                i.Id, i.SpecCode, i.InitiatorUserId, u.FullName,
                i.Status, i.CurrentStep, i.StartedAt, i.LastActivityAt);

        return await query.ToListAsync(ct);
    }

    public async Task<HrFlowInstanceDto> ApproveAsync(Guid instanceId, Guid actorUserId, string? comment, CancellationToken ct = default)
    {
        var instance = await db.HrFlowInstances.FirstOrDefaultAsync(i => i.Id == instanceId, ct)
            ?? throw new NotFoundException("HrFlowInstance", instanceId);

        var now = clock.UtcNow;

        switch (instance.Status)
        {
            case HrFlowStatus.PendingManager:
                if (instance.ResolvedManagerUserId != actorUserId)
                    throw new ForbiddenException("only the resolved manager can approve at this step");
                instance.Status = HrFlowStatus.PendingHr;
                instance.CurrentStep = HrFlowStep.HrApprove;
                instance.LastActivityAt = now;
                AddAction(instance.Id, actorUserId, HrFlowActionType.Approve, HrFlowStep.ManagerApprove, HrFlowStep.HrApprove, comment, now);
                break;

            case HrFlowStatus.PendingHr:
                if (!await IsHr(actorUserId, ct))
                    throw new ForbiddenException("only an HR user can approve at this step");
                instance.Status = HrFlowStatus.Completed;
                instance.CurrentStep = HrFlowStep.Closed;
                instance.LastActivityAt = now;
                instance.CompletedAt = now;
                AddAction(instance.Id, actorUserId, HrFlowActionType.Approve, HrFlowStep.HrApprove, HrFlowStep.Closed, comment, now);
                break;

            default:
                throw new ConflictException($"cannot approve from status {instance.Status}");
        }

        await db.SaveChangesAsync(ct);
        return await ToDto(instance.Id, ct);
    }

    public async Task<HrFlowInstanceDto> ReturnAsync(Guid instanceId, Guid actorUserId, string comment, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(comment))
            throw new ConflictException("return requires a non-empty comment");

        var instance = await db.HrFlowInstances.FirstOrDefaultAsync(i => i.Id == instanceId, ct)
            ?? throw new NotFoundException("HrFlowInstance", instanceId);

        if (instance.Status != HrFlowStatus.PendingManager)
            throw new ConflictException($"return is only allowed at PendingManager (current: {instance.Status})");
        if (instance.ResolvedManagerUserId != actorUserId)
            throw new ForbiddenException("only the resolved manager can return");

        var now = clock.UtcNow;
        instance.Status = HrFlowStatus.Returned;
        instance.CurrentStep = HrFlowStep.Apply;
        instance.LastActivityAt = now;
        AddAction(instance.Id, actorUserId, HrFlowActionType.Return, HrFlowStep.ManagerApprove, HrFlowStep.Apply, comment, now);

        await db.SaveChangesAsync(ct);
        return await ToDto(instance.Id, ct);
    }

    public async Task<HrFlowInstanceDto> ResubmitAsync(Guid instanceId, Guid actorUserId, JsonElement formData, CancellationToken ct = default)
    {
        var instance = await db.HrFlowInstances.FirstOrDefaultAsync(i => i.Id == instanceId, ct)
            ?? throw new NotFoundException("HrFlowInstance", instanceId);

        if (instance.Status != HrFlowStatus.Returned)
            throw new ConflictException($"resubmit is only allowed when status is Returned (current: {instance.Status})");
        if (instance.InitiatorUserId != actorUserId)
            throw new ForbiddenException("only the initiator can resubmit");

        var now = clock.UtcNow;
        instance.FormDataJson = formData.GetRawText();
        instance.Status = HrFlowStatus.PendingManager;
        instance.CurrentStep = HrFlowStep.ManagerApprove;
        instance.LastActivityAt = now;
        AddAction(instance.Id, actorUserId, HrFlowActionType.Submit, HrFlowStep.Apply, HrFlowStep.ManagerApprove, null, now);

        await db.SaveChangesAsync(ct);
        return await ToDto(instance.Id, ct);
    }

    public async Task<HrFlowInstanceDto> CancelAsync(Guid instanceId, Guid actorUserId, CancellationToken ct = default)
    {
        var instance = await db.HrFlowInstances.FirstOrDefaultAsync(i => i.Id == instanceId, ct)
            ?? throw new NotFoundException("HrFlowInstance", instanceId);

        if (instance.InitiatorUserId != actorUserId)
            throw new ForbiddenException("only the initiator can cancel");
        if (instance.Status == HrFlowStatus.Completed)
            throw new ConflictException("cannot cancel a completed instance");
        if (instance.Status == HrFlowStatus.Cancelled)
            throw new ConflictException("instance is already cancelled");

        var now = clock.UtcNow;
        var fromStep = instance.CurrentStep;
        instance.Status = HrFlowStatus.Cancelled;
        instance.LastActivityAt = now;
        instance.CancelledAt = now;
        AddAction(instance.Id, actorUserId, HrFlowActionType.Cancel, fromStep, fromStep, null, now);

        await db.SaveChangesAsync(ct);
        return await ToDto(instance.Id, ct);
    }

    private void AddAction(Guid instanceId, Guid actorUserId, HrFlowActionType action, HrFlowStep from, HrFlowStep to, string? comment, DateTime now)
    {
        db.HrFlowActions.Add(new HrFlowAction
        {
            InstanceId = instanceId,
            ActorUserId = actorUserId,
            Action = action,
            FromStep = from,
            ToStep = to,
            Comment = comment,
            CreatedAt = now,
            UpdatedAt = now,
        });
    }

    private async Task<bool> IsHr(Guid userId, CancellationToken ct)
    {
        return await (
            from ra in db.RoleAssignments
            join r in db.Roles on ra.RoleId equals r.Id
            where ra.PrincipalId == userId && r.Code == HrRoleCode
            select 1).AnyAsync(ct);
    }

    private async Task EnsureCanRead(HrFlowInstance instance, Guid requesterUserId, CancellationToken ct)
    {
        if (instance.InitiatorUserId == requesterUserId) return;
        if (instance.ResolvedManagerUserId == requesterUserId) return;
        if (instance.Status == HrFlowStatus.PendingHr && await IsHr(requesterUserId, ct)) return;
        throw new ForbiddenException("you do not have permission to read this instance");
    }

    private async Task<HrFlowInstanceDto> ToDto(Guid instanceId, CancellationToken ct)
    {
        var instance = await db.HrFlowInstances
            .AsNoTracking()
            .FirstAsync(i => i.Id == instanceId, ct);

        var initiator = await db.Users.AsNoTracking().FirstAsync(u => u.Id == instance.InitiatorUserId, ct);
        var manager = await db.Users.AsNoTracking().FirstAsync(u => u.Id == instance.ResolvedManagerUserId, ct);

        var actions = await (
            from a in db.HrFlowActions.AsNoTracking()
            where a.InstanceId == instance.Id
            join u in db.Users.AsNoTracking() on a.ActorUserId equals u.Id into au
            from u in au.DefaultIfEmpty()
            orderby a.CreatedAt
            select new HrFlowActionDto(
                a.Id, a.ActorUserId, u != null ? u.FullName : "(unknown)",
                a.Action, a.FromStep, a.ToStep, a.Comment, a.CreatedAt))
            .ToListAsync(ct);

        var formData = JsonDocument.Parse(string.IsNullOrWhiteSpace(instance.FormDataJson) ? "{}" : instance.FormDataJson).RootElement.Clone();

        return new HrFlowInstanceDto(
            instance.Id, instance.SpecCode,
            instance.InitiatorUserId, initiator.FullName,
            instance.ResolvedManagerUserId, manager.FullName,
            instance.Status, instance.CurrentStep,
            formData,
            instance.StartedAt, instance.LastActivityAt,
            instance.CompletedAt, instance.CancelledAt,
            actions);
    }
}
