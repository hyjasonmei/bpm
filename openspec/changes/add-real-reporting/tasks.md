# Tasks

## 1. Reporting service + aggregators

- [ ] 1.1 Create `IProcessReportingService.cs` with 8 methods
- [ ] 1.2 Per-report aggregator classes in Application/Reporting/Aggregators/
- [ ] 1.3 Use raw SQL (or LINQ-to-EF) for performance; index on (tenant_id, status, started_at) etc.
- [ ] 1.4 Tests with seeded fixture (synthetic 100 instances across 30 days)

## 2. Cache layer

- [ ] 2.1 Create `IReportingCache.cs` and implementation backed by IMemoryCache
- [ ] 2.2 5-min TTL, key-based invalidation
- [ ] 2.3 Hook into ProcessRuntime: invalidate on InstanceCompleted / InstanceCancelled / SlaBreached
- [ ] 2.4 Tests: cache hit / miss / invalidation

## 3. API endpoints

- [ ] 3.1 Create `ReportsController.cs` with 8 endpoints + CSV variant
- [ ] 3.2 Auth: `flow_admin` for spec-scoped; `tenant_admin` for global
- [ ] 3.3 Integration tests

## 4. Frontend chart components

- [ ] 4.1 Add `recharts` to package.json
- [ ] 4.2 Create reusable wrappers in `bpm-ui/src/components/charts/`:
  - `HistogramChart`
  - `LineTrendChart`
  - `HorizontalBarChart`
  - `StatCard` (already may exist; reuse)
- [ ] 4.3 Each accepts a typed data shape matching the API response

## 5. Reports screen rewrite

- [ ] 5.1 Rewrite `bpm-ui/src/screens/Report.tsx`:
  - Top filter bar (period selector / spec selector / my-flows toggle)
  - Stat cards row (4 cards: total / completed / breach rate / avg cycle)
  - 4-up chart grid
  - CSV export per chart
  - Drill-down navigation on click
- [ ] 5.2 Bilingual labels

## 6. End-to-end verification

- [ ] 6.1 Boot stack with seed (run synthetic data fixture creating 100 instances)
- [ ] 6.2 GET /api/reports/spec-overview?spec_code=LEAVE&period=30d; verify counts
- [ ] 6.3 Visit /reports; verify charts render; spot-check numbers vs DB
- [ ] 6.4 Click a histogram bucket; verify drill-down navigates correctly
- [ ] 6.5 Cache hit verification: reload page within 5 min → fast response (X-Cache: HIT header)
- [ ] 6.6 Complete an instance; verify cache invalidated + next request recomputes
- [ ] 6.7 **Demo guard**: 9 mock-up forms, Home, Search, lib/workflow.ts NOT modified; Report.tsx behavior swap intended

## 7. Commit

- [ ] 7.1 Commit in chunks (service + aggregators; cache; endpoints; charts; screen; verification)
- [ ] 7.2 Push via GitKraken
