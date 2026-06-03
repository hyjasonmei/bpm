using Bpm.Api.Common;
using Bpm.Application.Common.Directory;
using Bpm.Application.Features.PURCHASE_REQUEST.V1;
using Bpm.Domain.Features.PURCHASE_REQUEST.V1;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Bpm.Api.Features.PURCHASE_REQUEST.V1;

[ApiController]
[Authorize]
[Route("api/purchase-request/v1")]
public sealed class PURCHASE_REQUEST_V1_Controller(
    PURCHASE_REQUEST_V1_PurchaseRequestService service,
    IPURCHASE_REQUEST_V1_CaseStore store,
    IPrincipalDirectory directory) : BpmControllerBase
{
    [HttpPost]
    public async Task<ActionResult<PURCHASE_REQUEST_V1_CaseResponse>> Submit(
        [FromBody] PURCHASE_REQUEST_V1_SubmitRequest req,
        CancellationToken ct)
    {
        var userId = RequireUserId();
        var c = await service.SubmitAsync(
            PURCHASE_REQUEST_V1_DtoMapping.ToServiceInput(userId, req), ct);
        var resp = await BuildResponseAsync(c, ct);
        return CreatedAtAction(nameof(GetById), new { caseId = c.Id }, resp);
    }

    [HttpPost("{caseId:guid}/resubmit")]
    public async Task<ActionResult<PURCHASE_REQUEST_V1_CaseResponse>> Resubmit(
        Guid caseId,
        [FromBody] PURCHASE_REQUEST_V1_SubmitRequest req,
        CancellationToken ct)
    {
        var userId = RequireUserId();
        var c = await service.ResubmitAsync(
            caseId, userId,
            PURCHASE_REQUEST_V1_DtoMapping.ToServiceInput(userId, req), ct);
        return await BuildResponseAsync(c, ct);
    }

    [HttpPost("{caseId:guid}/dept-head-decision")]
    public async Task<ActionResult<PURCHASE_REQUEST_V1_CaseResponse>> DeptHeadDecision(
        Guid caseId,
        [FromBody] DeptHeadDecisionRequest req,
        CancellationToken ct)
    {
        var userId = RequireUserId();
        var c = req.Approve
            ? await service.ApproveByDeptHeadAsync(caseId, userId, req.Comment, ct)
            : await service.RejectByDeptHeadAsync(caseId, userId, req.Comment, ct);
        return await BuildResponseAsync(c, ct);
    }

    [HttpPost("{caseId:guid}/finance-decision")]
    public async Task<ActionResult<PURCHASE_REQUEST_V1_CaseResponse>> FinanceDecision(
        Guid caseId,
        [FromBody] FinanceDecisionRequest req,
        CancellationToken ct)
    {
        var userId = RequireUserId();
        var c = req.Approve
            ? await service.ApproveByFinanceAsync(caseId, userId, req.Comment, ct)
            : await service.RejectByFinanceAsync(caseId, userId, req.Comment, ct);
        return await BuildResponseAsync(c, ct);
    }

    [HttpPost("{caseId:guid}/cancel")]
    public async Task<ActionResult<PURCHASE_REQUEST_V1_CaseResponse>> Cancel(
        Guid caseId, CancellationToken ct)
    {
        var c = await service.CancelAsync(caseId, RequireUserId(), ct);
        return await BuildResponseAsync(c, ct);
    }

    [HttpGet("{caseId:guid}")]
    public async Task<ActionResult<PURCHASE_REQUEST_V1_CaseResponse>> GetById(
        Guid caseId, CancellationToken ct)
    {
        var c = await store.FindByIdAsync(caseId, ct);
        if (c is null) return NotFound();
        return await BuildResponseAsync(c, ct);
    }

    [HttpGet("mine")]
    public async Task<IReadOnlyList<PURCHASE_REQUEST_V1_CaseRowResponse>> Mine(CancellationToken ct)
    {
        var userId = RequireUserId();
        var cases = await store.FindMineAsync(userId, ct);
        return await BuildRowsAsync(cases, ct);
    }

    [HttpGet("pending")]
    public async Task<IReadOnlyList<PURCHASE_REQUEST_V1_CaseRowResponse>> Pending(CancellationToken ct)
    {
        var userId = RequireUserId();
        var cases = await store.FindPendingAsync(userId, ct);
        return await BuildRowsAsync(cases, ct);
    }

    private async Task<PURCHASE_REQUEST_V1_CaseResponse> BuildResponseAsync(
        PURCHASE_REQUEST_V1_Case c, CancellationToken ct)
    {
        var ids = new[]
        {
            (Guid?)c.SubmitterUserId, c.CurrentAssigneeUserId, c.DeptHeadUserId, c.FinanceUserId,
        }.Where(x => x is not null).Select(x => x!.Value).Distinct().ToArray();
        var lookups = await directory.GetManyAsync(ids, ct);
        var names = lookups.ToDictionary(kv => kv.Key, kv => kv.Value.DisplayName);
        return PURCHASE_REQUEST_V1_DtoMapping.ToResponse(c, names);
    }

    private async Task<IReadOnlyList<PURCHASE_REQUEST_V1_CaseRowResponse>> BuildRowsAsync(
        IReadOnlyList<PURCHASE_REQUEST_V1_Case> cases, CancellationToken ct)
    {
        if (cases.Count == 0) return Array.Empty<PURCHASE_REQUEST_V1_CaseRowResponse>();
        var ids = cases
            .SelectMany(c => new[] { (Guid?)c.SubmitterUserId, c.CurrentAssigneeUserId })
            .Where(x => x is not null).Select(x => x!.Value).Distinct().ToArray();
        var lookups = await directory.GetManyAsync(ids, ct);
        var names = lookups.ToDictionary(kv => kv.Key, kv => kv.Value.DisplayName);
        return cases.Select(c => new PURCHASE_REQUEST_V1_CaseRowResponse(
            Id: c.Id,
            Status: c.Status.ToString(),
            InvoiceCount: c.Invoices.Count,
            SubmitterUserId: c.SubmitterUserId,
            SubmitterDisplayName: names.GetValueOrDefault(c.SubmitterUserId),
            CurrentAssigneeUserId: c.CurrentAssigneeUserId,
            CurrentAssigneeDisplayName: c.CurrentAssigneeUserId is { } a ? names.GetValueOrDefault(a) : null,
            SubmittedAt: c.SubmittedAt,
            LastActivityAt: c.LastActivityAt)).ToList();
    }

    public sealed record DeptHeadDecisionRequest(bool Approve, string? Comment);
    public sealed record FinanceDecisionRequest(bool Approve, string? Comment);
}
