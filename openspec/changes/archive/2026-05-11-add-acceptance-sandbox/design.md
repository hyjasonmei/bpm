## Context

The product positions sandbox-driven UAT as selling point #2. Today the codebase has the *plumbing* (outbound gate + redirect log + sandbox toggle) but none of the *experience* (mailbox UI, time travel, state reset, server-side persona, bundle integration). This proposal welds the existing plumbing into a coherent acceptance loop.

The audience is **the customer's acceptance tester** — typically a single business user (HR / IT lead) who needs to verify that a flow works end-to-end before signing off on go-live. The current workaround at competing platforms is "borrow five colleagues and run through it together" or "ship it, hope, and patch in production." Both fail SMEs hard. Solo UAT is the differentiator.

## Goals / Non-Goals

**Goals**
- One acceptance tester can drive a flow from submit → all approvals → completion in one sitting, alone, in under 10 minutes
- Every notification / webhook the flow would have fired is observable: who would have received, what the body / payload would have been, when
- SLA breaches are testable in seconds, not days, via clock advance
- Re-running a scenario is one click; re-running 50 times to verify stability is feasible
- A bundle's `test-cases/*.json` can drive the whole loop unattended, asserting expected notifications / webhooks alongside expected node trace
- Sandbox state is impossible to confuse with production state (banner, badge, audit fields all distinct)

**Non-Goals**
- Real SMTP / SMS sending in sandbox mode (defeats the point — the whole value is *not* leaking)
- Replay-to-real-downstream of captured webhooks ("send this captured webhook for real now" — deferred; tester can manually re-trigger if needed in prod)
- Sandbox per-tenant isolation across a multi-tenant deployment (Phase B; current POC single-tenant)
- Persona switch in production (use existing impersonation feature; sandbox persona is sandbox-only)
- A separate sandbox database / second bpm-svc process (unnecessary complexity for POC; sandbox is a runtime *mode*, not a deployment)

## Decisions

### Decision: Sandbox is a tenant-level toggle, not per-user / per-session

Inheriting today's `TenantSettings.SandboxMode` boolean. Makes audit clear (one source of truth for "are we in UAT?") and avoids "user A in sandbox can see user B's real notifications" bugs. Trade-off: only one acceptance pass at a time per tenant. Acceptable for POC scale.

### Decision: Captured payload is stored in the database, not on disk

Storing in `SandboxCapturedMessage` (with `BodyHtml`, `PayloadJson` text columns) rather than dumping to a directory. Reasons: the mailbox UI needs SQL filters / sorting, retention is just a `DELETE WHERE CapturedAt < ...` cron, and developer story is simpler (one sqlite file = full UAT state). Cost: large notification bodies bloat DB; mitigation = TTL of 30 days on captured rows (configurable; default trims aggressively).

### Decision: Outbound gate captures BEFORE deciding the gate outcome

```csharp
async Task<GateOutcome<EmailMessage>> ApplyAsync(EmailMessage msg, ...) {
    if (_sandbox.IsActive) {
        await _capture.RecordEmailAsync(msg, ct);   // ALWAYS persist first
        return GateOutcome<EmailMessage>.Captured(msg); // workflow continues as if sent
    }
    // ... real-mode logic unchanged
}
```

Capture-then-decide means we have the full evidence even if the legacy `Rewrote`-to-fallback mode is later toggled on for hand-testing — the captured row is the source of truth, the fallback delivery is a side effect.

### Decision: New `Captured` outcome on `GateOutcome<T>`

Today `GateOutcome<T>` has `PassThrough`, `Rewrote`, `DropMessage`. Add `Captured`. From the workflow engine's perspective `Captured` is treated identically to `PassThrough` (continue), but downstream observers (notification dispatcher's success metric, webhook dispatcher's retry queue) check the flag and skip "delivery succeeded" telemetry that would skew sandbox metrics.

### Decision: Webhook gate fakes a 200 OK, never calls the URL

Even when the webhook URL is `https://localhost:9999/wat` (a fake), we don't HTTP it. The gate returns `Captured` immediately with `IsFakeOk = true`. The dispatcher treats it as success and the workflow proceeds. This avoids accidental real calls if the spec author put a real URL in by mistake during sandbox testing.

### Decision: Sandbox clock is per-tenant offset, not per-process

`SandboxClock` reads `TenantSettings.SandboxClockOffsetSeconds` on each `UtcNow` call (cached per request to avoid SQL on every nanosecond timestamp). When sandbox is OFF, the read short-circuits and returns real time. This survives bpm-svc restarts (offset persists in DB) so a tester who advanced the clock yesterday still sees yesterday's offset today — exactly what they want for resuming a long UAT session.

Trade-off: SQL read per request when sandbox is on. Acceptable: (a) sandbox is dev/UAT mode, not a prod hot path, (b) the tenant settings row is tiny and EF caches it.

### Decision: Clock advance triggers waker pass for time-sensitive workers

When sandbox advances 48h, the `SlaTimerJob` (running every 60s) wouldn't notice the jump until its next tick — 60s of wall-clock wait for the tester. So `POST /api/sandbox/clock/advance` synchronously calls `IBackgroundJobScheduler.KickAsync(["SlaTimer", "WebhookDispatch", "NotificationDispatch"])` to fire one immediate pass before responding. UAT feedback is instant.

### Decision: Persona switch issues a real JWT, server treats requester as the persona

Alternative considered: a per-request `X-Sandbox-As-User` header that controllers honor only when sandbox is on. Rejected: every controller would need awareness, and the audit trail would need a special "sandbox persona" extra field everywhere. Cleaner: issue a JWT with `sub` claim = persona id, plus `actual_actor_id` claim recording the real tester. Existing controllers read `sub` and write history rows naturally; the audit interceptor adds a `sandboxActualActor` field if present.

The new endpoint requires the requester to be admin (only admins can switch personas in sandbox), and refuses to issue when sandbox is OFF (defense in depth — even if the endpoint leaked into prod somehow, it can't issue persona JWTs there).

### Decision: Reset is hard-delete, not soft

Sandbox data is by definition disposable. Soft-delete would create endless detritus (every reset in 6 months of UAT = thousands of "deleted" rows the mailbox has to filter). Hard-delete keeps the sandbox table small and the experience snappy. The TenantSettings audit row records "reset performed" with the actor id and timestamp — that's the only durable trail of the reset itself.

### Decision: Bundle test-case driver runs in sandbox mode by definition

The `BundleReproducibilityRunner` from `add-spec-bundle-and-flow-library` flips sandbox ON before driving test-cases, then OFF again at end (or back to whatever it was). Test-cases assert against captured payloads — there is no other way. The "Install for runtime" flow gates on this assertion; if the bundle's test-cases assert "Mary gets email with subject containing '已核准'" and the captured email says '已通過', the install fails.

### Decision: Mailbox per-recipient filter uses the *intended* recipients, not the actual JWT user

A captured email's mailbox-filter view shows what each persona *would have* received in production. So Mary's "inbox" in the sandbox mailbox shows every email whose `IntendedRecipients` includes Mary's user id, regardless of what JWT the tester is currently using. This is what makes solo UAT possible: Jason-as-tester can read Mary's expected mail without becoming Mary.

## Risks / Trade-offs

**Risk**: A code path forgets to go through `IOutboundGate` and sends a real email from sandbox.
*Mitigation*: All `IEmailSender` / `IWebhookSender` / `ISmsSender` implementations MUST consume `IOutboundGate` first. Add a code review checklist + a guard test (boot bpm-svc with sandbox ON, exercise every notification trigger via integration tests, assert SMTP / HTTP outbound layers were never hit — use null backends with assertion that they were never called).

**Risk**: Sandbox toggle leaks into production by accident (admin clicks the wrong button).
*Mitigation*: SandboxBanner is impossible to miss (full-width amber bar, both UIs). Toggle requires admin role + confirmation modal. Audit log records every toggle with actor + timestamp + reason. Production deployment can ship with a `BPM_SANDBOX_TOGGLE_DISABLED=true` env var that hides the toggle endpoint entirely (`PUT /api/sandbox/status` returns 403).

**Risk**: Captured payloads accumulate and bloat the DB.
*Mitigation*: 30-day TTL on `SandboxCapturedMessage` (configurable per tenant). Daily cron runs `DELETE WHERE CapturedAt < now() - 30d`. The full-reset endpoint also hard-deletes everything.

**Risk**: Bundle test-case assertions are too brittle (small wording change in notification template breaks all bundle installs).
*Mitigation*: Default assertion is *substring* match on subject + recipient resolution match, not exact body equality. Bundles can opt into stricter regex or full-body match if their use case demands it. Document the trade-off in test-case format spec.

**Risk**: Time advance causes weird behavior in workflows that compare timestamps to `now()` mid-execution.
*Mitigation*: Time advance is monotonic only (forward). Backward time travel is forbidden — `POST /api/sandbox/clock/reset` snaps offset to 0 (which IS backwards, but full reset is a clean state, no in-flight comparison weirdness). Document: "advance the clock, run the scenario, reset, re-run."

**Risk**: Persona JWT leaks the `actual_actor_id` claim somewhere it shouldn't.
*Mitigation*: The JWT itself doesn't leave bpm-svc except as a Bearer token. The `actual_actor_id` is consumed by the audit interceptor at SaveChanges time and attached to history rows; it never round-trips back to the frontend in any response.

**Trade-off**: Sandbox mode is a global tenant flag, so two acceptance testers can't run independent UAT passes simultaneously. Acceptable for POC; if customers ever need parallel UAT, that's `add-multi-sandbox` later.

**Trade-off**: We deprecate `SandboxRedirect` and migrate to `SandboxCapturedMessage`. Old rows stay readable for one release, then get dropped. Anyone who was relying on `SandboxRedirect`-only data (probably nobody — POC stage) should migrate before that release.

## Migration Plan

1. Ship `SandboxCapturedMessage` entity + migration (additive, parallel to existing `SandboxRedirect`)
2. Update `IOutboundGate` implementations to write to BOTH tables for one release (redirect log for compatibility, captured message for new UI)
3. Wire Mailbox UI consuming the new table
4. Ship clock + persona + reset + bundle integration
5. One release later: drop `SandboxRedirect` writes, drop the entity in a follow-up cleanup PR
6. The capture-on-by-default semantics (`Captured` outcome replacing `Rewrote` as default) ships with the new gate writes — no special migration since UAT use is on a clean instance

## Open Questions

- Should the `Captured` outcome bubble up to the workflow engine's history events as a new `EventType.NotificationCaptured` (vs today's `EventType.NotificationDispatched`)? Probable answer: yes, otherwise post-UAT audit can't tell sandbox dispatches from real ones. Confirm with Jason before final implementation.
- Does the time advance offset apply to scheduled workers' "next-fire" computation, or do we re-derive from `IClock` everywhere? Probable answer: re-derive — workers always read `IClock.UtcNow`. Just verify no worker caches its next-fire timestamp internally.
- Should the persona switch be available in `bpm-ui` (end-user app) too, or only `bpm-admin-ui`? Probable answer: both — the demo flow above uses the end-user app for submit/approval. Need a sandbox-only mode in `bpm-ui`'s RoleSwitcher that lists bundle's sample-org users.
- For the bundle test-case `expectedNotifications[]` field, is the assertion key by `notificationId` (spec-defined identifier) or by `subject substring`? Probable answer: by `notificationId` (stable across template wording changes), with `subject` as an optional secondary check. Confirm.
