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

    public ChefFlowsController(AdminDbContext db, IFlowLifecycleService lifecycle, IFlowChatService chat, IBundleBuilder bundle)
    {
        _db = db;
        _lifecycle = lifecycle;
        _chat = chat;
        _bundle = bundle;
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
            f.GroupId, null));
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
                updated.GroupId, null));
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
        // Real bundle build is owned by the user-facing FlowsController
        // /bundle action which takes a richer request. For chef we
        // re-use the same builder with a minimal input shape; admin-ui
        // is expected to have stashed sampleOrg + testCases on the
        // flow when it was submitted.
        // TODO(K2): wire to a richer chef bundle builder once we
        // surface this from MCP. For now return a hint.
        return StatusCode(StatusCodes.Status501NotImplemented,
            "chef bundle download not wired yet — use the admin-ui Download bundle for now");
    }

    private bool RequireChef()
        => User?.IsInRole(ChefAuthDefaults.Role) == true;

    private static FlowChatMessageDto ToDto(FlowChatMessage m) => new(
        m.Id, m.FlowId, m.Sender.ToString(), m.Kind.ToString(),
        m.Content, m.ArtifactsJson, m.Version, m.CreatedAt, m.AuthorUserId);

    private static bool TryParseKind(string raw, out FlowChatKind kind)
        => Enum.TryParse(raw, ignoreCase: true, out kind);
}
