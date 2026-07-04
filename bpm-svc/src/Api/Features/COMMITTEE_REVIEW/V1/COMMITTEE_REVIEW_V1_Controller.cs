using Bpm.Api.Common;
using Bpm.Application.Common.Directory;
using Bpm.Application.Features.COMMITTEE_REVIEW.V1;
using Bpm.Application.Parallel;
using Bpm.Domain.Features.COMMITTEE_REVIEW.V1;
using Bpm.Domain.Parallel;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Bpm.Api.Features.COMMITTEE_REVIEW.V1;

[ApiController]
[Authorize]
[Route("api/committee-review/v1")]
public sealed class COMMITTEE_REVIEW_V1_Controller(
    COMMITTEE_REVIEW_V1_Service service,
    ICOMMITTEE_REVIEW_V1_CaseStore store,
    IParallelApprovalService parallel,
    IPrincipalDirectory directory) : BpmControllerBase
{
    [HttpPost]
    public async Task<ActionResult<COMMITTEE_REVIEW_V1_CaseResponse>> Submit(
        [FromBody] COMMITTEE_REVIEW_V1_SubmitRequest req, CancellationToken ct)
    {
        var userId = RequireUserId();
        var c = await service.SubmitAsync(ToInput(userId, req), ct);
        return CreatedAtAction(nameof(GetById), new { caseId = c.Id }, await BuildAsync(c, ct));
    }

    [HttpPost("{caseId:guid}/slots/{slotId:guid}/decision")]
    public async Task<ActionResult<COMMITTEE_REVIEW_V1_CaseResponse>> Decide(
        Guid caseId, Guid slotId, [FromBody] COMMITTEE_REVIEW_V1_DecisionRequest req, CancellationToken ct)
    {
        var c = await service.DecideAsync(caseId, slotId, RequireUserId(), req.Approve, req.Comment, ct);
        return await BuildAsync(c, ct);
    }

    [HttpPost("{caseId:guid}/resubmit")]
    public async Task<ActionResult<COMMITTEE_REVIEW_V1_CaseResponse>> Resubmit(
        Guid caseId, [FromBody] COMMITTEE_REVIEW_V1_SubmitRequest req, CancellationToken ct)
    {
        var userId = RequireUserId();
        var c = await service.ResubmitAsync(caseId, userId, ToInput(userId, req), req.RevisionNote, ct);
        return await BuildAsync(c, ct);
    }

    [HttpPost("{caseId:guid}/ceo-decision")]
    public async Task<ActionResult<COMMITTEE_REVIEW_V1_CaseResponse>> CeoDecision(
        Guid caseId, [FromBody] COMMITTEE_REVIEW_V1_DecisionRequest req, CancellationToken ct)
    {
        var c = await service.CeoDecideAsync(caseId, RequireUserId(), req.Approve, req.Comment, ct);
        return await BuildAsync(c, ct);
    }

    [HttpPost("{caseId:guid}/cancel")]
    public async Task<ActionResult<COMMITTEE_REVIEW_V1_CaseResponse>> Cancel(Guid caseId, CancellationToken ct)
    {
        var c = await service.CancelAsync(caseId, RequireUserId(), ct);
        return await BuildAsync(c, ct);
    }

    [HttpGet("{caseId:guid}")]
    public async Task<ActionResult<COMMITTEE_REVIEW_V1_CaseResponse>> GetById(Guid caseId, CancellationToken ct)
    {
        var c = await store.FindByIdAsync(caseId, ct);
        if (c is null) return NotFound();
        return await BuildAsync(c, ct);
    }

    [HttpGet("mine")]
    public async Task<IReadOnlyList<COMMITTEE_REVIEW_V1_RowResponse>> Mine(CancellationToken ct)
        => (await store.FindMineAsync(RequireUserId(), ct)).Select(ToRow).ToList();

    private static COMMITTEE_REVIEW_V1_Service.SubmitInput ToInput(Guid userId, COMMITTEE_REVIEW_V1_SubmitRequest req) =>
        new(userId, req.CaseTitle, req.ReviewCategory, req.ApplyAmount, req.BenefitDescription,
            req.ExecStart, req.ExecEnd, req.AttachmentFileId, req.Remarks);

    private static COMMITTEE_REVIEW_V1_RowResponse ToRow(COMMITTEE_REVIEW_V1_Case c) =>
        new(c.Id, c.CaseTitle, COMMITTEE_REVIEW_V1_Service.CategoryLabel(c.ReviewCategory), c.Status.ToString(),
            c.SubmittedAt, c.LastActivityAt);

    private async Task<COMMITTEE_REVIEW_V1_CaseResponse> BuildAsync(COMMITTEE_REVIEW_V1_Case c, CancellationToken ct)
    {
        // Parallel-review checklist for the current round.
        var group = await parallel.GetAsync(c.Id, COMMITTEE_REVIEW_V1_Service.ReviewGatewayKey(c.CurrentRound), ct);
        COMMITTEE_REVIEW_V1_ReviewView? review = null;
        if (group is not null)
        {
            var deciderIds = group.Slots.Where(s => s.DecisionByUserId is not null)
                .Select(s => s.DecisionByUserId!.Value).Distinct().ToArray();
            var names = await directory.GetManyAsync(deciderIds, ct);
            var approved = group.Slots.Count(s => s.Decision == SlotDecision.Approved);
            var policyLabel = group.Threshold >= group.TotalSlots
                ? "並簽 · 需全部核准"
                : $"門檻 {group.Threshold}/{group.TotalSlots}";
            review = new COMMITTEE_REVIEW_V1_ReviewView(
                policyLabel, group.Threshold, approved, group.TotalSlots,
                group.Slots.Select(s => new COMMITTEE_REVIEW_V1_SlotView(
                    s.Id, s.NodeId, s.AssigneeRoleCode, s.Decision.ToString().ToLowerInvariant(),
                    s.DecisionByUserId is { } d && names.TryGetValue(d, out var info) ? info.DisplayName : null,
                    s.Comment, s.DecisionAt)).ToList());
        }

        COMMITTEE_REVIEW_V1_CeoView? ceo = null;
        if (c.CeoUserId is not null || c.CeoApproved is not null)
        {
            string? name = c.CeoUserId is { } id ? (await directory.GetByIdAsync(id, ct))?.DisplayName : null;
            ceo = new COMMITTEE_REVIEW_V1_CeoView(c.CeoUserId, name, c.CeoApproved, c.CeoComment, c.CeoDecisionAt);
        }

        var submitter = await directory.GetByIdAsync(c.SubmitterUserId, ct);
        return new COMMITTEE_REVIEW_V1_CaseResponse(
            c.Id, c.CaseTitle, c.ReviewCategory, COMMITTEE_REVIEW_V1_Service.CategoryLabel(c.ReviewCategory),
            c.ApplyAmount, c.BenefitDescription, c.ExecStart, c.ExecEnd,
            c.AttachmentFileId, c.Remarks, c.RevisionNote, c.Status.ToString(), c.CurrentRound,
            c.SubmitterUserId, submitter?.DisplayName, c.SubmittedAt, c.LastActivityAt, c.CompletedAt,
            review, ceo);
    }
}
