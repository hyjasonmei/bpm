using Bpm.Application.Purchase.Commands;
using Bpm.Application.Purchase.Dtos;
using Bpm.Application.Purchase.Queries;
using MediatR;
using Microsoft.AspNetCore.Mvc;

namespace Bpm.Api.Controllers;

[ApiController]
[Route("api/purchase")]
public sealed class PurchaseController(ISender sender) : ControllerBase
{
    public sealed record SubmitPurchaseRequest(
        string TenantCode,
        string ApplicantUserId,
        string Vendor,
        string Category,
        decimal Amount,
        string Items,
        string Justification,
        string? QuoteFileName);

    public sealed record ApprovePurchaseRequest(string ApproverUserId);
    public sealed record RejectPurchaseRequest(string ApproverUserId, string Reason);
    public sealed record ExecutePurchaseRequest(string ExecUserId, string PoNumber, DateOnly ExpectedDelivery, string? ExecNote);

    [HttpPost("cases")]
    [ProducesResponseType(typeof(PurchaseCaseDto), StatusCodes.Status201Created)]
    public async Task<ActionResult<PurchaseCaseDto>> Submit([FromBody] SubmitPurchaseRequest body, CancellationToken ct)
    {
        var dto = await sender.Send(new SubmitPurchaseCommand(
            body.TenantCode,
            body.ApplicantUserId,
            body.Vendor,
            body.Category,
            body.Amount,
            body.Items,
            body.Justification,
            body.QuoteFileName), ct);
        return CreatedAtAction(nameof(GetById), new { id = dto.Id }, dto);
    }

    [HttpGet("cases/{id:guid}")]
    [ProducesResponseType(typeof(PurchaseCaseDto), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<ActionResult<PurchaseCaseDto>> GetById(Guid id, CancellationToken ct)
    {
        var dto = await sender.Send(new GetPurchaseCaseQuery(id), ct);
        return Ok(dto);
    }

    [HttpGet("cases")]
    [ProducesResponseType(typeof(IReadOnlyList<PurchaseCaseDto>), StatusCodes.Status200OK)]
    public async Task<ActionResult<IReadOnlyList<PurchaseCaseDto>>> List(
        [FromQuery] string? applicantUserId,
        [FromQuery] string? currentApproverUserId,
        CancellationToken ct)
    {
        var dtos = await sender.Send(new ListPurchaseCasesQuery(applicantUserId, currentApproverUserId), ct);
        return Ok(dtos);
    }

    [HttpPost("cases/{id:guid}/approve")]
    [ProducesResponseType(typeof(PurchaseCaseDto), StatusCodes.Status200OK)]
    public async Task<ActionResult<PurchaseCaseDto>> Approve(Guid id, [FromBody] ApprovePurchaseRequest body, CancellationToken ct)
    {
        var dto = await sender.Send(new ApprovePurchaseCommand(id, body.ApproverUserId), ct);
        return Ok(dto);
    }

    [HttpPost("cases/{id:guid}/reject")]
    [ProducesResponseType(typeof(PurchaseCaseDto), StatusCodes.Status200OK)]
    public async Task<ActionResult<PurchaseCaseDto>> Reject(Guid id, [FromBody] RejectPurchaseRequest body, CancellationToken ct)
    {
        var dto = await sender.Send(new RejectPurchaseCommand(id, body.ApproverUserId, body.Reason), ct);
        return Ok(dto);
    }

    [HttpPost("cases/{id:guid}/execute")]
    [ProducesResponseType(typeof(PurchaseCaseDto), StatusCodes.Status200OK)]
    public async Task<ActionResult<PurchaseCaseDto>> Execute(Guid id, [FromBody] ExecutePurchaseRequest body, CancellationToken ct)
    {
        var dto = await sender.Send(new ExecutePurchaseCommand(id, body.ExecUserId, body.PoNumber, body.ExpectedDelivery, body.ExecNote), ct);
        return Ok(dto);
    }
}
