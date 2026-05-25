using Bpm.Api.Common;
using Bpm.Persistence;
using Bpm.Persistence.Features.LEAVE.V1;
using Bpm.Persistence.SharedIdentity;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace Bpm.Api.Features.LEAVE.V1;

[ApiController]
[Authorize]
[Route("api/leave/v1")]
public sealed class LEAVE_V1_Controller(
    LEAVE_V1_LeaveService service,
    AppDbContext db) : BpmControllerBase
{
    [HttpPost]
    public async Task<ActionResult<LEAVE_V1_CaseResponse>> Submit(
        [FromBody] LEAVE_V1_SubmitRequest req,
        CancellationToken ct)
    {
        var userId = RequireUserId();
        var c = await service.SubmitAsync(new LEAVE_V1_LeaveService.SubmitInput(
            SubmitterUserId: userId,
            LeaveType: req.LeaveType,
            StartDate: req.DateRange.Start,
            EndDate: req.DateRange.End,
            Reason: req.Reason,
            CertFileId: req.CertFileId), ct);

        var resp = await BuildResponseAsync(c, ct);
        return CreatedAtAction(nameof(GetById), new { caseId = c.Id }, resp);
    }

    [HttpPost("{caseId:guid}/manager-decision")]
    public async Task<ActionResult<LEAVE_V1_CaseResponse>> ManagerDecision(
        Guid caseId,
        [FromBody] LEAVE_V1_DecisionRequest req,
        CancellationToken ct)
    {
        var userId = RequireUserId();
        var c = await service.ManagerDecisionAsync(caseId, userId, req.Approve, req.Comment, ct);
        return await BuildResponseAsync(c, ct);
    }

    [HttpPost("{caseId:guid}/vp-decision")]
    public async Task<ActionResult<LEAVE_V1_CaseResponse>> VpDecision(
        Guid caseId,
        [FromBody] LEAVE_V1_DecisionRequest req,
        CancellationToken ct)
    {
        var userId = RequireUserId();
        var c = await service.VpDecisionAsync(caseId, userId, req.Approve, req.Comment, ct);
        return await BuildResponseAsync(c, ct);
    }

    [HttpPost("{caseId:guid}/hr-archive")]
    public async Task<ActionResult<LEAVE_V1_CaseResponse>> HrArchive(
        Guid caseId,
        [FromBody] LEAVE_V1_HrArchiveRequest req,
        CancellationToken ct)
    {
        var userId = RequireUserId();
        var c = await service.HrArchiveAsync(caseId, userId, req.ArchiveNote, ct);
        return await BuildResponseAsync(c, ct);
    }

    [HttpGet("{caseId:guid}")]
    public async Task<ActionResult<LEAVE_V1_CaseResponse>> GetById(Guid caseId, CancellationToken ct)
    {
        var c = await db.LEAVE_V1_Cases.AsNoTracking().SingleOrDefaultAsync(x => x.Id == caseId, ct);
        if (c == null) return NotFound();
        return await BuildResponseAsync(c, ct);
    }

    [HttpGet("mine")]
    public async Task<IReadOnlyList<LEAVE_V1_CaseRowResponse>> Mine(CancellationToken ct)
    {
        var userId = RequireUserId();
        var cases = await db.LEAVE_V1_Cases.AsNoTracking()
            .Where(c => c.SubmitterUserId == userId)
            .OrderByDescending(c => c.LastActivityAt)
            .ToListAsync(ct);
        return await BuildRowsAsync(cases, ct);
    }

    [HttpGet("pending")]
    public async Task<IReadOnlyList<LEAVE_V1_CaseRowResponse>> Pending(CancellationToken ct)
    {
        var userId = RequireUserId();
        var cases = await db.LEAVE_V1_Cases.AsNoTracking()
            .Where(c => c.CurrentAssigneeUserId == userId
                        && c.Status != LEAVE_V1_CaseStatus.Completed
                        && c.Status != LEAVE_V1_CaseStatus.Rejected
                        && c.Status != LEAVE_V1_CaseStatus.Cancelled)
            .OrderByDescending(c => c.LastActivityAt)
            .ToListAsync(ct);
        return await BuildRowsAsync(cases, ct);
    }

    private async Task<LEAVE_V1_CaseResponse> BuildResponseAsync(LEAVE_V1_Case c, CancellationToken ct)
    {
        var userIds = new[]
        {
            (Guid?)c.SubmitterUserId, c.CurrentAssigneeUserId, c.ManagerUserId, c.VpUserId, c.HrUserId,
        }.Where(x => x != null).Select(x => x!.Value).Distinct().ToArray();
        var names = await LoadDisplayNamesAsync(userIds, ct);
        return LEAVE_V1_DtoMapping.ToResponse(c, names);
    }

    private async Task<IReadOnlyList<LEAVE_V1_CaseRowResponse>> BuildRowsAsync(IReadOnlyList<LEAVE_V1_Case> cases, CancellationToken ct)
    {
        if (cases.Count == 0) return Array.Empty<LEAVE_V1_CaseRowResponse>();
        var userIds = cases
            .SelectMany(c => new[] { (Guid?)c.SubmitterUserId, c.CurrentAssigneeUserId })
            .Where(x => x != null).Select(x => x!.Value).Distinct().ToArray();
        var names = await LoadDisplayNamesAsync(userIds, ct);
        return cases.Select(c => new LEAVE_V1_CaseRowResponse(
            Id: c.Id,
            LeaveType: c.LeaveType,
            StartDate: c.StartDate,
            EndDate: c.EndDate,
            Days: c.Days,
            Status: c.Status.ToString(),
            SubmitterUserId: c.SubmitterUserId,
            SubmitterDisplayName: names.GetValueOrDefault(c.SubmitterUserId),
            CurrentAssigneeUserId: c.CurrentAssigneeUserId,
            CurrentAssigneeDisplayName: c.CurrentAssigneeUserId is { } a ? names.GetValueOrDefault(a) : null,
            SubmittedAt: c.SubmittedAt,
            LastActivityAt: c.LastActivityAt)).ToList();
    }

    private async Task<IReadOnlyDictionary<Guid, string>> LoadDisplayNamesAsync(Guid[] userIds, CancellationToken ct)
    {
        if (userIds.Length == 0) return new Dictionary<Guid, string>();
        return await db.SharedPrincipals.AsNoTracking()
            .Where(p => userIds.Contains(p.Id))
            .ToDictionaryAsync(p => p.Id, p => p.DisplayName, ct);
    }
}
