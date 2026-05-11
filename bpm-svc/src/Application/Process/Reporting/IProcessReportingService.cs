namespace Bpm.Application.Process.Reporting;

/// <summary>
/// PR-K5 §8 — per-spec reporting aggregations powering the Process Admin
/// "Reports" dashboard. Pure read-side service; the implementation projects
/// existing process-runtime tables (no separate analytics store).
///
/// <para>
/// Cycle-time percentiles are computed in-memory after pulling all
/// completed-instance durations for the spec/period. That's fine for tens of
/// thousands of instances per spec and avoids dialect-specific percentile
/// SQL (Postgres has <c>percentile_cont</c>; SQLite has nothing). For the
/// few-hundred-thousand range we'll either cache aggressively or push the
/// aggregation down via raw SQL gated by an
/// <c>IPercentileCalculator</c> abstraction.
/// </para>
/// </summary>
public interface IProcessReportingService
{
    Task<PerSpecReport> GetPerSpecReportAsync(
        string specCode,
        ReportPeriod period,
        CancellationToken ct = default);
}

public sealed record PerSpecReport(
    string SpecCode,
    ReportPeriod Period,
    int TotalStarted,
    int TotalCompleted,
    int TotalCancelled,
    int TotalRunning,
    double AvgCycleTimeHours,
    double P50CycleTimeHours,
    double P90CycleTimeHours,
    int BreachCount,
    double BreachRate,
    IReadOnlyList<NodeBottleneck> Bottlenecks,
    IReadOnlyList<AssigneeLoad> AssigneeLoads,
    IReadOnlyList<CycleTimeBucket> CycleTimeHistogram);

/// <summary>
/// One node-level bottleneck stat. <see cref="AvgWaitTimeHours"/> is the
/// mean of (CompletedAt - CreatedAt) over completed tasks at this node — i.e.
/// the average residence time in queue+work, not just queue time. The simpler
/// definition matches what an admin reads "this node takes 4 hours on
/// average" and avoids needing a separate ClaimedAt-based pre-claim metric.
/// </summary>
public sealed record NodeBottleneck(
    string NodeId,
    double AvgWaitTimeHours,
    int CompletedCount);

public sealed record AssigneeLoad(
    Guid UserId,
    string? Email,
    int OpenTasks,
    int CompletedTasks,
    double AvgTaskTimeHours);

/// <summary>
/// Histogram bucket for cycle-time distribution. Buckets are fixed: 0-1h,
/// 1-4h, 4-24h, 1-7d, 7d+. The fixed scheme keeps the dashboard's chart code
/// trivial and avoids bucket-instability complaints when the data shifts.
/// </summary>
public sealed record CycleTimeBucket(string Range, int Count);

public enum ReportPeriod
{
    Last7Days = 7,
    Last30Days = 30,
    Last90Days = 90,
    AllTime = 0,
}
