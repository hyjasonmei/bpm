using Bpm.Admin.Api.Auth;
using Bpm.Admin.Application.Bundle;
using Bpm.Admin.Application.Flows;
using Bpm.Admin.Domain.Flows;
using Bpm.Admin.Persistence;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace Bpm.Admin.Api.Controllers;

/// <summary>
/// Chef-token gated endpoints. Mirror what the in-process MCP server
/// exposes — admin-ui doesn't call these; only chef Claude Code
/// sessions (via MCP) and the simulate buttons (PR-K3) do.
///
/// Auth: requires the ChefBearer scheme set up by
/// <see cref="ChefTokenAuthMiddleware"/>. Returns 401 / 403 otherwise.
/// </summary>
[ApiController]
[Route("api/chef/flows")]
public sealed class ChefFlowsController : ControllerBase
{
    private readonly AdminDbContext _db;
    private readonly IFlowLifecycleService _lifecycle;
    private readonly IFlowChatService _chat;
    private readonly IBundleBuilder _bundle;
    private readonly IDeployConfigService _deploy;

    public ChefFlowsController(AdminDbContext db, IFlowLifecycleService lifecycle, IFlowChatService chat, IBundleBuilder bundle, IDeployConfigService deploy)
    {
        _db = db;
        _lifecycle = lifecycle;
        _chat = chat;
        _bundle = bundle;
        _deploy = deploy;
    }

    /// <summary>Chef-token read of per-env deploy resource names, so the deploy
    /// worker (PublishManager) can fetch the target App Service / SWA names with
    /// its chef token — the admin Site Setting CRUD for the same data is admin-JWT
    /// only, which the agent doesn't hold.</summary>
    [HttpGet("deploy-config")]
    public async Task<ActionResult<IReadOnlyList<DeployEnvConfigDto>>> DeployConfig(CancellationToken ct)
    {
        if (!RequireChef()) return Forbid();
        return Ok(await _deploy.ListAsync(ct));
    }

    [HttpGet("{flowId:guid}")]
    public async Task<ActionResult<FlowDetailDto>> Get(Guid flowId, CancellationToken ct)
    {
        if (!RequireChef()) return Forbid();
        var f = await _db.Flows.AsNoTracking().FirstOrDefaultAsync(x => x.Id == flowId, ct);
        if (f is null) return NotFound();
        await _lifecycle.BumpChefHeartbeatAsync(flowId, ct);
        return Ok(new FlowDetailDto(
            f.Id, f.LineageId, f.Version, f.State, f.FlowCode, f.DisplayName, f.SpecJson, f.Notes,
            f.CreatedByUserId, f.CreatedAt, f.UpdatedAt, f.LastChefHeartbeatAt,
            f.GroupId, null, f.IconKey, f.DisplayOrder, f.ChefWorkContextJson, f.BpmnXml, f.PrUrl, f.MergedAt, f.PublishedAt, f.PublishFailedReason));
    }

    [HttpGet("{flowId:guid}/messages")]
    public async Task<ActionResult<IEnumerable<FlowChatMessageDto>>> ListMessages(
        Guid flowId,
        [FromQuery] DateTime? since,
        CancellationToken ct)
    {
        if (!RequireChef()) return Forbid();
        var rows = await _chat.ListAsync(flowId, since, ct);
        await _lifecycle.BumpChefHeartbeatAsync(flowId, ct);
        return Ok(rows.Select(ToDto));
    }

    [HttpPost("{flowId:guid}/messages")]
    public async Task<ActionResult<FlowChatMessageDto>> AppendMessage(
        Guid flowId, [FromBody] ChefAppendMessageRequest req, CancellationToken ct)
    {
        if (!RequireChef()) return Forbid();
        if (!TryParseKind(req.Kind, out var kind))
            return BadRequest($"unknown chat kind '{req.Kind}'");
        try
        {
            var row = await _chat.AppendAsync(new AppendChatMessageInput(
                FlowId: flowId,
                Sender: FlowChatSender.Chef,
                Kind: kind,
                Content: req.Content,
                ArtifactsJson: req.ArtifactsJson,
                Version: req.Version), ct);
            await _lifecycle.BumpChefHeartbeatAsync(flowId, ct);
            return Ok(ToDto(row));
        }
        catch (FlowLifecycleException ex) { return BadRequest(ex.Message); }
    }

    [HttpPost("{flowId:guid}/transition")]
    public async Task<ActionResult<FlowDetailDto>> Transition(
        Guid flowId, [FromBody] ChefTransitionRequest req, CancellationToken ct)
    {
        if (!RequireChef()) return Forbid();
        try
        {
            Flow updated;
            string targetLower = (req.Target ?? "").Trim();
            switch (targetLower)
            {
                case "Cooking":
                    updated = await _lifecycle.ChefAcceptAsync(flowId, ct);
                    break;
                case "Resume":
                    updated = await _lifecycle.ChefResumeAsync(flowId, ct);
                    break;
                case "OnHold":
                    if (string.IsNullOrWhiteSpace(req.Question))
                        return BadRequest("question required when transitioning to OnHold");
                    updated = await _lifecycle.OnHoldFromChefAsync(flowId, req.Question, ct);
                    // Append a chat row so the question is visible in CookPanel.
                    await _chat.AppendAsync(new AppendChatMessageInput(
                        FlowId: flowId,
                        Sender: FlowChatSender.Chef,
                        Kind: FlowChatKind.Question,
                        Content: req.Question), ct);
                    break;
                case "Committed":
                    updated = await _lifecycle.ChefCommitAsync(flowId, ct);
                    break;
                default:
                    return BadRequest($"unknown chef transition target '{req.Target}'");
            }

            return Ok(new FlowDetailDto(
                updated.Id, updated.LineageId, updated.Version, updated.State,
                updated.FlowCode, updated.DisplayName, updated.SpecJson, updated.Notes,
                updated.CreatedByUserId, updated.CreatedAt, updated.UpdatedAt, updated.LastChefHeartbeatAt,
                updated.GroupId, null, updated.IconKey, updated.DisplayOrder, updated.ChefWorkContextJson, updated.BpmnXml, updated.PrUrl, updated.MergedAt, updated.PublishedAt, updated.PublishFailedReason));
        }
        catch (FlowLifecycleException ex) { return Conflict(ex.Message); }
    }

    [HttpGet("{flowId:guid}/bundle")]
    public async Task<IActionResult> Bundle(Guid flowId, CancellationToken ct)
    {
        if (!RequireChef()) return Forbid();
        var f = await _db.Flows.AsNoTracking().FirstOrDefaultAsync(x => x.Id == flowId, ct);
        if (f is null) return NotFound();
        await _lifecycle.BumpChefHeartbeatAsync(flowId, ct);
        if (f.BundleBlob is null || f.BundleBlob.Length == 0)
        {
            return Conflict("Bundle hasn't been built yet — admin must download or submit at least once so the zip is cached.");
        }
        var filename = $"{f.FlowCode}_v{f.Version}.zip";
        Response.Headers["X-Bundle-Built-At"] = (f.BundleBuiltAt ?? default).ToString("o");
        return File(f.BundleBlob, "application/zip", filename);
    }

    /// <summary>The chef agent's polling target: everything actionable,
    /// grouped. Unlike the per-flow endpoints this bumps no heartbeat
    /// (it's not scoped to one flow).</summary>
    [HttpGet("tasks")]
    public async Task<ActionResult<ChefTaskListDto>> Tasks(CancellationToken ct)
    {
        if (!RequireChef()) return Forbid();

        var submitted = await _db.Flows.AsNoTracking()
            .Where(f => f.State == FlowState.Submitted)
            .OrderBy(f => f.UpdatedAt)
            .ToListAsync(ct);

        var approved = await _db.Flows.AsNoTracking()
            .Where(f => f.State == FlowState.Approved && f.MergedAt == null)
            .OrderBy(f => f.UpdatedAt)
            .ToListAsync(ct);

        var publishing = await _db.Flows.AsNoTracking()
            .Where(f => f.State == FlowState.Publishing)
            .OrderBy(f => f.UpdatedAt)
            .ToListAsync(ct);

        var stallCutoff = DateTime.UtcNow.AddMinutes(-30);
        var stalled = await _db.Flows.AsNoTracking()
            .Where(f => f.State == FlowState.Cooking
                        && (f.LastChefHeartbeatAt == null || f.LastChefHeartbeatAt < stallCutoff))
            .OrderBy(f => f.UpdatedAt)
            .ToListAsync(ct);

        // OnHold flows are "awaiting chef" only when the user spoke last
        // (a Reply or an Issue) — not when the last word was chef's own
        // Question. Group the last non-system message per flow in memory so
        // the query stays provider-portable (no GroupBy translation).
        var onHold = await _db.Flows.AsNoTracking()
            .Where(f => f.State == FlowState.OnHold)
            .ToListAsync(ct);
        var onHoldIds = onHold.Select(f => f.Id).ToList();
        var lastUserMsgAt = new Dictionary<Guid, DateTime>();
        var awaiting = new List<Flow>();
        if (onHoldIds.Count > 0)
        {
            var msgs = await _db.FlowChatMessages.AsNoTracking()
                .Where(m => onHoldIds.Contains(m.FlowId) && m.Sender != FlowChatSender.System)
                .ToListAsync(ct);
            foreach (var f in onHold)
            {
                var last = msgs.Where(m => m.FlowId == f.Id)
                    .OrderByDescending(m => m.CreatedAt)
                    .FirstOrDefault();
                if (last is { Sender: FlowChatSender.User })
                {
                    awaiting.Add(f);
                    lastUserMsgAt[f.Id] = last.CreatedAt;
                }
            }
            awaiting = awaiting.OrderBy(f => f.UpdatedAt).ToList();
        }

        ChefTaskDto ToTask(Flow f) => new(
            f.Id, f.FlowCode, f.Version, f.DisplayName, f.State, f.UpdatedAt,
            f.ChefWorkContextJson, f.PrUrl,
            lastUserMsgAt.TryGetValue(f.Id, out var at) ? at : null);

        return Ok(new ChefTaskListDto(
            submitted.Select(ToTask).ToList(),
            awaiting.Select(ToTask).ToList(),
            approved.Select(ToTask).ToList(),
            stalled.Select(ToTask).ToList(),
            publishing.Select(ToTask).ToList()));
    }

    public sealed record ChefSetPrRequest(string PrUrl);

    [HttpPost("{flowId:guid}/pr")]
    public async Task<IActionResult> SetPr(Guid flowId, [FromBody] ChefSetPrRequest req, CancellationToken ct)
    {
        if (!RequireChef()) return Forbid();
        try { await _lifecycle.SetPrUrlAsync(flowId, req.PrUrl, ct); return NoContent(); }
        catch (FlowLifecycleException ex) { return BadRequest(ex.Message); }
    }

    public sealed record ChefMarkMergedRequest(string? Source);

    [HttpPost("{flowId:guid}/merged")]
    public async Task<IActionResult> Merged(Guid flowId, [FromBody] ChefMarkMergedRequest req, CancellationToken ct)
    {
        if (!RequireChef()) return Forbid();
        try { await _lifecycle.MarkMergedAsync(flowId, null, req.Source ?? "chef-agent", ct); return NoContent(); }
        catch (FlowLifecycleException ex) { return BadRequest(ex.Message); }
    }

    // Task 6: the deploy worker reports the outcome of a Publishing deploy.
    [HttpPost("{flowId:guid}/published")]
    public async Task<IActionResult> Published(Guid flowId, CancellationToken ct)
    {
        if (!RequireChef()) return Forbid();
        try { await _lifecycle.MarkPublishedAsync(flowId, null, ct); return NoContent(); }
        catch (FlowLifecycleException ex) { return BadRequest(ex.Message); }
    }

    public sealed record ChefPublishFailedRequest(string? Reason);

    [HttpPost("{flowId:guid}/publish-failed")]
    public async Task<IActionResult> PublishFailed(Guid flowId, [FromBody] ChefPublishFailedRequest req, CancellationToken ct)
    {
        if (!RequireChef()) return Forbid();
        try { await _lifecycle.MarkPublishFailedAsync(flowId, req.Reason ?? "", null, ct); return NoContent(); }
        catch (FlowLifecycleException ex) { return BadRequest(ex.Message); }
    }

    private bool RequireChef()
        => User?.IsInRole(ChefAuthDefaults.Role) == true;

    private static FlowChatMessageDto ToDto(FlowChatMessage m) => new(
        m.Id, m.FlowId, m.Sender.ToString(), m.Kind.ToString(),
        m.Content, m.ArtifactsJson, m.Version, m.CreatedAt, m.AuthorUserId);

    private static bool TryParseKind(string raw, out FlowChatKind kind)
        => Enum.TryParse(raw, ignoreCase: true, out kind);
}
