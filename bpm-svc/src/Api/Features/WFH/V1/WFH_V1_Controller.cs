using Bpm.Api.Common;
using Bpm.Application.Common.Directory;
using Bpm.Application.Features.WFH.V1;
using Bpm.Domain.Features.WFH.V1;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Bpm.Api.Features.WFH.V1;

[ApiController]
[Authorize]
[Route("api/wfh/v1")]
public sealed class WFH_V1_Controller(
    WFH_V1_WfhService service,
    IWFH_V1_CaseStore store,
    IPrincipalDirectory directory) : BpmControllerBase
{
    [HttpPost]
    public async Task<ActionResult<WFH_V1_CaseResponse>> Submit([FromBody] WFH_V1_SubmitRequest req, CancellationToken ct)
    {
        var userId = RequireUserId();
        var c = await service.SubmitAsync(WFH_V1_DtoMapping.ToServiceInput(userId, req), ct);
        return CreatedAtAction(nameof(GetById), new { caseId = c.Id }, await BuildResponseAsync(c, ct));
    }

    [HttpPost("{caseId:guid}/resubmit")]
    public async Task<ActionResult<WFH_V1_CaseResponse>> Resubmit(Guid caseId, [FromBody] WFH_V1_SubmitRequest req, CancellationToken ct)
    {
        var userId = RequireUserId();
        var c = await service.ResubmitAsync(caseId, userId, WFH_V1_DtoMapping.ToServiceInput(userId, req), ct);
        return await BuildResponseAsync(c, ct);
    }

    [HttpPost("{caseId:guid}/manager-decision")]
    public async Task<ActionResult<WFH_V1_CaseResponse>> ManagerDecision(Guid caseId, [FromBody] WFH_V1_DecisionRequest req, CancellationToken ct)
    {
        var userId = RequireUserId();
        var c = req.Approve
            ? await service.ApproveByManagerAsync(caseId, userId, req.Comment, ct)
            : await service.RejectByManagerAsync(caseId, userId, req.Comment, ct);
        return await BuildResponseAsync(c, ct);
    }

    [HttpPost("{caseId:guid}/senior-decision")]
    public async Task<ActionResult<WFH_V1_CaseResponse>> SeniorDecision(Guid caseId, [FromBody] WFH_V1_DecisionRequest req, CancellationToken ct)
    {
        var userId = RequireUserId();
        var c = req.Approve
            ? await service.ApproveBySeniorAsync(caseId, userId, req.Comment, ct)
            : await service.RejectBySeniorAsync(caseId, userId, req.Comment, ct);
        return await BuildResponseAsync(c, ct);
    }

    [HttpPost("{caseId:guid}/cancel")]
    public async Task<ActionResult<WFH_V1_CaseResponse>> Cancel(Guid caseId, CancellationToken ct)
    {
        var userId = RequireUserId();
        var c = await service.CancelAsync(caseId, userId, ct);
        return await BuildResponseAsync(c, ct);
    }

    [HttpGet("{caseId:guid}")]
    public async Task<ActionResult<WFH_V1_CaseResponse>> GetById(Guid caseId, CancellationToken ct)
    {
        var c = await store.FindByIdAsync(caseId, ct);
        if (c is null) return NotFound();
        return await BuildResponseAsync(c, ct);
    }

    [HttpGet("mine")]
    public async Task<IReadOnlyList<WFH_V1_CaseRowResponse>> Mine(CancellationToken ct)
        => await BuildRowsAsync(await store.FindMineAsync(RequireUserId(), ct), ct);

    [HttpGet("pending")]
    public async Task<IReadOnlyList<WFH_V1_CaseRowResponse>> Pending(CancellationToken ct)
        => await BuildRowsAsync(await store.FindPendingAsync(RequireUserId(), ct), ct);

    private async Task<WFH_V1_CaseResponse> BuildResponseAsync(WFH_V1_Case c, CancellationToken ct)
    {
        var ids = new[] { (Guid?)c.SubmitterUserId, c.CurrentAssigneeUserId, c.ManagerUserId, c.SeniorUserId }
            .Where(x => x is not null).Select(x => x!.Value).Distinct().ToArray();
        var lookups = await directory.GetManyAsync(ids, ct);
        return WFH_V1_DtoMapping.ToResponse(c, lookups.ToDictionary(kv => kv.Key, kv => kv.Value.DisplayName));
    }

    private async Task<IReadOnlyList<WFH_V1_CaseRowResponse>> BuildRowsAsync(IReadOnlyList<WFH_V1_Case> cases, CancellationToken ct)
    {
        if (cases.Count == 0) return Array.Empty<WFH_V1_CaseRowResponse>();
        var ids = cases.SelectMany(c => new[] { (Guid?)c.SubmitterUserId, c.CurrentAssigneeUserId })
            .Where(x => x is not null).Select(x => x!.Value).Distinct().ToArray();
        var lookups = await directory.GetManyAsync(ids, ct);
        var names = lookups.ToDictionary(kv => kv.Key, kv => kv.Value.DisplayName);
        return cases.Select(c => new WFH_V1_CaseRowResponse(
            Id: c.Id, Days: c.Days, StartDate: c.StartDate, EndDate: c.EndDate,
            Status: c.Status.ToString(),
            SubmitterUserId: c.SubmitterUserId, SubmitterDisplayName: names.GetValueOrDefault(c.SubmitterUserId),
            CurrentAssigneeUserId: c.CurrentAssigneeUserId,
            CurrentAssigneeDisplayName: c.CurrentAssigneeUserId is { } a ? names.GetValueOrDefault(a) : null,
            SubmittedAt: c.SubmittedAt, LastActivityAt: c.LastActivityAt)).ToList();
    }
}
