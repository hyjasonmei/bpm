## Why

`Report.tsx` is a UI mock — chart placeholders + frozen numbers. Customers genuinely want to know:

- "How many leave requests were filed this month?"
- "What's the average approval time per spec?"
- "Who's our biggest bottleneck (approver with most overdue tasks)?"
- "What's our SLA breach rate trend?"
- "Which department generates the most flow activity?"
- "Per-employee load — who's drowning?"

Without real reporting, customers can't prove ROI of the platform, can't optimize processes, can't hold approvers accountable. This is differentiator territory.

## What Changes

### Reporting capability (NEW `bpm-reporting`)

**Service** `IProcessReportingService` — aggregations across ProcessInstances + Tasks + TaskHistory:

- `GetSpecOverview(specCode, period)` — totals: started / completed / cancelled / running / breach rate
- `GetCycleTimeDistribution(specCode, period)` — histogram of completion duration; p50, p75, p90, p95
- `GetBottleneckAnalysis(specCode, period)` — average time-in-node by NodeId; sorted descending
- `GetPerAssigneeLoad(period)` — current open tasks count per user; ordered descending
- `GetPerInitiatorActivity(period)` — instances submitted per initiator
- `GetSlaBreachTrend(specCode?, period)` — daily breach count + total open tasks (line chart data)
- `GetDepartmentActivity(period)` — instances by initiator's department
- `GetSpecComparison(specCodes[], period)` — side-by-side metrics

Caching: 5-minute TTL keyed by `{tenant_id}_{spec_code}_{period}_{report_type}`. Invalidated on InstanceCompleted / InstanceCancelled / SlaBreached events.

### Pre-aggregation (optional, performance)

For tenants with > 10K instances, on-the-fly aggregation gets slow. Option: nightly job pre-aggregates daily snapshots into `ReportingDailySnapshot` table. v1: skip pre-aggregation; rely on cache + indexes. Add when performance dictates.

### Chart-friendly response shapes

Each method returns data shaped for direct consumption by the frontend chart library (recharts / chart.js):

```jsonc
// GetCycleTimeDistribution
{
  "spec_code": "LEAVE",
  "period": "30d",
  "histogram": [
    { "bucket_label": "<1 day", "count": 45 },
    { "bucket_label": "1-2 days", "count": 23 },
    { "bucket_label": "2-3 days", "count": 8 },
    { "bucket_label": "3+ days", "count": 4 }
  ],
  "p50_hours": 12,
  "p95_hours": 56,
  "total_instances": 80
}
```

### API endpoints

- `GET /api/reports/spec-overview?spec_code=&period=`
- `GET /api/reports/cycle-time?spec_code=&period=`
- `GET /api/reports/bottlenecks?spec_code=&period=`
- `GET /api/reports/per-assignee-load?period=`
- `GET /api/reports/per-initiator-activity?period=`
- `GET /api/reports/sla-breach-trend?spec_code?=&period=`
- `GET /api/reports/department-activity?period=`
- `GET /api/reports/spec-comparison?spec_codes=A,B,C&period=`

Auth: `flow_admin:<spec_code>` for spec-scoped reports; `tenant_admin` for global.

### Frontend Reports screen

`bpm-ui/src/screens/Report.tsx` rewritten:

- Top: filter bar (period selector, spec filter, "my flows" toggle)
- Stat cards row (totals)
- Chart grid:
  - Cycle time histogram
  - SLA breach trend line
  - Bottleneck bar chart
  - Per-assignee load horizontal bar
- Drill-down: click a chart segment → filtered list of instances
- Export: per-chart PNG (png export from chart lib); raw CSV per dataset

### Charting library

`recharts` (most popular React chart lib, ~600 KB minified). Already common in shadcn ecosystem. Alternatives: chart.js, victory; pick recharts for stylistic consistency.

### Out of scope (future changes)

- Real-time live charts (polling at 30s sufficient)
- Custom dashboard builder (drag-drop widgets) — defer to v2
- Drill from chart to root-cause analysis (e.g., "why is bottleneck so slow?") — defer
- Anomaly detection ("breaches up 200% this week — alert!") — defer; could add later
- Forecast / predictive analytics
- Comparison vs industry benchmark (no benchmarks for now)
- Benchmark across tenants (no multi-tenant scope)

## Capabilities

### New Capabilities

- `bpm-reporting` — IProcessReportingService with 8 aggregation methods, chart-friendly response shapes, 5-min server-side caching, auth-scoped endpoints, replaced Report.tsx with real charts.

### Modified Capabilities

- None. Consumes existing tables.

## Impact

- **bpm-svc/src/Application/Reporting/IProcessReportingService.cs / ProcessReportingService.cs**: implementations
- **bpm-svc/src/Application/Reporting/ReportingCache.cs**: TTL cache (use IMemoryCache)
- **bpm-svc/src/Application/Reporting/Aggregators/**: per-report aggregator class (CycleTimeAggregator, BottleneckAggregator, ...)
- **bpm-svc/src/Api/Reports/ReportsController.cs**: 8 endpoints
- **bpm-svc/src/Application/Process/Runtime/ProcessRuntime.cs**: invalidate cache on relevant events (small extension)
- **bpm-ui/src/lib/reports.ts**: client + types
- **bpm-ui/src/screens/Report.tsx**: rewritten
- **bpm-ui/src/components/charts/**: chart wrapper components (Histogram, LineTrend, HorizontalBar, etc.)
- **NPM**: `recharts`
- **No DB migration**
- **Demo guard**: 9 mock-up forms NOT modified; Report.tsx behavior swap intended (mock → real)
