using Bpm.Application.Process.Reporting;
using Microsoft.Extensions.Caching.Memory;

namespace Bpm.Persistence.Process.Reporting;

/// <summary>
/// PR-K5 §8.2 / §8.3 — TTL-based cache wrapper around
/// <see cref="ProcessReportingService"/>. Five-minute absolute expiration
/// keyed by <c>{tenant}_{specCode}_{period}</c>. Event-based invalidation
/// (on InstanceCompleted / InstanceCancelled) is deferred — the in-process
/// runtime has no event bus today and adding one just for cache busting
/// would dwarf the benefit.
///
/// <para>
/// Tenant scope is "default" for v1; the field is parameter-shaped so we can
/// thread the real tenant through once multi-tenant queries land. Today both
/// callers (controller + tests) pass "default" via the constructor default.
/// </para>
/// </summary>
public sealed class CachedProcessReportingService(
    IProcessReportingService inner,
    IMemoryCache cache) : IProcessReportingService
{
    private static readonly TimeSpan Ttl = TimeSpan.FromMinutes(5);

    public Task<PerSpecReport> GetPerSpecReportAsync(
        string specCode,
        ReportPeriod period,
        CancellationToken ct = default)
        => GetPerSpecReportAsync(specCode, period, tenantCode: "default", ct);

    public async Task<PerSpecReport> GetPerSpecReportAsync(
        string specCode,
        ReportPeriod period,
        string tenantCode,
        CancellationToken ct)
    {
        var key = $"reports:per-spec:{tenantCode}:{specCode}:{period}";
        if (cache.TryGetValue<PerSpecReport>(key, out var cached) && cached is not null)
            return cached;

        var fresh = await inner.GetPerSpecReportAsync(specCode, period, ct);
        cache.Set(key, fresh, new MemoryCacheEntryOptions
        {
            AbsoluteExpirationRelativeToNow = Ttl,
        });
        return fresh;
    }
}
