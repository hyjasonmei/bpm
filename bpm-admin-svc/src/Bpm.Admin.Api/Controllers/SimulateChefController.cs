using Bpm.Admin.Application.Flows;
using Bpm.Admin.Domain.Flows;
using Microsoft.AspNetCore.Mvc;

namespace Bpm.Admin.Api.Controllers;

/// <summary>
/// Admin-ui demo helpers — let the user pretend to be chef when no
/// real Claude Code session is attached. Internally calls the same
/// chef Application services that the chef MCP tools / chef HTTP API
/// do, so the resulting timeline is identical regardless of which path
/// fired the message.
///
/// User-JWT gated so admin-ui never has to know the chef bearer token.
/// </summary>
[ApiController]
[Route("api/flows/{flowId:guid}/simulate-chef")]
public sealed class SimulateChefController : ControllerBase
{
    private readonly IFlowLifecycleService _lifecycle;
    private readonly IFlowChatService _chat;

    public SimulateChefController(IFlowLifecycleService lifecycle, IFlowChatService chat)
    {
        _lifecycle = lifecycle;
        _chat = chat;
    }

    [HttpPost("start")]
    public Task<ActionResult<FlowChatMessageDto>> Start(Guid flowId, [FromBody] SimulateMemoRequest? req, CancellationToken ct)
        => RunSim(flowId, ct,
            doTransition: () => _lifecycle.ChefAcceptAsync(flowId, ct),
            kind: FlowChatKind.Memo,
            content: req?.Content ?? "Picking up the spec. I'll scaffold **Domain → Application → Persistence → Api → UI** in that order, then run the integration suite.");

    [HttpPost("ask")]
    public async Task<ActionResult<FlowChatMessageDto>> Ask(Guid flowId, [FromBody] SimulateQuestionRequest req, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(req?.Question))
            return BadRequest("question required");
        try
        {
            await _lifecycle.OnHoldFromChefAsync(flowId, req.Question, ct);
            var row = await _chat.AppendAsync(new AppendChatMessageInput(
                FlowId: flowId, Sender: FlowChatSender.Chef, Kind: FlowChatKind.Question, Content: req.Question), ct);
            return Ok(ToDto(row));
        }
        catch (FlowLifecycleException ex) { return Conflict(ex.Message); }
    }

    [HttpPost("complete")]
    public Task<ActionResult<FlowChatMessageDto>> Complete(Guid flowId, [FromBody] SimulateCompleteRequest? req, CancellationToken ct)
        => RunSim(flowId, ct,
            doTransition: () => _lifecycle.ChefCommitAsync(flowId, ct),
            kind: FlowChatKind.Completion,
            content: req?.Content ?? "**Cook complete.** ✓ All layers scaffolded; integration suite green.",
            artifactsJson: req?.ArtifactsJson,
            version: req?.Version);

    [HttpPost("resume")]
    public Task<ActionResult<FlowChatMessageDto>> Resume(Guid flowId, [FromBody] SimulateMemoRequest? req, CancellationToken ct)
        => RunSim(flowId, ct,
            doTransition: () => _lifecycle.ChefResumeAsync(flowId, ct),
            kind: FlowChatKind.Memo,
            content: req?.Content ?? "Got it — resuming. Will fold the change into the next cook.");

    private async Task<ActionResult<FlowChatMessageDto>> RunSim(
        Guid flowId, CancellationToken ct,
        Func<Task<Flow>> doTransition,
        FlowChatKind kind,
        string content,
        string? artifactsJson = null,
        string? version = null)
    {
        try
        {
            await doTransition();
            var row = await _chat.AppendAsync(new AppendChatMessageInput(
                FlowId: flowId,
                Sender: FlowChatSender.Chef,
                Kind: kind,
                Content: content,
                ArtifactsJson: artifactsJson,
                Version: version), ct);
            return Ok(ToDto(row));
        }
        catch (FlowLifecycleException ex)
        {
            return Conflict(ex.Message);
        }
    }

    private static FlowChatMessageDto ToDto(FlowChatMessage m) => new(
        m.Id, m.FlowId, m.Sender.ToString(), m.Kind.ToString(),
        m.Content, m.ArtifactsJson, m.Version, m.CreatedAt, m.AuthorUserId);

}

public sealed record SimulateMemoRequest(string? Content);
public sealed record SimulateQuestionRequest(string Question);
public sealed record SimulateCompleteRequest(string? Content, string? ArtifactsJson, string? Version);
