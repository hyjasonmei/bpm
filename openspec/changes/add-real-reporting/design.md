# Design notes

## 1. Caching strategy

Aggregation queries over thousands of instances are expensive per-page-load. Cache:

- Key: `{tenant_id}:{report_type}:{spec_code|"all"}:{period}`
- Value: serialized response JSON
- TTL: 5 minutes
- Storage: `IMemoryCache` (in-process); single-tenant POC

When a relevant event fires (InstanceCompleted, InstanceCancelled, SlaBreached, TaskSubmitted on bottleneck-tracked node), the runtime invokes `IReportingCache.Invalidate(tenant_id, spec_code)` which removes matching keys.

For production scale: switch to Redis. v1 in-memory.

## 2. Cycle time computation

Cycle time = `instance.CompletedAt - instance.StartedAt` for each completed instance. Histogram buckets: <1d, 1-2d, 2-3d, 3-7d, 7-14d, 14+d. Adjustable per spec: short-cycle flows (LEAVE) might use hour buckets; long-cycle (HWP) day buckets. v1: use hour buckets when median < 1 day, day buckets otherwise.

p50 / p95: standard percentile over the cycle times in the period.

## 3. Bottleneck analysis

For each NodeId in a spec, aggregate `avg(task.CompletedAt - task.CreatedAt)` over all completed tasks at that node in the period. Sort descending = bottleneck list. Skip tasks that were Cancelled (e.g., auto-cancelled siblings in collection mode).

Useful UX: show as horizontal bar chart with node label + average time.

## 4. Per-assignee load

`SELECT actual_assignee_user_id, COUNT(*) FROM tasks WHERE Status IN (Pending, InProgress) GROUP BY ...` → ordered descending. Limit 50 results. Useful for "who's drowning in approvals".

## 5. Drill-down semantics

Click a chart segment (e.g., "1-2 days" bucket) → frontend navigates to `/processes/live-cases?cycle_time_min=1d&cycle_time_max=2d`. The query params propagate to live-cases / completed-cases pages.

## 6. Export

Per-chart PNG export is provided by recharts (built-in `useDom2Image` or similar). Raw CSV download is wired to a query-time CSV-formatted endpoint variant.

## 7. Open questions

- **Spec-cross comparison**: 3 specs side-by-side. Already supported by `spec-comparison` endpoint; UI may surface as a special view.
- **Time-of-day analysis**: when do most cases get submitted? Defer.
- **Approver SLA conformance**: per-approver breach rate. Already covered by per-assignee load if we extend with breach-count column. v2 enrichment.
- **Spec adoption / lifecycle**: when was each spec last edited / how often used. Useful for retiring stale flows. Defer.
