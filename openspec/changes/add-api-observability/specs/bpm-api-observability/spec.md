## ADDED Requirements

### Requirement: Structured logging via Serilog with enrichment

The system SHALL use Serilog as the logging provider. Every log line SHALL include enrichments for: `RequestId` (per-request correlation id), `TenantId`, `UserId`, `TraceId` (from OpenTelemetry). Output SHALL be JSON-formatted in production for greppability.

#### Scenario: Log line carries enrichments

- **GIVEN** an authenticated request from Wilson under tenant Acme
- **WHEN** any log line is emitted during request processing
- **THEN** the log includes `{ RequestId, TenantId: Acme, UserId: Wilson.Id, TraceId: ... }` fields

### Requirement: OpenTelemetry tracing with configurable exporter

The system SHALL emit OpenTelemetry traces for HTTP requests, EF Core queries, and outbound HTTP calls (notifications, webhooks, Microsoft Graph). The exporter SHALL be selectable via env: `console` (dev), `otlp` (prod). When OTLP, the endpoint SHALL come from `OTEL_EXPORTER_OTLP_ENDPOINT` env.

#### Scenario: Console exporter in dev

- **GIVEN** OTEL_EXPORTER=console
- **WHEN** a request is processed
- **THEN** a trace is printed to stdout with HTTP span + nested DB query spans

#### Scenario: OTLP exporter in prod

- **GIVEN** OTEL_EXPORTER=otlp and OTEL_EXPORTER_OTLP_ENDPOINT set
- **WHEN** the app starts
- **THEN** traces flow to the OTLP endpoint via gRPC

### Requirement: Health endpoints expose liveness and readiness

The system SHALL expose:

- `GET /health/live` — liveness; 200 if process is alive
- `GET /health/ready` — readiness; 200 only when DB is reachable, file storage backend is reachable, NotificationDispatchWorker last run < 5 min ago, SlaTimerJob last run < 2 min ago. 503 with failing components in body when any check fails.
- `GET /health/detail` — admin-only; returns per-check status JSON

These endpoints SHALL be excluded from rate limiting and authentication.

#### Scenario: Healthy ready

- **GIVEN** all sub-checks pass
- **WHEN** GET /health/ready
- **THEN** 200 with body `{ status: 'Healthy', checks: [{ name: 'database', status: 'Healthy' }, ...] }`

#### Scenario: Database down

- **GIVEN** the DB is unreachable
- **WHEN** GET /health/ready
- **THEN** 503 with body listing the failing 'database' check

### Requirement: Rate limiting protects API

The system SHALL apply rate limits:

- Per-IP: 100 requests / minute
- Per-authenticated-user: 600 / minute
- Per-tenant: 6000 / minute

When exceeded: 429 Too Many Requests with `Retry-After: <seconds>` header and JSON body explaining the cause. Health and metrics endpoints SHALL be exempt.

#### Scenario: Per-IP limit triggers 429

- **WHEN** an unauthenticated client makes 101 requests in 60 seconds from the same IP
- **THEN** the 101st request returns 429 with Retry-After header

#### Scenario: Health exempt

- **WHEN** an unauthenticated client makes 1000 requests/min to /health/live
- **THEN** all return 200 (rate limit not applied)

### Requirement: Prometheus metrics endpoint

The system SHALL expose `GET /metrics` returning Prometheus text format. The endpoint SHALL include:

- Counters: bpm_http_requests_total (with method/endpoint/status labels), bpm_notifications_dispatched_total, bpm_sla_breaches_total, bpm_sso_login_total, bpm_processes_started_total, bpm_processes_completed_total
- Histograms: bpm_http_request_duration_seconds, bpm_db_query_duration_seconds, bpm_outbound_http_duration_seconds
- Gauges: bpm_open_tasks, bpm_open_instances

Auth: requires authentication in prod; open in dev.

#### Scenario: Metrics endpoint format

- **WHEN** GET /metrics
- **THEN** the response is `Content-Type: text/plain; version=0.0.4`; body matches Prometheus exposition format

### Requirement: OpenAPI spec auto-generated

The system SHALL auto-generate an OpenAPI 3.0 spec at `/swagger/v1/swagger.json` covering every controller endpoint. Endpoints SHALL be tagged by capability for navigability. XML doc comments on controllers SHALL appear in the generated descriptions.

The Swagger UI (`/swagger`) SHALL be available in dev (`BPM_AUTH_MODE=dev`) and disabled in prod.

#### Scenario: Spec covers all endpoints

- **WHEN** GET /swagger/v1/swagger.json
- **THEN** every controller's endpoints are present with method / path / parameters / responses

#### Scenario: Swagger UI prod disabled

- **GIVEN** BPM_AUTH_MODE=prod
- **WHEN** a client navigates to /swagger
- **THEN** 404

### Requirement: Correlation ID propagation

The system SHALL extract `X-Correlation-Id` from incoming requests (or generate a new UUID if absent) and propagate it to:

- All Serilog log lines for the request
- All outbound HTTP calls (notifications, webhooks, Graph) via the same header
- Webhook delivery payloads (in metadata)

#### Scenario: Inbound correlation propagates outbound

- **GIVEN** a request arrives with `X-Correlation-Id: abc123`
- **WHEN** during processing the system makes a webhook delivery
- **THEN** the webhook POST includes `X-Correlation-Id: abc123` in headers
- **AND** all logs for this request carry `correlation_id: abc123`
