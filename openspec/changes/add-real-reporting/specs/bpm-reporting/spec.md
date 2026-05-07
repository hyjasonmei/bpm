## ADDED Requirements

### Requirement: ProcessReportingService provides aggregate metrics

The system SHALL expose `IProcessReportingService` with methods returning chart-friendly aggregations: spec overview (totals + breach rate), cycle time distribution (histogram + percentiles), bottleneck analysis (avg time per node), per-assignee load (open task counts), per-initiator activity, SLA breach trend (daily series), department activity, spec comparison.

#### Scenario: Spec overview returns counts

- **GIVEN** 80 completed LEAVE instances and 5 cancelled in last 30 days
- **WHEN** `GetSpecOverview("LEAVE", "30d")` is called
- **THEN** the result includes `total_started=85, total_completed=80, total_cancelled=5`

#### Scenario: Cycle time histogram

- **WHEN** `GetCycleTimeDistribution("LEAVE", "30d")` is called
- **THEN** the result includes a histogram array with `{ bucket_label, count }` items, plus p50, p95, total_instances

#### Scenario: Bottleneck identifies slowest node

- **GIVEN** approval_manager averages 6 hours, hr_archive averages 3 hours
- **WHEN** `GetBottleneckAnalysis("LEAVE", "30d")` is called
- **THEN** approval_manager appears first; hr_archive second; numeric average times included

### Requirement: 5-minute cache with event-driven invalidation

Reporting query results SHALL be cached for 5 minutes per `(tenant, report_type, spec_code, period)` key. The cache MUST be invalidated when state events occur (InstanceCompleted, InstanceCancelled, SlaBreached). After invalidation, the next request rebuilds and re-caches.

#### Scenario: Cache hit on second call

- **GIVEN** GetSpecOverview was just called for LEAVE
- **WHEN** the same call repeats within 5 minutes
- **THEN** the second call returns from cache; response includes header `X-Cache: HIT`

#### Scenario: Cache invalidated on completion

- **GIVEN** the cache was populated; an instance just completed
- **WHEN** the next call comes in
- **THEN** the response is freshly computed; header is `X-Cache: MISS`

### Requirement: Endpoint auth scoped to flow_admin or tenant_admin

Reporting endpoints SHALL be auth-scoped:

- Spec-scoped reports (`spec_code` param) → require `flow_admin:<spec_code>` OR `tenant_admin`
- Global reports (no spec_code) → require `tenant_admin`

Regular users SHALL NOT have access to reporting endpoints; the UI route is hidden for them.

#### Scenario: Flow admin sees own spec

- **GIVEN** Wilson has flow_admin:LEAVE role
- **WHEN** Wilson calls GET /api/reports/spec-overview?spec_code=LEAVE
- **THEN** 200 OK

#### Scenario: Flow admin blocked from other spec

- **WHEN** Wilson calls GET /api/reports/spec-overview?spec_code=PURCHASE (without flow_admin:PURCHASE)
- **THEN** 403

#### Scenario: Tenant admin sees global

- **WHEN** a tenant_admin calls GET /api/reports/per-assignee-load
- **THEN** 200 OK with all users' loads

### Requirement: Drill-down navigation supported

Reporting endpoints SHALL return query parameters appropriate for drill-down. For example, a histogram bucket "1-2 days" includes filter values `cycle_time_min=24h&cycle_time_max=48h` so the frontend can build a deep link to `/processes/live-cases?...&cycle_time_min=24h&cycle_time_max=48h`.

#### Scenario: Histogram bucket carries filter

- **WHEN** the histogram returns a bucket "1-2 days"
- **THEN** the bucket includes `{ filter: { cycle_time_min: '24h', cycle_time_max: '48h' } }` for drill-down use
