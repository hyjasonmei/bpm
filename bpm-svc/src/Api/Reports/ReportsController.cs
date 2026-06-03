using System.Linq;
using Bpm.Application.Common.Abstractions;
using Bpm.Persistence;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace Bpm.Api.Reports;

/// <summary>
/// Cross-flow reporting over every chef-cooked case table — the real-data
/// source behind admin's Reports page.
///
/// <para><b>Auto-discovering:</b> rather than hard-coding the flow list, this
/// asks the EF model for every entity whose CLR type is named
/// <c>&lt;CODE&gt;_V1_Case</c> and that exposes <c>SubmittedAt</c> / <c>Status</c>
/// (and optionally <c>CompletedAt</c>). A newly chef-cooked flow therefore shows
/// up in the report the moment its case table is registered — no edit here, no
/// per-flow report code. Each flow keeps its own status enum, so we collapse to
/// four cross-flow outcome buckets.</para>
///
/// <para>POC: anonymous + read-only — reached by admin-ui via its /bpmsvc dev
/// proxy. Add auth once the admin → bpm token bridge lands.</para>
/// </summary>
[ApiController]
[Route("api/reports")]
[AllowAnonymous]
public sealed class ReportsController(AppDbContext db, IClock clock) : ControllerBase
{
    private sealed record CaseFact(string FlowCode, string Status, DateTime Submitted, DateTime? Completed);

    private const string CaseSuffix = "_V1_Case";

    [HttpGet("summary")]
    public ReportSummaryDto Summary()
    {
        var facts = CollectFacts();

        var now = clock.UtcNow;
        var monthStart = new DateTime(now.Year, now.Month, 1, 0, 0, 0, DateTimeKind.Utc);

        var total = facts.Count;
        var completed = facts.Count(f => f.Status == "Completed");
        var cancelled = facts.Count(f => f.Status == "Cancelled");
        var rejected = facts.Count(f => f.Status == "Rejected");
        var terminal = completed + cancelled + rejected;
        var inProgress = total - terminal;
        var thisMonth = facts.Count(f => f.Submitted >= monthStart);

        var approvalRate = terminal == 0 ? 0d : (double)completed / terminal;

        var done = facts.Where(f => f.Status == "Completed" && f.Completed is not null).ToList();
        double? avgCycleDays = done.Count == 0
            ? null
            : Math.Round(done.Average(f => (f.Completed!.Value - f.Submitted).TotalDays), 1);

        var byFlow = facts
            .GroupBy(f => f.FlowCode)
            .Select(g => new FlowCount(g.Key, g.Count()))
            .OrderByDescending(x => x.Count)
            .ToList();

        var byStatus = facts
            .GroupBy(f => Bucket(f.Status))
            .Select(g => new StatusCount(g.Key, g.Count()))
            .ToList();

        var monthly = new List<MonthCount>();
        for (var i = 5; i >= 0; i--)
        {
            var m = monthStart.AddMonths(-i);
            var next = m.AddMonths(1);
            monthly.Add(new MonthCount(m.ToString("yyyy-MM"), facts.Count(f => f.Submitted >= m && f.Submitted < next)));
        }

        return new ReportSummaryDto(
            TotalCases: total,
            ThisMonth: thisMonth,
            Completed: completed,
            InProgress: inProgress,
            ApprovalRate: approvalRate,
            AvgCycleDays: avgCycleDays,
            ByFlow: byFlow,
            ByStatus: byStatus,
            Monthly: monthly);
    }

    /// <summary>Reflectively pull (status, submitted, completed) from every
    /// registered <c>*_V1_Case</c> entity.</summary>
    private List<CaseFact> CollectFacts()
    {
        var facts = new List<CaseFact>();

        var caseTypes = db.Model.GetEntityTypes()
            .Select(e => e.ClrType)
            .Where(t => t.Name.EndsWith(CaseSuffix, StringComparison.Ordinal))
            .Distinct();

        var setMethod = typeof(DbContext).GetMethods()
            .First(m => m.Name == nameof(DbContext.Set) && m.IsGenericMethod && m.GetParameters().Length == 0);

        foreach (var t in caseTypes)
        {
            var pSubmitted = t.GetProperty("SubmittedAt");
            var pStatus = t.GetProperty("Status");
            if (pSubmitted is null || pStatus is null) continue;
            var pCompleted = t.GetProperty("CompletedAt");

            var flowCode = t.Name[..^CaseSuffix.Length];

            var queryable = (IQueryable)setMethod.MakeGenericMethod(t).Invoke(db, null)!;
            foreach (var row in queryable.Cast<object>().ToList())
            {
                var status = pStatus.GetValue(row)?.ToString() ?? "Unknown";
                var submitted = (DateTime)pSubmitted.GetValue(row)!;
                var completed = pCompleted?.GetValue(row) as DateTime?;
                facts.Add(new CaseFact(flowCode, status, submitted, completed));
            }
        }

        return facts;
    }

    // Coarse cross-flow status bucket — per-flow enums differ, so collapse to
    // the four outcomes that read the same across every flow.
    private static string Bucket(string status) => status switch
    {
        "Completed" => "Completed",
        "Cancelled" => "Cancelled",
        "Rejected" => "Rejected",
        _ => "In progress",
    };
}

public sealed record ReportSummaryDto(
    int TotalCases,
    int ThisMonth,
    int Completed,
    int InProgress,
    double ApprovalRate,
    double? AvgCycleDays,
    IReadOnlyList<FlowCount> ByFlow,
    IReadOnlyList<StatusCount> ByStatus,
    IReadOnlyList<MonthCount> Monthly);

public sealed record FlowCount(string FlowCode, int Count);
public sealed record StatusCount(string Bucket, int Count);
public sealed record MonthCount(string Month, int Count);
