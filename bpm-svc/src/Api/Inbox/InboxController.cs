using Bpm.Api.Common;
using Bpm.Application.Inbox;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Bpm.Api.Inbox;

/// <summary>
/// Unified inbox: fans out across every registered
/// <see cref="ITypedInboxProvider"/> and merges the rows by
/// <see cref="InboxRow.LastActivityAt"/>. bpm-ui's Home page reads
/// these two endpoints instead of touching per-flow URLs, so adding a
/// new chef-cooked flow lights up the Home cards without any UI edit.
/// </summary>
[ApiController]
[Route("api/inbox")]
[Authorize]
public sealed class InboxController(IEnumerable<ITypedInboxProvider> providers) : BpmControllerBase
{
    [HttpGet("mine")]
    public async Task<IReadOnlyList<InboxRow>> Mine(CancellationToken ct)
    {
        var userId = RequireUserId();
        var tasks = providers.Select(p => p.GetMineAsync(userId, ct)).ToArray();
        var results = await Task.WhenAll(tasks);
        return results.SelectMany(r => r)
            .OrderByDescending(r => r.LastActivityAt)
            .ToList();
    }

    [HttpGet("pending")]
    public async Task<IReadOnlyList<InboxRow>> Pending(CancellationToken ct)
    {
        var userId = RequireUserId();
        var tasks = providers.Select(p => p.GetPendingAsync(userId, ct)).ToArray();
        var results = await Task.WhenAll(tasks);
        return results.SelectMany(r => r)
            .OrderByDescending(r => r.LastActivityAt)
            .ToList();
    }
}
