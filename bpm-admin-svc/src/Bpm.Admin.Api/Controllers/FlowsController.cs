using System.Security.Claims;
using Bpm.Admin.Application.Flows;
using Bpm.Admin.Domain.Flows;
using Bpm.Admin.Persistence;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace Bpm.Admin.Api.Controllers;

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
