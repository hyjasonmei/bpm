using System.Security.Claims;
using System.Text.Json;
using Bpm.Admin.Application.Audit;
using Bpm.Admin.Application.Bundle;
using Bpm.Admin.Application.Flows;
using Bpm.Admin.Domain.Flows;
using Bpm.Admin.Persistence;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace Bpm.Admin.Api.Controllers;

public record BuildBundleRequest(
    string BpmnXml,
    SampleOrgSnapshot SampleOrg,
    IReadOnlyList<TestCaseSnapshot> TestCases,
    string? SourceInstanceId);

[ApiController]
[Route("api/flows")]
public class FlowsController : ControllerBase
{
    private readonly AdminDbContext _db;
    private readonly IFlowLifecycleService _lifecycle;

    public FlowsController(AdminDbContext db, IFlowLifecycleService lifecycle)
    {
        _db = db;
        _lifecycle = lifecycle;
    }

    private Guid? CurrentUserId()
    {
        var raw = User.FindFirstValue(ClaimTypes.NameIdentifier);
        return Guid.TryParse(raw, out var g) ? g : null;
    }

    [HttpGet]
    public async Task<ActionResult<IEnumerable<FlowSummaryDto>>> List(
        [FromQuery] FlowState? state,
        [FromQuery] Guid? lineageId,
        CancellationToken ct)
    {
        var q = _db.Flows.AsNoTracking().AsQueryable();
        if (state.HasValue) q = q.Where(f => f.State == state.Value);
        if (lineageId.HasValue) q = q.Where(f => f.LineageId == lineageId.Value);

        // LEFT JOIN against FlowGroups so the row carries the group code
        // for the admin-ui chip without a second round-trip per row.
        var rows = await q
            .OrderByDescending(f => f.UpdatedAt)
            .Select(f => new FlowSummaryDto(
                f.Id, f.LineageId, f.Version, f.State, f.FlowCode, f.DisplayName, f.CreatedAt, f.UpdatedAt, f.LastChefHeartbeatAt,
                f.GroupId,
                f.GroupId == null ? null : _db.FlowGroups.Where(g => g.Id == f.GroupId).Select(g => g.Code).FirstOrDefault(),
                f.IconKey, f.DisplayOrder,
                f.ChefWorkContextJson))
            .ToListAsync(ct);
        return Ok(rows);
    }

    [HttpGet("{id:guid}")]
    public async Task<ActionResult<FlowDetailDto>> Get(Guid id, CancellationToken ct)
    {
        var f = await _db.Flows.AsNoTracking().FirstOrDefaultAsync(x => x.Id == id, ct);
        if (f is null) return NotFound();
        return Ok(ToDetail(f));
    }

    [HttpPost]
    public async Task<ActionResult<FlowDetailDto>> Create([FromBody] CreateFlowRequest req, CancellationToken ct)
    {
        try
        {
            var f = await _lifecycle.CreateDraftAsync(req.FlowCode, req.DisplayName, req.SpecJson, CurrentUserId(), ct);
            return CreatedAtAction(nameof(Get), new { id = f.Id }, ToDetail(f));
        }
        catch (FlowLifecycleException ex) { return BadRequest(ex.Message); }
    }

    /// <summary>
    /// One-click backfill: register flows whose runtime code is deployed on
    /// bpm-svc but that never went through the wizard, directly in Approved
    /// state so the bpm launcher lists them. Idempotent. Drives admin-ui's
    /// "register shipped flows" button in AI Kitchen.
    /// </summary>
    [HttpPost("register-shipped")]
    public async Task<ActionResult<RegisterShippedResult>> RegisterShipped([FromBody] RegisterShippedRequest req, CancellationToken ct)
    {
        var result = await _lifecycle.RegisterShippedAsync(req.Flows ?? new List<ShippedFlowInput>(), CurrentUserId(), ct);
        return Ok(result);
    }

    [HttpPut("{id:guid}/spec")]
    public async Task<ActionResult<FlowDetailDto>> UpdateSpec(Guid id, [FromBody] UpdateFlowSpecRequest req, CancellationToken ct)
    {
        try
        {
            var f = await _lifecycle.UpdateSpecAsync(id, req.SpecJson, req.FlowCode, req.DisplayName, CurrentUserId(), ct);
            return Ok(ToDetail(f));
        }
        catch (FlowLifecycleException ex) { return Conflict(ex.Message); }
    }

    /// <summary>Rename a flow's display label only — allowed in any state.
    /// Does not touch flowCode or spec behaviour (only syncs meta.flowName).</summary>
    [HttpPatch("{id:guid}/display-name")]
    public Task<ActionResult<FlowDetailDto>> Rename(Guid id, [FromBody] RenameFlowRequest req, CancellationToken ct)
        => RunTransition(() => _lifecycle.RenameAsync(id, req.DisplayName, CurrentUserId(), ct));

    [HttpPost("{id:guid}/submit")]
    public Task<ActionResult<FlowDetailDto>> Submit(Guid id, CancellationToken ct)
        => RunTransition(() => _lifecycle.SubmitAsync(id, CurrentUserId(), ct));

    [HttpPost("{id:guid}/cancel")]
    public Task<ActionResult<FlowDetailDto>> Cancel(Guid id, CancellationToken ct)
        => RunTransition(() => _lifecycle.CancelAsync(id, CurrentUserId(), ct));

    [HttpPost("{id:guid}/resume")]
    public Task<ActionResult<FlowDetailDto>> Resume(Guid id, CancellationToken ct)
        => RunTransition(() => _lifecycle.ResumeAsync(id, CurrentUserId(), ct));

    [HttpPost("{id:guid}/clone-version")]
    public Task<ActionResult<FlowDetailDto>> CloneVersion(Guid id, CancellationToken ct)
        => RunTransition(() => _lifecycle.CloneVersionAsync(id, CurrentUserId(), ct));

    /// <summary>User-side ship-it (PR-S1): Committed → Approved. Called
    /// from the Serve tab Approve button.</summary>
    [HttpPost("{id:guid}/approve")]
    public Task<ActionResult<FlowDetailDto>> Approve(Guid id, CancellationToken ct)
        => RunTransition(() => _lifecycle.ApproveAsync(id, CurrentUserId(), ct));

    /// <summary>Approved → Published. Makes the flow live in this environment's
    /// launcher. (Serve tab Publish button.)</summary>
    [HttpPost("{id:guid}/publish")]
    public Task<ActionResult<FlowDetailDto>> Publish(Guid id, CancellationToken ct)
        => RunTransition(() => _lifecycle.PublishAsync(id, CurrentUserId(), ct));

    /// <summary>Published → Approved. Takes it offline here but keeps it reviewed.</summary>
    [HttpPost("{id:guid}/unpublish")]
    public Task<ActionResult<FlowDetailDto>> Unpublish(Guid id, CancellationToken ct)
        => RunTransition(() => _lifecycle.UnpublishAsync(id, CurrentUserId(), ct));

    [HttpPost("{id:guid}/retire")]
    public Task<ActionResult<FlowDetailDto>> Retire(Guid id, CancellationToken ct)
        => RunTransition(() => _lifecycle.RetireAsync(id, CurrentUserId(), ct));

    [HttpPost("{id:guid}/unretire")]
    public Task<ActionResult<FlowDetailDto>> Unretire(Guid id, CancellationToken ct)
        => RunTransition(() => _lifecycle.UnretireAsync(id, CurrentUserId(), ct));

    /// <summary>
    /// User-side escape hatch when chef appears stalled (state stuck on
    /// Cooking with no recent <c>LastChefHeartbeatAt</c>). Drops the
    /// flow back to Submitted so a fresh chef session can re-accept.
    /// </summary>
    [HttpPost("{id:guid}/chef-stall-reset")]
    public Task<ActionResult<FlowDetailDto>> ChefStallReset(Guid id, CancellationToken ct)
        => RunTransition(() => _lifecycle.ChefStallResetAsync(id, CurrentUserId(), ct));

    // ── PR-K1: Cook tab chat thread (user side) ──────────────────────

    /// <summary>
    /// User-visible message thread. Same payload chef sees over MCP /
    /// the chef HTTP API, but gated on user JWT.
    /// </summary>
    [HttpGet("{id:guid}/messages")]
    public async Task<ActionResult<IEnumerable<FlowChatMessageDto>>> ListMessages(
        Guid id,
        [FromQuery] DateTime? since,
        [FromServices] IFlowChatService chat,
        CancellationToken ct)
    {
        var exists = await _db.Flows.AnyAsync(f => f.Id == id, ct);
        if (!exists) return NotFound();
        var rows = await chat.ListAsync(id, since, ct);
        return Ok(rows.Select(m => new FlowChatMessageDto(
            m.Id, m.FlowId, m.Sender.ToString(), m.Kind.ToString(),
            m.Content, m.ArtifactsJson, m.Version, m.CreatedAt, m.AuthorUserId)));
    }

    /// <summary>
    /// User posts a reply / issue. Only legal when the flow is OnHold
    /// (Reply) or Committed (Issue). Chef picks it up on its next
    /// <c>chef_get_messages</c> call.
    /// </summary>
    [HttpPost("{id:guid}/chat-reply")]
    public async Task<ActionResult<FlowChatMessageDto>> ChatReply(
        Guid id,
        [FromBody] UserChatReplyRequest req,
        [FromServices] IFlowChatService chat,
        CancellationToken ct)
    {
        if (!Enum.TryParse<FlowChatKind>(req.Kind, ignoreCase: true, out var kind)
            || (kind != FlowChatKind.Reply && kind != FlowChatKind.Issue))
        {
            return BadRequest("kind must be 'Reply' or 'Issue'");
        }
        var flow = await _db.Flows.AsNoTracking().FirstOrDefaultAsync(f => f.Id == id, ct);
        if (flow is null) return NotFound();

        if (kind == FlowChatKind.Reply && flow.State != FlowState.OnHold)
            return Conflict($"Reply only allowed when state == OnHold (current: {flow.State})");
        if (kind == FlowChatKind.Issue && flow.State != FlowState.Committed && flow.State != FlowState.Approved)
            return Conflict($"Issue only allowed when state == Committed/Approved (current: {flow.State})");

        try
        {
            var row = await chat.AppendAsync(new AppendChatMessageInput(
                FlowId: id,
                Sender: FlowChatSender.User,
                Kind: kind,
                Content: req.Content,
                AuthorUserId: CurrentUserId()), ct);
            // PR-X4: Issue auto-reopens the flow so chef's next session
            // sees state=OnHold and resumes via chef_transition('Resume').
            // Reply doesn't change state — the flow is already OnHold,
            // chef just needs to read it.
            if (kind == FlowChatKind.Issue)
            {
                try
                {
                    await _lifecycle.ReopenForIssueAsync(id, CurrentUserId(), ct);
                    await chat.AppendAsync(new AppendChatMessageInput(
                        FlowId: id,
                        Sender: FlowChatSender.System,
                        Kind: FlowChatKind.System,
                        Content: $"Issue opened — flow auto-reopened (state → OnHold). Chef will pick up on next session via chef_transition('Resume')."), ct);
                }
                catch (FlowLifecycleException ex)
                {
                    // Transition refused (e.g. raced past Approved) — keep
                    // the chat row, but tell the caller the auto-reopen
                    // didn't fire. Frontend can still poll the state.
                    return Conflict($"Issue logged but auto-reopen failed: {ex.Message}");
                }
            }
            return Ok(new FlowChatMessageDto(
                row.Id, row.FlowId, row.Sender.ToString(), row.Kind.ToString(),
                row.Content, row.ArtifactsJson, row.Version, row.CreatedAt, row.AuthorUserId));
        }
        catch (FlowLifecycleException ex) { return BadRequest(ex.Message); }
    }

    [HttpDelete("{id:guid}")]
    public async Task<IActionResult> Delete(Guid id, CancellationToken ct)
    {
        try
        {
            await _lifecycle.SoftDeleteDraftAsync(id, CurrentUserId(), ct);
            return NoContent();
        }
        catch (FlowLifecycleException ex) { return Conflict(ex.Message); }
    }

    /// <summary>Build a portable .zip bundle from the row's current spec
    /// plus runtime-only inputs (bpmn.xml, sample-org, test-cases) the
    /// wizard already has in memory. Server reads spec from the row so a
    /// stale UI copy can't drift.</summary>
    [HttpPost("{id:guid}/bundle")]
    public async Task<IActionResult> Bundle(
        Guid id,
        [FromBody] BuildBundleRequest req,
        [FromServices] IBundleBuilder builder,
        [FromServices] IAuditLogger audit,
        CancellationToken ct)
    {
        var row = await _db.Flows.AsNoTracking().FirstOrDefaultAsync(f => f.Id == id, ct);
        if (row is null) return NotFound();

        JsonElement specJson;
        try
        {
            using var doc = JsonDocument.Parse(row.SpecJson);
            specJson = doc.RootElement.Clone();
        }
        catch (JsonException ex)
        {
            return Conflict($"Row spec is not valid JSON: {ex.Message}");
        }

        try
        {
            var buildReq = new BundleBuildRequest(
                DraftSpecJson: specJson,
                BpmnXml: req.BpmnXml ?? string.Empty,
                SampleOrg: req.SampleOrg,
                TestCases: req.TestCases ?? Array.Empty<TestCaseSnapshot>(),
                SourceInstanceId: req.SourceInstanceId ?? $"flow:{row.Id}",
                FlowId: row.Id);
            var bytes = await builder.BuildAsync(buildReq, ct);

            // PR-X3: cache the latest zip on the Flow row so chef can
            // re-download via MCP without admin-ui being open.
            var tracked = await _db.Flows.FirstAsync(f => f.Id == row.Id, ct);
            tracked.BundleBlob = bytes;
            tracked.BundleBuiltAt = DateTime.UtcNow;
            await _db.SaveChangesAsync(ct);

            await audit.LogAsync(
                actionType: "flow_bundle_built",
                targetType: "flow",
                targetId: row.Id.ToString(),
                actorUserId: CurrentUserId(),
                actorPrincipalId: null,
                after: new { row.FlowCode, row.Version, ByteCount = bytes.LongLength, FileCount = req.TestCases?.Count ?? 0 },
                ct: ct);

            var filename = $"{row.FlowCode}_v{row.Version}.zip";
            return File(bytes, "application/zip", filename);
        }
        catch (BundleBuildException ex) { return BadRequest(ex.Message); }
    }

    /// <summary>chef-facing endpoint. Authenticated by a shared secret header so chef
    /// doesn't need a session cookie. v0 secret comes from configuration.</summary>
    [HttpPost("{id:guid}/on-hold")]
    public async Task<ActionResult<FlowDetailDto>> OnHold(
        Guid id,
        [FromBody] OnHoldRequest req,
        [FromHeader(Name = "X-Chef-Secret")] string? chefSecret,
        [FromServices] IConfiguration config,
        CancellationToken ct)
    {
        var expected = config["Chef:SharedSecret"];
        if (string.IsNullOrEmpty(expected) || chefSecret != expected) return Unauthorized();

        try
        {
            var f = await _lifecycle.OnHoldFromChefAsync(id, req.Question, ct);
            return Ok(ToDetail(f));
        }
        catch (FlowLifecycleException ex) { return Conflict(ex.Message); }
    }

    private async Task<ActionResult<FlowDetailDto>> RunTransition(Func<Task<Flow>> action)
    {
        try
        {
            var f = await action();
            return Ok(ToDetail(f));
        }
        catch (FlowLifecycleException ex) { return Conflict(ex.Message); }
    }

    private FlowDetailDto ToDetail(Flow f)
    {
        var groupCode = f.GroupId.HasValue
            ? _db.FlowGroups.AsNoTracking().Where(g => g.Id == f.GroupId.Value).Select(g => g.Code).FirstOrDefault()
            : null;
        return new(
            f.Id, f.LineageId, f.Version, f.State, f.FlowCode, f.DisplayName, f.SpecJson, f.Notes,
            f.CreatedByUserId, f.CreatedAt, f.UpdatedAt, f.LastChefHeartbeatAt,
            f.GroupId, groupCode, f.IconKey, f.DisplayOrder, f.ChefWorkContextJson, f.BpmnXml);
    }

    /// <summary>Set or clear (<c>{ "iconKey": null }</c>) the launcher
    /// icon. Curated lucide name; display metadata only.</summary>
    [HttpPost("{id:guid}/icon")]
    public async Task<ActionResult<FlowDetailDto>> SetIcon(
        Guid id,
        [FromBody] SetFlowIconRequest req,
        CancellationToken ct)
    {
        try
        {
            var flow = await _lifecycle.SetIconAsync(id, req.IconKey, CurrentUserId(), ct);
            return Ok(ToDetail(flow));
        }
        catch (FlowLifecycleException ex) { return Conflict(ex.Message); }
    }

    /// <summary>Drag-to-reorder: body carries flow ids in display order;
    /// each row's <c>DisplayOrder</c> is set to its index.</summary>
    [HttpPost("reorder")]
    public async Task<IActionResult> Reorder([FromBody] ReorderFlowsRequest req, CancellationToken ct)
    {
        await _lifecycle.ReorderAsync(req.FlowIds ?? Array.Empty<Guid>(), CurrentUserId(), ct);
        return NoContent();
    }

    /// <summary>Set or clear the flow's launcher group. Empty body /
    /// <c>{ "groupId": null }</c> unassigns.</summary>
    [HttpPost("{id:guid}/group")]
    public async Task<ActionResult<FlowDetailDto>> AssignGroup(
        Guid id,
        [FromBody] AssignFlowGroupRequest req,
        CancellationToken ct)
    {
        try
        {
            var flow = await _lifecycle.AssignGroupAsync(id, req.GroupId, CurrentUserId(), ct);
            return Ok(ToDetail(flow));
        }
        catch (FlowLifecycleException ex) { return Conflict(ex.Message); }
    }
}
