using Bpm.Api.Common;
using Bpm.Application.Common.Abstractions;
using Bpm.Domain.Entities.Notifications;
using Bpm.Persistence;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace Bpm.Api.Notifications;

/// <summary>
/// Per-user in-app notification feed for the header 🔔 bell. Reads /
/// mutates the caller's own <see cref="UserNotification"/> rows only.
/// </summary>
[ApiController]
[Authorize]
[Route("api/notifications")]
public sealed class NotificationsController(AppDbContext db, IClock clock) : BpmControllerBase
{
    [HttpGet("mine")]
    public async Task<NotificationsResponse> Mine([FromQuery] int limit = 20, CancellationToken ct = default)
    {
        var userId = RequireUserId();
        var take = Math.Clamp(limit, 1, 100);

        var items = await db.Set<UserNotification>().AsNoTracking()
            .Where(n => n.UserId == userId)
            .OrderByDescending(n => n.CreatedAt)
            .Take(take)
            .Select(n => new NotificationDto(
                n.Id, n.Title, n.Body, n.Link, n.FlowCode, n.IsRead, n.CreatedAt))
            .ToListAsync(ct);

        var unread = await db.Set<UserNotification>()
            .CountAsync(n => n.UserId == userId && !n.IsRead, ct);

        return new NotificationsResponse(unread, items);
    }

    [HttpPost("{id:guid}/read")]
    public async Task<IActionResult> MarkRead(Guid id, CancellationToken ct)
    {
        var userId = RequireUserId();
        var n = await db.Set<UserNotification>()
            .SingleOrDefaultAsync(x => x.Id == id && x.UserId == userId, ct);
        if (n is null) return NotFound();
        if (!n.IsRead)
        {
            n.IsRead = true;
            n.ReadAt = clock.UtcNow;
            await db.SaveChangesAsync(ct);
        }
        return NoContent();
    }

    [HttpPost("read-all")]
    public async Task<IActionResult> MarkAllRead(CancellationToken ct)
    {
        var userId = RequireUserId();
        var unread = await db.Set<UserNotification>()
            .Where(n => n.UserId == userId && !n.IsRead)
            .ToListAsync(ct);
        if (unread.Count > 0)
        {
            var now = clock.UtcNow;
            foreach (var n in unread)
            {
                n.IsRead = true;
                n.ReadAt = now;
            }
            await db.SaveChangesAsync(ct);
        }
        return NoContent();
    }
}

public sealed record NotificationDto(
    Guid Id, string Title, string Body, string? Link, string? FlowCode, bool IsRead, DateTime CreatedAt);

public sealed record NotificationsResponse(int UnreadCount, IReadOnlyList<NotificationDto> Items);
