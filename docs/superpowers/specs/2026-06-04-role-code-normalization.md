# Role: add Code, make Name meaningful, fix the `admin` mismatch

**Date:** 2026-06-04
**Status:** Approved (開發者, Telegram "做") — SCREAMING_SNAKE Code, single-Chinese Name, fold in the admin fix.

## Problem

`Role` is `{ Id, Name, IsSystem, Description }`. `Name` is overloaded as the
**stable identifier** (seeder keys, flow actor-resolution constants,
`[Authorize(Roles=...)]`, the JWT `roles` claim, bpm-ui role checks) AND as the
display label — with inconsistent format (`Approver` / `Finance` / `HR_Manager` /
`SystemAdmin` / `Persona_Switch`). Plus a latent bug: code gates on role
`"admin"` which is **not seeded** (real admin roles are `SystemAdmin` /
`Persona_Switch`), so bare-`admin` endpoints are unreachable.

## Design

Add a stable **`Code`** to `Role`; repurpose `Name` for display.

- `Code` — SCREAMING_SNAKE (matches FlowCode), unique. The new identifier
  everywhere: `SYSTEM_ADMIN`, `PERSONA_SWITCH`, `FINANCE`, `HR_MANAGER`,
  `PROCUREMENT`, `APPROVER`, `SUBMITTER`, `REVIEWER`, `DIRECTOR`, `CEO`, `CFO`,
  `AUDITOR`, `FLOW_OWNER`, `WATCHER`.
- `Name` — human display (zh-TW): 系統管理員 / 財務 / 採購 / 人資主管 / …
- `Description` — longer explanation (kept).

All identifier usages switch from Name → Code. The JWT `roles` claim emits
**Code**; `[Authorize]` and bpm-ui checks use Code; flows resolve `role:<code>`.

### Name → (Code, display Name) map

| old Name | Code | Name (zh) |
|---|---|---|
| Approver | APPROVER | 簽核者 |
| Submitter | SUBMITTER | 申請人 |
| Reviewer | REVIEWER | 審查者 |
| Director | DIRECTOR | 總監 |
| CEO | CEO | 執行長 |
| CFO | CFO | 財務長 |
| HR_Manager | HR_MANAGER | 人資主管 |
| Procurement | PROCUREMENT | 採購 |
| Finance | FINANCE | 財務 |
| Auditor | AUDITOR | 稽核 |
| FlowOwner | FLOW_OWNER | 流程負責人 |
| SystemAdmin | SYSTEM_ADMIN | 系統管理員 |
| Persona_Switch | PERSONA_SWITCH | Persona 切換權限 |
| Watcher | WATCHER | 關注者 |

## Touch list

**bpm-admin-svc**
- `Domain/Roles/Role.cs` — add `Code`; `Configurations/RoleConfiguration.cs` —
  Code maxlen + unique index (move uniqueness off Name).
- `Seed/Seeder.cs` — seed `(Code, Name, Description)`; key `roleIds` by Code.
- Migration `AddRoleCode` — add Code column; backfill Code + zh Name per the map
  above (raw SQL `UPDATE` per role); add unique index on Code.
- `RolesController` + `RoleAdminService` + DTOs — surface Code (read; create/edit
  takes Code).

**bpm-svc**
- `SharedIdentity/SharedRole.cs` (+ config) — add `Code`.
- `Api/Auth/AuthController.cs` + `PersonaLoginService.cs` — the `roles` claim/
  summary now selects `r.Code` (was `r.Name`). `JwtTokenService` unchanged (takes
  role strings).
- `Persistence/Org/OrgChartReader.cs` — `GetRoleAssigneesAsync` joins by `Code`.
- Flow role constants → codes: `TEO`/`PURCHASE_REQUEST` `FinanceRoleName` →
  `"FINANCE"`; `VENDOR_EXPENSE` `ProcurementRoleName` → `"PROCUREMENT"`; `LEAVE`
  `role:HR` → `"HR_MANAGER"`. (LEAVE `role:VP` is a pre-existing unseeded
  fallback — left as `"VP"`, out of scope.)
- `[Authorize(Roles="admin")]` → `"SYSTEM_ADMIN"`;
  `[Authorize(Roles="Persona_Switch,SystemAdmin,admin")]` → `"PERSONA_SWITCH,SYSTEM_ADMIN"`.

**bpm-ui**
- `lib/jwt.ts` — `isAdmin` → `roles.includes('SYSTEM_ADMIN')`; `isPersonaSwitcher`
  → `'PERSONA_SWITCH' | 'SYSTEM_ADMIN'`.

**bpm-admin-ui**
- Role management UI (User & Role → roles) — show Code + Name.

**chef**
- `chef/skill/conventions.md` — `role:<code>` (was `role:<name>`); "Role assignees
  by code".

## Out of scope

- Adding missing roles (`VP`, a dedicated `HR`): pre-existing gaps, separate work.
- Per-role i18n object: single zh Name + Description is enough for now.

## Verification

- Login (dev persona) → JWT `roles` carries Codes (e.g. `SYSTEM_ADMIN`,
  `PERSONA_SWITCH`); admin-gated endpoints reachable by Jack; sandbox persona
  switch still gated correctly.
- A finance-routed flow (TEO/PURCHASE_REQUEST) still resolves its approver
  (`role:FINANCE` → active member).
- Role list in admin shows Code + meaningful zh Name.
- All apps build / tsc clean; migrate backfills Code on the existing dev DB.
