using Bpm.Application.Common.Abstractions;
using Bpm.Application.Common.Authorization;
using Bpm.Application.Common.Exceptions;
using Bpm.Application.Parallel;
using Bpm.Domain.Parallel;
using Microsoft.EntityFrameworkCore;

namespace Bpm.Persistence.Parallel;

/// <summary>
/// Impl of the shared parallel-approval primitive. Join rule (v1):
/// 1) any slot Rejected → group Rejected, remaining Pending → Skipped;
/// 2) else Approved count ≥ Threshold → group Approved, remaining Pending → Skipped;
/// 3) else stay Open.
/// </summary>
public sealed class ParallelApprovalService(AppDbContext db, IClock clock, IActorAuthorizer auth)
    : IParallelApprovalService
{
    public async Task<ParallelApprovalGroup> OpenAsync(string flowCode, int flowVersion, Guid caseId,
        string gatewayNodeId, IReadOnlyList<SlotSpec> slots, int threshold, CancellationToken ct)
    {
        if (slots.Count == 0) throw new ConflictException("parallel gateway needs at least one slot");
        if (threshold < 1 || threshold > slots.Count)
            throw new ConflictException($"threshold {threshold} out of range 1..{slots.Count}");

        var now = clock.UtcNow;
        var g = new ParallelApprovalGroup
        {
            Id = Guid.NewGuid(),
            FlowCode = flowCode,
            FlowVersion = flowVersion,
            CaseId = caseId,
            GatewayNodeId = gatewayNodeId,
            Threshold = threshold,
            TotalSlots = slots.Count,
            Status = ParallelGroupStatus.Open,
            OpenedAt = now,
            Slots = slots.Select(s => new ParallelApprovalSlot
            {
                Id = Guid.NewGuid(),
                NodeId = s.NodeId,
                AssigneeRoleCode = s.RoleCode,
                AssigneeUserId = s.UserId,
                Decision = SlotDecision.Pending,
            }).ToList(),
        };
        db.ParallelApprovalGroups.Add(g);
        await db.SaveChangesAsync(ct);
        return g;
    }

    public async Task<DecisionResult> DecideAsync(Guid slotId, Guid actorUserId, bool approve, string? comment, CancellationToken ct)
    {
        var slot = await db.ParallelApprovalSlots.FirstOrDefaultAsync(s => s.Id == slotId, ct)
                   ?? throw new NotFoundException(nameof(ParallelApprovalSlot), slotId);

        var g = await db.ParallelApprovalGroups.Include(x => x.Slots)
                    .FirstAsync(x => x.Id == slot.GroupId, ct);

        if (g.Status != ParallelGroupStatus.Open)
            throw new ConflictException("parallel approval already resolved");

        var live = g.Slots.First(s => s.Id == slotId);
        if (live.Decision != SlotDecision.Pending)
            throw new ConflictException("this slot has already been decided");

        // Role-aware (+ delegation) authorization via the shared authorizer.
        var canAct = await auth.CanActAsync(live.AssigneeUserId, live.AssigneeRoleCode, actorUserId, ct);
        if (!canAct) throw new ForbiddenException("you are not an assignee of this approval slot");

        live.Decision = approve ? SlotDecision.Approved : SlotDecision.Rejected;
        live.Comment = comment;
        live.DecisionByUserId = actorUserId;
        live.DecisionAt = clock.UtcNow;

        Recompute(g);
        await db.SaveChangesAsync(ct);
        return new DecisionResult(g.Status, g);
    }

    private void Recompute(ParallelApprovalGroup g)
    {
        if (g.Slots.Any(s => s.Decision == SlotDecision.Rejected))
            g.Status = ParallelGroupStatus.Rejected;
        else if (g.Slots.Count(s => s.Decision == SlotDecision.Approved) >= g.Threshold)
            g.Status = ParallelGroupStatus.Approved;
        else
            return; // still Open — nothing to skip

        foreach (var s in g.Slots.Where(s => s.Decision == SlotDecision.Pending))
            s.Decision = SlotDecision.Skipped;
        g.ResolvedAt = clock.UtcNow;
    }

    public Task<ParallelApprovalGroup?> GetAsync(Guid caseId, string gatewayNodeId, CancellationToken ct) =>
        db.ParallelApprovalGroups.Include(x => x.Slots)
          .FirstOrDefaultAsync(x => x.CaseId == caseId && x.GatewayNodeId == gatewayNodeId, ct);

    public async Task<IReadOnlyList<PendingSlot>> FindPendingForUserAsync(
        string flowCode, Guid userId, IReadOnlyCollection<string> roleCodes, CancellationToken ct)
    {
        var q = from s in db.ParallelApprovalSlots
                join g in db.ParallelApprovalGroups on s.GroupId equals g.Id
                where g.FlowCode == flowCode
                      && g.Status == ParallelGroupStatus.Open
                      && s.Decision == SlotDecision.Pending
                      && (s.AssigneeUserId == userId
                          || (s.AssigneeRoleCode != null && roleCodes.Contains(s.AssigneeRoleCode)))
                select new PendingSlot(s.Id, s.NodeId, g.CaseId, g.GatewayNodeId);
        return await q.ToListAsync(ct);
    }
}
