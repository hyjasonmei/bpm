using Bpm.Application.Common.Abstractions;
using Bpm.Application.Common.Directory;
using Bpm.Application.Common.Exceptions;
using Bpm.Application.Notifications;
using Bpm.Application.Support;
using Bpm.Domain.Entities.Support;
using FluentValidation.Results;
using Microsoft.EntityFrameworkCore;

namespace Bpm.Persistence.Support;

public sealed class SupportIssueService(
    AppDbContext db,
    IClock clock,
    IPrincipalDirectory directory,
    INotifyDispatcher notify) : ISupportIssueService
{
    private static readonly string[] Kinds = { "bug", "feature", "question" };

    public async Task<IssueDto> SubmitAsync(Guid userId, SubmitIssueRequest req, string? userAgent, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(req.Title))
            throw Invalid(nameof(req.Title), "標題為必填");
        if (string.IsNullOrWhiteSpace(req.Description))
            throw Invalid(nameof(req.Description), "描述為必填");
        var kind = Kinds.Contains(req.Kind) ? req.Kind : "bug";

        var row = new SupportIssue
        {
            UserId = userId,
            Kind = kind,
            Title = req.Title.Trim(),
            Description = req.Description.Trim(),
            Contact = string.IsNullOrWhiteSpace(req.Contact) ? null : req.Contact.Trim(),
            Page = string.IsNullOrWhiteSpace(req.Page) ? null : req.Page.Trim(),
            UserAgent = userAgent,
            SubmittedAt = clock.UtcNow,
        };
        db.SupportIssues.Add(row);
        await db.SaveChangesAsync(ct);

        await NotifyAdminsAsync(row, ct);
        return await ToDtoAsync(row, ct);
    }

    public async Task<IReadOnlyList<IssueDto>> ListAsync(SupportIssueStatus? status, CancellationToken ct = default)
    {
        var q = db.SupportIssues.AsNoTracking();
        if (status is { } s) q = q.Where(i => i.Status == s);
        var rows = await q.OrderByDescending(i => i.SubmittedAt).Take(200).ToListAsync(ct);
        return await ToDtosAsync(rows, ct);
    }

    public async Task<IssueDto> SetStatusAsync(Guid issueId, SupportIssueStatus status, CancellationToken ct = default)
    {
        var row = await db.SupportIssues.FirstOrDefaultAsync(i => i.Id == issueId, ct)
            ?? throw new NotFoundException($"issue {issueId} not found");
        row.Status = status;
        await db.SaveChangesAsync(ct);
        return await ToDtoAsync(row, ct);
    }

    private async Task NotifyAdminsAsync(SupportIssue row, CancellationToken ct)
    {
        var adminIds = await directory.GetUsersInRoleAsync("SYSTEM_ADMIN", ct);
        if (adminIds.Count == 0) return;
        var names = await directory.GetManyAsync(adminIds.Append(row.UserId).Distinct().ToArray(), ct);
        var who = names.GetValueOrDefault(row.UserId)?.DisplayName ?? "使用者";
        await notify.DispatchAsync(new NotifyMessage(
            SourceId: "SUPPORT_ISSUE.notify_submit",
            Subject: $"[問題回報] {row.Kind}：{row.Title}",
            Body: $"{who} 回報了問題（{row.Kind}）：{row.Title}\n\n{row.Description}\n\n頁面:{row.Page ?? "—"}",
            Channels: new[] { "email", "in_app" },
            Recipients: adminIds.Select(id => new NotifyRecipient(
                id, names.GetValueOrDefault(id)?.Email, names.GetValueOrDefault(id)?.DisplayName)).ToList(),
            Context: new Dictionary<string, string?> { ["issueId"] = row.Id.ToString() }), ct);
    }

    private static ValidationException Invalid(string field, string message)
        => new(new[] { new ValidationFailure(field, message) });

    private async Task<IssueDto> ToDtoAsync(SupportIssue row, CancellationToken ct)
        => (await ToDtosAsync(new[] { row }, ct))[0];

    private async Task<IReadOnlyList<IssueDto>> ToDtosAsync(IReadOnlyList<SupportIssue> rows, CancellationToken ct)
    {
        if (rows.Count == 0) return Array.Empty<IssueDto>();
        var names = await directory.GetManyAsync(rows.Select(r => r.UserId).Distinct().ToArray(), ct);
        return rows.Select(r => new IssueDto(
            r.Id, r.UserId,
            names.GetValueOrDefault(r.UserId)?.DisplayName ?? "—",
            r.Kind, r.Title, r.Description, r.Contact, r.Page, r.UserAgent,
            r.Status, r.SubmittedAt)).ToList();
    }
}
