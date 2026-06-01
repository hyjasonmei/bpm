using Bpm.Api.Common;
using Bpm.Application.Common.Directory;
using Bpm.Application.Features.TEO.V1;
using Bpm.Domain.Features.TEO.V1;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Bpm.Api.Features.TEO.V1;

[ApiController]
[Authorize]
[Route("api/teo/v1")]
public sealed class TEO_V1_Controller(
    TEO_V1_TravelExpenseService service,
    ITEO_V1_CaseStore store,
    IPrincipalDirectory directory) : BpmControllerBase
{
    [HttpPost]
    public async Task<ActionResult<TEO_V1_CaseResponse>> Submit([FromBody] TEO_V1_SubmitRequest req, CancellationToken ct)
    {
        var userId = RequireUserId();
        var c = await service.SubmitAsync(TEO_V1_DtoMapping.ToServiceInput(userId, req), ct);
        return CreatedAtAction(nameof(GetById), new { caseId = c.Id }, await BuildResponseAsync(c, ct));
    }

    [HttpPost("{caseId:guid}/resubmit")]
    public async Task<ActionResult<TEO_V1_CaseResponse>> Resubmit(Guid caseId, [FromBody] TEO_V1_SubmitRequest req, CancellationToken ct)
    {
        var userId = RequireUserId();
        var c = await service.ResubmitAsync(caseId, userId, TEO_V1_DtoMapping.ToServiceInput(userId, req), ct);
        return await BuildResponseAsync(c, ct);
    }

    [HttpPost("{caseId:guid}/manager-decision")]
    public async Task<ActionResult<TEO_V1_CaseResponse>> ManagerDecision(Guid caseId, [FromBody] DecisionRequest req, CancellationToken ct)
    {
        var userId = RequireUserId();
        var c = req.Approve
            ? await service.ApproveByManagerAsync(caseId, userId, req.Comment, ct)
            : await service.RejectByManagerAsync(caseId, userId, req.Comment, ct);
        return await BuildResponseAsync(c, ct);
    }

    [HttpPost("{caseId:guid}/finance-decision")]
    public async Task<ActionResult<TEO_V1_CaseResponse>> FinanceDecision(Guid caseId, [FromBody] DecisionRequest req, CancellationToken ct)
    {
        var userId = RequireUserId();
        var c = req.Approve
            ? await service.ApproveByFinanceAsync(caseId, userId, req.Comment, ct)
            : await service.RejectByFinanceAsync(caseId, userId, req.Comment, ct);
        return await BuildResponseAsync(c, ct);
    }

    [HttpGet("{caseId:guid}")]
    public async Task<ActionResult<TEO_V1_CaseResponse>> GetById(Guid caseId, CancellationToken ct)
    {
        var c = await store.FindByIdAsync(caseId, ct);
        if (c is null) return NotFound();
        return await BuildResponseAsync(c, ct);
    }

    [HttpGet("mine")]
    public async Task<IReadOnlyList<TEO_V1_CaseRowResponse>> Mine(CancellationToken ct)
        => await BuildRowsAsync(await store.FindMineAsync(RequireUserId(), ct), ct);

    [HttpGet("pending")]
    public async Task<IReadOnlyList<TEO_V1_CaseRowResponse>> Pending(CancellationToken ct)
        => await BuildRowsAsync(await store.FindPendingAsync(RequireUserId(), ct), ct);

    private async Task<TEO_V1_CaseResponse> BuildResponseAsync(TEO_V1_Case c, CancellationToken ct)
    {
        var ids = new[] { (Guid?)c.SubmitterUserId, c.CurrentAssigneeUserId, c.ManagerUserId, c.FinanceUserId }
            .Where(x => x is not null).Select(x => x!.Value).Distinct().ToArray();
        var lookups = await directory.GetManyAsync(ids, ct);
        return TEO_V1_DtoMapping.ToResponse(c, lookups.ToDictionary(kv => kv.Key, kv => kv.Value.DisplayName));
    }

    private async Task<IReadOnlyList<TEO_V1_CaseRowResponse>> BuildRowsAsync(IReadOnlyList<TEO_V1_Case> cases, CancellationToken ct)
    {
        if (cases.Count == 0) return Array.Empty<TEO_V1_CaseRowResponse>();
        var ids = cases.SelectMany(c => new[] { (Guid?)c.SubmitterUserId, c.CurrentAssigneeUserId })
            .Where(x => x is not null).Select(x => x!.Value).Distinct().ToArray();
        var lookups = await directory.GetManyAsync(ids, ct);
        var names = lookups.ToDictionary(kv => kv.Key, kv => kv.Value.DisplayName);
        return cases.Select(c => new TEO_V1_CaseRowResponse(
            Id: c.Id, Status: c.Status.ToString(), ItemCount: c.ExpenseItems.Count,
            SubmitterUserId: c.SubmitterUserId, SubmitterDisplayName: names.GetValueOrDefault(c.SubmitterUserId),
            CurrentAssigneeUserId: c.CurrentAssigneeUserId,
            CurrentAssigneeDisplayName: c.CurrentAssigneeUserId is { } a ? names.GetValueOrDefault(a) : null,
            SubmittedAt: c.SubmittedAt, LastActivityAt: c.LastActivityAt)).ToList();
    }

    public sealed record DecisionRequest(bool Approve, string? Comment);
}
