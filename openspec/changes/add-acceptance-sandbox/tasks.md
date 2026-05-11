# Tasks

## 1. Domain entities

- [x] 1.1 Create `bpm-svc/src/Domain/Entities/Sandbox/SandboxCapturedMessage.cs` (inherits AuditableEntity): Id, TenantCode, ProcessInstanceId?, TaskId?, Channel (Email/Webhook/Sms enum), IntendedRecipientsJson, Subject?, BodyHtml?, BodyText?, Url?, HeadersJson?, PayloadJson?, EventType?, Body?, CapturedAt, ReadByUserIdsJson, OriginatingNotificationId?, OriginatingWebhookSubscriptionId? (PR-J1; IsFakeOk deferred to PR-J2 with the gate change)
- [ ] 1.2 Extend `Domain/Entities/Sandbox/TenantSettings.cs` with `SandboxClockOffsetSeconds (long)` defaulting to 0
- [ ] 1.3 Create `Domain/Entities/Sandbox/SandboxClockEvent.cs` audit row: Id, TenantId, ActorUserId, OldOffsetSeconds, NewOffsetSeconds, ChangedAt, Action (Advance|Reset)

## 2. Persistence — EF configuration + migrations

- [x] 2.1 Create `Persistence/Configurations/Sandbox/SandboxCapturedMessageConfiguration.cs`: indexes `(TenantCode, CapturedAt DESC)`, `(TenantCode, Channel, CapturedAt DESC)`, `(ProcessInstanceId, CapturedAt DESC)`, `(EventType, CapturedAt DESC)` (PR-J1)
- [ ] 2.2 Create `Persistence/Configurations/Sandbox/SandboxClockEventConfiguration.cs`: index (TenantId, ChangedAt DESC)
- [x] 2.3 Add `DbSet<SandboxCapturedMessage>` to `AppDbContext` (PR-J1; `SandboxClockEvent` DbSet deferred until §1.3 lands)
- [x] 2.4 Generate migration: `dotnet ef migrations add AddSandboxCapturedMessages` (PR-J1; clock-event migration follows in a later PR)
- [x] 2.5 Apply locally; verified schema with `sqlite3 bpm-svc/src/Api/bpm.db ".schema SandboxCapturedMessages"` (PR-J1)

## 3. IOutboundGate — capture semantics

- [x] 3.1 Evolved `GateOutcome<T>` (PR-J2): added `Captured` (bool) + `CapturedMessageId` (Guid?) fields and `Capture(Guid id)` factory. Naming: chose `Capture` (verb) over `Captured_` to keep it idiomatic; `Captured` is the boolean flag on the record. The `IsFakeOk` flag was folded into `Captured` itself — xmldoc on `GateOutcome` makes the fake-200-OK contract explicit so consumers don't need a second flag.
- [x] 3.2-3.4 Capture writes happen inside `OutboundGate` directly (PR-J2). The dedicated `ISandboxCaptureRecorder` + `ProcessContext` extraction was deferred — instead, the originating-context fields (`OriginatingNotificationId`, `OriginatingWebhookSubscriptionId`, `ProcessInstanceId`, `TaskId`) were added as optional parameters on `EmailMessage`/`WebhookDelivery`/`SmsMessage` records. Future callers populate them; existing callers compile unchanged. Cleaner than threading a `ProcessContext` through every dispatcher signature.
- [x] 3.5 `OutboundGate` default path captures (PR-J2): writes a `SandboxCapturedMessage` row with full Subject/BodyHtml/BodyText (or Url/Headers/Payload, or Body) + IntendedRecipientsJson, returns `Capture(id)`.
- [x] 3.6 Legacy rewrite kept behind `SandboxConfigDto.LegacyRewriteEnabled` (default false) (PR-J2). When true, the gate ALSO writes the new capture row so the Mailbox stays consistent regardless of mode.
- [x] 3.7 Audit/AuditableEntity behaviour verified — `SandboxCapturedMessage` inherits `AuditableEntity` so the existing `AuditSaveChangesInterceptor` populates `CreatedAt`/`CreatedBy` automatically (covered by the round-trip tests added in PR-J1).
- [x] 3.8 Unit tests added: `OutboundGateCaptureTests` covers Email/Webhook/SMS capture in default sandbox mode, including correct `Captured = true` + `CapturedMessageId` round-trip and full payload persistence.
- [x] 3.9 Unit tests added: sandbox-off PassThrough verified (no capture row, no audit row); legacy-mode rewrite + drop both verified to ALWAYS write a capture row alongside their legacy outcome.

## 4. Sandbox-aware IClock

- [x] 4.1 Create `Persistence/Common/SandboxClock.cs` implementing `IClock` (decorator pattern over `SystemClock`); reads `TenantSettings.SandboxClockOffsetSeconds` and adds to `SystemClock.UtcNow`. Added `SandboxClockOffsetSeconds` typed column on `TenantSettings` with EF migration `AddSandboxClockOffset` (NOT NULL default 0). [PR-J3]
- [x] 4.2 Per-request cache: inject as `Scoped`; snapshot is cached per instance so repeated `UtcNow` reads in one request hit the DB once. [PR-J3]
- [x] 4.3 Updated `Persistence/DependencyInjection.cs` so `IClock` resolves to `SandboxClock` (Scoped) wrapping `SystemClock` (Singleton). Side-effect: `CelNetExpressionEvaluator` registration moved Singleton → Scoped to satisfy DI scope validation. [PR-J3]
- [x] 4.4 Created `Application/Sandbox/ISandboxClockService.cs` with `GetAsync`, `AdvanceAsync(days, hours, minutes, seconds, ct)`, `ResetAsync(ct)`. DTO is `SandboxClockDto(RealNow, SandboxNow, OffsetSeconds, SandboxOn)`. [PR-J3]
- [x] 4.5 Implemented `Persistence/Sandbox/SandboxClockService.cs`: writes new offset to `TenantSettings`, throws `SandboxOffException` when sandbox is off. Negative deltas allowed (clarified vs original spec — useful for nudging back). [PR-J3]
- [~] 4.6 Created `Application/Common/Abstractions/IScheduledJobKicker.cs` (renamed from `IBackgroundJobScheduler` per PR-J3 prompt) with `Task KickAsync(CancellationToken)` — single method, no job-name array since v1 has no workers yet. Default `NoOpScheduledJobKicker` registered Scoped; SLA / webhook proposals can replace it. [PR-J3]
- [x] 4.7 `SandboxClockService.AdvanceAsync` calls `IScheduledJobKicker.KickAsync` after offset update + logs an Info-level "would trigger Y scheduled jobs" line. [PR-J3]
- [~] 4.8 Audit `SandboxClockEvent` row skipped for v1 — Info-level log only, per PR-J3 prompt. [PR-J3]
- [x] 4.9 Tests: `SandboxClockTests` (7 cases) + `SandboxClockControllerTests` (9 cases) cover sandbox off pass-through, on with various offsets, snapshot cache, invalidation, GET/advance/reset round-trip, advance/reset 400 when off, multi-call accumulation, days+hours+minutes+seconds sum, negative delta. 139 → 155 tests. [PR-J3]

## 5. Sandbox state reset

- [ ] 5.1 Add `IResetService.ResetInstanceAsync(Guid instanceId, Guid actor, CancellationToken)` to `Application/Sandbox/`: hard-deletes ProcessInstance + ProcessTasks + TaskHistory + SandboxCapturedMessages for that instance; refuses if sandbox is OFF
- [ ] 5.2 Add `IResetService.ResetAllAsync(Guid actor, CancellationToken)`: hard-deletes ALL ProcessInstances, ProcessTasks, TaskHistory, SandboxCapturedMessages, SandboxClockEvents for current tenant; refuses if sandbox is OFF; refuses if non-admin
- [ ] 5.3 Both methods write a `SandboxClockEvent`-style audit (or extend audit with ResetPerformed event type) for the reset itself
- [ ] 5.4 Integration test: submit instance, capture some mail, reset instance → verify all related rows gone, sandbox toggle still on, other instances untouched
- [ ] 5.5 Integration test: ResetAllAsync wipes everything but spec / org / bundle data is preserved

## 6. Sandbox persona switch

- [ ] 6.1 Add `POST /api/sandbox/persona` endpoint in `Api/Sandbox/SandboxController.cs`; body `{ userId: Guid }`; auth `[Authorize(Roles = "admin")]`; refuses 400 if sandbox OFF; returns new JWT
- [ ] 6.2 Extend `JwtTokenService` with `IssueSandboxPersonaTokenAsync(Guid personaUserId, Guid actualActorUserId, CancellationToken)`: builds JWT with `sub` = persona id, `actual_actor_id` claim = real tester id, `sandbox_actor` = true claim
- [ ] 6.3 Create `Application/Common/Abstractions/ISandboxActorContext.cs`: `Guid? ActualActorUserId { get; }`; populated by middleware reading `actual_actor_id` claim
- [ ] 6.4 Implement `Api/Common/SandboxActorContextMiddleware.cs` reading the claim into request scope
- [ ] 6.5 Extend `AuditSaveChangesInterceptor`: when writing audit rows in sandbox mode, also persist `SandboxActualActor` (Guid?) field; nullable because non-sandbox writes won't have it
- [ ] 6.6 Add `SandboxActualActor` (Guid?) column to relevant audit tables (TaskHistory, AuditEvent if present); migration
- [ ] 6.7 Unit test: persona endpoint refuses when sandbox off (400)
- [ ] 6.8 Integration test: admin switches to Mary persona, submits a task → TaskHistory row has `ActorUserId = Mary`, `SandboxActualActor = admin`

## 7. Mailbox API

- [ ] 7.1 `GET /api/sandbox/captured?channel=email|webhook|sms&recipientUserId=&processInstanceId=&unread=` — list with filters
- [ ] 7.2 `GET /api/sandbox/captured/{id}` — full payload (HTML body, headers, JSON, etc.)
- [ ] 7.3 `POST /api/sandbox/captured/{id}/read` — mark as read by current user (appends to ReadByUserIdsJson)
- [ ] 7.4 `GET /api/sandbox/captured/unread-count?byChannel=true` — count summary, used by SandboxBanner counter
- [ ] 7.5 All endpoints `[Authorize]`; refuse if sandbox off (so unread-count returns 0 in prod, no DB hit)
- [ ] 7.6 Add `SandboxConfigDto.CaptureRetentionDays` (default 30); daily cron deletes captured rows older than retention

## 8. Clock + reset API

- [x] 8.1 `POST /api/sandbox/clock/advance` body `{ days?, hours?, minutes?, seconds? }` — calls `SandboxClockService.AdvanceAsync`; returns 200 with new state, 400 `{ error: "sandbox_off" }` when sandbox is off. [PR-J3]
- [x] 8.2 `POST /api/sandbox/clock/reset` — clears offset to 0; same 400 path when off. [PR-J3]
- [x] 8.3 `GET /api/sandbox/clock` — returns `{ realNow, sandboxNow, offsetSeconds, sandboxOn }`. `lastChangedAt`/`lastChangedByUserId` deferred (no audit table in v1 per §4.8). [PR-J3]
- [ ] 8.4 `POST /api/sandbox/reset/instance/{id}` — calls `IResetService.ResetInstanceAsync`
- [ ] 8.5 `POST /api/sandbox/reset/all` — calls `ResetAllAsync`; admin-only
- [~] 8.6 Clock advance/reset enforce `[Authorize(Roles = "admin")]` and refuse with 400 sandbox_off; remaining admin-mutating endpoints (reset, persona) tracked in PR-J4. [PR-J3]

## 9. Frontend (`bpm-admin-ui`) — Sandbox Mailbox screen

- [ ] 9.1 Add `sandbox-mailbox` to `AdminScreen` union in `components/AdminLayout.tsx`; nav entry with `Mail` icon
- [ ] 9.2 Create `screens/sandbox/SandboxMailbox.tsx` with tabs: Mail / Webhooks / SMS / Clock
- [ ] 9.3 Mail tab: list of captured emails (recipient filter, unread-only toggle, clear-all action); click row → modal with rendered HTML body + headers + intended recipients + originating notification deep-link
- [ ] 9.4 Webhooks tab: list of captured deliveries (filter by event type or subscription); click row → modal with URL / headers / pretty-printed payload + "Fake 200 OK" badge
- [ ] 9.5 SMS tab: same shape as Mail (low priority — placeholder UI is fine for v1)
- [ ] 9.6 Clock tab: current real time / sandbox time / offset display; quick-advance buttons (+1h / +1d / +1w / +1mo); precise input form; reset button; recent advance/reset audit log
- [ ] 9.7 Add captured-count badge to AdminLayout's existing nav next to "Sandbox Mailbox" entry — polls `/api/sandbox/captured/unread-count` every 10s when sandbox on
- [ ] 9.8 Update `components/SandboxBanner.tsx` in `bpm-admin-ui` to show "captured: N mail / M webhook / clock +Xd Yh" live counter

## 10. Frontend (`bpm-ui`) — RoleSwitcher sandbox mode + banner

- [ ] 10.1 Update `bpm-ui/src/components/RoleSwitcher.tsx`: when sandbox is on (poll `/api/sandbox/status`), fetch sample-org users from currently-installed bundle (or from `/api/sandbox/personas`) and list as switchable options instead of hard-coded admin/manager/employee
- [ ] 10.2 On select: `POST /api/sandbox/persona` with chosen userId; replace stored JWT; refetch app state
- [ ] 10.3 Show "now acting as <persona name> (sandbox)" pill in top bar
- [ ] 10.4 Update `bpm-ui/src/components/SandboxBanner.tsx` to mirror the admin banner's captured-count + clock-offset display
- [ ] 10.5 New `Sandbox Mailbox` link in end-user nav (visible only when sandbox on) — opens read-only mailbox view (no admin actions)

## 11. Bundle integration (couples with `add-spec-bundle-and-flow-library`)

- [ ] 11.1 Extend test-case JSON schema with optional `expectedNotifications[]` and `expectedWebhooks[]`; document in `bpm-spec-bundle/spec.md` (ADD a Modified Requirement entry)
- [ ] 11.2 Update `BundleReproducibilityRunner` (specced in add-spec-bundle): before running test-cases, save current sandbox state (on/off + offset), flip sandbox ON, run cases, restore state at end
- [ ] 11.3 After each test-case completes, query `SandboxCapturedMessage` for that instance's notifications + webhooks; compare against expected (substring match for subject, structural diff for webhook payload, recipient resolution match)
- [ ] 11.4 Extend `ReproReport.CaseResult` with `NotificationAssertions[]` and `WebhookAssertions[]` arrays — each entry: `expected`, `actual`, `passed`, `diff`
- [ ] 11.5 The repro check fails (Status = Fail) if any notification or webhook assertion fails, even if node trace passes
- [ ] 11.6 Integration test: bundle with one test-case asserting `expectedNotifications: [{ notificationId: "manager_review_requested", subjectContains: "請假" }]` → install via `mode=install` → repro runner asserts captured email matches, OverallStatus = Pass

## 12. SandboxBanner everywhere + audit hardening

- [ ] 12.1 Audit log entry on every sandbox toggle (already in `ISandboxService.SetStatusAsync` — verify presence)
- [ ] 12.2 Audit log entry on every sandbox persona issuance (recorded by JwtTokenService)
- [ ] 12.3 Audit log entry on every reset (instance / all) — already covered by §5.3 above
- [ ] 12.4 Add `BPM_SANDBOX_TOGGLE_DISABLED=true` env var support in `Api/Program.cs`: when true, `PUT /api/sandbox/status` returns 403 (defense in depth for prod deploys)
- [ ] 12.5 Document in `docs/sandbox-acceptance-loop.md`: the demo path (toggle on → install bundle → submit → advance time → check mailbox → switch persona → approve → reset)

## 13. Cleanup of legacy `SandboxRedirect`

- [ ] 13.1 One release after Mailbox UI consumes `SandboxCapturedMessage`, drop writes to `SandboxRedirect` from outbound gate
- [ ] 13.2 Migrate any consumers of `GET /api/sandbox/redirects` to `GET /api/sandbox/captured?channel=email`
- [ ] 13.3 Drop `SandboxRedirect` entity + table in a follow-up cleanup PR

## 14. End-to-end acceptance test

- [ ] 14.1 Write `bpm-svc/test/Integration/SandboxAcceptanceLoopTests.cs` exercising the full demo path: toggle on, submit instance via Wilson persona, advance clock 48h, verify SLA-warning email captured, switch persona to Mary, approve, verify approved email + customer-system webhook captured, reset, re-run identically — all via API calls, no UI
- [ ] 14.2 Add to CI as a slower "sandbox" suite
- [ ] 14.3 Document the runbook in `docs/sandbox-acceptance-loop.md` so the partner can demo it directly to candidate customers
