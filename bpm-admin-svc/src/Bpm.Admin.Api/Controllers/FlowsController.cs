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

        var rows = await q
            .OrderByDescending(f => f.UpdatedAt)
            .Select(f => new FlowSummaryDto(
                f.Id, f.LineageId, f.Version, f.State, f.FlowCode, f.DisplayName, f.CreatedAt, f.UpdatedAt))
            .ToListAsync(ct);
        return Ok(rows);
    }

    [HttpGet("{id:guid}")]
    public async Task<ActionResult<FlowDetailDto>> Get(Guid id, CancellationToken ct)
    {
        var f = await _db.Flows.AsNoTracking().FirstOrDefaultAsync(x => x.Id == id, ct);
        if (f is null) return NotFound();
        return Ok(new FlowDetailDto(
            f.Id, f.LineageId, f.Version, f.State, f.FlowCode, f.DisplayName, f.SpecJson, f.Notes,
            f.CreatedByUserId, f.CreatedAt, f.UpdatedAt));
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

    [HttpPost("{id:guid}/retire")]
    public Task<ActionResult<FlowDetailDto>> Retire(Guid id, CancellationToken ct)
        => RunTransition(() => _lifecycle.RetireAsync(id, CurrentUserId(), ct));

    [HttpPost("{id:guid}/unretire")]
    public Task<ActionResult<FlowDetailDto>> Unretire(Guid id, CancellationToken ct)
        => RunTransition(() => _lifecycle.UnretireAsync(id, CurrentUserId(), ct));

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
                SourceInstanceId: req.SourceInstanceId ?? $"flow:{row.Id}");
            var bytes = await builder.BuildAsync(buildReq, ct);

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

    private static FlowDetailDto ToDetail(Flow f) => new(
        f.Id, f.LineageId, f.Version, f.State, f.FlowCode, f.DisplayName, f.SpecJson, f.Notes,
        f.CreatedByUserId, f.CreatedAt, f.UpdatedAt);
}
