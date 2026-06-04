# Process Doctor — Design (v1)

**Date:** 2026-06-04
**Status:** Approved scope (Jason, Telegram). Doctor sequenced before delegation.
**Owner:** lead (admin-ui + bpm-svc shared platform)

## Goal

A new top-level **Doctor** page in `bpm-admin-ui` (nav slot **above Site
Setting**) that diagnoses operational health problems — stuck/abandoned cases
and broken org wiring — and lets an operator remediate the case-level ones in
place. The headline scenario: an approval is mid-flight and the approver
resigns; the Doctor surfaces it and lets you reassign.

## Scope (v1)

- **Case health** (per in-flight case): R1 resigned/disabled approver, R2
  ownerless case, R3 stalled-too-long.
- **Org health** (identity wiring): R4a no manager / no dept head, R4b role/
  group with no active members (the generic "whole group resigned" check).
- **Remediation** (case-level only): reassign one case, batch-reassign a
  departed person's cases, force-cancel. Org-level findings are diagnose +
  deep-link to User & Role (admin-svc owns identity writes).

Out of scope for v1: delegation-aware detection, auto re-resolve-actor,
pretty case titles, Admin_AuditEvents integration, case-level group-pending
detection (no flow uses group approval yet).

## Identity model it must fit (confirmed)

`Principal` (Type = User/Dept/Group) + separate `Role`; assignment via
`PrincipalRole(PrincipalId, RoleId, InheritToMembers)`. Membership:
`UserDept(IsPrimary)`, `DeptParent` (tree), `DeptHead`, `UserManager`,
`GroupMember` (members may be User/Dept/Group, nested), `Delegation`
(time-boxed). bpm-svc mirrors the **full** model via `Shared*` DbSets
(including `SharedDeptParent`, `SharedGroupMember`) and exposes
`IOrgChartReader` — the same resolver the flows use at runtime
(`GetManagerId`, `GetDepartmentHeadId`, `ExpandGroupAsync` transitive +
cycle-safe, `GetRoleAssigneesAsync`).

**Design decisions driven by the model:**

1. **Use the flows' own resolver** (`IOrgChartReader`/`IActorResolver`) for
   org-health, so "empty role / unresolvable" means exactly what the flow
   would hit at runtime — not a parallel re-implementation. Natively handles
   nested groups, group-of-groups, dept-as-member.
2. **"Gone" = `Active == false` OR `DeletedAt != null`.** bpm-svc sees
   soft-deleted rows (no global filter on the Shared mirror), so both are
   checked; "active member" = `Active && DeletedAt == null`.
3. **Assignee is always a single User.** Actor refs (role/group/dept/manager)
   are resolved to one concrete user written to `CurrentAssigneeUserId`, so
   case-level R1 is a single-user check.
4. **Delegation is currently a no-op at runtime** (`StubDelegationService`
   returns null; the live hook only exists in the retired Model-A
   `ProcessRuntime`). The Doctor matches runtime and ignores delegation in
   v1. When delegation is wired into Model-B, the Doctor gains: factor active
   delegates into "really stuck?" + suggest the delegate as a reassign target.

## Detection

Reflection-scan every `<CODE>_V\d+_Case` entity (same regex as
reports/flow-codes/reset). A case is **open** when `CompletedAt == null`.
Common columns present on all 10 flows: `Status`, `CurrentAssigneeUserId`,
`SubmittedAt`, `LastActivityAt`, `CompletedAt`, `SubmitterUserId`.

| Rule | Condition | Severity |
|---|---|---|
| R1 resigned approver | open ∧ `CurrentAssigneeUserId` → principal with `Active==false` or `DeletedAt!=null` | 🔴 high |
| R2 ownerless | open ∧ `CurrentAssigneeUserId == null` | 🔴 high |
| R3 stalled | open ∧ `LastActivityAt < now − N days` (N default 14, query param) | 🟠 med |
| R4a broken chain | user with no `UserManager` / dept with no `DeptHead` | 🟡 info |
| R4b empty role/group | role or group that resolves (via `IOrgChartReader`) to 0 active users | 🟡 info |

R4a/R4b are org-level (not tied to a specific case) — informational, with a
deep-link to the relevant User & Role screen. R4b is the generic answer to
"whole group resigned": scan each role/group, expand to active users, flag
zero. (Not every empty role breaks a flow — only ones a flow routes to — so
it's advisory.)

A finding row: `{ id, rule, severity, flowCode?, caseId?, caseStatus?,
assigneeUserId?, assigneeName?, assigneeGone?, submitterName?, lastActivityAt?,
daysStuck?, suggestedReassignee?, targetKind?, targetName?, fixLink? }`.

## Remediation (case-level, generic override)

Reassign and cancel operate generically on whatever case table the finding
names — reassign is state-neutral (only changes who holds it), so we bypass
each flow's state machine and write the column directly. The flows read
`CurrentAssigneeUserId`, so a reassigned/cancelled case behaves correctly on
next load.

- **Reassign one case** — parameterized `UPDATE "<table>" SET
  CurrentAssigneeUserId=@to, LastActivityAt=@now WHERE Id=@id` (table from EF
  metadata, trusted). Suggested target computed from the departed approver:
  manager (`GetManagerId`) → primary dept head (`GetDepartmentHeadId`);
  operator may override with any active user.
- **Batch reassign** — for a departed user, update every open case across all
  case tables where `CurrentAssigneeUserId == departedId` to one chosen target.
- **Force-cancel** — set the case's `Status` to its `Cancelled` enum value
  (found by reflection on the Status enum type — chef names it `Cancelled`
  consistently), `CompletedAt = now`, `CurrentAssigneeUserId = null`.
  Confirm-gated in the UI.
- Every action writes a `DoctorActionLog` row (id, timestamp, action, flowCode,
  caseId, fromUserId, toUserId, reason, operator). Operator id is passed from
  admin-ui (the logged-in admin). Audit-event integration deferred.

## Architecture

- **bpm-svc** (case data + org resolver live here):
  - `IDoctorService` (Application) + impl (Persistence): `ScanAsync()`,
    `ReassignAsync(flowCode, caseId, toUserId, operator, reason)`,
    `BatchReassignAsync(fromUserId, toUserId, operator, reason)`,
    `CancelAsync(flowCode, caseId, operator, reason)`,
    `GetCandidatesAsync(forUserId)` (suggested + active-user list).
  - `DoctorController` `[Route("api/doctor")] [AllowAnonymous]` (console
    pattern, reached via `/bpmsvc` proxy like reports/sandbox): `GET /scan`,
    `GET /candidates?userId=`, `GET /users`, `POST /reassign`,
    `POST /batch-reassign`, `POST /cancel`.
  - `DoctorActionLog` entity + config + migration.
- **bpm-admin-ui**:
  - `api/doctor.ts`, `pages/DoctorPage.tsx`, nav entry above Site Setting in
    `AppShell.tsx`.
  - Findings grouped by severity; case rows get inline reassign (with suggested
    target prefilled) + cancel; a "by departed person" view lists people who
    are gone but still hold open cases → one-click batch reassign; org findings
    (R4) link out to User & Role.

## Files (touch list)

**bpm-svc**
- `src/Application/Doctor/IDoctorService.cs` + DTOs (new)
- `src/Persistence/Doctor/DoctorService.cs` (new)
- `src/Domain/Entities/Doctor/DoctorActionLog.cs` (new)
- `src/Persistence/Configurations/Doctor/DoctorActionLogConfiguration.cs` (new)
- `src/Api/Doctor/DoctorController.cs` (new)
- `src/Persistence/AppDbContext.cs` (DbSet) + `DependencyInjection.cs` (register)
- `src/Persistence/Migrations/*` (AddDoctorActionLog)

**bpm-admin-ui**
- `src/flowcook/api/doctor.ts` (new)
- `src/flowcook/pages/DoctorPage.tsx` (new)
- `src/flowcook/app/AppShell.tsx` (nav entry + route, above Site Setting)

## Verification

- bpm-svc build + migrate clean; admin-ui `tsc` clean.
- Seed/throwaway: submit an APE, deactivate its approver (Active=false) →
  Doctor R1 flags it with the approver shown gone; suggested target = approver's
  manager; reassign → case's `CurrentAssigneeUserId` updates, the new approver
  sees it in their inbox, `DoctorActionLog` row written.
- Batch: deactivate a user holding several cases → "by departed person" lists
  them → batch reassign clears all.
- Force-cancel a stalled case → status = Cancelled, removed from open scan.
- R4b: empty a role/group (or point at a role with all-inactive members) →
  appears as info finding with a User & Role link.
- New throwaway `DEMOFLOW_V2_Case` proves reflection auto-discovery (then
  remove), mirroring the reports/reset proof.
