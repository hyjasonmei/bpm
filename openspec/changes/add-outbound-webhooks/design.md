# Design notes

## 1. Why HMAC, not OAuth / mTLS

For SME B2B integration, HMAC-SHA256 over a shared secret is:

- Simple to implement on the customer side (no token management)
- Battle-tested (Stripe, GitHub, Slack all use HMAC for webhooks)
- Replay-protected via timestamp window

mTLS is overkill for SME; OAuth requires customer to host an OAuth server. HMAC + HTTPS is the SME-friendly sweet spot.

## 2. Per-(subscription, event) Delivery rows

Why one row per delivery vs broadcast?

- Each (subscription, event) is independently retryable
- Failed deliveries to one subscription don't affect others
- Audit trail per-customer per-event

For 5 subscriptions × 100 events per day = 500 rows. Trivial.

## 3. Worker dispatch pattern (mirrors notification dispatcher)

The same pattern as `NotificationDispatchWorker`:

- 30s polling
- Concurrent dispatch (up to N parallel HTTP calls)
- Backoff on failure
- Terminal Failed after attempts ≥ 3

Single-instance for now. Multi-instance via lock table later.

## 4. Payload size cap

A reasonable limit: 100 KB per payload. ProcessInstances with massive form data (large repeater arrays) get truncated form_data with a `_truncated: true` flag and a `data_url: /api/processes/{id}` for the customer to fetch full state.

## 5. Test delivery in admin UI

When admin clicks "Test delivery" in /admin/webhooks:

- Synthesize a sample event (e.g., `instance.completed` with placeholder data)
- POST to the configured URL with proper signature
- Show response status + body in modal
- Useful for verifying customer's endpoint receives + parses correctly

## 6. Idempotency hint

Each delivery includes a unique `event_id` (ULID). Customers can store seen event_ids and skip duplicates on retry.

We don't enforce idempotency on our side (retry sends same event_id; customer chooses how to handle).

## 7. Open questions

- **Filtered events vs noisy "everything" mode**: filters are inclusive only (specify which to receive). v1 makes everything opt-in; if customer wants "all events", they create a subscription with empty filter array meaning "all spec codes, all triggers".
- **Delivery to multiple URLs from one subscription**: not in v1; admin creates one subscription per URL. Cleaner.
- **Encryption at rest of stored secrets**: same as SSO ClientSecret — IDataProtector + DPAPI.
- **Audit of webhook deliveries**: WebhookDelivery rows are themselves auditable; surface in /admin/audit via the unified audit reader.
