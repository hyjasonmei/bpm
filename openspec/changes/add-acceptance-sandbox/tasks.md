# Tasks

## 1. Domain entities

- [ ] 1.1 Create `bpm-svc/src/Domain/Entities/Sandbox/SandboxCapturedMessage.cs` (inherits AuditableEntity): Id, TenantId, ProcessInstanceId?, TaskId?, Channel (Email/Webhook/Sms enum), IntendedRecipientsJson, Subject?, BodyHtml?, BodyText?, Url?, HeadersJson?, PayloadJson?, EventType?, Body?, CapturedAt, ReadByUserIdsJson, OriginatingNotificationId?, OriginatingWebhookSubscriptionId?, IsFakeOk
- [ ] 1.2 Extend `Domain/Entities/Sandbox/TenantSettings.cs` with `SandboxClockOffsetSeconds (long)` defaulting to 0
- [ ] 1.3 Create `Domain/Entities/Sandbox/SandboxClockEvent.cs` audit row: Id, TenantId, ActorUserId, OldOffsetSeconds, NewOffsetSeconds, ChangedAt, Action (Advance|Reset)

## 2. Persistence — EF configuration + migrations

- [ ] 2.1 Create `Persistence/Configurations/Sandbox/SandboxCapturedMessageConfiguration.cs`: index (TenantId, CapturedAt DESC), (ProcessInstanceId, CapturedAt), (Channel, CapturedAt)
- [ ] 2.2 Create `Persistence/Configurations/Sandbox/SandboxClockEventConfiguration.cs`: index (TenantId, ChangedAt DESC)
- [ ] 2.3 Add `DbSet<SandboxCapturedMessage>` and `DbSet<SandboxClockEvent>` to `AppDbContext`
- [ ] 2.4 Generate migration: `dotnet ef migrations add AddSandboxCaptureAndClock`
- [ ] 2.5 Apply locally; verify schema with `sqlite3 bpm-svc/src/Api/bpm.db .schema "SandboxCapturedMessages"` and similar

## 3. IOutboundGate — capture semantics

- [ ] 3.1 Add `Captured` factory method on `GateOutcome<T>`: `public static GateOutcome<T> Captured(T msg) => new(msg, Dropped: false, Rewritten: false) { IsCaptured = true }`; add `IsCaptured` (bool) and `IsFakeOk` (bool) flags
- [ ] 3.2 Create `Application/Sandbox/ISandboxCaptureRecorder.cs`: `Task RecordEmailAsync(EmailMessage, ProcessContext?, CancellationToken)`, `Task RecordWebhookAsync(WebhookDelivery, ...)`, `Task RecordSmsAsync(SmsMessage, ...)`
- [ ] 3.3 Create `ProcessContext.cs` record: `Guid? ProcessInstanceId`, `Guid? TaskId`, `Guid[] IntendedRecipientUserIds`, `string? OriginatingNotificationId`, `Guid? OriginatingWebhookSubscriptionId`
- [ ] 3.4 Implement `SandboxCaptureRecorder.cs`: writes `SandboxCapturedMessage` row, populates IntendedRecipientsJson from ProcessContext, sets IsFakeOk for webhooks
- [ ] 3.5 Update existing `IOutboundGate` implementation in `Application/Sandbox/`: when `_sandboxStatus.IsActive`, call recorder first, then return `Captured(msg)` instead of `Rewrote` / `DropMessage` as default
- [ ] 3.6 Keep legacy `Rewrote`-to-fallback as opt-in via `TenantSettings.SandboxConfigJson.LegacyRewriteEnabled` flag (default false)
- [ ] 3.7 Audit: add SaveChanges interceptor entry for `SandboxCapturedMessage` writes — they already audit via AuditableEntity, just verify CreatedBy / TenantId populated correctly
- [ ] 3.8 Unit test: gate in sandbox mode → Captured outcome with FakeOk for webhook, recorder called once with full payload
- [ ] 3.9 Unit test: gate in non-sandbox mode → recorder NOT called, real-mode logic unchanged

## 4. Sandbox-aware IClock

- [ ] 4.1 Create `Application/Common/Services/SandboxClock.cs` implementing `IClock` (decorator pattern over `SystemClock`); reads `_sandboxStatus.GetClockOffsetSeconds()` and adds to `SystemClock.UtcNow`
- [ ] 4.2 Per-request cache: inject as `Scoped`; cache the offset read for the lifetime of one HTTP request to avoid SQL per timestamp call
- [ ] 4.3 Update `Persistence/DependencyInjection.cs` so `IClock` resolves to `SandboxClock` wrapping the existing `SystemClock` registration
- [ ] 4.4 Create `Application/Sandbox/ISandboxClockService.cs`: `AdvanceAsync(TimeSpan delta, Guid actor, CancellationToken)`, `ResetAsync(Guid actor, CancellationToken)`, `GetCurrentAsync(CancellationToken) -> (DateTimeOffset realNow, DateTimeOffset sandboxNow, long offsetSeconds)`
- [ ] 4.5 Implement `SandboxClockService.cs`: writes new offset to `TenantSettings`, writes `SandboxClockEvent` audit row in same transaction, refuses negative deltas (forbid backward time)
- [ ] 4.6 Create `Application/Common/Abstractions/IBackgroundJobScheduler.cs` (if not present): `Task KickAsync(string[] jobNames, CancellationToken)` triggers an immediate pass of named workers
- [ ] 4.7 In `SandboxClockService.AdvanceAsync` after successful offset update, call `KickAsync(["SlaTimer", "WebhookDispatch", "NotificationDispatch"])` so testers see consequences without waiting for the next worker tick
- [ ] 4.8 Unit test: SandboxClock returns offset-adjusted time when sandbox on; pass-through when off
- [ ] 4.9 Unit test: AdvanceAsync rejects negative delta; ResetAsync clears offset to 0 and writes audit row

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

- [ ] 8.1 `POST /api/sandbox/clock/advance` body `{ days?, hours?, minutes? }` — calls `SandboxClockService.AdvanceAsync`; returns new offset + new sandboxNow
- [ ] 8.2 `POST /api/sandbox/clock/reset` — clears offset to 0
- [ ] 8.3 `GET /api/sandbox/clock` — returns `{ realNow, sandboxNow, offsetSeconds, lastChangedAt, lastChangedByUserId }`
- [ ] 8.4 `POST /api/sandbox/reset/instance/{id}` — calls `IResetService.ResetInstanceAsync`
- [ ] 8.5 `POST /api/sandbox/reset/all` — calls `ResetAllAsync`; admin-only
- [ ] 8.6 All admin-mutating endpoints require `[Authorize(Roles = "admin")]` and refuse when sandbox off

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
