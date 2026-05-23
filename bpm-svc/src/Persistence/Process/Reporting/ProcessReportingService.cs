using Bpm.Application.Common.Abstractions;
using Bpm.Application.Process.Reporting;
using Bpm.Domain.Entities.Process;
using Microsoft.EntityFrameworkCore;
using TaskStatus = Bpm.Domain.Entities.Process.TaskStatus;

namespace Bpm.Persistence.Process.Reporting;

/// <summary>
/// Read-side aggregator over <see cref="AppDbContext"/>. Pure queries — no
/// state mutation; safe to call concurrently. The
/// <see cref="CachedProcessReportingService"/> wraps this with a 5-min TTL
/// memo so the dashboard doesn't re-run aggregations every render.
/// </summary>
public sealed class ProcessReportingService(AppDbContext db, IClock clock) : IProcessReportingService
{
    public async Task<PerSpecReport> GetPerSpecReportAsync(
        string specCode,
        ReportPeriod period,
        CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(specCode))
            specCode = string.Empty;

        var now = clock.UtcNow;
        DateTime? since = period switch
        {
            ReportPeriod.Last7Days => now.AddDays(-7),
            ReportPeriod.Last30Days => now.AddDays(-30),
            ReportPeriod.Last90Days => now.AddDays(-90),
            _ => null,
        };

        // Per-instance scope: instances that fall in the period window OR are
        // still running (we want the dashboard's "Running" count to reflect
        // current state, not period-bounded starts).
        var instanceQuery = db.ProcessInstances.AsNoTracking()
            .Where(i => i.SpecCode == specCode);

        var allInstances = await instanceQuery.ToListAsync(ct);
        var inPeriod = since is null
            ? allInstances
            : allInstances.Where(i => i.StartedAt >= since.Value).ToList();

        var completed = inPeriod.Where(i => i.Status == InstanceStatus.Completed).ToList();
        var cancelled = inPeriod.Where(i => i.Status == InstanceStatus.Cancelled).ToList();
        var running = allInstances.Count(i => i.Status == InstanceStatus.Running || i.Status == InstanceStatus.Errored);

        // Cycle times — completed only. Cancelled cases have a cycle time too
        // but lumping them into "average completion time" muddies the metric.
        var cycleHours = completed
            .Where(i => i.CompletedAt.HasValue)
            .Select(i => (i.CompletedAt!.Value - i.StartedAt).TotalHours)
            .ToList();

        var avg = cycleHours.Count == 0 ? 0d : cycleHours.Average();
        var p50 = Percentile(cycleHours, 0.50);
        var p90 = Percentile(cycleHours, 0.90);

        // Tasks for bottleneck + assignee load + breach. We need both
        // completed (for AvgWait + AvgTaskTime + breach-on-complete) and
        // open (for breach-on-now + open-task counts).
        var instanceIds = inPeriod.Select(i => i.Id).ToHashSet();
        var allInstanceIds = allInstances.Select(i => i.Id).ToHashSet();

        var taskRows = await db.ProcessTasks.AsNoTracking()
            .Where(t => allInstanceIds.Contains(t.ProcessInstanceId))
            .ToListAsync(ct);

        // Breach: a completed task whose CompletedAt > DueAt, or an open task
        // whose DueAt is in the past. We scope breach to tasks whose owning
        // instance falls in the in-period set (so the rate matches "of the
        // X completions in this window, Y breached SLA").
        var inPeriodTasks = taskRows.Where(t => instanceIds.Contains(t.ProcessInstanceId)).ToList();
        var breachCount = inPeriodTasks.Count(t =>
            t.DueAt is DateTime due
            && ((t.CompletedAt is DateTime ct2 && ct2 > due)
                || ((t.Status == TaskStatus.Pending || t.Status == TaskStatus.InProgress) && due < now)));

        var breachRate = completed.Count == 0 ? 0d : (double)breachCount / completed.Count;

        // Bottlenecks: per-NodeId mean (CompletedAt - CreatedAt) hours over
        // tasks completed inside the window. We scope by task completion time
        // (not instance termination) so a long-running case's already-finished
        // task contributes — bottleneck analysis should reflect node behaviour
        // independent of whether the case has fully terminated.
        var completedTaskCutoff = since;
        var completedTasksInWindow = taskRows
            .Where(t => t.Status == TaskStatus.Completed && t.CompletedAt.HasValue
                        && (completedTaskCutoff is null || t.CompletedAt!.Value >= completedTaskCutoff.Value))
            .ToList();
        var bottlenecks = completedTasksInWindow
            .GroupBy(t => t.NodeId)
            .Select(g => new NodeBottleneck(
                NodeId: g.Key,
                AvgWaitTimeHours: g.Average(t => (t.CompletedAt!.Value - t.CreatedAt).TotalHours),
                CompletedCount: g.Count()))
            .OrderByDescending(b => b.AvgWaitTimeHours)
            .Take(20)
            .ToList();

        // Assignee load: per ActualAssigneeUserId across the all-instances
        // task list (Open uses current state; CompletedTasks uses
        // in-period completion; AvgTaskTime over completed in-period).
        var assigneeIds = taskRows
            .Where(t => t.ActualAssigneeUserId.HasValue)
            .Select(t => t.ActualAssigneeUserId!.Value)
            .Distinct()
            .ToList();
        var emails = await db.SharedPrincipals.AsNoTracking()
            .Where(u => assigneeIds.Contains(u.Id))
            .Select(u => new { u.Id, u.Email })
            .ToListAsync(ct);
        var emailLookup = emails.ToDictionary(u => u.Id, u => u.Email ?? string.Empty);

        var loads = taskRows
            .Where(t => t.ActualAssigneeUserId.HasValue)
            .GroupBy(t => t.ActualAssigneeUserId!.Value)
            .Select(g =>
            {
                var openCount = g.Count(t => t.Status == TaskStatus.Pending || t.Status == TaskStatus.InProgress);
                var completedTasks = g
                    .Where(t => t.Status == TaskStatus.Completed
                                && t.CompletedAt.HasValue
                                && (completedTaskCutoff is null || t.CompletedAt!.Value >= completedTaskCutoff.Value))
                    .ToList();
                var avgTaskHours = completedTasks.Count == 0
                    ? 0d
                    : completedTasks.Average(t => (t.CompletedAt!.Value - t.CreatedAt).TotalHours);
                emailLookup.TryGetValue(g.Key, out var email);
                return new AssigneeLoad(
                    UserId: g.Key,
                    Email: email,
                    OpenTasks: openCount,
                    CompletedTasks: completedTasks.Count,
                    AvgTaskTimeHours: avgTaskHours);
            })
            .Where(a => a.OpenTasks > 0 || a.CompletedTasks > 0)
            .OrderByDescending(a => a.OpenTasks).ThenByDescending(a => a.CompletedTasks)
            .Take(50)
            .ToList();

        var histogram = BuildHistogram(cycleHours);

        return new PerSpecReport(
            SpecCode: specCode,
            Period: period,
            TotalStarted: inPeriod.Count,
            TotalCompleted: completed.Count,
            TotalCancelled: cancelled.Count,
            TotalRunning: running,
            AvgCycleTimeHours: avg,
            P50CycleTimeHours: p50,
            P90CycleTimeHours: p90,
            BreachCount: breachCount,
            BreachRate: breachRate,
            Bottlenecks: bottlenecks,
            AssigneeLoads: loads,
            CycleTimeHistogram: histogram);
    }

    /// <summary>
    /// Linear-interpolation percentile over an unsorted list. Empty input
    /// returns 0. Matches the semantics of NumPy <c>percentile</c> with
    /// default <c>linear</c> interpolation — close enough to what a v1
    /// dashboard reader expects from a P50 / P90 number.
    /// </summary>
    public static double Percentile(IReadOnlyList<double> values, double pct)
    {
        if (values is null || values.Count == 0) return 0d;
        var sorted = values.OrderBy(v => v).ToArray();
        if (sorted.Length == 1) return sorted[0];
        var rank = pct * (sorted.Length - 1);
        var lo = (int)Math.Floor(rank);
        var hi = (int)Math.Ceiling(rank);
        if (lo == hi) return sorted[lo];
        var frac = rank - lo;
        return sorted[lo] + (sorted[hi] - sorted[lo]) * frac;
    }

    private static IReadOnlyList<CycleTimeBucket> BuildHistogram(IReadOnlyList<double> cycleHours)
    {
        var buckets = new[]
        {
            ("0-1h",   0.0,    1.0),
            ("1-4h",   1.0,    4.0),
            ("4-24h",  4.0,   24.0),
            ("1-7d",  24.0,  168.0),
            ("7d+",  168.0,  double.PositiveInfinity),
        };
        return buckets
            .Select(b => new CycleTimeBucket(b.Item1, cycleHours.Count(h => h >= b.Item2 && h < b.Item3)))
            .ToList();
    }
}
