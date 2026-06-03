using Bpm.Api.Common;
using Bpm.Application.Common.Directory;
using Bpm.Application.Features.TRQ.V1;
using Bpm.Domain.Features.TRQ.V1;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Bpm.Api.Features.TRQ.V1;

[ApiController]
[Authorize]
[Route("api/trq/v1")]
public sealed class TRQ_V1_Controller(
    TRQ_V1_TravelRequestService service,
    ITRQ_V1_CaseStore store,
    IPrincipalDirectory directory) : BpmControllerBase
{
    [HttpPost]
    public async Task<ActionResult<TRQ_V1_CaseResponse>> Submit(
        [FromBody] TRQ_V1_SubmitRequest req,
        CancellationToken ct)
    {
        var userId = RequireUserId();
        var c = await service.SubmitAsync(TRQ_V1_DtoMapping.ToServiceInput(userId, req), ct);
        var resp = await BuildResponseAsync(c, ct);
        return CreatedAtAction(nameof(GetById), new { caseId = c.Id }, resp);
    }

    [HttpPost("{caseId:guid}/resubmit")]
    public async Task<ActionResult<TRQ_V1_CaseResponse>> Resubmit(
        Guid caseId,
        [FromBody] TRQ_V1_SubmitRequest req,
        CancellationToken ct)
    {
        var userId = RequireUserId();
        var c = await service.ResubmitAsync(
            caseId, userId, TRQ_V1_DtoMapping.ToServiceInput(userId, req), ct);
        return await BuildResponseAsync(c, ct);
    }

    [HttpPost("{caseId:guid}/manager-decision")]
    public async Task<ActionResult<TRQ_V1_CaseResponse>> ManagerDecision(
        Guid caseId,
        [FromBody] ManagerDecisionRequest req,
        CancellationToken ct)
    {
        var userId = RequireUserId();
        var c = req.Approve
            ? await service.ApproveByManagerAsync(caseId, userId, req.Comment, ct)
            : await service.RejectByManagerAsync(caseId, userId, req.Comment, ct);
        return await BuildResponseAsync(c, ct);
    }

    [HttpPost("{caseId:guid}/cancel")]
    public async Task<ActionResult<TRQ_V1_CaseResponse>> Cancel(Guid caseId, CancellationToken ct)
    {
        var userId = RequireUserId();
        var c = await service.CancelAsync(caseId, userId, ct);
        return await BuildResponseAsync(c, ct);
    }

    [HttpGet("{caseId:guid}")]
    public async Task<ActionResult<TRQ_V1_CaseResponse>> GetById(Guid caseId, CancellationToken ct)
    {
        var c = await store.FindByIdAsync(caseId, ct);
        if (c is null) return NotFound();
        return await BuildResponseAsync(c, ct);
    }

    [HttpGet("mine")]
    public async Task<IReadOnlyList<TRQ_V1_CaseRowResponse>> Mine(CancellationToken ct)
    {
        var userId = RequireUserId();
        var cases = await store.FindMineAsync(userId, ct);
        return await BuildRowsAsync(cases, ct);
    }

    [HttpGet("pending")]
    public async Task<IReadOnlyList<TRQ_V1_CaseRowResponse>> Pending(CancellationToken ct)
    {
        var userId = RequireUserId();
        var cases = await store.FindPendingAsync(userId, ct);
        return await BuildRowsAsync(cases, ct);
    }

    private async Task<TRQ_V1_CaseResponse> BuildResponseAsync(TRQ_V1_Case c, CancellationToken ct)
    {
        var ids = new[] { (Guid?)c.SubmitterUserId, c.CurrentAssigneeUserId, c.ManagerUserId }
            .Where(x => x is not null).Select(x => x!.Value).Distinct().ToArray();
        var lookups = await directory.GetManyAsync(ids, ct);
        var names = lookups.ToDictionary(kv => kv.Key, kv => kv.Value.DisplayName);
        return TRQ_V1_DtoMapping.ToResponse(c, names);
    }

    private async Task<IReadOnlyList<TRQ_V1_CaseRowResponse>> BuildRowsAsync(
        IReadOnlyList<TRQ_V1_Case> cases, CancellationToken ct)
    {
        if (cases.Count == 0) return Array.Empty<TRQ_V1_CaseRowResponse>();
        var ids = cases
            .SelectMany(c => new[] { (Guid?)c.SubmitterUserId, c.CurrentAssigneeUserId })
            .Where(x => x is not null).Select(x => x!.Value).Distinct().ToArray();
        var lookups = await directory.GetManyAsync(ids, ct);
        var names = lookups.ToDictionary(kv => kv.Key, kv => kv.Value.DisplayName);
        return cases.Select(c => new TRQ_V1_CaseRowResponse(
            Id: c.Id,
            Status: c.Status.ToString(),
            DepartureCity: c.DepartureCity,
            DestinationCity: c.DestinationCity,
            SubmitterUserId: c.SubmitterUserId,
            SubmitterDisplayName: names.GetValueOrDefault(c.SubmitterUserId),
            CurrentAssigneeUserId: c.CurrentAssigneeUserId,
            CurrentAssigneeDisplayName: c.CurrentAssigneeUserId is { } a ? names.GetValueOrDefault(a) : null,
            SubmittedAt: c.SubmittedAt,
            LastActivityAt: c.LastActivityAt)).ToList();
    }

    public sealed record ManagerDecisionRequest(bool Approve, string? Comment);
}
