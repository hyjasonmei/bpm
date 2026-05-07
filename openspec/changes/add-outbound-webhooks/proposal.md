## Why

Customers need flow events forwarded into their downstream systems:

- "When a leave is approved, push to our HRIS to deduct vacation balance"
- "When a purchase is approved, create a PO in our ERP"
- "When an invoice is processed, post to our accounting system"
- "When a new hire onboards, provision the AD account"

Without outbound integration, the BPM platform is a silo. This change ships outbound webhooks: per-tenant configurable HTTP POST notifications when ProcessInstance events occur.

## What Changes

### Webhook capability (NEW `bpm-webhooks`)

**Entity** — `WebhookSubscription`:

- `Id`, `TenantId`, `Name` (admin-friendly label)
- `TargetUrl` (HTTPS only in prod; HTTP allowed only in dev)
- `Secret` (random; used to sign payloads via HMAC-SHA256)
- `EventFilters[]` — list of `(spec_code?, trigger)` pairs the subscription cares about; trigger one of: `instance.started`, `instance.completed`, `instance.cancelled`, `task.spawned`, `task.completed`, `approval.approved`, `approval.rejected`
- `IsActive`
- `RetryPolicyJson` — optional override of default retry; defaults to 3 retries with exponential backoff

**Entity** — `WebhookDelivery`:

- `Id`, `TenantId`, `WebhookSubscriptionId`, `EventType`, `EventTimestamp`, `PayloadJson`, `Status` (Queued / Sent / Failed / Abandoned), `Attempts`, `LastAttemptAt`, `NextAttemptAt`, `LastResponseStatus`, `LastResponseBody` (truncated)

**Service** `IWebhookService`:

- `EnqueueAsync(eventType, payload, contextTenantId)` — called by ProcessRuntime hooks; finds matching subscriptions; creates Delivery rows
- `DispatchPendingAsync(ct)` — worker pulls Queued / Failed-with-retry-due rows; performs HTTP POST; updates status

**Worker** `WebhookDispatchWorker` (BackgroundService):

- Polls every 30s
- Picks Queued + (Failed with attempts < 3 and NextAttemptAt <= now)
- Backoff: 1 min, 5 min, 30 min
- Cap: 3 retries; abandoned after

### Payload shape

Standard JSON envelope:

```json
{
  "event_type": "instance.completed",
  "event_id": "01HX...",                 // ULID
  "tenant_id": "...",
  "timestamp": "2026-05-08T14:23:00Z",
  "data": {
    "instance_id": "...",
    "spec_code": "LEAVE",
    "spec_version": 3,
    "initiator_user_id": "...",
    "initiator_email": "wilson@x.com",
    "form_data": {...},
    "completed_at": "..."
  }
}
```

Per-event `data` schema documented per event type.

### Signature header

Every POST includes `X-BPM-Signature: t=<unix_ts>,v1=<hex_hmac>` where the HMAC is HMAC-SHA256(`secret`, `<unix_ts>.<payload_body>`). Customers verify this to ensure authenticity. Replay protection: timestamp checked within ±5 minutes.

### Customer endpoint contract

Customer's endpoint:

- MUST respond within 30 seconds
- MUST return 2xx for "delivery acknowledged"
- 4xx (other than 429) → no retry (customer's bug)
- 429 + Retry-After → respect the Retry-After header
- 5xx / timeout / network error → retry per policy

### Configuration UI in System Admin

`/admin/webhooks`:

- List subscriptions; create / edit / disable
- Test delivery button (sends a test event to the URL)
- Recent deliveries view (per subscription)
- Failed-delivery panel (admin can manually retry)

### Out of scope (future changes)

- Inbound webhooks (customer's system pushing events INTO BPM)
- AsyncAPI spec generation
- Subscription-level rate limiting
- Replay (re-delivery of historical events; admin UI shows; bulk replay later)
- Webhook signing rotation (single key for v1; rotation later)
- Pre-shared SSL certificates / mTLS

## Capabilities

### New Capabilities

- `bpm-webhooks` — WebhookSubscription + WebhookDelivery entities, IWebhookService, WebhookDispatchWorker, HMAC signing, retry policy, event filtering, admin config UI consumed by `/admin/webhooks`.

### Modified Capabilities

- `bpm-process-runtime` — at every state event (instance start / complete / cancel / task spawn / submit / approve / reject), invoke `IWebhookService.EnqueueAsync` so subscriptions matching the (spec_code, trigger) filter receive the payload.

## Impact

- **bpm-svc/src/Domain/Entities/Webhook/WebhookSubscription.cs / WebhookDelivery.cs**: new entities
- **bpm-svc/src/Application/Webhooks/IWebhookService.cs / WebhookService.cs**: orchestration
- **bpm-svc/src/Application/Webhooks/WebhookSigner.cs**: HMAC builder
- **bpm-svc/src/Infrastructure/Webhooks/WebhookDispatchWorker.cs**: BackgroundService
- **bpm-svc/src/Persistence/Configurations/Webhook/**: EF + migration `AddWebhooks`
- **bpm-svc/src/Application/Process/Runtime/ProcessRuntime.cs**: invoke EnqueueAsync at state events
- **bpm-svc/src/Api/Webhooks/WebhooksController.cs**: 6 admin endpoints
- **bpm-ui/src/screens/admin/webhooks/**: list / detail / form / deliveries view
- **NuGet**: `System.Net.Http.Json` (likely already present)
- **DB migration**: 2 new tables
- **Demo guard**: 9 mock-up forms NOT modified
