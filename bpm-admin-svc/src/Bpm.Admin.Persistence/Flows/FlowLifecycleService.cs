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

    public async Task<RegisterShippedResult> RegisterShippedAsync(
        IReadOnlyList<ShippedFlowInput> flows, Guid? actorUserId, CancellationToken ct = default)
    {
        var registered = new List<string>();
        var skipped = new List<string>();

        foreach (var f in flows)
        {
            var code = (f.FlowCode ?? string.Empty).Trim().ToUpperInvariant();
            if (string.IsNullOrWhiteSpace(code)) continue;

            // Idempotent: an active (non-archived / non-retired / non-deleted)
            // row with this code already covers the launcher → skip.
            var exists = await _db.Flows.AnyAsync(x =>
                x.FlowCode == code && x.ArchivedAt == null && x.DeletedAt == null && x.State != FlowState.Retired, ct);
            if (exists) { skipped.Add(code); continue; }

            var row = new Flow
            {
                Id = Guid.NewGuid(),
                LineageId = Guid.NewGuid(),
                Version = 1,
                // Blank-env one-click bootstrap: deployed flows go straight to
                // Published (live), not just Approved — that's the button's whole job.
                State = FlowState.Published,
                FlowCode = code,
                DisplayName = string.IsNullOrWhiteSpace(f.DisplayName) ? code : f.DisplayName.Trim(),
                SpecJson = "{}",
                // Code-registered flows have no AI-Kitchen spec; pull the
                // canonical BPMN from the shipped bundle so the admin SOURCE
                // step can show a read-only diagram (survives reset/reseed).
                BpmnXml = TryReadBundleBpmn(code),
                CreatedByUserId = actorUserId,
            };
            _db.Flows.Add(row);
            await _db.SaveChangesAsync(ct);

            await _audit.LogAsync(
                actionType: "flow_registered_shipped",
                targetType: "flow",
                targetId: row.Id.ToString(),
                actorUserId: actorUserId,
                actorPrincipalId: null,
                after: new { row.Id, row.LineageId, row.Version, row.FlowCode, row.DisplayName, State = row.State.ToString() },
                ct: ct);

            registered.Add(code);
        }

        return new RegisterShippedResult(registered, skipped);
    }

    /// <summary>
    /// Best-effort read of a shipped flow's canonical BPMN from the repo's
    /// bpm-ui feature folder (<c>bpm-ui/src/features/&lt;CODE&gt;/V1/&lt;CODE&gt;_V1.bpmn.xml</c>).
    /// Dev/POC convenience so register-shipped (and Reset, which re-registers)
    /// persists the diagram into <see cref="Flow.BpmnXml"/> without an
    /// AI-Kitchen spec. Returns null when the file isn't reachable (e.g. a
    /// deployed admin-svc without the source tree) — the preview just stays
    /// empty. Proper long-term path: chef cook persists the bundle bpmn.
    /// </summary>
    private static string? TryReadBundleBpmn(string flowCode)
    {
        try
        {
            var dir = AppContext.BaseDirectory;
            for (var i = 0; i < 10 && dir is not null; i++)
            {
                var featuresDir = System.IO.Path.Combine(dir, "bpm-ui", "src", "features");
                if (System.IO.Directory.Exists(featuresDir))
                {
                    var path = System.IO.Path.Combine(featuresDir, flowCode, "V1", $"{flowCode}_V1.bpmn.xml");
                    return System.IO.File.Exists(path) ? System.IO.File.ReadAllText(path) : null;
                }
                dir = System.IO.Directory.GetParent(dir)?.FullName;
            }
        }
        catch
        {
            // Best-effort only — a missing/unreadable file leaves BpmnXml null.
        }
        return null;
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

    public async Task<Flow> SubmitAsync(Guid flowId, Guid? actorUserId, CancellationToken ct = default)
    {
        // FlowCode uniqueness check (PR-F1): an "active" flow with the
        // same code blocks Submit. Retired flows are OK because their
        // chef tables stay live and end-user-facing case data keeps
        // working; archived flows are OK because their tables are
        // renamed and the namespace is free.
        var row = await Load(flowId, ct);
        var clash = await _db.Flows
            .AsNoTracking()
            .AnyAsync(f =>
                f.Id != row.Id &&
                f.FlowCode == row.FlowCode &&
                f.ArchivedAt == null &&
                f.State != FlowState.Retired,
                ct);
        if (clash)
            throw new FlowLifecycleException(
                $"FlowCode '{row.FlowCode}' is already used by another active flow; archive or retire that one first.");

        // PR-X2: server-side spec sanity gate. admin-ui's per-step
        // validators are the rich check; backend keeps a small subset
        // here so half-baked drafts can't slip past via raw API call.
        var problems = SubmitSanityIssues(row);
        if (problems.Count > 0)
            throw new FlowLifecycleException(
                $"Spec is not ready to submit: {string.Join("; ", problems)}");

        return await TransitionAsync(flowId, FlowState.Submitted, "flow_submitted", actorUserId, new[] { FlowState.Draft }, ct);
    }

    private static IReadOnlyList<string> SubmitSanityIssues(Flow row)
    {
        var problems = new List<string>();
        if (string.IsNullOrWhiteSpace(row.FlowCode)) problems.Add("flowCode is empty");
        if (string.IsNullOrWhiteSpace(row.DisplayName)) problems.Add("flow name is empty");
        if (string.IsNullOrWhiteSpace(row.SpecJson) || row.SpecJson == "{}")
        {
            problems.Add("spec is empty — walk the wizard before submitting");
            return problems;
        }
        try
        {
            using var doc = System.Text.Json.JsonDocument.Parse(row.SpecJson);
            var root = doc.RootElement;
            if (root.TryGetProperty("flow", out var flow) && flow.TryGetProperty("nodes", out var nodes) && nodes.ValueKind == System.Text.Json.JsonValueKind.Array)
            {
                var hasStart = false; var hasEnd = false; var userTaskCount = 0;
                foreach (var n in nodes.EnumerateArray())
                {
                    var type = n.TryGetProperty("type", out var t) ? t.GetString() : null;
                    if (type == "startEvent") hasStart = true;
                    else if (type == "endEvent") hasEnd = true;
                    else if (type == "userTask") userTaskCount++;
                }
                if (!hasStart) problems.Add("flow missing a startEvent");
                if (!hasEnd) problems.Add("flow missing an endEvent");
                if (userTaskCount == 0) problems.Add("flow has no userTask — chef has nothing to scaffold a form against");
            }
            else
            {
                problems.Add("spec.flow.nodes is missing or not an array");
            }

            if (root.TryGetProperty("userTasks", out var userTasks) && userTasks.ValueKind == System.Text.Json.JsonValueKind.Array)
            {
                var hasAnyField = false;
                foreach (var ut in userTasks.EnumerateArray())
                {
                    if (ut.TryGetProperty("fields", out var fields)
                        && fields.ValueKind == System.Text.Json.JsonValueKind.Array
                        && fields.GetArrayLength() > 0)
                    {
                        hasAnyField = true; break;
                    }
                }
                if (!hasAnyField) problems.Add("no user task has any field configured");
            }
            else
            {
                problems.Add("spec.userTasks missing");
            }
        }
        catch (System.Text.Json.JsonException)
        {
            problems.Add("spec is not valid JSON");
        }
        return problems;
    }

    public Task<Flow> CancelAsync(Guid flowId, Guid? actorUserId, CancellationToken ct = default)
        => TransitionAsync(flowId, FlowState.Draft, "flow_cancelled", actorUserId,
            new[] { FlowState.Submitted, FlowState.Cooking, FlowState.OnHold }, ct);

    public Task<Flow> ResumeAsync(Guid flowId, Guid? actorUserId, CancellationToken ct = default)
        => TransitionAsync(flowId, FlowState.Submitted, "flow_resumed", actorUserId, new[] { FlowState.OnHold }, ct);

    public Task<Flow> RetireAsync(Guid flowId, Guid? actorUserId, CancellationToken ct = default)
        => TransitionAsync(flowId, FlowState.Retired, "flow_retired", actorUserId, new[] { FlowState.Approved, FlowState.Published }, ct);

    public Task<Flow> UnretireAsync(Guid flowId, Guid? actorUserId, CancellationToken ct = default)
        => TransitionAsync(flowId, FlowState.Approved, "flow_unretired", actorUserId, new[] { FlowState.Retired }, ct);

    public Task<Flow> PublishAsync(Guid flowId, Guid? actorUserId, CancellationToken ct = default)
        => TransitionAsync(flowId, FlowState.Published, "flow_published", actorUserId, new[] { FlowState.Approved }, ct);

    public Task<Flow> UnpublishAsync(Guid flowId, Guid? actorUserId, CancellationToken ct = default)
        => TransitionAsync(flowId, FlowState.Approved, "flow_unpublished", actorUserId, new[] { FlowState.Published }, ct);

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

    public Task<Flow> ApproveAsync(Guid flowId, Guid? actorUserId, CancellationToken ct = default)
        => TransitionAsync(flowId, FlowState.Approved, "flow_approved", actorUserId,
            new[] { FlowState.Committed }, ct);

    public async Task BumpChefHeartbeatAsync(Guid flowId, CancellationToken ct = default)
    {
        var row = await Load(flowId, ct);
        row.LastChefHeartbeatAt = DateTime.UtcNow;
        await _db.SaveChangesAsync(ct);
    }

    public async Task<Flow> SetChefWorkContextAsync(Guid flowId, string? branch, string? notes, CancellationToken ct = default)
    {
        var row = await Load(flowId, ct);
        if (string.IsNullOrWhiteSpace(branch))
        {
            row.ChefWorkContextJson = null;
        }
        else
        {
            var payload = new Dictionary<string, string?>
            {
                ["branch"] = branch.Trim(),
                ["notes"]  = string.IsNullOrWhiteSpace(notes) ? null : notes.Trim(),
                ["setAt"]  = DateTime.UtcNow.ToString("o"),
            };
            row.ChefWorkContextJson = System.Text.Json.JsonSerializer.Serialize(payload);
        }
        row.LastChefHeartbeatAt = DateTime.UtcNow;
        await _db.SaveChangesAsync(ct);

        await _audit.LogAsync(
            actionType: "chef_work_context_set",
            targetType: "flow",
            targetId: row.Id.ToString(),
            actorUserId: null,
            actorPrincipalId: null,
            after: new { Branch = branch, Notes = notes },
            reason: "chef session",
            ct: ct);

        return row;
    }

    public Task<Flow> ReopenForIssueAsync(Guid flowId, Guid? actorUserId, CancellationToken ct = default)
        => TransitionAsync(flowId, FlowState.OnHold, "flow_reopened_for_issue", actorUserId,
            new[] { FlowState.Committed, FlowState.Approved }, ct);

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

    public async Task<Flow> SetIconAsync(Guid flowId, string? iconKey, Guid? actorUserId, CancellationToken ct = default)
    {
        var row = await Load(flowId, ct);
        var before = new { row.IconKey };
        row.IconKey = string.IsNullOrWhiteSpace(iconKey) ? null : iconKey.Trim();
        await _db.SaveChangesAsync(ct);

        await _audit.LogAsync(
            actionType: "flow_icon_set",
            targetType: "flow",
            targetId: row.Id.ToString(),
            actorUserId: actorUserId,
            actorPrincipalId: null,
            before: before,
            after: new { row.IconKey },
            ct: ct);

        return row;
    }

    public async Task ReorderAsync(IReadOnlyList<Guid> orderedFlowIds, Guid? actorUserId, CancellationToken ct = default)
    {
        if (orderedFlowIds.Count == 0) return;

        var rows = await _db.Flows
            .Where(f => orderedFlowIds.Contains(f.Id))
            .ToListAsync(ct);

        var byId = rows.ToDictionary(f => f.Id);
        for (var i = 0; i < orderedFlowIds.Count; i++)
        {
            if (byId.TryGetValue(orderedFlowIds[i], out var row))
                row.DisplayOrder = i;
        }
        await _db.SaveChangesAsync(ct);

        await _audit.LogAsync(
            actionType: "flow_reordered",
            targetType: "flow",
            targetId: string.Join(",", rows.Select(r => r.Id)),
            actorUserId: actorUserId,
            actorPrincipalId: null,
            after: new { OrderedFlowIds = orderedFlowIds },
            ct: ct);
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
            // Carry forward launcher display metadata so a re-cook keeps
            // its group / icon / order instead of resetting to defaults.
            GroupId = source.GroupId,
            IconKey = source.IconKey,
            DisplayOrder = source.DisplayOrder,
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
