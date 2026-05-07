# Tasks

## 1. Domain + persistence

- [ ] 1.1 Create WebhookSubscription, WebhookDelivery, DeliveryStatus enum
- [ ] 1.2 EF configs; migration `AddWebhooks`
- [ ] 1.3 Indexes: (TenantId, IsActive); (Status, NextAttemptAt); (WebhookSubscriptionId, EventTimestamp DESC)

## 2. Service + signer

- [ ] 2.1 Create `IWebhookService.cs / WebhookService.cs`
- [ ] 2.2 EnqueueAsync: find matching subscriptions (by spec_code + event_type); insert Delivery rows
- [ ] 2.3 Create `WebhookSigner.cs`: build HMAC-SHA256(secret, ts.body); format header
- [ ] 2.4 Tests

## 3. Dispatch worker

- [ ] 3.1 Create `WebhookDispatchWorker.cs`
- [ ] 3.2 Polling, retries (1m / 5m / 30m), abandonment after 3
- [ ] 3.3 HTTP POST with Content-Type application/json + X-BPM-Signature header
- [ ] 3.4 30s timeout per request
- [ ] 3.5 Handle 4xx (no retry except 429), 5xx (retry), network error (retry)

## 4. Runtime hook

- [ ] 4.1 Update ProcessRuntime: at every state event, call `IWebhookService.EnqueueAsync` with appropriate event_type + payload
- [ ] 4.2 Map runtime events to webhook event names
- [ ] 4.3 Tests: enqueueing creates Delivery rows for matching subscriptions

## 5. Admin endpoints

- [ ] 5.1 Create `WebhooksController.cs`:
  - GET / POST / PUT / DELETE on /api/admin/webhooks
  - GET /api/admin/webhooks/{id}/deliveries — recent deliveries
  - POST /api/admin/webhooks/{id}/test — sends a test event

## 6. Frontend admin UI

- [ ] 6.1 List screen: subscriptions table
- [ ] 6.2 Form: target URL, secret (auto-generated, regenerate button), event filter list, retry policy
- [ ] 6.3 Deliveries view per subscription
- [ ] 6.4 Test button + modal showing the response

## 7. End-to-end verification

- [ ] 7.1 `dotnet build` clean
- [ ] 7.2 Apply migration
- [ ] 7.3 Configure a subscription pointing to https://webhook.site/<test-id> for verification
- [ ] 7.4 Complete a LEAVE instance; verify webhook fires; check webhook.site received the POST with correct signature
- [ ] 7.5 Configure subscription with filter `instance.completed` only; verify other events don't fire to it
- [ ] 7.6 Test signature verification on customer side using Python / Node example
- [ ] 7.7 Test retry: point subscription at a 5xx-returning endpoint; verify 3 attempts then Abandoned
- [ ] 7.8 **Demo guard**: 9 mock-up forms NOT modified

## 8. Docs

- [ ] 8.1 Add `bpm-svc/docs/webhooks.md` with payload schemas per event type + signature verification example code

## 9. Commit

- [ ] 9.1 Commit in chunks
- [ ] 9.2 Push via GitKraken
