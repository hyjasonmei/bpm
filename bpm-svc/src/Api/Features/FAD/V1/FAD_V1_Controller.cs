using Bpm.Api.Common;
using Bpm.Application.Common.Directory;
using Bpm.Application.Features.FAD.V1;
using Bpm.Domain.Features.FAD.V1;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Bpm.Api.Features.FAD.V1;

[ApiController]
[Authorize]
[Route("api/fad/v1")]
public sealed class FAD_V1_Controller(
    FAD_V1_DisposalService service,
    IFAD_V1_CaseStore store,
    IPrincipalDirectory directory) : BpmControllerBase
{
    [HttpPost]
    public async Task<ActionResult<FAD_V1_CaseResponse>> Submit([FromBody] FAD_V1_SubmitRequest req, CancellationToken ct)
    {
        var userId = RequireUserId();
        var c = await service.SubmitAsync(FAD_V1_DtoMapping.ToServiceInput(userId, req), ct);
        return CreatedAtAction(nameof(GetById), new { caseId = c.Id }, await BuildResponseAsync(c, ct));
    }

    [HttpPost("{caseId:guid}/resubmit")]
    public async Task<ActionResult<FAD_V1_CaseResponse>> Resubmit(Guid caseId, [FromBody] FAD_V1_SubmitRequest req, CancellationToken ct)
    {
        var userId = RequireUserId();
        var c = await service.ResubmitAsync(caseId, userId, FAD_V1_DtoMapping.ToServiceInput(userId, req), ct);
        return await BuildResponseAsync(c, ct);
    }

    [HttpPost("{caseId:guid}/manager-decision")]
    public async Task<ActionResult<FAD_V1_CaseResponse>> ManagerDecision(Guid caseId, [FromBody] DecisionRequest req, CancellationToken ct)
    {
        var userId = RequireUserId();
        var c = req.Approve
            ? await service.ApproveByManagerAsync(caseId, userId, req.Comment, ct)
            : await service.RejectByManagerAsync(caseId, userId, req.Comment, ct);
        return await BuildResponseAsync(c, ct);
    }

    [HttpPost("{caseId:guid}/confirm")]
    public async Task<ActionResult<FAD_V1_CaseResponse>> Confirm(Guid caseId, [FromBody] ConfirmRequest req, CancellationToken ct)
    {
        var userId = RequireUserId();
        var c = await service.CompleteConfirmAsync(caseId, userId, req.HandlingResult, req.Remark, ct);
        return await BuildResponseAsync(c, ct);
    }

    [HttpGet("{caseId:guid}")]
    public async Task<ActionResult<FAD_V1_CaseResponse>> GetById(Guid caseId, CancellationToken ct)
    {
        var c = await store.FindByIdAsync(caseId, ct);
        if (c is null) return NotFound();
        return await BuildResponseAsync(c, ct);
    }

    [HttpGet("mine")]
    public async Task<IReadOnlyList<FAD_V1_CaseRowResponse>> Mine(CancellationToken ct)
        => await BuildRowsAsync(await store.FindMineAsync(RequireUserId(), ct), ct);

    [HttpGet("pending")]
    public async Task<IReadOnlyList<FAD_V1_CaseRowResponse>> Pending(CancellationToken ct)
        => await BuildRowsAsync(await store.FindPendingAsync(RequireUserId(), ct), ct);

    private async Task<FAD_V1_CaseResponse> BuildResponseAsync(FAD_V1_Case c, CancellationToken ct)
    {
        var ids = new[] { (Guid?)c.SubmitterUserId, c.CurrentAssigneeUserId, c.ManagerUserId, c.ConfirmedByUserId }
            .Where(x => x is not null).Select(x => x!.Value).Distinct().ToArray();
        var lookups = await directory.GetManyAsync(ids, ct);
        return FAD_V1_DtoMapping.ToResponse(c, lookups.ToDictionary(kv => kv.Key, kv => kv.Value.DisplayName));
    }

    private async Task<IReadOnlyList<FAD_V1_CaseRowResponse>> BuildRowsAsync(IReadOnlyList<FAD_V1_Case> cases, CancellationToken ct)
    {
        if (cases.Count == 0) return Array.Empty<FAD_V1_CaseRowResponse>();
        var ids = cases.SelectMany(c => new[] { (Guid?)c.SubmitterUserId, c.CurrentAssigneeUserId })
            .Where(x => x is not null).Select(x => x!.Value).Distinct().ToArray();
        var lookups = await directory.GetManyAsync(ids, ct);
        var names = lookups.ToDictionary(kv => kv.Key, kv => kv.Value.DisplayName);
        return cases.Select(c => new FAD_V1_CaseRowResponse(
            Id: c.Id, Status: c.Status.ToString(), AssetName: c.AssetName,
            SubmitterUserId: c.SubmitterUserId, SubmitterDisplayName: names.GetValueOrDefault(c.SubmitterUserId),
            CurrentAssigneeUserId: c.CurrentAssigneeUserId,
            CurrentAssigneeDisplayName: c.CurrentAssigneeUserId is { } a ? names.GetValueOrDefault(a) : null,
            SubmittedAt: c.SubmittedAt, LastActivityAt: c.LastActivityAt)).ToList();
    }

    public sealed record DecisionRequest(bool Approve, string? Comment);
    public sealed record ConfirmRequest(string HandlingResult, string? Remark);
}
