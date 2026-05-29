using System.ComponentModel;
using Bpm.Admin.Application.Flows;
using Bpm.Admin.Application.Principals;
using Bpm.Admin.Domain.Flows;
using Bpm.Admin.Persistence;
using Microsoft.EntityFrameworkCore;
using ModelContextProtocol.Server;

namespace Bpm.Admin.Api.Mcp;

/// <summary>
/// MCP tool surface for chef Claude Code sessions. 1:1 with
/// <c>/api/chef/flows/*</c> — both call the same Application services
/// so MCP and HTTP paths can't drift. Auth is enforced at the
/// transport level (MCP HTTP endpoint sits behind
/// <c>ChefTokenAuthMiddleware</c> just like the chef controller).
/// </summary>
[McpServerToolType]
public sealed class ChefMcpTools
{
    private readonly AdminDbContext _db;
    private readonly IFlowLifecycleService _lifecycle;
    private readonly IFlowChatService _chat;
    private readonly IChefOrgQueryService _org;

    public ChefMcpTools(AdminDbContext db, IFlowLifecycleService lifecycle, IFlowChatService chat, IChefOrgQueryService org)
    {
        _db = db;
        _lifecycle = lifecycle;
        _chat = chat;
        _org = org;
    }

    [McpServerTool(Name = "chef_get_flow")]
    [Description("Fetch flow metadata + spec JSON. Call at chef session start; bumps the chef heartbeat.")]
    public async Task<object?> GetFlow(
        [Description("Flow id (UUID) — read from bundle manifest.json.flowId")] string flowId,
        CancellationToken ct = default)
    {
        if (!Guid.TryParse(flowId, out var id)) return Error($"invalid flowId '{flowId}'");
        var f = await _db.Flows.AsNoTracking().FirstOrDefaultAsync(x => x.Id == id, ct);
        if (f is null) return Error($"flow {id} not found");
        await _lifecycle.BumpChefHeartbeatAsync(id, ct);
        return new
        {
            id = f.Id,
            flowCode = f.FlowCode,
            displayName = f.DisplayName,
            version = f.Version,
            state = f.State.ToString(),
            specJson = f.SpecJson,
            updatedAt = f.UpdatedAt,
        };
    }

    [McpServerTool(Name = "chef_get_messages")]
    [Description("Fetch the Cook tab chat thread (user replies, chef memos, system rows). Pass `since` (ISO-8601) to fetch only new messages.")]
    public async Task<object?> GetMessages(
        [Description("Flow id (UUID)")] string flowId,
        [Description("Optional ISO-8601 cutoff. Returns messages with CreatedAt > since.")] string? since = null,
        CancellationToken ct = default)
    {
        if (!Guid.TryParse(flowId, out var id)) return Error($"invalid flowId '{flowId}'");
        DateTime? sinceDt = null;
        if (!string.IsNullOrWhiteSpace(since))
        {
            if (!DateTime.TryParse(since, null, System.Globalization.DateTimeStyles.AdjustToUniversal | System.Globalization.DateTimeStyles.AssumeUniversal, out var parsed))
                return Error($"invalid since timestamp '{since}'");
            sinceDt = parsed;
        }
        var rows = await _chat.ListAsync(id, sinceDt, ct);
        await _lifecycle.BumpChefHeartbeatAsync(id, ct);
        return rows.Select(m => new
        {
            id = m.Id,
            sender = m.Sender.ToString(),
            kind = m.Kind.ToString(),
            content = m.Content,
            artifactsJson = m.ArtifactsJson,
            version = m.Version,
            createdAt = m.CreatedAt,
        });
    }

    [McpServerTool(Name = "chef_post_message")]
    [Description("Append a chef-authored chat row (memo / question / completion). Bumps heartbeat.")]
    public async Task<object?> PostMessage(
        [Description("Flow id (UUID)")] string flowId,
        [Description("One of: Memo | Question | Completion")] string kind,
        [Description("Markdown body shown to the user")] string content,
        [Description("Optional JSON metadata (e.g. {branch, fileCount, testsPassing}); free form")] string? artifactsJson = null,
        [Description("Optional version label for completion rows (e.g. 'V1.0')")] string? version = null,
        CancellationToken ct = default)
    {
        if (!Guid.TryParse(flowId, out var id)) return Error($"invalid flowId '{flowId}'");
        if (!Enum.TryParse<FlowChatKind>(kind, ignoreCase: true, out var k))
            return Error($"unknown chat kind '{kind}'");
        if (k is not (FlowChatKind.Memo or FlowChatKind.Question or FlowChatKind.Completion))
            return Error($"chef may only post Memo / Question / Completion (got {kind})");

        var row = await _chat.AppendAsync(new AppendChatMessageInput(
            FlowId: id,
            Sender: FlowChatSender.Chef,
            Kind: k,
            Content: content,
            ArtifactsJson: artifactsJson,
            Version: version), ct);
        await _lifecycle.BumpChefHeartbeatAsync(id, ct);
        return new { id = row.Id, createdAt = row.CreatedAt };
    }

    [McpServerTool(Name = "chef_transition")]
    [Description("Move the flow's state machine. Target: Cooking | Resume | OnHold (needs question) | Committed.")]
    public async Task<object?> Transition(
        [Description("Flow id (UUID)")] string flowId,
        [Description("Cooking | Resume | OnHold | Committed")] string target,
        [Description("Question text — REQUIRED when target=OnHold")] string? question = null,
        [Description("Optional human-readable reason (for audit)")] string? reason = null,
        CancellationToken ct = default)
    {
        if (!Guid.TryParse(flowId, out var id)) return Error($"invalid flowId '{flowId}'");
        try
        {
            Flow updated;
            switch (target)
            {
                case "Cooking":
                    updated = await _lifecycle.ChefAcceptAsync(id, ct);
                    break;
                case "Resume":
                    updated = await _lifecycle.ChefResumeAsync(id, ct);
                    break;
                case "OnHold":
                    if (string.IsNullOrWhiteSpace(question))
                        return Error("question required when target=OnHold");
                    updated = await _lifecycle.OnHoldFromChefAsync(id, question, ct);
                    await _chat.AppendAsync(new AppendChatMessageInput(
                        FlowId: id, Sender: FlowChatSender.Chef, Kind: FlowChatKind.Question, Content: question), ct);
                    break;
                case "Committed":
                    updated = await _lifecycle.ChefCommitAsync(id, ct);
                    break;
                default:
                    return Error($"unknown target '{target}'");
            }
            _ = reason; // currently audit only (placeholder for future structured reasons)
            return new { id = updated.Id, state = updated.State.ToString(), lastHeartbeat = updated.LastChefHeartbeatAt };
        }
        catch (FlowLifecycleException ex)
        {
            return Error(ex.Message);
        }
    }

    [McpServerTool(Name = "chef_set_worktree")]
    [Description("Record the git branch (or worktree path) chef is cooking in, so admin operators can find your in-flight code. Call right after `chef_transition('Cooking')`. Optional `notes` for a one-line status. Pass `branch=''` to clear.")]
    public async Task<object?> SetWorktree(
        [Description("Flow id (UUID)")] string flowId,
        [Description("Branch name (e.g. 'leave-test-6') or empty string to clear.")] string branch,
        [Description("Optional one-line status note (e.g. 'Domain layer done; about to run migration').")] string? notes = null,
        CancellationToken ct = default)
    {
        if (!Guid.TryParse(flowId, out var id)) return Error($"invalid flowId '{flowId}'");
        try
        {
            var row = await _lifecycle.SetChefWorkContextAsync(id, branch, notes, ct);
            return new { id = row.Id, branch, notes, setAt = DateTime.UtcNow };
        }
        catch (FlowLifecycleException ex)
        {
            return Error(ex.Message);
        }
    }

    // ── PR-M1: org-graph read tools ──────────────────────────────────

    [McpServerTool(Name = "chef_list_roles")]
    [Description("List every active role in admin's org graph with member counts. Use this to confirm a spec's `role:XXX` actor refs target a role that actually exists.")]
    public async Task<object?> ListRoles(CancellationToken ct = default)
    {
        var rows = await _org.ListRolesAsync(ct);
        return rows.Select(r => new
        {
            id = r.Id,
            name = r.Name,
            description = r.Description,
            memberCount = r.MemberCount,
            isSystem = r.IsSystem,
        });
    }

    [McpServerTool(Name = "chef_list_principals")]
    [Description("List principals (default: first 100 users). Filter by `roleName`, `kind` (user/dept/group), or `search` (display name + email substring).")]
    public async Task<object?> ListPrincipals(
        [Description("Filter to principals holding this role (matches Admin_Roles.Name).")] string? roleName = null,
        [Description("Filter by kind: user | dept | group")] string? kind = null,
        [Description("Substring match against DisplayName + Email.")] string? search = null,
        [Description("Max rows; default 100, max 500.")] int take = 100,
        CancellationToken ct = default)
    {
        try
        {
            var rows = await _org.ListPrincipalsAsync(roleName, kind, search, take, ct);
            return rows.Select(p => new
            {
                id = p.Id,
                kind = p.Kind,
                displayName = p.DisplayName,
                email = p.Email,
                active = p.Active,
                roles = p.Roles,
            });
        }
        catch (FlowLifecycleException ex)
        {
            return Error(ex.Message);
        }
    }

    [McpServerTool(Name = "chef_walk_org")]
    [Description("Resolve an ActorRef.expr-style path (e.g. 'manager', 'manager.manager', 'department.head', 'department.parent.head') starting from a submitter user id. Returns a step-by-step trace plus the final resolved principals — use this to sanity-check the resolver C# you're about to write.")]
    public async Task<object?> WalkOrg(
        [Description("Submitter user id (UUID) to start the walk from.")] string submitterUserId,
        [Description("Dot-separated path. Supported segments: manager | department | head | parent.")] string path,
        CancellationToken ct = default)
    {
        if (!Guid.TryParse(submitterUserId, out var id))
            return Error($"invalid submitterUserId '{submitterUserId}'");
        try
        {
            var result = await _org.WalkOrgAsync(id, path, ct);
            return new
            {
                startUserId = result.StartUserId,
                path = result.Path,
                steps = result.Steps.Select(s => new
                {
                    segment = s.Segment,
                    kind = s.Kind,
                    resolved = s.Resolved.Select(p => new { id = p.Id, displayName = p.DisplayName, email = p.Email, kind = p.Kind }),
                }),
                final = result.Final.Select(p => new { id = p.Id, displayName = p.DisplayName, email = p.Email, kind = p.Kind, roles = p.Roles }),
            };
        }
        catch (FlowLifecycleException ex)
        {
            return Error(ex.Message);
        }
    }

    private static object Error(string message)
        => new { error = message };
}
