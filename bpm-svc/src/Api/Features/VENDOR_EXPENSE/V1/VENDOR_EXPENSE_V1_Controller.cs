using Bpm.Api.Common;
using Bpm.Application.Common.Directory;
using Bpm.Application.Features.VENDOR_EXPENSE.V1;
using Bpm.Domain.Features.VENDOR_EXPENSE.V1;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Bpm.Api.Features.VENDOR_EXPENSE.V1;

/// <summary>
/// REST surface for VENDOR_EXPENSE V1. One POST per state-machine entry
/// point (submit / resubmit / the three stage decisions) plus the read
/// endpoints the form, case-detail page and inbox consume.
/// </summary>
[ApiController]
[Authorize]
[Route("api/vendor-expense/v1")]
public sealed class VENDOR_EXPENSE_V1_Controller(
    VENDOR_EXPENSE_V1_VendorExpenseService service,
    IVENDOR_EXPENSE_V1_CaseStore store,
    IPrincipalDirectory directory) : BpmControllerBase
{
    [HttpPost]
    public async Task<ActionResult<VENDOR_EXPENSE_V1_CaseResponse>> Submit(
        [FromBody] VENDOR_EXPENSE_V1_SubmitRequest req,
        CancellationToken ct)
    {
        var userId = RequireUserId();
        var c = await service.SubmitAsync(
            VENDOR_EXPENSE_V1_DtoMapping.ToServiceInput(userId, req), ct);
        var resp = await BuildResponseAsync(c, ct);
        return CreatedAtAction(nameof(GetById), new { caseId = c.Id }, resp);
    }

    [HttpPost("{caseId:guid}/resubmit")]
    public async Task<ActionResult<VENDOR_EXPENSE_V1_CaseResponse>> Resubmit(
        Guid caseId,
        [FromBody] VENDOR_EXPENSE_V1_SubmitRequest req,
        CancellationToken ct)
    {
        var userId = RequireUserId();
        var c = await service.ResubmitAsync(
            caseId, userId,
            VENDOR_EXPENSE_V1_DtoMapping.ToServiceInput(userId, req), ct);
        return await BuildResponseAsync(c, ct);
    }

    [HttpPost("{caseId:guid}/cancel")]
    public async Task<ActionResult<VENDOR_EXPENSE_V1_CaseResponse>> Cancel(
        Guid caseId, CancellationToken ct)
    {
        var c = await service.CancelAsync(caseId, RequireUserId(), ct);
        return await BuildResponseAsync(c, ct);
    }

    [HttpPost("{caseId:guid}/supervisor-decision")]
    public async Task<ActionResult<VENDOR_EXPENSE_V1_CaseResponse>> SupervisorDecision(
        Guid caseId,
        [FromBody] VENDOR_EXPENSE_V1_DecisionRequest req,
        CancellationToken ct)
    {
        var userId = RequireUserId();
        var c = req.Approve
            ? await service.ApproveBySupervisorAsync(caseId, userId, req.Comment, ct)
            : await service.RejectBySupervisorAsync(caseId, userId, req.Comment, ct);
        return await BuildResponseAsync(c, ct);
    }

    [HttpPost("{caseId:guid}/procurement-decision")]
    public async Task<ActionResult<VENDOR_EXPENSE_V1_CaseResponse>> ProcurementDecision(
        Guid caseId,
        [FromBody] VENDOR_EXPENSE_V1_DecisionRequest req,
        CancellationToken ct)
    {
        var userId = RequireUserId();
        var c = req.Approve
            ? await service.ApproveByProcurementAsync(caseId, userId, req.Comment, ct)
            : await service.RejectByProcurementAsync(caseId, userId, req.Comment, ct);
        return await BuildResponseAsync(c, ct);
    }

    [HttpPost("{caseId:guid}/sign-decision")]
    public async Task<ActionResult<VENDOR_EXPENSE_V1_CaseResponse>> SignDecision(
        Guid caseId,
        [FromBody] VENDOR_EXPENSE_V1_DecisionRequest req,
        CancellationToken ct)
    {
        var userId = RequireUserId();
        var c = req.Approve
            ? await service.ApproveBySignAsync(caseId, userId, req.Comment, ct)
            : await service.RejectBySignAsync(caseId, userId, req.Comment, ct);
        return await BuildResponseAsync(c, ct);
    }

    [HttpGet("{caseId:guid}")]
    public async Task<ActionResult<VENDOR_EXPENSE_V1_CaseResponse>> GetById(
        Guid caseId, CancellationToken ct)
    {
        var c = await store.FindByIdAsync(caseId, ct);
        if (c is null) return NotFound();
        return await BuildResponseAsync(c, ct);
    }

    [HttpGet("mine")]
    public async Task<IReadOnlyList<VENDOR_EXPENSE_V1_CaseRowResponse>> Mine(CancellationToken ct)
    {
        var userId = RequireUserId();
        var cases = await store.FindMineAsync(userId, ct);
        return await BuildRowsAsync(cases, ct);
    }

    [HttpGet("pending")]
    public async Task<IReadOnlyList<VENDOR_EXPENSE_V1_CaseRowResponse>> Pending(CancellationToken ct)
    {
        var userId = RequireUserId();
        var myRoles = await directory.GetRoleCodesForUserAsync(userId, ct);
        var cases = await store.FindPendingAsync(userId, myRoles, ct);
        return await BuildRowsAsync(cases, ct);
    }

    private async Task<VENDOR_EXPENSE_V1_CaseResponse> BuildResponseAsync(
        VENDOR_EXPENSE_V1_Case c, CancellationToken ct)
    {
        var ids = new[]
        {
            (Guid?)c.SubmitterUserId, c.CurrentAssigneeUserId,
            c.SupervisorUserId, c.ProcurementUserId, c.SignUserId,
        }.Where(x => x is not null).Select(x => x!.Value).Distinct().ToArray();
        var lookups = await directory.GetManyAsync(ids, ct);
        var names = lookups.ToDictionary(kv => kv.Key, kv => kv.Value.DisplayName);
        return VENDOR_EXPENSE_V1_DtoMapping.ToResponse(c, names);
    }

    private async Task<IReadOnlyList<VENDOR_EXPENSE_V1_CaseRowResponse>> BuildRowsAsync(
        IReadOnlyList<VENDOR_EXPENSE_V1_Case> cases, CancellationToken ct)
    {
        if (cases.Count == 0) return Array.Empty<VENDOR_EXPENSE_V1_CaseRowResponse>();
        var ids = cases
            .SelectMany(c => new[] { (Guid?)c.SubmitterUserId, c.CurrentAssigneeUserId })
            .Where(x => x is not null).Select(x => x!.Value).Distinct().ToArray();
        var lookups = await directory.GetManyAsync(ids, ct);
        var names = lookups.ToDictionary(kv => kv.Key, kv => kv.Value.DisplayName);
        return cases.Select(c => new VENDOR_EXPENSE_V1_CaseRowResponse(
            Id: c.Id,
            Status: c.Status.ToString(),
            Vendor: c.Vendor,
            InvoiceCount: c.Invoices.Count,
            SubmitterUserId: c.SubmitterUserId,
            SubmitterDisplayName: names.GetValueOrDefault(c.SubmitterUserId),
            CurrentAssigneeUserId: c.CurrentAssigneeUserId,
            CurrentAssigneeDisplayName: c.CurrentAssigneeUserId is { } a ? names.GetValueOrDefault(a) : null,
            SubmittedAt: c.SubmittedAt,
            LastActivityAt: c.LastActivityAt)).ToList();
    }
}
