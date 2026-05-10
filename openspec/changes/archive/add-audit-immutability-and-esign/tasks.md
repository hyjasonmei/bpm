# Tasks

## 1. Domain — IHashChained interface + ChainKey attribute

- [ ] 1.1 Create `Domain/Common/IHashChained.cs` interface (`string? PrevRowHash { get; set; }`, `string? RowHash { get; set; }`)
- [ ] 1.2 Create `Domain/Common/ChainKeyAttribute.cs` (optional property name) — apply to entity classes
- [ ] 1.3 Apply `IHashChained` + `[ChainKey]` to: `HrFlowAction` (key=InstanceId), `ActorResolutionAudit` (key=RequestId), `BrandingChange` (no key), `RoleAssignmentChange` (key=TargetUserId), `SandboxRedirect` (key=TenantCode), `ImpersonationSession` (key=ImpersonatorUserId)
- [ ] 1.4 Add hash columns to each entity (string?, max 64)

## 2. Domain — Electronic signature on HrFlowAction

- [ ] 2.1 Create `Domain/Entities/HrFlows/SignatureMeaning.cs` enum (Approved, Reviewed, Witnessed, Returned, Acknowledged)
- [ ] 2.2 Add to HrFlowAction: SignatureMeaning?, SignerName? (string,200), SignerEmail? (string,254), SignedAtUtc?, SignatureHash? (string,64), NtpSyncedAt?, NtpSyncDeltaMs? (int)
- [ ] 2.3 Update HrFlowActionConfiguration with column constraints

## 3. Application — IClock extension + NtpClock

- [ ] 3.1 Extend `Application/Common/Abstractions/IClock.cs` with `DateTime? NtpSyncedAt { get; }` and `int? NtpSyncDeltaMs { get; }`
- [ ] 3.2 Create `Persistence/Common/NtpClock.cs` implementing IClock + IHostedService
- [ ] 3.3 On start: call NTP server (default `time.google.com`, fallback `pool.ntp.org`) using `System.Net.NetworkInformation.Ping`-style or `NTPClient` lib; record delta + timestamp
- [ ] 3.4 IHostedService background loop: re-sync every 6 hours
- [ ] 3.5 If sync fails: keep last good values; if first sync fails: log critical, allow startup
- [ ] 3.6 Replace existing `SystemClock` registration with `NtpClock` in DI

## 4. Persistence — interceptor extension for hash chain

- [ ] 4.1 Extend `AuditSaveChangesInterceptor`:
  - For each `IHashChained` entity in `EntityState.Added`:
    - Determine chain key from `[ChainKey]` attribute
    - Query DB for last row in same chain (table + chainKey value); read its RowHash
    - Compute new RowHash = SHA-256(deterministic JSON of all business fields + PrevRowHash)
    - Set entry.PrevRowHash + entry.RowHash
- [ ] 4.2 Helper `ComputeRowHash(entity, prevHash)` — uses reflection to enumerate non-navigation, non-hash properties, sorts by name, JSON-serializes deterministically
- [ ] 4.3 Unit test deterministic hash: same input → same hash; different orders → same hash; different content → different hash

## 5. Persistence — NTP delta stamping on signature fields

- [ ] 5.1 In HrFlowService.Approve / Return: when writing HrFlowAction, populate SignatureMeaning, SignerName (from User), SignerEmail, SignedAtUtc=clock.UtcNow, NtpSyncedAt=clock.NtpSyncedAt, NtpSyncDeltaMs=clock.NtpSyncDeltaMs
- [ ] 5.2 SignatureHash = HMAC-SHA256(server_secret, $"{SignerId}|{SignedAtUtc:O}|{SignatureMeaning}|{InstanceId}|{Comment ?? ""}")
- [ ] 5.3 server_secret comes from JWT secret (existing) or new `BPM_SIGNATURE_SECRET` env var (preferred separate secret)

## 6. Persistence — DB triggers for append-only

- [ ] 6.1 Migration `AddAuditTriggers` — for each audit table, create `BEFORE UPDATE` and `BEFORE DELETE` triggers using `RAISE(FAIL, '<msg>')`
- [ ] 6.2 Affected tables: HrFlowActions, ActorResolutionAudits, BrandingChanges, RoleAssignmentChanges, SandboxRedirects, ImpersonationSessions
- [ ] 6.3 Note in migration: triggers also need to be recreated if table is rebuilt (SQLite `ALTER TABLE` rebuilds drop triggers — handle in any future schema change)
- [ ] 6.4 For Postgres prep: write equivalent CREATE TRIGGER ... FOR EACH ROW EXECUTE FUNCTION raise_audit_immutability() — bundle in same migration with conditional based on provider (or split file)

## 7. Migrations

- [ ] 7.1 `AddHashChain` — adds PrevRowHash, RowHash columns to all 6 audit tables
- [ ] 7.2 `AddSignatureFields` — adds 7 cols to HrFlowAction
- [ ] 7.3 `AddAuditTriggers` — creates triggers (last in sequence)
- [ ] 7.4 Apply locally; verify schema with `sqlite3 .schema HrFlowActions` and `.schema --indent`

## 8. API — verify-chain endpoint

- [ ] 8.1 `Api/Audit/AuditVerifyController.cs` (NEW)
- [ ] 8.2 `GET /api/audit/verify-chain?table=HrFlowAction&chainKey=<value>&since?&until?` → admin only
- [ ] 8.3 Implementation: read chain, recompute, compare; return `{ ok, rowsChecked, brokenAt? }`
- [ ] 8.4 Map result to ChainVerificationDto

## 9. API — clock-status endpoint

- [ ] 9.1 `Api/System/ClockStatusController.cs`
- [ ] 9.2 `GET /api/system/clock-status` → admin only; returns IClock state

## 10. Frontend — Approve/Return form changes

- [ ] 10.1 In `bpm-ui/src/screens/forms/ResignForm.tsx` and `DeptxForm.tsx`: extend approve / return modals with `Signature meaning` select
- [ ] 10.2 Default values: approve → "Approved", return → "Returned"; disabled options for inappropriate combos
- [ ] 10.3 Send `signatureMeaning` in approve / return API body
- [ ] 10.4 After successful approve, toast shows: "✓ Signed by Wilson You · 2026-05-08 10:32:15 UTC · Approved"

## 11. Backend — accept signatureMeaning in API

- [ ] 11.1 Update `ApproveRequest` / `ReturnRequest` DTOs with `SignatureMeaning` field
- [ ] 11.2 HrFlowsController validates required for approve / return; default to Approved/Returned if absent (transition aid)

## 12. Frontend — Audit Logs page in admin UI

- [ ] 12.1 Replace `bpm-admin-ui/src/screens/AuditLogs.tsx` placeholder with real UI
- [ ] 12.2 Tab: Verify Chain — table picker + chainKey + date range + Run button
- [ ] 12.3 Show result panel: ✓ N rows OK, or ✗ broken at row X with detail
- [ ] 12.4 Tab: Recent activity — list recent rows from each audit table (read-only browse)
- [ ] 12.5 Tab: Clock status — shows last NTP sync, delta, server; auto-refresh every 30s

## 13. Frontend — clock-drift banner

- [ ] 13.1 In `bpm-admin-ui/src/components/AdminLayout.tsx`, fetch `/api/system/clock-status` on mount + every 5 minutes
- [ ] 13.2 If `Math.abs(deltaMs) > 1000` or `now - lastSync > 24h` → show orange banner above SandboxBanner
- [ ] 13.3 Banner: "⚠️ System clock drift {deltaMs}ms — last sync {ago}"

## 14. Tests

- [ ] 14.1 Unit: ComputeRowHash deterministic for same input
- [ ] 14.2 Unit: change one field → different hash
- [ ] 14.3 Integration: insert 5 HrFlowAction rows in same instance → chain links correctly (each row.PrevRowHash == previous row.RowHash)
- [ ] 14.4 Integration: tampered row (manually UPDATE via SQL) → trigger raises (DB-level)
- [ ] 14.5 Integration: tampered row via EF SaveChanges → app interceptor raises
- [ ] 14.6 Integration: verify-chain endpoint detects manually inserted bad row (skip trigger, use raw INSERT) → reports brokenAt
- [ ] 14.7 Integration: signature hash binds — modifying Comment after insert (via raw INSERT to a parallel test table) → verify-chain catches it
- [ ] 14.8 Integration: clock-status reflects NTP sync at startup
- [ ] 14.9 E2E: approve via UI without signatureMeaning select change → defaults to Approved; HrFlowAction row has SignatureMeaning=Approved, SignedAtUtc populated, SignatureHash set

## 15. Documentation

- [ ] 15.1 Add `docs/compliance-audit-grade.md` with the Part 11 mapping table from the proposal (give to customer QA)
- [ ] 15.2 Update `docs/process-engine-customer.md` to mention the new audit guarantees
- [ ] 15.3 Note in CLAUDE.md the new IHashChained pattern + IImpersonable pattern as cross-cutting concerns

## 16. Out-of-scope notes

- [ ] 16.1 Stub `add-tsa-integration` change folder with one-line proposal "future: connect to RFC 3161 TSA"
- [ ] 16.2 Stub `add-pki-esignature` change folder with one-line proposal "future: per-user x509 cert signatures"
