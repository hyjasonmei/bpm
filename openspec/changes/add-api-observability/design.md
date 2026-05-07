# Design notes

## 1. Why Serilog over the default ILogger

Default `ILogger` works but lacks:

- JSON structured output (greppable, parseable)
- Built-in enrichers
- Multiple sinks per logger

Serilog is the de facto .NET logging library; minimal overhead; easy migration (existing `ILogger<T>` calls work via Serilog provider).

## 2. Why OpenTelemetry over Application Insights direct

OpenTelemetry is vendor-neutral. Today: console exporter for dev. Tomorrow: customer wants Datadog / Tempo / Application Insights → swap exporter, no code change.

OTLP-grpc is the universal protocol; most APMs accept it.

## 3. Health check shape

`/health/ready` returns 200 only when:

- Database accepts a `SELECT 1`
- File storage backend (local fs accessible / S3 reachable / Azure connectable)
- Notification dispatcher worker last ran < 5 minutes ago
- SLA timer worker last ran < 2 minutes ago

If any fails: 503 with the failing components listed in body.

Liveness (`/health/live`) is simpler: always 200 unless the process is hung. Used by k8s for "should we restart this pod?"

## 4. Rate limiting strategy

Three layers:

- Per-IP: 100/min — protects against unauthenticated abuse
- Per-user: 600/min — protects against authenticated runaway scripts
- Per-tenant: 6000/min — admin / multi-user aggregate

Limits are configurable via env. Defaults reasonable for SME load.

When limited: 429 + Retry-After: <seconds> + JSON body explaining why.

## 5. Prometheus metrics — what to expose

Counters:
- `bpm_http_requests_total{method,endpoint,status}` — HTTP traffic
- `bpm_notifications_dispatched_total{trigger,channel}` — notification volume
- `bpm_sla_breaches_total{spec_code}` — breach count
- `bpm_sso_login_total{provider,result}` — auth events
- `bpm_processes_started_total{spec_code}` — instance starts
- `bpm_processes_completed_total{spec_code}` — instance completions

Histograms:
- `bpm_http_request_duration_seconds{endpoint}` — latency
- `bpm_db_query_duration_seconds{operation}` — DB perf
- `bpm_outbound_http_duration_seconds{target}` — calls to external (Graph, webhook destinations, email senders)

Gauges:
- `bpm_open_tasks{spec_code}` — current load
- `bpm_open_instances{spec_code}` — current load

## 6. Correlation IDs

Each incoming request gets `X-Correlation-Id` (generate if missing). Propagated to:
- All logs for this request
- All outbound calls (Graph / webhook / email)

For the customer's webhook receiver, we include this in the payload metadata so the customer can grep our logs by their event correlation.

## 7. Open questions

- **Sampling**: trace 100% in dev, 10% in prod? Configurable.
- **Log retention**: handled by sink choice (Seq / Loki manage themselves; file rolling = manual rotation policy).
- **PII scrubbing**: never log email body / form data verbatim. Document logging conventions.
