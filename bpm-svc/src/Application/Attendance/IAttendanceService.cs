using Bpm.Application.Attendance.Dtos;

namespace Bpm.Application.Attendance;

public interface IAttendanceService
{
    Task<PunchDto> CheckInAsync(Guid userId, CancellationToken ct = default);
    Task<PunchDto> CheckOutAsync(Guid userId, CancellationToken ct = default);
    Task<TodayStatusDto> GetTodayAsync(Guid userId, CancellationToken ct = default);
    Task<IReadOnlyList<DailySummaryDto>> GetHistoryAsync(Guid userId, int days, CancellationToken ct = default);
}
