using Bpm.Api.Common;
using Bpm.Application.Common.Abstractions;
using Bpm.Application.Delegation;
using Bpm.Application.Inbox;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Data.Sqlite;

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
public sealed class InboxController : BpmControllerBase
{
    private readonly IEnumerable<ITypedInboxProvider> _providers;
    private readonly IDelegationService _delegation;
    private readonly IClock _clock;
    private readonly ILogger<InboxController> _log;

    public InboxController(IEnumerable<ITypedInboxProvider> providers, IDelegationService delegation, IClock clock, ILogger<InboxController> log)
    {
        _providers = providers;
        _delegation = delegation;
        _clock = clock;
        _log = log;
    }

    [HttpGet("mine")]
    public Task<IReadOnlyList<InboxRow>> Mine(CancellationToken ct)
        => CollectAsync(p => p.GetMineAsync(RequireUserId(), ct), ct);

    /// <summary>
    /// Pending tasks for the caller — PLUS the pending tasks of anyone who has
    /// currently delegated to the caller, so a delegate sees (and can act on)
    /// the delegator's queue while the delegation is active. Deduped by case id.
    /// </summary>
    [HttpGet("pending")]
    public async Task<IReadOnlyList<InboxRow>> Pending(CancellationToken ct)
    {
        var me = RequireUserId();
        var actFor = await _delegation.GetActiveDelegatorsAsync(me, _clock.UtcNow, ct);

        var seen = new HashSet<Guid>();
        var merged = new List<InboxRow>();
        foreach (var uid in new[] { me }.Concat(actFor))
        {
            var rows = await CollectAsync(p => p.GetPendingAsync(uid, ct), ct);
            foreach (var r in rows)
                if (seen.Add(r.CaseId)) merged.Add(r);
        }
        return merged.OrderByDescending(r => r.LastActivityAt).ToList();
    }

    /// <summary>
    /// Sequential fan-out (shared scoped AppDbContext can't multiplex).
    /// Wraps each provider call so an archived feature table — its
    /// backing SQLite table renamed away by admin's
    /// <c>FeatureTablesService.ArchiveAsync</c> — skips that provider
    /// instead of failing the entire inbox response. Anything else
    /// (column drift, syntax error, FK violation) still bubbles up so
    /// real bugs aren't masked.
    /// </summary>
    private async Task<IReadOnlyList<InboxRow>> CollectAsync(
        Func<ITypedInboxProvider, Task<IReadOnlyList<InboxRow>>> fetch,
        CancellationToken ct)
    {
        var rows = new List<InboxRow>();
        foreach (var p in _providers)
        {
            try
            {
                rows.AddRange(await fetch(p));
            }
            catch (SqliteException ex) when (IsMissingTable(ex))
            {
                _log.LogWarning(
                    "Inbox provider {Provider} skipped — backing table missing (likely archived via admin Feature Tables tab). {Message}",
                    p.GetType().Name, ex.Message);
            }
        }
        return rows.OrderByDescending(r => r.LastActivityAt).ToList();
    }

    private static bool IsMissingTable(SqliteException ex)
        => ex.Message.Contains("no such table", StringComparison.OrdinalIgnoreCase);
}
