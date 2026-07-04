using Bpm.Api.Common;
using Bpm.Application.Common.Directory;
using Bpm.Application.Features.CONTRACT_REVIEW.V1;
using Bpm.Application.Parallel;
using Bpm.Domain.Features.CONTRACT_REVIEW.V1;
using Bpm.Domain.Parallel;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Bpm.Api.Features.CONTRACT_REVIEW.V1;

[ApiController]
[Authorize]
[Route("api/contract-review/v1")]
public sealed class CONTRACT_REVIEW_V1_Controller(
    CONTRACT_REVIEW_V1_Service service,
    ICONTRACT_REVIEW_V1_CaseStore store,
    IParallelApprovalService parallel,
    IPrincipalDirectory directory) : BpmControllerBase
{
    [HttpPost]
    public async Task<ActionResult<CONTRACT_REVIEW_V1_CaseResponse>> Submit(
        [FromBody] CONTRACT_REVIEW_V1_SubmitRequest req, CancellationToken ct)
    {
        var userId = RequireUserId();
        var c = await service.SubmitAsync(ToInput(userId, req), ct);
        return CreatedAtAction(nameof(GetById), new { caseId = c.Id }, await BuildAsync(c, ct));
    }

    [HttpPost("{caseId:guid}/slots/{slotId:guid}/decision")]
    public async Task<ActionResult<CONTRACT_REVIEW_V1_CaseResponse>> Decide(
        Guid caseId, Guid slotId, [FromBody] CONTRACT_REVIEW_V1_DecisionRequest req, CancellationToken ct)
    {
        var c = await service.DecideAsync(caseId, slotId, RequireUserId(), req.Approve, req.Comment, ct);
        return await BuildAsync(c, ct);
    }

    [HttpPost("{caseId:guid}/resubmit")]
    public async Task<ActionResult<CONTRACT_REVIEW_V1_CaseResponse>> Resubmit(
        Guid caseId, [FromBody] CONTRACT_REVIEW_V1_SubmitRequest req, CancellationToken ct)
    {
        var userId = RequireUserId();
        var c = await service.ResubmitAsync(caseId, userId, ToInput(userId, req), req.RevisionNote, ct);
        return await BuildAsync(c, ct);
    }

    [HttpPost("{caseId:guid}/legal-manager-decision")]
    public async Task<ActionResult<CONTRACT_REVIEW_V1_CaseResponse>> LegalManagerDecision(
        Guid caseId, [FromBody] CONTRACT_REVIEW_V1_DecisionRequest req, CancellationToken ct)
    {
        var c = await service.LegalManagerDecideAsync(caseId, RequireUserId(), req.Approve, req.Comment, ct);
        return await BuildAsync(c, ct);
    }

    [HttpPost("{caseId:guid}/cancel")]
    public async Task<ActionResult<CONTRACT_REVIEW_V1_CaseResponse>> Cancel(Guid caseId, CancellationToken ct)
    {
        var c = await service.CancelAsync(caseId, RequireUserId(), ct);
        return await BuildAsync(c, ct);
    }

    [HttpGet("{caseId:guid}")]
    public async Task<ActionResult<CONTRACT_REVIEW_V1_CaseResponse>> GetById(Guid caseId, CancellationToken ct)
    {
        var c = await store.FindByIdAsync(caseId, ct);
        if (c is null) return NotFound();
        return await BuildAsync(c, ct);
    }

    [HttpGet("mine")]
    public async Task<IReadOnlyList<CONTRACT_REVIEW_V1_RowResponse>> Mine(CancellationToken ct)
        => (await store.FindMineAsync(RequireUserId(), ct)).Select(ToRow).ToList();

    private static CONTRACT_REVIEW_V1_Service.SubmitInput ToInput(Guid userId, CONTRACT_REVIEW_V1_SubmitRequest req) =>
        new(userId, req.CounterpartyName, req.ContractSubject, req.Amount, req.PeriodStart, req.PeriodEnd, req.DraftFileId, req.Remarks);

    private static CONTRACT_REVIEW_V1_RowResponse ToRow(CONTRACT_REVIEW_V1_Case c) =>
        new(c.Id, c.ContractSubject, c.CounterpartyName, c.Status.ToString(), c.SubmittedAt, c.LastActivityAt);

    private async Task<CONTRACT_REVIEW_V1_CaseResponse> BuildAsync(CONTRACT_REVIEW_V1_Case c, CancellationToken ct)
    {
        // Parallel-review checklist for the current round.
        var group = await parallel.GetAsync(c.Id, CONTRACT_REVIEW_V1_Service.ReviewGatewayKey(c.CurrentRound), ct);
        CONTRACT_REVIEW_V1_ReviewView? review = null;
        if (group is not null)
        {
            var deciderIds = group.Slots.Where(s => s.DecisionByUserId is not null)
                .Select(s => s.DecisionByUserId!.Value).Distinct().ToArray();
            var names = await directory.GetManyAsync(deciderIds, ct);
            var approved = group.Slots.Count(s => s.Decision == SlotDecision.Approved);
            var policyLabel = group.Threshold >= group.TotalSlots
                ? "並簽 · 需全部核准"
                : $"門檻 {group.Threshold}/{group.TotalSlots}";
            review = new CONTRACT_REVIEW_V1_ReviewView(
                policyLabel, group.Threshold, approved, group.TotalSlots,
                group.Slots.Select(s => new CONTRACT_REVIEW_V1_SlotView(
                    s.Id, s.NodeId, s.AssigneeRoleCode, s.Decision.ToString().ToLowerInvariant(),
                    s.DecisionByUserId is { } d && names.TryGetValue(d, out var info) ? info.DisplayName : null,
                    s.Comment, s.DecisionAt)).ToList());
        }

        CONTRACT_REVIEW_V1_LegalManagerView? mgr = null;
        if (c.LegalManagerUserId is not null || c.LegalManagerApproved is not null)
        {
            string? name = c.LegalManagerUserId is { } id ? (await directory.GetByIdAsync(id, ct))?.DisplayName : null;
            mgr = new CONTRACT_REVIEW_V1_LegalManagerView(
                c.LegalManagerUserId, name, c.LegalManagerApproved, c.LegalManagerComment, c.LegalManagerDecisionAt);
        }

        var submitter = await directory.GetByIdAsync(c.SubmitterUserId, ct);
        return new CONTRACT_REVIEW_V1_CaseResponse(
            c.Id, c.CounterpartyName, c.ContractSubject, c.Amount, c.PeriodStart, c.PeriodEnd,
            c.DraftFileId, c.Remarks, c.RevisionNote, c.Status.ToString(), c.CurrentRound,
            c.SubmitterUserId, submitter?.DisplayName, c.SubmittedAt, c.LastActivityAt, c.CompletedAt,
            review, mgr);
    }
}
