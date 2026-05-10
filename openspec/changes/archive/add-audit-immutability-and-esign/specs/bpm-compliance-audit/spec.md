## ADDED Requirements

### Requirement: Audit rows form a per-chain SHA-256 hash chain

Every entity implementing `IHashChained` SHALL include `PrevRowHash` and `RowHash` columns. On insert, the system SHALL:

1. Determine the chain by reading `[ChainKey(propName)]` attribute (no attribute → single-table chain)
2. Query the most recent row in the same chain (by `CreatedAt` desc); read its `RowHash` as `PrevRowHash`
3. Compute `RowHash = SHA-256(deterministic-JSON(<all business fields> + PrevRowHash))`
4. Persist both fields atomically with the row

The first row in a chain SHALL have `PrevRowHash = null`.

#### Scenario: First row in instance chain

- **GIVEN** an empty HrFlowActions table
- **WHEN** Wilson submits a RESIGN, creating instance I and Submit action row R1
- **THEN** R1.PrevRowHash is null and R1.RowHash is a 64-char lowercase hex string

#### Scenario: Second row links to first

- **GIVEN** R1 exists for instance I with RowHash = `abc123...`
- **WHEN** Wilson's manager Approves, creating row R2 in same instance
- **THEN** R2.PrevRowHash = `abc123...` and R2.RowHash is a fresh hash

#### Scenario: Different chains do not link across

- **GIVEN** instances I1 and I2 each have rows
- **WHEN** a new row in I1 is inserted
- **THEN** its PrevRowHash refers only to I1's last row (not I2's, even if I2 had a more recent row globally)

### Requirement: Audit tables enforce append-only at DB layer

The system SHALL install `BEFORE UPDATE` and `BEFORE DELETE` triggers on every audit table that immediately raise an error preventing the operation. This applies to `HrFlowActions`, `ActorResolutionAudits`, `BrandingChanges`, `RoleAssignmentChanges`, `SandboxRedirects`, `ImpersonationSessions` (and any future audit table).

#### Scenario: SQL UPDATE blocked

- **GIVEN** an HrFlowAction row exists
- **WHEN** a DBA executes `UPDATE HrFlowActions SET Comment='changed' WHERE Id=...`
- **THEN** the database raises an error containing "audit table is append-only"
- **AND** the row remains unchanged

#### Scenario: SQL DELETE blocked

- **GIVEN** an audit row
- **WHEN** a DBA executes `DELETE FROM HrFlowActions WHERE Id=...`
- **THEN** the database raises an error
- **AND** the row remains

#### Scenario: ORM-layer prevention still works

- **GIVEN** application code attempts to load and modify an audit row via EF Core SaveChanges
- **WHEN** SaveChanges is called
- **THEN** the AuditSaveChangesInterceptor throws an exception (DB trigger never reached because SaveChanges fails first)

### Requirement: Verify-chain endpoint detects tampering

The system SHALL provide `GET /api/audit/verify-chain?table=<name>&chainKey=<value>&since=<dt>&until=<dt>` that recomputes each row's expected hash and compares it to the persisted hash. The response SHALL be:

- On success: `{ ok: true, rowsChecked: N, fromRowId: <first>, toRowId: <last> }`
- On failure: `{ ok: false, brokenAt: <rowId>, expected: <hash>, actual: <hash>, reason: <enum> }`

Reasons: `RowHashMismatch` (row content was altered post-insert), `PrevHashMismatch` (chain link broken), `MissingPrevRow` (referenced previous row not found).

The endpoint SHALL be `[Authorize(Roles="admin")]`.

#### Scenario: Clean chain verifies

- **GIVEN** 50 HrFlowAction rows in instance I, never tampered
- **WHEN** admin calls verify-chain for table=HrFlowAction, chainKey=I
- **THEN** the response is `{ ok: true, rowsChecked: 50 }`

#### Scenario: Tampered row detected (after bypassing triggers, e.g., disabling)

- **GIVEN** rows R1...R10 in chain; row R5's Comment column has been altered out-of-band (e.g., trigger temporarily disabled)
- **WHEN** verify-chain is called
- **THEN** response is `{ ok: false, brokenAt: R5.Id, reason: RowHashMismatch, expected: <recomputed>, actual: R5.RowHash }`

#### Scenario: Inserted-out-of-band row detected

- **GIVEN** an attacker disabled triggers and INSERTed a fake R5.5 between R5 and R6
- **WHEN** verify-chain is called
- **THEN** response is `{ ok: false, brokenAt: R5.5.Id, reason: PrevHashMismatch }` (the fake row's PrevRowHash doesn't match R5's RowHash) OR R6's PrevRowHash doesn't link, whichever is hit first

### Requirement: Approval rows carry electronic signature fields

Every HrFlowAction with Action `Approve` or `Return` SHALL persist:

- `SignatureMeaning` (enum: Approved, Reviewed, Witnessed, Returned, Acknowledged)
- `SignerName` (snapshot of User.FullName at sign time)
- `SignerEmail` (snapshot of User.Email)
- `SignedAtUtc` (UTC datetime from `IClock.UtcNow`)
- `SignatureHash` (HMAC-SHA256 hex)
- `NtpSyncedAt` (DateTime?, snapshot of last successful NTP sync)
- `NtpSyncDeltaMs` (int?, snapshot of NTP delta at sync time)

`SignatureHash = HMAC-SHA256(server_secret, "{SignerId}|{SignedAtUtc:O}|{SignatureMeaning}|{InstanceId}|{Comment}")`.

#### Scenario: Manager approves with default meaning

- **GIVEN** Elton approves Wilson's RESIGN
- **WHEN** the action is recorded
- **THEN** the row has SignatureMeaning=Approved, SignerName="Elton Yang (Manager)", SignedAtUtc set, SignatureHash is a 64-char hex string

#### Scenario: Manager returns with default meaning

- **GIVEN** Elton returns the request with comment "缺離職日"
- **THEN** the row has SignatureMeaning=Returned, SignatureHash includes the comment in the input

#### Scenario: SignerName preserved after user rename

- **GIVEN** Wilson approves something and SignerName="Wilson You (Employee)"
- **WHEN** Wilson's User.FullName is later updated to "Wilson Y. (Employee)"
- **THEN** the audit row's SignerName remains "Wilson You (Employee)"

### Requirement: SignatureHash binds signer + record + comment + timestamp

The system SHALL compute SignatureHash such that any post-hoc modification to SignerId, SignedAtUtc, SignatureMeaning, InstanceId, or Comment renders verification impossible (HMAC-SHA256 with server_secret). Verification (recomputing hash with same inputs) SHALL match the stored value for untampered rows.

#### Scenario: Recomputation matches for clean row

- **GIVEN** an HrFlowAction row R with all signature fields set
- **WHEN** verify-chain (or a dedicated signature verifier) recomputes HMAC over R's fields
- **THEN** the recomputed hex equals `R.SignatureHash`

#### Scenario: Tampering breaks verification

- **GIVEN** the comment field was altered out-of-band
- **WHEN** signature is recomputed
- **THEN** the recomputed hash differs from the stored SignatureHash

### Requirement: Server clock is NTP-synced and observable

The system SHALL synchronize its clock with at least one NTP server (default `time.google.com`, fallback `pool.ntp.org`) at process startup and every 6 hours thereafter. The IClock implementation SHALL expose `NtpSyncedAt` (last successful sync time, UTC) and `NtpSyncDeltaMs` (signed millisecond delta at last sync).

#### Scenario: NTP sync at startup

- **WHEN** the server starts
- **THEN** within 5 seconds an NTP query is attempted
- **AND** if successful, IClock.NtpSyncedAt is set and NtpSyncDeltaMs is recorded

#### Scenario: Periodic re-sync

- **GIVEN** the server has been running 6h+
- **WHEN** the periodic sync fires
- **THEN** NtpSyncedAt is updated

#### Scenario: NTP failure tolerated

- **GIVEN** NTP server unreachable
- **WHEN** sync fails
- **THEN** the process continues running with stale NtpSyncedAt; warning logged; clock-status endpoint reflects the failure

### Requirement: Clock status visible to admins

The endpoint `GET /api/system/clock-status` SHALL return current server time, last NTP sync time, last delta, server hostname used, and configured re-sync interval. The endpoint SHALL be `[Authorize(Roles="admin")]`.

The admin UI SHALL render an orange warning banner above other content when `|NtpSyncDeltaMs| > 1000` or `now - NtpSyncedAt > 24h`.

#### Scenario: Banner shown when drifted

- **GIVEN** the last NTP sync had delta = 2400ms
- **WHEN** an admin loads the admin UI
- **THEN** an orange banner is shown reading "⚠️ System clock drift 2400ms — last sync 5 minutes ago"

#### Scenario: Banner hidden when normal

- **GIVEN** delta=12ms, last sync 3 hours ago
- **WHEN** admin loads any page
- **THEN** no clock banner is rendered

### Requirement: API documents Part 11 mapping for customer QA

The system SHALL include a documentation artifact at `docs/compliance-audit-grade.md` containing a row for each 21 CFR Part 11 sub-clause mapping to:

- The technical mechanism in this system that addresses it (or "N/A")
- Whether it's the customer's responsibility (e.g., training records, SOPs) vs the platform's

This document is intended to be handed to customer QA teams to support their CSV / IQ-OQ-PQ documentation.

#### Scenario: Mapping covers §11.10(b) and §11.50

- **GIVEN** the doc exists
- **WHEN** a reader looks for §11.10(b) (record protection through retention period)
- **THEN** the doc points to "Append-only DB triggers + EF interceptor + hash chain (this capability)"

- **WHEN** a reader looks for §11.50 (signature components)
- **THEN** the doc points to "SignatureMeaning + SignerName + SignedAtUtc + SignatureHash on HrFlowAction (this capability)"
