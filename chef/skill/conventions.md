# chef conventions — naming, paths, primitives

Quick lookup during a chef session. The model B SKILL.md (loaded
first) wins if these drift.

## Path map

chef writes inside csprojs the solution already references —
specifically inside `Features/<CODE>/V<N>/` subtrees of those csprojs.
Don't create new csproj files or edit `bpm-svc.slnx`.

| Allowed (write) | What lives here |
|---|---|
| `bpm-svc/src/Domain/Features/<CODE>/V<N>/**` | Entity + status enum + value objects (POCO, no deps) |
| `bpm-svc/src/Application/Features/<CODE>/V<N>/**` | State-machine service + notification templates + `ITypedInboxProvider` impl + actor-resolution helpers (all business logic) |
| `bpm-svc/src/Persistence/Features/<CODE>/V<N>/**` | **EF mapping only** (`<CODE>_V<N>_<Purpose>Configuration.cs`) |
| `bpm-svc/src/Persistence/Migrations/<ts>_<CODE>_V<N>_*.cs` + `AppDbContextModelSnapshot.cs` | `dotnet ef migrations add` regenerates these — let it drive |
| `bpm-svc/src/Api/Features/<CODE>/V<N>/**` | Controller + DTOs |
| `bpm-svc/tests/Bpm.Tests/Features/<CODE>/V<N>/**` | Unit + integration tests |
| `bpm-ui/src/features/<CODE>/V<N>/**` | React form + manifest + case-detail page; registry globs `*/V*/manifest.ts` automatically |

The five Clean-Arch layers (Domain / Application / Persistence /
Api / SeedCli) are the same shape both backends use. **Entities don't
drop into Persistence; business logic doesn't drop into Api.** EF
mapping is the only thing in Persistence/Features — the entity it
maps lives in Domain/Features and the service that operates on it
lives in Application/Features.

| Read-only | Why |
|---|---|
| `bpm-svc/src/{Api,Application,Domain,Persistence,Functions,SeedCli}/**` outside `Features/<CODE>/V<N>/` | Shared platform — lead owns it |
| `bpm-admin-svc/**`, `bpm-admin-ui/**` | Admin tooling — not chef's territory |
| `bpm-www/**`, `chef/**`, `docs/**`, `openspec/**` | Docs / self |
| `bpm-ui/src/screens/forms/Reference_*.tsx` | Hand-coded visual reference set |
| `bpm-ui/src/screens/Home.tsx`, `App.tsx`, `router.tsx`, `lib/workflow.ts` | Top-level shell — lead owns it |

If you need to read something that isn't listed, it's probably fine —
the read list is "you'll need these"; the forbidden list is hard.

## Naming

`<CODE>` is the spec's `meta.flowCode` upper-cased. `<N>` is the
spec's `meta.flowVersion` integer.

| Artifact | Pattern | Example (LEAVE V1) | Lives in |
|---|---|---|---|
| Entity (Domain) | `<CODE>_V<N>_<PurposeCamel>` | `LEAVE_V1_Case` | `Domain/Features/<CODE>/V<N>/` |
| Status enum (Domain) | `<CODE>_V<N>_<PurposeCamel>` | `LEAVE_V1_CaseStatus` | `Domain/Features/<CODE>/V<N>/` |
| State-machine service (Application) | `<CODE>_V<N>_<PurposeCamel>` | `LEAVE_V1_LeaveService` | `Application/Features/<CODE>/V<N>/` |
| Notification templates (Application) | `<CODE>_V<N>_NotificationTemplates` | `LEAVE_V1_NotificationTemplates` | `Application/Features/<CODE>/V<N>/` |
| Inbox provider (Application) | `<CODE>_V<N>_InboxProvider` | `LEAVE_V1_InboxProvider` | `Application/Features/<CODE>/V<N>/` |
| EF configuration (Persistence) | `<CODE>_V<N>_<Purpose>Configuration` | `LEAVE_V1_CaseConfiguration` | `Persistence/Features/<CODE>/V<N>/` |
| DB table (output of mapping) | `<CODE>_V<N>_<purpose_snake>` | `LEAVE_V1_leave_case` | — |
| EF migration | `<CODE>_V<N>_<PurposeCamel>` | `LEAVE_V1_InitialCreate` | `Persistence/Migrations/` |
| Controller + DTOs (Api) | `<CODE>_V<N>_Controller` / `<CODE>_V<N>_Dtos` | `LEAVE_V1_Controller` | `Api/Features/<CODE>/V<N>/` |
| C# file | matches class | `LEAVE_V1_LeaveService.cs` | — |
| React component | `<CODE>_V<N>_<PurposeCamel>` | `LEAVE_V1_LeaveForm` | `bpm-ui/src/features/<CODE>/V<N>/` |
| React file | matches component | `LEAVE_V1_LeaveForm.tsx` | — |
| Test file (C#) | `<CODE>_V<N>_<Aspect>Tests.cs` | `LEAVE_V1_LeaveServiceTests.cs` | `tests/Bpm.Tests/Features/<CODE>/V<N>/` |

The prefix is part of the identifier — no `namespace LEAVE.V1`,
no `LeaveForm` "inside the LEAVE folder it's obvious". Flat prefix,
everywhere.

## Versioning (no feature-flag service in MVP)

`<CODE>_V<N>_` is the only isolation chef needs in MVP:

- **Backend**: prefix means `LEAVE_V1_Case` and `LEAVE_V2_Case` are
  different classes mapped to different tables. Each version owns its
  own state machine + controller route (`/api/leave/v1/...` vs
  `/api/leave/v2/...`). Old and new coexist on the same process.
- **Frontend**: `bpm-ui/src/features/registry.ts` globs every
  `features/*/V*/manifest.ts` at startup and resolves a flow code to
  its highest version automatically. Drop a V2 manifest folder and
  the registry picks it up on next dev-server reload.

There is no `IFeatureFlagService`.

## Variables

`spec.variables[]` is the only source for environment-dependent values:

```ts
spec.variables = [
  { name: 'ERP_URL',        defaultValue: 'https://erp.acme.example', sensitive: false },
  { name: 'ERP_BEARER',     defaultValue: '',                          sensitive: true  },
  { name: 'HR_REVIEW_DAYS', defaultValue: '3',                         sensitive: false },
]
```

C# side: generate a typed `<CODE>_V<N>_Variables` record bound to
`IConfiguration`. Never call `Environment.GetEnvironmentVariable`
directly.

UI side: secrets (`sensitive: true`) never reach the UI bundle.

## ActorRef DSL

Five shapes, in priority order (use the highest that fits):

1. `expr` — `submitter.manager`, `submitter.department.head` …
   resolved by chef-side queries against SharedIdentity tables.
2. `principal` — `user:<uuid>`, `dept:<uuid>`, `role:<name>`.
3. `conditional` — `{ condition: { field, op, value }, then, else }`.
4. `collection` — `any` / `all` of N actors with optional `min_approvals`.
5. `natural_language` — last-resort string. Read it, decide. If you
   can't bake the rule cleanly into structured logic, stop and ask
   Jason.

`fallback: { text }` carries the "primary resolved to nobody" case —
also natural language. Bake structurally when you can (the LEAVE V1
spec's `approval_vp` fallback "若部門主管不在請走 VP 角色" became
`dept_head primary → role:VP fallback` in
`LEAVE_V1_LeaveService.ResolveDeptHeadAsync` + `ResolveFirstUserInRoleAsync`).

## Actor resolution helpers

Chef-side, against admin's SharedIdentity tables (read-only DbSets
already on `AppDbContext` — chef uses `db.Set<T>()`):

| Need | Tables to read |
|---|---|
| Manager of a user | `SharedUserManager` (UserId → ManagerUserId) |
| Primary dept of a user | `SharedUserDept` where `IsPrimary` |
| Dept head | `SharedDeptHead` (DeptId → HeadUserId) |
| First user in a role | `SharedRole` (by Name) → `SharedPrincipalRole` |
| Membership check | `SharedRole` + `SharedPrincipalRole` |
| Display name | `SharedPrincipal` (Id → DisplayName) |

See `LEAVE_V1_LeaveService` for the canonical helper shapes.

## Cross-cutting primitives chef MUST consume

Lead-maintained — chef imports, never reinvents.

| Concern | Primitive | Notes |
|---|---|---|
| Unified inbox | `Bpm.Application.Inbox.ITypedInboxProvider` + `InboxRow` | Required per feature. `DependencyInjection` auto-registers all impls. |
| File upload (UI) | `@/components/ui/FilePicker` | Returns `{ id, fileName, contentType, sizeBytes }` |
| File storage (backend) | `Bpm.Application.Files.IFileStorageService` | Read bytes by id |
| Buttons | `@/components/ui/button` | `variant=primary\|outline\|ghost\|destructive`, `size=xs\|sm\|md` |
| Form inputs | `@/components/ui/form` | `<Input>`, `<Textarea>`, `<Select>`, `<Field>`, `<InfoBanner>` |
| Section cards | `@/components/ui/card` | `<SectionCard>`, `<SectionTitle>` |
| Confirm dialog | `@/components/ui/ConfirmDialog` | Used by every form submit |
| Read-only field | `@/components/ui/readonly` | |
| API fetch (UI) | `@/lib/apiFetch` | Wraps fetch with the JWT |
| Auth (backend) | `BpmControllerBase.RequireUserId()` | JWT `sub` claim |
| Logging | `ILogger<T>` | Notification dispatch is a stub today — log subject + recipient |

If you need a UI control or backend service not in this table, stop
and ask Jason. Lead ships the primitive; chef consumes it.

## Spec construct → render pattern

Scalar types (`text`, `number`, `date`, `select`, `textarea`,
`checkbox`, `radio`) render with the inline `components/ui/` inputs —
no entry needed. Anything more complex:

| Spec construct | Render / handle pattern |
|---|---|
| `field.type === 'file'` | UI: `<FilePicker value={…} onChange={…} accept="…" />`. Backend: store `value.id` as `Guid?` on the entity. Read bytes via `IFileStorageService.OpenReadAsync(id)`. |
| `field.type === 'daterange'` | Render as two `<Input type="date" />`. Persist as two `DateOnly` columns (`StartDate`, `EndDate`). |
| `field.type === 'derived'` | Compute in TypeScript (`useMemo`) AND in C# (server-side, on submit). Don't trust the client value; recompute on the server before persisting. LEAVE V1's `days` field is the canonical example: `businessDaysBetween(start, end)` in both languages. |
| `field.conditional` (CEL) | Render the field's input conditionally on form state. Recheck on submit; reject the request when a required-conditional field is missing. |
| `layout.banner` | `<InfoBanner>` between section content. |
| `layout.row` | A 12-column grid with `colSpan` per fieldRef. |
| `layout.repeater` | Render an array section with add / remove buttons and an inline totals strip (`totals[]` formulas evaluated client-side). |

When the spec uses a construct that isn't here yet, **stop and ask
Jason** — lead ships the primitive (or extends this table) before
chef ships.

## Inbox provider

Every chef-cooked feature MUST implement
`Bpm.Application.Inbox.ITypedInboxProvider` and drop it in
`Application/Features/<CODE>/V<N>/` (it's business logic — actor
resolution + per-flow query — so it belongs in Application, not
Persistence). A DI assembly scan auto-registers it at startup;
verify the scan covers the Application assembly before declaring
the cook done. If it doesn't (today the scan only covers the
Persistence assembly), stop and ask — lead-side fix.

The interface:

```csharp
public interface ITypedInboxProvider
{
    string FlowCode { get; }
    int FlowVersion { get; }

    Task<IReadOnlyList<InboxRow>> GetMineAsync(Guid userId, CancellationToken ct);
    Task<IReadOnlyList<InboxRow>> GetPendingAsync(Guid userId, CancellationToken ct);
}
```

- `GetMineAsync` — cases this user submitted.
- `GetPendingAsync` — cases waiting on this user (typically
  `CurrentAssigneeUserId == userId` plus a "not terminal" filter).

The `InboxRow.title` is what shows up in Home — make it readable. The
manager-side title in LEAVE V1 is "Bob 申請 特休 3.0 天"; the
submitter-side title is "特休 3.0 天". Resolve display names once per
call (`SharedPrincipal.DisplayName`) and reuse.

`InboxRow.detailUrl` is the per-flow case-detail page chef provides
(e.g. `/cases/leave/{caseId}`). The bpm-ui router needs a matching
route — for v0 features lead seeds it; future features should
coordinate with lead before adding new top-level routes.

## Case-detail page

Every chef-cooked feature MUST ship a read-only detail page in
`bpm-ui/src/features/<CODE>/V<N>/<CODE>_V<N>_CaseDetail.tsx`,
exported via `manifest.detailComponent`. The
`/cases/:flowCode/:caseId` route auto-binds:

```ts
import type { FormManifest } from '@/features/registry'
import { LEAVE_V1_CaseDetail } from './LEAVE_V1_CaseDetail'
// …
const manifest: FormManifest = { …, detailComponent: LEAVE_V1_CaseDetail }
```

The page is **view-only** — no approve / reject buttons on detail. A
small footer banner pointing approvers at the Pending inbox keeps the
contract obvious. Fetch the case via `apiFetch('/api/<flow>/v<n>/{id}')`
and render: header (title + status pill), field grid (every business
data + spec-driven fields), 簽核 timeline (one row per approval stage
with state dot / actor display / decision timestamp / comment), plus
a "View BPMN" button that opens the shared `BpmnView` modal pre-fed
with status-derived markers (see next section).

## BPMN passthrough

Every chef-cooked feature MUST plumb the **bundle's canonical
bpmn.xml** through `manifest.bpmnXml` so `BpmnView` (both the modal
on /apply/<CODE> and the one on the case-detail page) renders the
exact diagram admin's modeler exports — gateways, diamonds, layout
and all. Three pieces:

1. **Copy the file**. Drop the bundle's `bpmn.xml` into
   `bpm-ui/src/features/<CODE>/V<N>/<CODE>_V<N>.bpmn.xml` verbatim.
   Don't rewrite, prettify, or strip the `bpmndi:` section — the
   diagram positions live there.

2. **Vite `?raw` import** in `manifest.ts`:

   ```ts
   import <CODE>_V<N>_BpmnXml from './<CODE>_V<N>.bpmn.xml?raw'
   //   …
   const manifest: FormManifest = { …, bpmnXml: <CODE>_V<N>_BpmnXml }
   ```

   `?raw` makes Vite inline the XML as a string. `vite/client` types
   (declared in `tsconfig.app.json`) already model `?raw` so tsc stays
   clean.

3. **Status-to-node mapping in `CaseDetail`**. Write a per-flow helper
   `deriveTrail(case)` that returns `{ completed: string[]; current:
   string | null }` using `spec.flow.nodes[].id` strings (NOT the
   per-flow status enum, NOT FORMS step ids). Feed it to `BpmnView`:

   ```ts
   <BpmnView
     open={bpmnOpen}
     onClose={() => setBpmnOpen(false)}
     formLabel={`${FORMS.<CODE>.code} — ${FORMS.<CODE>.label}`}
     steps={FORMS.<CODE>.steps}
     activeStep={0}
     ownerByStep={FORMS.<CODE>.ownerByStep}
     bpmnXml={manifest?.bpmnXml}
     completedNodes={trail?.completed}
     currentNode={trail?.current}
   />
   ```

   The mapping is per-flow, not generic, because each spec's gateway
   branches differ. The LEAVE V1 shape (`PendingManager` →
   `current=approval_manager`; `PendingVp` → completed includes
   `gateway_days`, current `approval_vp`; etc.) is the canonical
   reference. Skipped branches (LEAVE V1's `approval_vp` when days < 7)
   stay out of `completed` so they don't appear walked-through.

When the spec ships a node id that doesn't show up in any branch your
state machine takes, drop it from the trail — the diagram will render
it uncoloured, which is the desired "not on this case's path" look.

## Form layout

`userTask.fields[]` is canonical (types / required / CEL).
`userTask.layout[]` is the visual tree:

- `section` — titled card; optional CEL `condition` for show/hide.
- `row` — 12-column grid; each `FieldRef` has `colSpan` 3 / 4 / 6 / 8 / 12.
- `banner` — info / warn / danger inline note; optional CEL.
- `repeater` — bounded list with `itemFields[]` + `itemLayout[]` +
  optional `totals[]`.
- `fieldRef` — leaf pointing at a `fields[].id`.

Render rules:

- A `fieldRef` whose id isn't in `fields[]` is a stop-and-ask.
- A `field` not referenced anywhere in `layout[]` falls through as a
  full-width row at the end of the last section. Surface the orphan
  in your summary message.
- Repeater item fields live in their own namespace.

## Tests chef must always include

| Aspect | Test |
|---|---|
| State-machine transitions | One unit test per branch (Submit happy, each decision approve, each decision reject, each terminal state). |
| Actor resolution | One test per resolver path (direct, fallback, "no match returns ConflictException"). |
| Notification templates | One render assertion per template (subject + body substitution). |
| Form validation | Server-side: required-field rejection, conditional-field rejection. Client-side: `tsc` + chrome happy-path. |
| End-to-end | One integration test driving every node of the spec via the state-machine service. |

### Spec ⇄ sampleOrg drift

`spec.access` and `actors` may reference admin principals (real UUIDs)
that aren't in `sampleOrg.users[]` (the bundle fixture). When you find
such drift:

1. **Production code** — use the spec's literal UUID as-is.
2. **Integration tests** — substitute the nearest semantic match from
   `sampleOrg.users[]` and comment why test ≠ prod.
3. **Final report** — list every drift instance under a
   "spec ⇄ sampleOrg drift" bullet (wizard followup).

## Commit shape

One commit per logical step:

1. `feat(<CODE>_V<N>): EF entity + migration`
2. `feat(<CODE>_V<N>): state machine + notifications + controller`
3. `feat(<CODE>_V<N>): inbox provider`
4. `feat(<CODE>_V<N>): React form + manifest`
5. `feat(<CODE>_V<N>): tests`

The LEAVE V1 cook on `leave-test-4` packed (2) into one commit and
shipped (3) as part of a separate lead-side inbox commit. For future
features, ship the inbox provider in the same commit as the
controller — it's the same logical step.

A single bundle commit is fine if the work is genuinely small.
