using Bpm.Application.Attendance;
using Bpm.Application.Attendance.Dtos;
using Bpm.Application.Common.Exceptions;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Bpm.Api.Attendance;

[ApiController]
[Route("api/attendance")]
[Authorize]
public sealed class AttendanceController(IAttendanceService service) : ControllerBase
{
    [HttpPost("checkin")]
    public async Task<IActionResult> CheckIn(CancellationToken ct)
    {
        var dto = await service.CheckInAsync(RequireUserId(), ct);
        return Created($"/api/attendance/punches/{dto.Id}", dto);
    }

    [HttpPost("checkout")]
    public async Task<IActionResult> CheckOut(CancellationToken ct)
    {
        var dto = await service.CheckOutAsync(RequireUserId(), ct);
        return Created($"/api/attendance/punches/{dto.Id}", dto);
    }

    [HttpGet("today")]
    public async Task<TodayStatusDto> Today(CancellationToken ct)
        => await service.GetTodayAsync(RequireUserId(), ct);

    [HttpGet("history")]
    public async Task<IReadOnlyList<DailySummaryDto>> History([FromQuery] int days = 30, CancellationToken ct = default)
        => await service.GetHistoryAsync(RequireUserId(), days, ct);

    // ── corrections (補打卡) ──────────────────────────────────────

    [HttpPost("corrections")]
    public async Task<IActionResult> SubmitCorrection(
        [FromServices] IAttendanceCorrectionService corrections,
        [FromBody] SubmitCorrectionRequest req, CancellationToken ct)
    {
        var dto = await corrections.SubmitAsync(RequireUserId(), req, ct);
        return Created($"/api/attendance/corrections/{dto.Id}", dto);
    }

    [HttpGet("corrections/mine")]
    public async Task<IReadOnlyList<CorrectionDto>> MyCorrections(
        [FromServices] IAttendanceCorrectionService corrections, CancellationToken ct)
        => await corrections.MineAsync(RequireUserId(), ct);

    [HttpGet("corrections/{id:guid}")]
    public async Task<ActionResult<CorrectionDto>> GetCorrection(
        [FromServices] IAttendanceCorrectionService corrections, Guid id, CancellationToken ct)
    {
        var dto = await corrections.FindAsync(id, ct);
        return dto is null ? NotFound() : dto;
    }

    [HttpPost("corrections/{id:guid}/decision")]
    public async Task<CorrectionDto> DecideCorrection(
        [FromServices] IAttendanceCorrectionService corrections,
        Guid id, [FromBody] DecideCorrectionRequest req, CancellationToken ct)
        => await corrections.DecideAsync(RequireUserId(), id, req, ct);

    private Guid RequireUserId()
    {
        var raw = User?.FindFirst("sub")?.Value
            ?? User?.FindFirst(System.Security.Claims.ClaimTypes.NameIdentifier)?.Value
            ?? User?.Identity?.Name;
        if (Guid.TryParse(raw, out var id)) return id;
        throw new ForbiddenException("authenticated user id missing or invalid");
    }
}
