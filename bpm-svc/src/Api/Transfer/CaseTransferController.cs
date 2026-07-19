using Bpm.Api.Common;
using Bpm.Application.Transfer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Bpm.Api.Transfer;

/// <summary>
/// Generic end-user case transfer (轉簽) surface — one endpoint for every
/// model-B flow. Authorization semantics live in
/// <see cref="ICaseTransferService"/>; this controller only maps error codes
/// onto HTTP statuses.
/// </summary>
[ApiController]
[Authorize]
[Route("api/case-transfer")]
public sealed class CaseTransferController(ICaseTransferService transfers) : BpmControllerBase
{
    public sealed record TransferRequest(Guid ToUserId, string? Reason);
    public sealed record TransferResponse(bool Ok, string? Error);

    [HttpPost("{flowCode}/{caseId:guid}")]
    public async Task<ActionResult<TransferResponse>> Transfer(
        string flowCode, Guid caseId, [FromBody] TransferRequest req, CancellationToken ct)
    {
        var userId = RequireUserId();
        var r = await transfers.TransferAsync(flowCode, caseId, userId, req.ToUserId, req.Reason, ct);
        if (r.Ok) return Ok(new TransferResponse(true, null));
        return r.Error switch
        {
            "unknown_flow" or "not_found_or_closed" => NotFound(new TransferResponse(false, r.Error)),
            "not_current_assignee" => StatusCode(403, new TransferResponse(false, r.Error)),
            _ => BadRequest(new TransferResponse(false, r.Error)),
        };
    }

    [HttpGet("candidates")]
    public async Task<IReadOnlyList<TransferCandidate>> Candidates([FromQuery] string? q, CancellationToken ct)
    {
        RequireUserId();
        return await transfers.CandidatesAsync(q, ct);
    }
}
