# Tasks

## 1. Domain

- [ ] 1.1 Create `Domain/Entities/Impersonation/ImpersonationSession.cs` (Id, ImpersonatorUserId, TargetUserId, StartedAt, EndedAt?, EndReason?, Reason)
- [ ] 1.2 Create `Domain/Entities/Impersonation/EndReason.cs` enum (`ManualExit`, `AutoExpired`, `AdminRevoked`)
- [ ] 1.3 Create `Domain/Common/IImpersonable.cs` interface (single property: `Guid? ImpersonatedByUserId { get; set; }`)
- [ ] 1.4 Add `ImpersonatedByUserId` (Guid?, nullable) to `HrFlowAction` and make it implement `IImpersonable`
- [ ] 1.5 Add `ImpersonatedByUserId` to `ActorResolutionAudit` and make it implement `IImpersonable`

## 2. Persistence

- [ ] 2.1 EF configuration for `ImpersonationSession`; index (ImpersonatorUserId, StartedAt DESC), (EndedAt) WHERE EndedAt IS NULL
- [ ] 2.2 Update HrFlowActionConfiguration / ActorResolutionAuditConfiguration to include the new column
- [ ] 2.3 DbSet in AppDbContext
- [ ] 2.4 Migration `AddImpersonation` (creates ImpersonationSessions table + ALTER TABLE for the two audit tables)
- [ ] 2.5 Apply locally; verify schema

## 3. ICurrentUser extension

- [ ] 3.1 Extend `Application/Common/Abstractions/ICurrentUser.cs` with `Guid? ImpersonatedById` and `Guid? ImpersonationSessionId`
- [ ] 3.2 Update `Api/Common/HttpContextCurrentUser.cs` to read claims `impersonated_by` and `imp_session_id`
- [ ] 3.3 Update `Application/Common/Services/SystemCurrentUser.cs` to return null for both

## 4. AuditSaveChangesInterceptor extension

- [ ] 4.1 Add a foreach pass over `EntityEntry<IImpersonable>` entries in EntityState.Added
- [ ] 4.2 If `currentUser.ImpersonatedById` is not null, set `entry.Entity.ImpersonatedByUserId`
- [ ] 4.3 Test: impersonated context creates HrFlowAction → row carries ImpersonatedByUserId
- [ ] 4.4 Test: non-impersonated context → row carries null

## 5. JwtTokenService extension

- [ ] 5.1 Add `MintImpersonationToken(targetUser, impersonatorUserId, sessionId, lifetimeMinutes=30)` method
- [ ] 5.2 Claims: `sub=target.Id`, `email=target.Email`, `roles=target_role_codes`, `impersonated_by=impersonatorUserId`, `imp_session_id=sessionId`, `exp=now+30min`
- [ ] 5.3 NameClaimType / RoleClaimType already wired in Program.cs

## 6. Application service

- [ ] 6.1 `Application/Impersonation/IImpersonationService.cs` with: `StartAsync(impersonatorId, targetUserId, reason) → (token, sessionId)`, `EndAsync(sessionId, reason) → void`, `GetActiveAsync(impersonatorId) → session?`, `GetHistoryAsync(days) → session[]`, `RevokeAsync(sessionId, byUserId) → void` (admin)
- [ ] 6.2 Impl in `Persistence/Impersonation/ImpersonationService.cs`
- [ ] 6.3 StartAsync validation: caller must be admin role; target must exist & be active; target != caller; no other active session for same impersonator (return Conflict if exists); if caller's own JWT carries `impersonated_by`, reject with Conflict (no nesting)
- [ ] 6.4 EndAsync: lookup session, set EndedAt + EndReason

## 7. API

- [ ] 7.1 `Api/Impersonation/ImpersonationController.cs` (NEW; `[Authorize]` not `[AllowAnonymous]`)
- [ ] 7.2 `POST /api/impersonation/start` body `{ targetUserId, reason }` → returns `{ token, expiresAt, target: { id, fullName } }`
- [ ] 7.3 `POST /api/impersonation/end` → marks current session ManualExit, returns 204
- [ ] 7.4 `GET /api/impersonation/status` → returns active session for current impersonator (if any) — used by frontend on mount to decide banner
- [ ] 7.5 `GET /api/impersonation/sessions?days=30` → admin-only history (recent N days)
- [ ] 7.6 `POST /api/impersonation/sessions/{id}/revoke` → admin-only force-end (records EndReason=AdminRevoked)
- [ ] 7.7 Authorization handlers: start/sessions/revoke require admin role

## 8. Frontend — token + helper

- [ ] 8.1 `bpm-ui/src/lib/api/impersonation.ts` with `startImpersonation(targetUserId, reason)`, `endImpersonation()`, `getImpersonationStatus()`, `getSessionHistory()`
- [ ] 8.2 `bpm-ui/src/lib/impersonationToken.ts` helper:
  - `enterImpersonation(token)` — saves current jwt to `bpm_jwt_pre_impersonation`, replaces `bpm_jwt`
  - `exitImpersonation()` — restores pre-impersonation jwt, removes pre-key, calls api end
  - `isImpersonating()` — returns true if pre-key exists
  - `parseImpersonationFromJwt()` — decodes current jwt, returns `{ impersonatedBy, sessionId, exp }` or null
- [ ] 8.3 Extend `apiFetch.ts` 401 handler: if currently impersonating and 401 → silently exitImpersonation() then retry once with restored token

## 9. Frontend — UI surfaces

- [ ] 9.1 `bpm-ui/src/components/ImpersonationBanner.tsx` — red banner top of every page when impersonating
  - Shows `⚠️ ACTING AS <fullName> · <mm:ss left> · [Exit]`
  - Auto-updates countdown every second
  - Last 5 minutes → amber; last 1 minute → red blink (subtle)
  - Exit button calls exitImpersonation + page reload
- [ ] 9.2 Wire banner into AppLayout (between sandbox banner and header); decide ordering: sandbox first if both
- [ ] 9.3 Extend RoleSwitcher dropdown: when current user has admin role and not currently impersonating, append `🎭 Act as another user...` row
- [ ] 9.4 Click → open modal:
  - User search/picker (queries `/api/org/users?q=`)
  - Reason textarea (required, max 500 chars)
  - Confirm button → call startImpersonation → enterImpersonation(token) → page reload
- [ ] 9.5 On AppLayout mount: call `getImpersonationStatus()`; if active session exists but localStorage has no impersonation token → restore (handles browser restart mid-session)

## 10. Tests

- [ ] 10.1 Unit: StartAsync as non-admin → ForbiddenException
- [ ] 10.2 Unit: StartAsync targeting self → Conflict
- [ ] 10.3 Unit: StartAsync while already impersonating → Conflict
- [ ] 10.4 Unit: StartAsync with two different active sessions for same impersonator → second fails (one active at a time)
- [ ] 10.5 Integration: Start → call HrFlow service in impersonated context → resulting HrFlowAction.ImpersonatedByUserId == admin id, ActorUserId == target id
- [ ] 10.6 Integration: End session → status endpoint returns no active
- [ ] 10.7 Integration: JWT expires after 30 min → next call 401, frontend swap-back simulated → admin token regains access

## 11. Documentation

- [ ] 11.1 Add a section to `add-tenant-sandbox-mode/design.md` (if not already noted) on co-existence
- [ ] 11.2 Note in CLAUDE.md the impersonation pattern + audit columns
