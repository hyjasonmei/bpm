using Bpm.Application.Travel.Commands;
using Bpm.Application.Travel.Dtos;
using Bpm.Application.Travel.Queries;
using MediatR;
using Microsoft.AspNetCore.Mvc;

namespace Bpm.Api.Controllers;

[ApiController]
[Route("api/travel")]
public sealed class TravelController(ISender sender) : ControllerBase
{
    public sealed record SubmitTravelRequest(
        string TenantCode,
        string ApplicantUserId,
        string DestinationType,
        string Destination,
        DateOnly DepartDate,
        DateOnly ReturnDate,
        string Purpose,
        decimal EstimatedCost);

    public sealed record ApproveTravelRequest(string ApproverUserId);
    public sealed record RejectTravelRequest(string ApproverUserId, string Reason);
    public sealed record BookTravelRequest(string AdminUserId, string TicketRef, string? HotelRef, string? BookNote);

    [HttpPost("cases")]
    [ProducesResponseType(typeof(TravelCaseDto), StatusCodes.Status201Created)]
    public async Task<ActionResult<TravelCaseDto>> Submit([FromBody] SubmitTravelRequest body, CancellationToken ct)
    {
        var dto = await sender.Send(new SubmitTravelCommand(
            body.TenantCode, body.ApplicantUserId, body.DestinationType, body.Destination,
            body.DepartDate, body.ReturnDate, body.Purpose, body.EstimatedCost), ct);
        return CreatedAtAction(nameof(GetById), new { id = dto.Id }, dto);
    }

    [HttpGet("cases/{id:guid}")]
    [ProducesResponseType(typeof(TravelCaseDto), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<ActionResult<TravelCaseDto>> GetById(Guid id, CancellationToken ct)
    {
        var dto = await sender.Send(new GetTravelCaseQuery(id), ct);
        return Ok(dto);
    }

    [HttpGet("cases")]
    [ProducesResponseType(typeof(IReadOnlyList<TravelCaseDto>), StatusCodes.Status200OK)]
    public async Task<ActionResult<IReadOnlyList<TravelCaseDto>>> List(
        [FromQuery] string? applicantUserId,
        [FromQuery] string? currentApproverUserId,
        CancellationToken ct)
    {
        var dtos = await sender.Send(new ListTravelCasesQuery(applicantUserId, currentApproverUserId), ct);
        return Ok(dtos);
    }

    [HttpPost("cases/{id:guid}/approve")]
    [ProducesResponseType(typeof(TravelCaseDto), StatusCodes.Status200OK)]
    public async Task<ActionResult<TravelCaseDto>> Approve(Guid id, [FromBody] ApproveTravelRequest body, CancellationToken ct)
    {
        var dto = await sender.Send(new ApproveTravelCommand(id, body.ApproverUserId), ct);
        return Ok(dto);
    }

    [HttpPost("cases/{id:guid}/reject")]
    [ProducesResponseType(typeof(TravelCaseDto), StatusCodes.Status200OK)]
    public async Task<ActionResult<TravelCaseDto>> Reject(Guid id, [FromBody] RejectTravelRequest body, CancellationToken ct)
    {
        var dto = await sender.Send(new RejectTravelCommand(id, body.ApproverUserId, body.Reason), ct);
        return Ok(dto);
    }

    [HttpPost("cases/{id:guid}/book")]
    [ProducesResponseType(typeof(TravelCaseDto), StatusCodes.Status200OK)]
    public async Task<ActionResult<TravelCaseDto>> Book(Guid id, [FromBody] BookTravelRequest body, CancellationToken ct)
    {
        var dto = await sender.Send(new BookTravelCommand(id, body.AdminUserId, body.TicketRef, body.HotelRef, body.BookNote), ct);
        return Ok(dto);
    }
}
