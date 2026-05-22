using Bpm.Application.Attendance;
using Bpm.Application.Attendance.Dtos;
using Bpm.Application.Common.Abstractions;
using Bpm.Domain.Entities.Attendance;
using Microsoft.EntityFrameworkCore;

namespace Bpm.Persistence.Attendance;

// Tenant timezone is hard-coded to Asia/Taipei for the POC.
// When multi-tenant support lands, lift this from a tenant config row.
public sealed class AttendanceService(AppDbContext db, IClock clock) : IAttendanceService
{
    private static readonly TimeZoneInfo TenantTz = ResolveTenantTz();

    public async Task<PunchDto> CheckInAsync(Guid userId, CancellationToken ct = default)
        => await WritePunch(userId, PunchType.In, ct);

    public async Task<PunchDto> CheckOutAsync(Guid userId, CancellationToken ct = default)
        => await WritePunch(userId, PunchType.Out, ct);

    public async Task<TodayStatusDto> GetTodayAsync(Guid userId, CancellationToken ct = default)
    {
        var nowUtc = clock.UtcNow;
        var today = LocalDateOf(nowUtc);

        var punches = await db.AttendancePunches.AsNoTracking()
            .Where(p => p.UserId == userId && p.LocalDate == today)
            .OrderBy(p => p.PunchAt)
            .ToListAsync(ct);

        var (workHours, inProgress) = ComputeWorkHoursForDay(punches, nowUtc);
        var lastIn = punches.Where(p => p.PunchType == PunchType.In).Select(p => (DateTime?)p.PunchAt).LastOrDefault();
        var lastOut = punches.Where(p => p.PunchType == PunchType.Out).Select(p => (DateTime?)p.PunchAt).LastOrDefault();
        var status = DeriveStatus(punches);

        return new TodayStatusDto(
            status,
            Math.Round(workHours, 2),
            inProgress,
            lastIn,
            lastOut,
            punches.Select(ToDto).ToList());
    }

    public async Task<IReadOnlyList<DailySummaryDto>> GetHistoryAsync(Guid userId, int days, CancellationToken ct = default)
    {
        if (days < 1) days = 1;
        if (days > 90) days = 90;

        var nowUtc = clock.UtcNow;
        var today = LocalDateOf(nowUtc);
        var fromDate = today.AddDays(-(days - 1));

        var punches = await db.AttendancePunches.AsNoTracking()
            .Where(p => p.UserId == userId && p.LocalDate >= fromDate && p.LocalDate <= today)
            .OrderBy(p => p.PunchAt)
            .ToListAsync(ct);

        return punches
            .GroupBy(p => p.LocalDate)
            .OrderByDescending(g => g.Key)
            .Select(g =>
            {
                var dayPunches = g.ToList();
                // For past days, use end-of-day instead of "now" for trailing-In calc.
                // For today, use now (handled by caller passing nowUtc).
                var refTime = g.Key == today ? nowUtc : EndOfLocalDayUtc(g.Key);
                var (hours, _) = ComputeWorkHoursForDay(dayPunches, refTime);
                var firstIn = dayPunches.Where(p => p.PunchType == PunchType.In).Select(p => (DateTime?)p.PunchAt).FirstOrDefault();
                var lastOut = dayPunches.Where(p => p.PunchType == PunchType.Out).Select(p => (DateTime?)p.PunchAt).LastOrDefault();
                return new DailySummaryDto(g.Key, firstIn, lastOut, Math.Round(hours, 2), dayPunches.Count);
            })
            .ToList();
    }

    // Pairs In/Out punches in time order. Trailing In uses refTime as virtual close.
    // Consecutive Ins: drop the earlier. Consecutive Outs: drop the later (no segment).
    // Lone Out (no preceding In): contributes 0.
    public static (double hours, bool inProgress) ComputeWorkHoursForDay(IEnumerable<AttendancePunch> punches, DateTime refTime)
    {
        var ordered = punches.OrderBy(p => p.PunchAt).ToList();
        double total = 0;
        bool inProgress = false;
        DateTime? openIn = null;

        foreach (var p in ordered)
        {
            if (p.PunchType == PunchType.In)
            {
                openIn = p.PunchAt;
            }
            else
            {
                if (openIn.HasValue)
                {
                    total += (p.PunchAt - openIn.Value).TotalHours;
                    openIn = null;
                }
            }
        }

        if (openIn.HasValue)
        {
            var virtualClose = refTime > openIn.Value ? refTime : openIn.Value;
            total += (virtualClose - openIn.Value).TotalHours;
            inProgress = true;
        }

        if (total < 0) total = 0;
        return (total, inProgress);
    }

    private async Task<PunchDto> WritePunch(Guid userId, PunchType type, CancellationToken ct)
    {
        var nowUtc = clock.UtcNow;
        var localDate = LocalDateOf(nowUtc);
        var punch = new AttendancePunch
        {
            UserId = userId,
            PunchType = type,
            PunchAt = nowUtc,
            LocalDate = localDate,
            Source = PunchSource.Manual,
        };
        db.AttendancePunches.Add(punch);
        await db.SaveChangesAsync(ct);
        return ToDto(punch);
    }

    private static TodayState DeriveStatus(IReadOnlyList<AttendancePunch> punches)
    {
        if (punches.Count == 0) return TodayState.NotCheckedIn;
        return punches[^1].PunchType == PunchType.In ? TodayState.OnDuty : TodayState.OffDuty;
    }

    private static DateOnly LocalDateOf(DateTime utc)
    {
        var local = TimeZoneInfo.ConvertTimeFromUtc(DateTime.SpecifyKind(utc, DateTimeKind.Utc), TenantTz);
        return DateOnly.FromDateTime(local);
    }

    private static DateTime EndOfLocalDayUtc(DateOnly date)
    {
        var localEnd = new DateTime(date.Year, date.Month, date.Day, 23, 59, 59, DateTimeKind.Unspecified);
        return TimeZoneInfo.ConvertTimeToUtc(localEnd, TenantTz);
    }

    private static PunchDto ToDto(AttendancePunch p)
        => new(p.Id, p.PunchType, p.PunchAt, p.LocalDate, p.Source);

    private static TimeZoneInfo ResolveTenantTz()
    {
        // Try IANA first (Linux/macOS), fall back to Windows id.
        try { return TimeZoneInfo.FindSystemTimeZoneById("Asia/Taipei"); }
        catch (TimeZoneNotFoundException) { return TimeZoneInfo.FindSystemTimeZoneById("Taipei Standard Time"); }
    }
}
