using Bpm.Domain.Entities.Attendance;

namespace Bpm.Application.Attendance;

public sealed record SubmitCorrectionRequest(
    DateOnly Date,
    PunchType PunchType,
    string Time,          // tenant-local "HH:mm"
    string Reason);

public sealed record DecideCorrectionRequest(bool Approve, string? Note);

public sealed record CorrectionDto(
    Guid Id,
    Guid UserId,
    string UserName,
    DateOnly Date,
    PunchType PunchType,
    DateTime RequestedPunchAt,
    string Reason,
    CorrectionStatus Status,
    DateTime SubmittedAt,
    Guid? DeciderUserId,
    string? DeciderName,
    DateTime? DecidedAt,
    string? DecisionNote);

public interface IAttendanceCorrectionService
{
    /// Employee files a back-fill request; the direct manager is notified.
    Task<CorrectionDto> SubmitAsync(Guid userId, SubmitCorrectionRequest req, CancellationToken ct = default);

    /// Direct manager (or SYSTEM_ADMIN) approves/rejects. Approval writes the
    /// missing AttendancePunch (Source = Correction) and notifies the requester.
    Task<CorrectionDto> DecideAsync(Guid deciderUserId, Guid correctionId, DecideCorrectionRequest req, CancellationToken ct = default);

    Task<CorrectionDto?> FindAsync(Guid correctionId, CancellationToken ct = default);
    Task<IReadOnlyList<CorrectionDto>> MineAsync(Guid userId, CancellationToken ct = default);
    /// Pending requests whose requester reports to this approver.
    Task<IReadOnlyList<CorrectionDto>> PendingForApproverAsync(Guid approverUserId, CancellationToken ct = default);
}
