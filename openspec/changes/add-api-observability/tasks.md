# Tasks

## 1. Logging

- [ ] 1.1 Add NuGets: Serilog.AspNetCore, Serilog.Sinks.Console, Serilog.Sinks.File, Serilog.Enrichers.*
- [ ] 1.2 Configure in Program.cs: console sink (dev), file rolling (prod), enrichers (RequestId, TenantId, UserId)
- [ ] 1.3 Replace ConfigureLogging defaults with UseSerilog
- [ ] 1.4 Document logging conventions in CLAUDE.md (no PII; use scopes for context)

## 2. Tracing

- [ ] 2.1 Add NuGets: OpenTelemetry.Extensions.Hosting, OpenTelemetry.Instrumentation.AspNetCore, OpenTelemetry.Instrumentation.EntityFrameworkCore, OpenTelemetry.Instrumentation.HttpClient, OpenTelemetry.Exporter.Console, OpenTelemetry.Exporter.OpenTelemetryProtocol
- [ ] 2.2 Configure tracer in Program.cs with environment-based exporter selection
- [ ] 2.3 Tag spans with tenant_id / user_id from current context

## 3. Health checks

- [ ] 3.1 Add NuGet AspNetCore.HealthChecks
- [ ] 3.2 Configure /health/live (always 200 if process alive)
- [ ] 3.3 Configure /health/ready with sub-checks:
  - SQLite reachable
  - File storage backend reachable (local exists / S3 list)
  - Notification worker last successful run < 5 min
  - SLA worker last successful run < 2 min
- [ ] 3.4 /health/detail returns per-check status (admin only)

## 4. Rate limiting

- [ ] 4.1 Configure `Microsoft.AspNetCore.RateLimiting` policies in Program.cs
- [ ] 4.2 Per-IP: 100/min
- [ ] 4.3 Per-user (authenticated): 600/min
- [ ] 4.4 Per-tenant: 6000/min
- [ ] 4.5 Apply to all controllers; opt-out for /health/* and /metrics

## 5. Metrics

- [ ] 5.1 Add NuGet prometheus-net.AspNetCore
- [ ] 5.2 Register metrics endpoint /metrics (auth required for non-dev)
- [ ] 5.3 Define counters / histograms / gauges per design.md
- [ ] 5.4 Instrument key code paths (request middleware, dispatcher, runtime events)

## 6. OpenAPI

- [ ] 6.1 Configure full schema generation in Program.cs (already partially set)
- [ ] 6.2 Enable XML doc generation in csproj; add comments to controllers
- [ ] 6.3 Tag controllers by capability
- [ ] 6.4 Available in dev at /swagger; prod returns 404

## 7. Correlation IDs

- [ ] 7.1 Middleware reads X-Correlation-Id; generates if absent; sets in HttpContext
- [ ] 7.2 Serilog enricher attaches to every log
- [ ] 7.3 Outbound HttpClient propagates the header

## 8. End-to-end verification

- [ ] 8.1 Boot service; hit /health/live → 200; /health/ready → 200; /metrics → text/plain Prom format
- [ ] 8.2 Hit /swagger → JSON spec available
- [ ] 8.3 Make 101 requests to a public endpoint from same IP → 101st returns 429
- [ ] 8.4 Verify logs include trace_id + tenant_id + user_id
- [ ] 8.5 Run instance through full LEAVE flow; verify metrics increment (processes_started, notifications_dispatched, etc.)
- [ ] 8.6 **Demo guard**: 9 mock-up forms NOT modified

## 9. Commit

- [ ] 9.1 Commit in chunks (logging; tracing; health; rate limit; metrics; OpenAPI; correlation)
- [ ] 9.2 Push via GitKraken
