## ADDED Requirements

### Requirement: WebhookSubscription configures per-tenant outbound URLs

The system SHALL persist `WebhookSubscription` per tenant carrying TargetUrl, Secret (random), EventFilters[], IsActive, RetryPolicyJson. TargetUrl MUST be HTTPS in production (`BPM_AUTH_MODE=prod`); HTTP allowed in dev only. Secret SHALL be auto-generated on creation; admin can regenerate (invalidates old signature).

#### Scenario: Create subscription with filter

- **WHEN** an admin POSTs `/api/admin/webhooks` with target_url, filters [{spec_code: "LEAVE", trigger: "instance.completed"}]
- **THEN** a row is inserted; secret returned once (subsequent GETs show only last 4 chars)

#### Scenario: HTTP rejected in prod

- **GIVEN** BPM_AUTH_MODE=prod
- **WHEN** admin POSTs with target_url = "http://acme.com/hook"
- **THEN** 400 with "HTTPS required for webhook targets in prod"

### Requirement: HMAC signature on every POST

The dispatch worker SHALL compute `X-BPM-Signature: t=<unix_ts>,v1=<hex_hmac>` where the HMAC is HMAC-SHA256(secret, `<ts>.<body>`). Customer endpoints verify this header to authenticate the source. The timestamp SHOULD be checked within ±5 minutes by customers to prevent replay.

#### Scenario: Signature header attached

- **WHEN** the worker dispatches a delivery
- **THEN** the request includes `X-BPM-Signature: t=1714050000,v1=abc123def...`

#### Scenario: Customer can verify

- **GIVEN** a customer receives the POST
- **WHEN** the customer recomputes HMAC-SHA256(shared_secret, `${ts}.${body}`)
- **THEN** the value matches the `v1` portion of the signature header

### Requirement: Retry policy with exponential backoff

The worker SHALL retry failed deliveries with backoff: 1 min, 5 min, 30 min. After 3 attempts the delivery becomes terminal `Abandoned`. The customer's endpoint:

- 2xx → success
- 4xx (except 429) → no retry; mark Failed (terminal)
- 429 with Retry-After → next attempt at the requested time
- 5xx / timeout / network error → retry per policy

#### Scenario: 5xx triggers retry

- **GIVEN** a delivery to an endpoint returning 503
- **WHEN** the worker dispatches
- **THEN** Status = Queued (not Failed terminal); NextAttemptAt = now + 1 minute; Attempts = 1

#### Scenario: 4xx terminal

- **GIVEN** an endpoint returning 400
- **WHEN** the worker dispatches
- **THEN** Status = Failed (terminal); no retries; admin can manually retry from UI

#### Scenario: 429 respects Retry-After

- **GIVEN** an endpoint returning 429 with Retry-After: 600 (10 minutes)
- **WHEN** the worker dispatches
- **THEN** NextAttemptAt = now + 10 minutes (overrides default backoff)

### Requirement: Event filters narrow delivery

`EventFilters` SHALL be an array of `(spec_code?, trigger)` rules. A delivery is created only when the runtime event matches at least one rule. Empty filters mean "all events for any spec".

#### Scenario: Spec-specific filter

- **GIVEN** subscription with filters `[{spec_code: "LEAVE", trigger: "instance.completed"}]`
- **WHEN** a PURCHASE instance completes
- **THEN** no Delivery row is created for this subscription

#### Scenario: Empty filters = all events

- **GIVEN** subscription with filters = []
- **WHEN** any state event fires for any spec
- **THEN** Delivery rows are created for this subscription

### Requirement: Test delivery endpoint in admin

`POST /api/admin/webhooks/{id}/test` SHALL synthesize a sample event (e.g., `instance.completed` with placeholder data), POST it with proper signature, and return the customer endpoint's response status + body to the admin UI.

#### Scenario: Test successful

- **WHEN** admin clicks Test on a working subscription
- **THEN** the modal shows 200 OK + customer's response body

#### Scenario: Test customer 5xx

- **WHEN** the customer endpoint returns 503 to test
- **THEN** the modal shows 503 + body; admin can debug
