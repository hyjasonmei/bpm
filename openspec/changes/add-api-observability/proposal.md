## Why

The BPM API has accumulated ~80 endpoints across 22 capabilities. For production readiness:

- Customer's IT team wants OpenAPI spec to integrate via auto-generated clients
- Ops needs structured logging + tracing to debug production issues
- Health endpoints for k8s / Docker liveness / readiness probes
- Rate limiting to protect the dispatcher worker from runaway clients
- Metrics for capacity planning (RPS, p95 latency, error rate)

Today: Swagger is enabled for `/swagger`, basic logging via `ILogger`. No formal OpenAPI export, no tracing, no rate limiting, minimal health endpoint.

This change ships:

- OpenAPI 3.0 spec generation (`/swagger/v1/swagger.json`)
- Structured logging (Serilog + JSON sinks)
- Distributed tracing (OpenTelemetry → console exporter; configurable to OTLP)
- `/health` endpoints (readiness, liveness)
- Rate limiting per API key / IP
- Prometheus metrics endpoint

## What Changes

### Observability capability (NEW `bpm-api-observability`)

**OpenAPI**:

- Already has `Microsoft.AspNetCore.OpenApi`; configure full schema generation
- Per-endpoint XML doc comments → Swagger UI descriptions
- Tag endpoints by capability (e.g., `[Tags("Process")]`)
- `/swagger/v1/swagger.json` available; `/swagger` UI in dev only

**Structured logging**:

- Adopt Serilog with sinks: Console (dev), File rolling daily (prod), optional Seq / Loki
- Enrich every log with `tenant_id`, `user_id`, `request_id`, `trace_id`
- Configure log levels per namespace via env

**Tracing**:

- OpenTelemetry SDK + ASP.NET Core instrumentation
- Default exporter: Console (dev), OTLP-grpc (prod) configurable via env
- Capture: HTTP requests, EF Core queries, outbound HTTP (notifications, webhooks, Graph)

**Health endpoints**:

- `GET /health/live` — liveness (always 200 unless app is dead)
- `GET /health/ready` — readiness; checks DB connection, file storage backend reachable, notification dispatcher worker running
- `GET /health/detail` — admin-only; returns per-component status

**Rate limiting**:

- Use `Microsoft.AspNetCore.RateLimiting` (built into ASP.NET 7+)
- Per-IP: 100 req/min
- Per-authenticated-user: 600 req/min
- Per-tenant: 6000 req/min
- 429 response with Retry-After header

**Metrics**:

- Expose `/metrics` Prometheus endpoint (admin-only or behind auth)
- Counters: requests_total (per endpoint, per status), notifications_dispatched_total, sla_breaches_total, sso_login_total
- Histograms: request_duration_seconds, db_query_duration_seconds

### Out of scope (future changes)

- APM / vendor-specific (Datadog / New Relic / Sentry) — OTLP can forward
- Detailed audit-log streaming to SIEM
- Tracing of background workers (only HTTP path covered initially)
- Custom dashboards (build outside via Grafana)
- Trace sampling strategies beyond default
- Cost / usage reporting

## Capabilities

### New Capabilities

- `bpm-api-observability` — OpenAPI spec auto-gen, Serilog structured logging with enrichment, OpenTelemetry tracing (HTTP + EF + outbound HTTP), health endpoints (live/ready/detail), per-IP/user/tenant rate limiting with 429 + Retry-After, Prometheus metrics endpoint.

### Modified Capabilities

- None directly — modifies bpm-svc startup wiring across the board.

## Impact

- **bpm-svc/src/Api/Program.cs**: registers Serilog + OpenTelemetry + health checks + rate limiter + Prometheus
- **bpm-svc/src/Api/Common/HealthChecks/**: per-component health-check classes (Database, FileStorage, NotificationWorker, etc.)
- **bpm-svc/src/Api/Common/RequestLoggingMiddleware.cs**: enrichment + correlation ID
- **bpm-svc/src/Api/{capability}/Controller.cs**: XML doc comments added for OpenAPI
- **bpm-svc/src/Api/Common/Metrics/**: counter/histogram registrations
- **NuGet adds**: Serilog.AspNetCore + sinks; OpenTelemetry.Extensions.Hosting + OpenTelemetry.Exporter.OpenTelemetryProtocol; AspNetCore.HealthChecks.* (Db check, custom checks); prometheus-net.AspNetCore
- **No DB migration**
- **Demo guard**: 9 mock-up forms NOT modified
