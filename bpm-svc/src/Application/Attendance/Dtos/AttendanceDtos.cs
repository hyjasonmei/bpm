using Bpm.Domain.Entities.Attendance;

namespace Bpm.Application.Attendance.Dtos;

public sealed record PunchDto(Guid Id, PunchType PunchType, DateTime PunchAt, DateOnly LocalDate, PunchSource Source);

public enum TodayState { NotCheckedIn = 1, OnDuty = 2, OffDuty = 3 }

public sealed record TodayStatusDto(
    TodayState Status,
    double WorkHours,
    bool InProgress,
    DateTime? LastInAt,
    DateTime? LastOutAt,
    IReadOnlyList<PunchDto> Punches);

public sealed record DailySummaryDto(
    DateOnly Date,
    DateTime? FirstIn,
    DateTime? LastOut,
    double WorkHours,
    int PunchCount);
