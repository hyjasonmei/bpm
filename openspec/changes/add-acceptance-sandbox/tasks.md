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

- [x] 5.1 `IResetService.ResetInstanceAsync` lives in `Application/Sandbox/IResetService.cs` (PR-J4); impl in `Persistence/Sandbox/ResetService.cs` uses `ExecuteDeleteAsync` so the append-only TaskHistory guard is bypassed without needing an `IResetContext` flag — pure EF, Postgres-portable.
- [x] 5.2 `ResetAllAsync` deletes all default-tenant ProcessInstances/Tasks/History/CapturedMessages and resets the clock offset to 0 (sandbox-on toggle preserved). Admin-only enforced at the controller via `[Authorize(Roles = "admin")]`. (PR-J4)
- [~] 5.3 Audit row deferred for v1, mirroring §4.8's clock-audit decision — Info-level log only (`logger.LogInformation` in `ResetService`). SandboxClockEvent / ResetEvent table will land if/when an auditor asks. (PR-J4)
- [x] 5.4 `ResetServiceTests.ResetInstanceAsync_does_not_touch_other_instances` covers the per-instance scoping invariant; `ResetServiceTests.ResetInstanceAsync_deletes_all_related_rows_and_returns_counts` proves the TaskHistory rows actually disappear (the interceptor-bypass sanity check the prompt asked for). (PR-J4)
- [x] 5.5 `ResetServiceTests.ResetAllAsync_does_not_delete_specs_or_tenant_settings_row` covers preservation; `ResetAllAsync_wipes_all_default_tenant_rows_and_resets_clock_offset` covers the wipe + offset-reset. (PR-J4)

## 6. Sandbox persona switch

- [x] 6.1 `POST /api/sandbox/persona` added to `SandboxController` (PR-J4): admin-only, refuses 400 with `{ error: "sandbox_off" }` if sandbox off, 404 if user missing, returns `{ token, expiresAt, persona, actualActor }`.
- [x] 6.2 `JwtTokenService.IssueSandboxPersonaToken` mints an 8h token with `sub = personaUserId`, `actual_actor_id`, `actual_actor_email`, `sandbox_actor=true`, plus the persona's role codes. Sync (no async needed — DB lookup happens in the controller). (PR-J4)
- [x] 6.3 `ISandboxActorContext` lives in `Application/Common/Abstractions`; `SystemSandboxActor` is the no-op default registered via `AddApplication`. (PR-J4)
- [~] 6.4 Resolved via DI rather than middleware: `Api/Common/HttpContextSandboxActor` reads claims through `IHttpContextAccessor` and is registered in `Program.cs`. Middleware would buy nothing here — the existing `HttpContextCurrentUser` follows the same pattern. (PR-J4)
- [x] 6.5 `AuditSaveChangesInterceptor` extended to take an optional `ISandboxActorContext`; when `IsSandboxActor` it stamps `SandboxActualActor` on every newly-inserted ProcessTask + TaskHistory. Existing 2-arg ctor calls in tests stay source-compatible (default falls back to `SystemSandboxActor`). (PR-J4)
- [~] 6.6 No migration needed: `SandboxActualActor` (Guid?) was already declared on `ProcessTask.cs` and `TaskHistory.cs` in PR-B (verified by reading both files). (PR-J4)
- [x] 6.7 `PersonaSwitchTests.SwitchPersona_when_sandbox_off_returns_400_sandbox_off`. (PR-J4)
- [x] 6.8 `SandboxActorStampingTests` covers the audit stamping invariant directly against the interceptor (3 tests: stamps on ProcessTask insert, stamps on TaskHistory insert, leaves null when not in sandbox-actor mode). End-to-end "admin → Mary submits → TaskHistory row" wiring will land with the SLA / runtime-uses-IClock work; the interceptor logic itself is now proven. (PR-J4)

## 7. Mailbox API

- [x] 7.1 `GET /api/sandbox/captured` with `channel` / `recipientUserId` / `processInstanceId` / `unread` / `limit` (max 200, default 50) implemented in `SandboxController.ListCaptured`. JSON-text filter uses `EF.Functions.Like` on `IntendedRecipientsJson` / `ReadByUserIdsJson` so the same query compiles on SQLite + Postgres (no `json_extract`). v1 acceptable; real JSON-aware filter lives in `add-real-search`. (PR-J4)
- [x] 7.2 `GET /api/sandbox/captured/{id:guid}` returns `CapturedMessageDetailDto` with all body / headers / payload fields surfaced. (PR-J4)
- [x] 7.3 `POST /api/sandbox/captured/{id:guid}/read` — idempotent append to `ReadByUserIdsJson`; returns `{ id, readByMe: true }`. (PR-J4)
- [x] 7.4 `GET /api/sandbox/captured/unread-count` returns `{ total, byChannel: { Email, Webhook, Sms } }`. (PR-J4)
- [x] 7.5 List + get + mark-read endpoints return 403 `{ error: "sandbox_off" }` when sandbox off; unread-count returns silent zero counts WITHOUT a DB hit (per the §7 spec). (PR-J4)
- [~] 7.6 `SandboxConfigDto.CaptureRetentionDays` field added (default 30). The daily cron worker is deferred until `add-sla-timer-escalation` lands (no scheduled-job infra in v1). (PR-J4)

## 8. Clock + reset API

- [x] 8.1 `POST /api/sandbox/clock/advance` body `{ days?, hours?, minutes?, seconds? }` — calls `SandboxClockService.AdvanceAsync`; returns 200 with new state, 400 `{ error: "sandbox_off" }` when sandbox is off. [PR-J3]
- [x] 8.2 `POST /api/sandbox/clock/reset` — clears offset to 0; same 400 path when off. [PR-J3]
- [x] 8.3 `GET /api/sandbox/clock` — returns `{ realNow, sandboxNow, offsetSeconds, sandboxOn }`. `lastChangedAt`/`lastChangedByUserId` deferred (no audit table in v1 per §4.8). [PR-J3]
- [x] 8.4 `POST /api/sandbox/reset/instance/{id:guid}` admin-only — returns `ResetSummary { InstancesDeleted, TasksDeleted, HistoryRowsDeleted, CapturedMessagesDeleted }`; 400 sandbox_off path covered. (PR-J4)
- [x] 8.5 `POST /api/sandbox/reset/all` admin-only — returns `ResetSummary`; clock offset reset + sandbox toggle preserved. (PR-J4)
- [~] 8.6 Clock advance/reset enforce `[Authorize(Roles = "admin")]` and refuse with 400 sandbox_off; remaining admin-mutating endpoints (reset, persona) tracked in PR-J4. [PR-J3]

## 9. Frontend (`bpm-admin-ui`) — Sandbox Mailbox screen

- [x] 9.1 Added `sandbox-mailbox` to `AdminScreen` union in `components/AdminLayout.tsx`; nav entry uses `Mail` icon, slotted between Site Settings and Users & Roles. (PR-J5)
- [x] 9.2 Created `screens/sandbox/SandboxMailbox.tsx` with sidebar tabs Mail / Webhooks / SMS / Clock following the BundleDetail (PR-I6) tab convention. (PR-J5)
- [x] 9.3 Mail tab: list (CapturedAt relative + Subject + unread dot), unread-only toggle, free-text filter (subject/event substring — recipient dropdown deferred since recipients live on the detail row, not the summary), refresh, mark-all-as-read; click row opens a modal with sandboxed HTML body + intended recipients + originating notification id (deep-link is text-only per the prompt). (PR-J5)
- [x] 9.4 Webhooks tab: same scaffold, columns are Event type + Subject; modal shows URL / headers / pretty-printed JSON payload + "Fake 200 OK" badge + originating subscription id. (PR-J5)
- [x] 9.5 SMS tab: minimal v1 placeholder — same shape, single Body column. (PR-J5)
- [x] 9.6 Clock tab: real / sandbox / offset display, quick-advance buttons (+1h / +1d / +1w / +1mo), precise days/hours/minutes/seconds form, reset button (with confirm). (PR-J5)
- [~] 9.6 audit-log sub-bullet: `(no audit log in v1)` text rendered in the Reset card — mirrors §4.8 / §5.3 deferral. (PR-J5)
- [x] 9.7 Captured-count badge wired via new `hooks/useSandboxUnreadCount.ts` — polls `/api/sandbox/captured/unread-count` every 10s when sandbox is on, renders as a rose-500 pill on the Sandbox Mailbox nav entry. (PR-J5)
- [x] 9.8 `SandboxBanner` in `bpm-admin-ui` now polls captured + clock every 10s and renders `SANDBOX MODE ACTIVE — captured: N mail / M webhook · clock +Xd Yh`. (PR-J5)

## 10. Frontend (`bpm-ui`) — RoleSwitcher sandbox mode + banner

- [x] 10.1 `bpm-ui/src/components/RoleSwitcher.tsx` polls `/api/sandbox/status` on mount + on dropdown open; when on, fetches `/api/sandbox/personas` (new admin-controller endpoint) and renders them under a "Sandbox personas" divider below the original PERSONAS list. (PR-J5)
- [x] 10.2 On select: `POST /api/sandbox/persona`, `setJwt(token)`, then `window.location.reload()` so all screens (Home / NotificationsMenu / etc.) refetch under the new persona — `useActivePersona` only handles the dev-login persona codes, not arbitrary user ids. (PR-J5)
- [x] 10.3 "Acting as <persona> (sandbox)" amber pill rendered next to the persona dropdown trigger when the JWT carries `sandbox_actor=true`. New `isSandboxActor` helper in `lib/jwt.ts`. (PR-J5)
- [x] 10.4 `bpm-ui/src/components/SandboxBanner.tsx` mirrors the admin banner — same captured/clock poller, same string format. (PR-J5)
- [x] 10.5 New `screens/SandboxMailbox.tsx` (read-only) wired into `App.tsx` + `AppLayout.tsx`. Nav entry only visible when `sandboxOn` (polled every 30s). (PR-J5)

## 11. Bundle integration (couples with `add-spec-bundle-and-flow-library`)

- [ ] 11.1 Extend test-case JSON schema with optional `expectedNotifications[]` and `expectedWebhooks[]`; document in `bpm-spec-bundle/spec.md` (ADD a Modified Requirement entry)
- [ ] 11.2 Update `BundleReproducibilityRunner` (specced in add-spec-bundle): before running test-cases, save current sandbox state (on/off + offset), flip sandbox ON, run cases, restore state at end
- [ ] 11.3 After each test-case completes, query `SandboxCapturedMessage` for that instance's notifications + webhooks; compare against expected (substring match for subject, structural diff for webhook payload, recipient resolution match)
- [ ] 11.4 Extend `ReproReport.CaseResult` with `NotificationAssertions[]` and `WebhookAssertions[]` arrays — each entry: `expected`, `actual`, `passed`, `diff`
- [ ] 11.5 The repro check fails (Status = Fail) if any notification or webhook assertion fails, even if node trace passes
- [ ] 11.6 Integration test: bundle with one test-case asserting `expectedNotifications: [{ notificationId: "manager_review_requested", subjectContains: "請假" }]` → install via `mode=install` → repro runner asserts captured email matches, OverallStatus = Pass

## 12. SandboxBanner everywhere + audit hardening

- [x] 12.1 `SandboxService.SetStatusAsync` previously had no audit/log line — added Info-level log capturing previous→next + actor + tenant + timestamp. Dedicated audit table still skipped per §4.8 deferral. (PR-J5)
- [x] 12.2 `JwtTokenService.IssueSandboxPersonaToken` now logs an Info line on every issuance with actor email/id + persona email/id + roles + expiry (had no logger injected before — added optional `ILogger<JwtTokenService>` param defaulting to `NullLogger` so test ctors stay source-compatible). (PR-J5)
- [x] 12.3 `ResetService` Info-level logs already present from PR-J4 §5.3 — verified by reading `ResetInstanceAsync` / `ResetAllAsync`. (PR-J5)
- [x] 12.4 `BPM_SANDBOX_TOGGLE_DISABLED=true` env var honored in `SandboxController.SetStatus`: returns 403 `{ error: "sandbox_toggle_disabled" }` before delegating to `ISandboxService`. Wired inline in the controller (not as middleware) so the existing `[Authorize(Roles="admin")]` runs first and the unauthorized-vs-toggle-disabled distinction stays clean. (PR-J5)
- [x] 12.5 `docs/sandbox-acceptance-loop.md` (79 lines) walks the demo: toggle on → install bundle → submit → advance time → check mailbox → switch persona → approve → reset. (PR-J5)

## 13. Cleanup of legacy `SandboxRedirect`

- [ ] 13.1 One release after Mailbox UI consumes `SandboxCapturedMessage`, drop writes to `SandboxRedirect` from outbound gate
- [ ] 13.2 Migrate any consumers of `GET /api/sandbox/redirects` to `GET /api/sandbox/captured?channel=email`
- [ ] 13.3 Drop `SandboxRedirect` entity + table in a follow-up cleanup PR

## 14. End-to-end acceptance test

- [ ] 14.1 Write `bpm-svc/test/Integration/SandboxAcceptanceLoopTests.cs` exercising the full demo path: toggle on, submit instance via Wilson persona, advance clock 48h, verify SLA-warning email captured, switch persona to Mary, approve, verify approved email + customer-system webhook captured, reset, re-run identically — all via API calls, no UI
- [ ] 14.2 Add to CI as a slower "sandbox" suite
- [ ] 14.3 Document the runbook in `docs/sandbox-acceptance-loop.md` so the partner can demo it directly to candidate customers
