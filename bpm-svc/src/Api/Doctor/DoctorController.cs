using Bpm.Application.Doctor;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Bpm.Api.Doctor;

/// <summary>
/// Console-facing Process Doctor surface for the admin-ui Doctor page. Reached
/// by admin-ui through its <c>/bpmsvc</c> dev proxy (no bpm bearer), so it is
/// <c>[AllowAnonymous]</c> like branding / reports / sandbox-admin. POC
/// deferral: the real admin↔bpm auth bridge lands later; operator id is passed
/// from admin-ui for the action log.
/// </summary>
[ApiController]
[Route("api/doctor")]
[AllowAnonymous]
public sealed class DoctorController(IDoctorService doctor) : ControllerBase
{
    [HttpGet("scan")]
    public Task<DoctorReport> Scan([FromQuery] int stalledDays = 14, CancellationToken ct = default)
        => doctor.ScanAsync(stalledDays, ct);

    [HttpGet("candidates")]
    public Task<DoctorCandidates> Candidates([FromQuery] Guid? userId, [FromQuery] string? q, CancellationToken ct = default)
        => doctor.GetCandidatesAsync(userId, q, ct);

    [HttpPost("reassign")]
    public Task<DoctorActionResult> Reassign([FromBody] ReassignRequest req, CancellationToken ct)
        => doctor.ReassignAsync(req.FlowCode, req.CaseId, req.ToUserId, req.OperatorUserId, req.Reason, ct);

    [HttpPost("batch-reassign")]
    public Task<DoctorActionResult> BatchReassign([FromBody] BatchReassignRequest req, CancellationToken ct)
        => doctor.BatchReassignAsync(req.FromUserId, req.ToUserId, req.OperatorUserId, req.Reason, ct);

    [HttpPost("cancel")]
    public Task<DoctorActionResult> Cancel([FromBody] CancelRequest req, CancellationToken ct)
        => doctor.CancelAsync(req.FlowCode, req.CaseId, req.OperatorUserId, req.Reason, ct);
}

public sealed record ReassignRequest(string FlowCode, Guid CaseId, Guid ToUserId, Guid? OperatorUserId = null, string? Reason = null);
public sealed record BatchReassignRequest(Guid FromUserId, Guid ToUserId, Guid? OperatorUserId = null, string? Reason = null);
public sealed record CancelRequest(string FlowCode, Guid CaseId, Guid? OperatorUserId = null, string? Reason = null);
