using Bpm.Admin.Application.Audit;
using Bpm.Admin.Application.Flows;
using Bpm.Admin.Domain.Flows;
using Microsoft.EntityFrameworkCore;

namespace Bpm.Admin.Persistence.Flows;

public class FlowLifecycleService : IFlowLifecycleService
{
    private readonly AdminDbContext _db;
    private readonly IAuditLogger _audit;

    public FlowLifecycleService(AdminDbContext db, IAuditLogger audit)
    {
        _db = db;
        _audit = audit;
    }

    public async Task<Flow> CreateDraftAsync(string flowCode, string displayName, string? specJson, Guid? actorUserId, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(flowCode)) throw new FlowLifecycleException("flowCode required");
        if (string.IsNullOrWhiteSpace(displayName)) throw new FlowLifecycleException("displayName required");

        var row = new Flow
        {
            Id = Guid.NewGuid(),
            LineageId = Guid.NewGuid(),
            Version = 1,
            State = FlowState.Draft,
            FlowCode = flowCode.Trim().ToUpperInvariant(),
            DisplayName = displayName.Trim(),
            SpecJson = string.IsNullOrWhiteSpace(specJson) ? "{}" : specJson!,
            CreatedByUserId = actorUserId,
        };
        _db.Flows.Add(row);
        await _db.SaveChangesAsync(ct);

        await _audit.LogAsync(
            actionType: "flow_created",
            targetType: "flow",
            targetId: row.Id.ToString(),
            actorUserId: actorUserId,
            actorPrincipalId: null,
            after: new { row.Id, row.LineageId, row.Version, row.FlowCode, row.DisplayName, State = row.State.ToString() },
            ct: ct);

        return row;
    }

    public async Task<Flow> UpdateSpecAsync(Guid flowId, string specJson, string? flowCode, string? displayName, Guid? actorUserId, CancellationToken ct = default)
    {
        var row = await Load(flowId, ct);
        if (row.State != FlowState.Draft)
            throw new FlowLifecycleException($"Only Draft flows can be edited; current state is {row.State}");

        var before = SnapshotSpec(row);
        row.SpecJson = specJson ?? row.SpecJson;
        if (!string.IsNullOrWhiteSpace(flowCode)) row.FlowCode = flowCode.Trim().ToUpperInvariant();
        if (!string.IsNullOrWhiteSpace(displayName)) row.DisplayName = displayName.Trim();
        await _db.SaveChangesAsync(ct);

        await _audit.LogAsync(
            actionType: "flow_spec_updated",
            targetType: "flow",
            targetId: row.Id.ToString(),
            actorUserId: actorUserId,
            actorPrincipalId: null,
            before: before,
            after: SnapshotSpec(row),
            ct: ct);

        return row;
    }

    public Task<Flow> SubmitAsync(Guid flowId, Guid? actorUserId, CancellationToken ct = default)
        => TransitionAsync(flowId, FlowState.Submitted, "flow_submitted", actorUserId, new[] { FlowState.Draft }, ct);

    public Task<Flow> CancelAsync(Guid flowId, Guid? actorUserId, CancellationToken ct = default)
        => TransitionAsync(flowId, FlowState.Draft, "flow_cancelled", actorUserId,
            new[] { FlowState.Submitted, FlowState.Cooking, FlowState.OnHold }, ct);

    public Task<Flow> ResumeAsync(Guid flowId, Guid? actorUserId, CancellationToken ct = default)
        => TransitionAsync(flowId, FlowState.Submitted, "flow_resumed", actorUserId, new[] { FlowState.OnHold }, ct);

    public Task<Flow> RetireAsync(Guid flowId, Guid? actorUserId, CancellationToken ct = default)
        => TransitionAsync(flowId, FlowState.Retired, "flow_retired", actorUserId, new[] { FlowState.Approved }, ct);

    public Task<Flow> UnretireAsync(Guid flowId, Guid? actorUserId, CancellationToken ct = default)
        => TransitionAsync(flowId, FlowState.Approved, "flow_unretired", actorUserId, new[] { FlowState.Retired }, ct);

    // ── chef-driven transitions (PR-K1) ──────────────────────────────

    public Task<Flow> ChefAcceptAsync(Guid flowId, CancellationToken ct = default)
        => ChefTransitionAsync(flowId, FlowState.Cooking, "chef_accepted",
            new[] { FlowState.Submitted, FlowState.OnHold }, ct);

    public Task<Flow> ChefResumeAsync(Guid flowId, CancellationToken ct = default)
        => ChefTransitionAsync(flowId, FlowState.Cooking, "chef_resumed",
            new[] { FlowState.OnHold }, ct);

    public Task<Flow> ChefCommitAsync(Guid flowId, CancellationToken ct = default)
        => ChefTransitionAsync(flowId, FlowState.Committed, "chef_committed",
            new[] { FlowState.Cooking }, ct);

    public Task<Flow> ChefStallResetAsync(Guid flowId, Guid? actorUserId, CancellationToken ct = default)
        => TransitionAsync(flowId, FlowState.Submitted, "chef_stall_reset", actorUserId,
            new[] { FlowState.Cooking, FlowState.OnHold }, ct);

    public async Task BumpChefHeartbeatAsync(Guid flowId, CancellationToken ct = default)
    {
        var row = await Load(flowId, ct);
        row.LastChefHeartbeatAt = DateTime.UtcNow;
        await _db.SaveChangesAsync(ct);
    }

    public async Task<Flow> AssignGroupAsync(Guid flowId, Guid? groupId, Guid? actorUserId, CancellationToken ct = default)
    {
        var row = await Load(flowId, ct);
        if (groupId.HasValue)
        {
            var groupExists = await _db.FlowGroups.AnyAsync(g => g.Id == groupId.Value, ct);
            if (!groupExists)
                throw new FlowLifecycleException($"flow group {groupId} not found");
        }
        var before = new { GroupId = row.GroupId };
        row.GroupId = groupId;
        await _db.SaveChangesAsync(ct);

        await _audit.LogAsync(
            actionType: "flow_group_assigned",
            targetType: "flow",
            targetId: row.Id.ToString(),
            actorUserId: actorUserId,
            actorPrincipalId: null,
            before: before,
            after: new { GroupId = row.GroupId },
            ct: ct);

        return row;
    }

    private async Task<Flow> ChefTransitionAsync(
        Guid flowId,
        FlowState target,
        string actionType,
        FlowState[] allowedFrom,
        CancellationToken ct)
    {
        var row = await Load(flowId, ct);
        if (!allowedFrom.Contains(row.State))
        {
            throw new FlowLifecycleException(
                $"Cannot {actionType} from state {row.State}; expected one of {string.Join(", ", allowedFrom)}");
        }

        var before = new { State = row.State.ToString() };
        row.State = target;
        row.LastChefHeartbeatAt = DateTime.UtcNow;
        await _db.SaveChangesAsync(ct);

        await _audit.LogAsync(
            actionType: actionType,
            targetType: "flow",
            targetId: row.Id.ToString(),
            actorUserId: null,
            actorPrincipalId: null,
            before: before,
            after: new { State = row.State.ToString() },
            reason: "chef session",
            ct: ct);

        return row;
    }

    public async Task<Flow> CloneVersionAsync(Guid flowId, Guid? actorUserId, CancellationToken ct = default)
    {
        var source = await Load(flowId, ct);
        if (source.State != FlowState.Approved && source.State != FlowState.Retired)
            throw new FlowLifecycleException($"Can only clone from Approved or Retired (state was {source.State})");

        var maxVersion = await _db.Flows
            .Where(f => f.LineageId == source.LineageId)
            .Select(f => (int?)f.Version)
            .MaxAsync(ct) ?? source.Version;

        var clone = new Flow
        {
            Id = Guid.NewGuid(),
            LineageId = source.LineageId,
            Version = maxVersion + 1,
            State = FlowState.Draft,
            FlowCode = source.FlowCode,
            DisplayName = source.DisplayName,
            SpecJson = source.SpecJson,
            Notes = null,
            CreatedByUserId = actorUserId,
        };
        _db.Flows.Add(clone);
        await _db.SaveChangesAsync(ct);

        await _audit.LogAsync(
            actionType: "flow_version_cloned",
            targetType: "flow",
            targetId: clone.Id.ToString(),
            actorUserId: actorUserId,
            actorPrincipalId: null,
            before: new { source.Id, source.LineageId, source.Version },
            after: new { clone.Id, clone.LineageId, clone.Version, State = clone.State.ToString() },
            ct: ct);

        return clone;
    }

    public async Task<Flow> OnHoldFromChefAsync(Guid flowId, string question, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(question))
            throw new FlowLifecycleException("question required");

        var row = await Load(flowId, ct);
        if (row.State != FlowState.Cooking)
            throw new FlowLifecycleException($"On-hold can only come from Cooking (state was {row.State})");

        var before = new { State = row.State.ToString(), Notes = row.Notes };
        row.State = FlowState.OnHold;
        var stamp = DateTime.UtcNow.ToString("u");
        row.Notes = string.IsNullOrEmpty(row.Notes)
            ? $"[chef@{stamp}] {question}"
            : $"{row.Notes}\n\n[chef@{stamp}] {question}";
        await _db.SaveChangesAsync(ct);

        await _audit.LogAsync(
            actionType: "flow_on_hold",
            targetType: "flow",
            targetId: row.Id.ToString(),
            actorUserId: null,
            actorPrincipalId: null,
            before: before,
            after: new { State = row.State.ToString(), Notes = row.Notes },
            reason: "chef requested clarification",
            ct: ct);

        return row;
    }

    public async Task SoftDeleteDraftAsync(Guid flowId, Guid? actorUserId, CancellationToken ct = default)
    {
        var row = await Load(flowId, ct);
        if (row.State != FlowState.Draft)
            throw new FlowLifecycleException($"Only Draft flows can be deleted (state was {row.State}). Cancel first if needed.");

        row.DeletedAt = DateTime.UtcNow;
        await _db.SaveChangesAsync(ct);

        await _audit.LogAsync(
            actionType: "flow_deleted",
            targetType: "flow",
            targetId: row.Id.ToString(),
            actorUserId: actorUserId,
            actorPrincipalId: null,
            before: new { State = row.State.ToString(), row.FlowCode, row.DisplayName, row.Version },
            ct: ct);
    }

    private async Task<Flow> TransitionAsync(
        Guid flowId,
        FlowState target,
        string actionType,
        Guid? actorUserId,
        FlowState[] allowedFrom,
        CancellationToken ct)
    {
        var row = await Load(flowId, ct);
        if (!allowedFrom.Contains(row.State))
        {
            throw new FlowLifecycleException(
                $"Cannot {actionType} from state {row.State}; expected one of {string.Join(", ", allowedFrom)}");
        }

        var before = new { State = row.State.ToString() };
        row.State = target;
        await _db.SaveChangesAsync(ct);

        await _audit.LogAsync(
            actionType: actionType,
            targetType: "flow",
            targetId: row.Id.ToString(),
            actorUserId: actorUserId,
            actorPrincipalId: null,
            before: before,
            after: new { State = row.State.ToString() },
            ct: ct);

        return row;
    }

    private async Task<Flow> Load(Guid flowId, CancellationToken ct)
    {
        var row = await _db.Flows.FirstOrDefaultAsync(f => f.Id == flowId, ct);
        if (row is null) throw new FlowLifecycleException($"Flow {flowId} not found");
        return row;
    }

    private static object SnapshotSpec(Flow f) => new { f.FlowCode, f.DisplayName, SpecJsonLength = f.SpecJson?.Length ?? 0 };
}
