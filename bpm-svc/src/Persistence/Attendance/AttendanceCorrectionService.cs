using Bpm.Application.Attendance;
using Bpm.Application.Common.Abstractions;
using Bpm.Application.Common.Directory;
using Bpm.Application.Common.Exceptions;
using Bpm.Application.Notifications;
using Bpm.Domain.Entities.Attendance;
using FluentValidation.Results;
using Microsoft.EntityFrameworkCore;

namespace Bpm.Persistence.Attendance;

// Tenant timezone is hard-coded to Asia/Taipei for the POC, same as
// AttendanceService — lift both from tenant config when multi-tenant lands.
public sealed class AttendanceCorrectionService(
    AppDbContext db,
    IClock clock,
    IPrincipalDirectory directory,
    INotifyDispatcher notify) : IAttendanceCorrectionService
{
    private static readonly TimeZoneInfo TenantTz = ResolveTenantTz();
    private const int MaxBackfillDays = 30;

    public async Task<CorrectionDto> SubmitAsync(Guid userId, SubmitCorrectionRequest req, CancellationToken ct = default)
    {
        Validate(req, clock.UtcNow, out var requestedUtc);

        var duplicate = await db.AttendanceCorrections.AnyAsync(
            c => c.UserId == userId && c.Date == req.Date && c.PunchType == req.PunchType
                 && c.Status == CorrectionStatus.Pending, ct);
        if (duplicate)
            throw Invalid(nameof(req.Date), "同一天同型別已有待審的補卡申請");

        var row = new AttendanceCorrection
        {
            UserId = userId,
            Date = req.Date,
            PunchType = req.PunchType,
            RequestedPunchAt = requestedUtc,
            Reason = req.Reason.Trim(),
            Status = CorrectionStatus.Pending,
            SubmittedAt = clock.UtcNow,
        };
        db.AttendanceCorrections.Add(row);
        await db.SaveChangesAsync(ct);

        await NotifyManagerAsync(row, ct);
        return await ToDtoAsync(row, ct);
    }

    public async Task<CorrectionDto> DecideAsync(Guid deciderUserId, Guid correctionId, DecideCorrectionRequest req, CancellationToken ct = default)
    {
        var row = await db.AttendanceCorrections.FirstOrDefaultAsync(c => c.Id == correctionId, ct)
            ?? throw new NotFoundException($"correction {correctionId} not found");

        if (row.Status != CorrectionStatus.Pending)
            throw new ConflictException("此補卡申請已被處理。");
        if (row.UserId == deciderUserId)
            throw new ForbiddenException("不能核准自己的補卡申請。");
        if (!await CanDecideAsync(deciderUserId, row.UserId, ct))
            throw new ForbiddenException("只有申請人的直屬主管（或系統管理員）能審核補卡。");

        row.Status = req.Approve ? CorrectionStatus.Approved : CorrectionStatus.Rejected;
        row.DeciderUserId = deciderUserId;
        row.DecidedAt = clock.UtcNow;
        row.DecisionNote = string.IsNullOrWhiteSpace(req.Note) ? null : req.Note.Trim();

        if (req.Approve)
        {
            var punch = new AttendancePunch
            {
                UserId = row.UserId,
                PunchType = row.PunchType,
                PunchAt = row.RequestedPunchAt,
                LocalDate = row.Date,
                Source = PunchSource.Correction,
            };
            db.AttendancePunches.Add(punch);
            row.CreatedPunchId = punch.Id;
        }

        await db.SaveChangesAsync(ct);
        await NotifyRequesterAsync(row, ct);
        return await ToDtoAsync(row, ct);
    }

    public async Task<CorrectionDto?> FindAsync(Guid correctionId, CancellationToken ct = default)
    {
        var row = await db.AttendanceCorrections.AsNoTracking().FirstOrDefaultAsync(c => c.Id == correctionId, ct);
        return row is null ? null : await ToDtoAsync(row, ct);
    }

    public async Task<IReadOnlyList<CorrectionDto>> MineAsync(Guid userId, CancellationToken ct = default)
    {
        var rows = await db.AttendanceCorrections.AsNoTracking()
            .Where(c => c.UserId == userId)
            .OrderByDescending(c => c.SubmittedAt)
            .Take(50)
            .ToListAsync(ct);
        return await ToDtosAsync(rows, ct);
    }

    public async Task<IReadOnlyList<CorrectionDto>> PendingForApproverAsync(Guid approverUserId, CancellationToken ct = default)
    {
        var reportIds = await db.SharedUserManagers.AsNoTracking()
            .Where(m => m.ManagerUserId == approverUserId)
            .Select(m => m.UserId)
            .ToListAsync(ct);
        if (reportIds.Count == 0) return Array.Empty<CorrectionDto>();

        var rows = await db.AttendanceCorrections.AsNoTracking()
            .Where(c => reportIds.Contains(c.UserId) && c.Status == CorrectionStatus.Pending)
            .OrderBy(c => c.SubmittedAt)
            .ToListAsync(ct);
        return await ToDtosAsync(rows, ct);
    }

    // ── authz / notify ──────────────────────────────────────────────

    private async Task<bool> CanDecideAsync(Guid deciderUserId, Guid requesterUserId, CancellationToken ct)
    {
        var managerId = await db.SharedUserManagers.AsNoTracking()
            .Where(m => m.UserId == requesterUserId)
            .Select(m => (Guid?)m.ManagerUserId)
            .FirstOrDefaultAsync(ct);
        if (managerId == deciderUserId) return true;

        var roles = await directory.GetRoleCodesForUserAsync(deciderUserId, ct);
        return roles.Contains("SYSTEM_ADMIN");
    }

    private async Task NotifyManagerAsync(AttendanceCorrection row, CancellationToken ct)
    {
        var managerId = await db.SharedUserManagers.AsNoTracking()
            .Where(m => m.UserId == row.UserId)
            .Select(m => (Guid?)m.ManagerUserId)
            .FirstOrDefaultAsync(ct);
        if (managerId is not { } mid) return;   // no manager wired — request stays reachable via admin

        var names = await directory.GetManyAsync(new[] { row.UserId, mid }, ct);
        var who = names.GetValueOrDefault(row.UserId)?.DisplayName ?? "同仁";
        var manager = names.GetValueOrDefault(mid);
        await notify.DispatchAsync(new NotifyMessage(
            SourceId: "ATTENDANCE_CORRECTION.notify_submit",
            Subject: $"[補打卡] {who} 申請 {Describe(row)}",
            Body: $"{who} 申請補打卡：{Describe(row)}。\n事由:{row.Reason}\n請至簽核匣審核。",
            Channels: new[] { "email", "in_app" },
            Recipients: new[] { new NotifyRecipient(mid, manager?.Email, manager?.DisplayName) },
            Context: Ctx(row)), ct);
    }

    private async Task NotifyRequesterAsync(AttendanceCorrection row, CancellationToken ct)
    {
        var requester = await directory.GetByIdAsync(row.UserId, ct);
        var verdict = row.Status == CorrectionStatus.Approved ? "已核准，紀錄已補上" : "已駁回";
        await notify.DispatchAsync(new NotifyMessage(
            SourceId: "ATTENDANCE_CORRECTION.notify_decision",
            Subject: $"[補打卡] {Describe(row)} {verdict}",
            Body: $"你的補打卡申請（{Describe(row)}）{verdict}。"
                  + (row.DecisionNote is null ? string.Empty : $"\n主管備註:{row.DecisionNote}"),
            Channels: new[] { "email", "in_app" },
            Recipients: new[] { new NotifyRecipient(row.UserId, requester?.Email, requester?.DisplayName) },
            Context: Ctx(row)), ct);
    }

    private static IReadOnlyDictionary<string, string?> Ctx(AttendanceCorrection row) =>
        new Dictionary<string, string?>
        {
            ["correctionId"] = row.Id.ToString(),
            ["flowCode"] = "ATTENDANCE_CORRECTION",
        };

    private static string Describe(AttendanceCorrection row)
        => $"{row.Date:MM/dd} {(row.PunchType == PunchType.In ? "上班卡" : "下班卡")}";

    // ── validation / mapping ────────────────────────────────────────

    private void Validate(SubmitCorrectionRequest req, DateTime nowUtc, out DateTime requestedUtc)
    {
        if (string.IsNullOrWhiteSpace(req.Reason))
            throw Invalid(nameof(req.Reason), "補卡事由為必填");
        if (!TimeOnly.TryParseExact(req.Time, "HH:mm", out var time))
            throw Invalid(nameof(req.Time), "時間格式須為 HH:mm");

        var todayLocal = DateOnly.FromDateTime(TimeZoneInfo.ConvertTimeFromUtc(DateTime.SpecifyKind(nowUtc, DateTimeKind.Utc), TenantTz));
        if (req.Date > todayLocal)
            throw Invalid(nameof(req.Date), "不能補未來日期的卡");
        if (req.Date < todayLocal.AddDays(-MaxBackfillDays))
            throw Invalid(nameof(req.Date), $"僅能補 {MaxBackfillDays} 天內的卡");

        var local = new DateTime(req.Date.Year, req.Date.Month, req.Date.Day, time.Hour, time.Minute, 0, DateTimeKind.Unspecified);
        requestedUtc = TimeZoneInfo.ConvertTimeToUtc(local, TenantTz);
        if (requestedUtc > nowUtc)
            throw Invalid(nameof(req.Time), "補卡時間不能在未來");
    }

    private static ValidationException Invalid(string field, string message)
        => new(new[] { new ValidationFailure(field, message) });

    private async Task<CorrectionDto> ToDtoAsync(AttendanceCorrection row, CancellationToken ct)
        => (await ToDtosAsync(new[] { row }, ct))[0];

    private async Task<IReadOnlyList<CorrectionDto>> ToDtosAsync(IReadOnlyList<AttendanceCorrection> rows, CancellationToken ct)
    {
        if (rows.Count == 0) return Array.Empty<CorrectionDto>();
        var ids = rows.SelectMany(r => new[] { r.UserId, r.DeciderUserId ?? Guid.Empty })
            .Where(id => id != Guid.Empty).Distinct().ToArray();
        var names = await directory.GetManyAsync(ids, ct);
        return rows.Select(r => new CorrectionDto(
            r.Id, r.UserId,
            names.GetValueOrDefault(r.UserId)?.DisplayName ?? "—",
            r.Date, r.PunchType, r.RequestedPunchAt, r.Reason, r.Status, r.SubmittedAt,
            r.DeciderUserId,
            r.DeciderUserId is { } d ? names.GetValueOrDefault(d)?.DisplayName : null,
            r.DecidedAt, r.DecisionNote)).ToList();
    }

    private static TimeZoneInfo ResolveTenantTz()
    {
        try { return TimeZoneInfo.FindSystemTimeZoneById("Asia/Taipei"); }
        catch (TimeZoneNotFoundException) { return TimeZoneInfo.FindSystemTimeZoneById("Taipei Standard Time"); }
    }
}
