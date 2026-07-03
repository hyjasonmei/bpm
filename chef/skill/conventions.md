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
| `bpm-svc/src/Application/Features/<CODE>/V<N>/**` | State-machine service + notification templates + `ITypedInboxProvider` impl + actor-resolution helpers + per-flow `I<CODE>_V<N>_CaseStore` interface (all business logic) |
| `bpm-svc/src/Persistence/Features/<CODE>/V<N>/**` | EF `<CODE>_V<N>_<Purpose>Configuration` + EF-backed `<CODE>_V<N>_CaseStore` impl of the Application-side interface (Persistence is the only place that knows the per-flow entity by type) |
| `bpm-svc/src/Persistence/Migrations/<ts>_<CODE>_V<N>_*.cs` + `AppDbContextModelSnapshot.cs` | `dotnet ef migrations add` regenerates these — let it drive |
| `bpm-svc/src/Api/Features/<CODE>/V<N>/**` | Controller + DTOs |
| `bpm-svc/tests/Bpm.Tests/Features/<CODE>/V<N>/**` | Unit + integration tests |
| `bpm-ui/src/features/<CODE>/V<N>/**` | React form + manifest + case-detail page + bundle's `bpmn.xml` (copy verbatim); registry globs `*/V*/manifest.ts` automatically |

Bounded lead-side touches (allowed when unavoidable, flag in final report):

| Lead-side path | Why chef has to touch it |
|---|---|
| `bpm-svc/src/Application/DependencyInjection.cs` | `AddScoped<<CODE>_V<N>_<Purpose>Service>()` registration. (The `ITypedInboxProvider` scan in this file already picks up providers automatically — usually no edit needed.) |
| `bpm-svc/src/Persistence/DependencyInjection.cs` | Bind `I<CODE>_V<N>_CaseStore` to its EF impl. |
| `bpm-ui/src/lib/workflow.ts` | Extend `FormCode` union + add `FORMS.<CODE>` entry (label / steps / ownerByStep) so `FormShell` + `BpmnView` can render the flow. |

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
| `bpm-ui/src/screens/forms/{FormShell,NotCookedYet}.tsx` | Lead-owned shared shell + not-cooked page (model-A `Reference_*.tsx` removed) |
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
2. `principal` — `user:<uuid>`, `dept:<uuid>`, `role:<code>`.
3. `conditional` — `{ condition: { field, op, value }, then, else }`.
4. `collection` — `any` / `all` of N actors with optional `min_approvals`.
5. `natural_language` — last-resort string. Read it, decide. If you
   can't bake the rule cleanly into structured logic, stop and ask
   the operator.

`fallback: { text }` carries the "primary resolved to nobody" case —
also natural language. Bake structurally when you can (the LEAVE V1
spec's `approval_vp` fallback "若部門主管不在請走 VP 角色" became
`dept_head primary → role:VP fallback` in
`LEAVE_V1_LeaveService.ResolveDeptHeadAsync` + `ResolveFirstUserInRoleAsync`).

## Actor resolution helpers

Chef-side resolvers run in **Application** and therefore can't touch
SharedX DbSets directly. Use the lead-shipped ports:

| Need | Port + method | Returns |
|---|---|---|
| Manager of a user | `IOrgChartReader.GetManagerIdAsync(userId)` | `Guid?` |
| Primary dept of a user | `IOrgChartReader.GetPrimaryDepartmentIdAsync(userId)` | `Guid?` |
| Dept head | `IOrgChartReader.GetDepartmentHeadIdAsync(deptId)` | `Guid?` |
| Dept parent | `IOrgChartReader.GetDepartmentParentIdAsync(deptId)` | `Guid?` |
| Role assignees by code | `IOrgChartReader.GetRoleAssigneesAsync(roleName, flowCode?)` | `(PrincipalId, RoleId)[]` |
| Group expansion | `IOrgChartReader.ExpandGroupAsync(groupId)` | `GroupExpansion` |
| First active User in role by code | `IPrincipalDirectory.FindFirstUserInRoleAsync(roleName)` | `Guid?` |
| Display name / email lookup | `IPrincipalDirectory.GetByIdAsync(principalId)` / `GetManyAsync(ids)` | `PrincipalInfo?` / dict |

For per-flow case data — your own table — go through your
`I<CODE>_V<N>_CaseStore` (see SKILL §3.4), never `AppDbContext`.

⚠️ The LEAVE V1 reference still queries SharedX DbSets directly
because its service lives in Persistence. **Don't copy that
pattern** — see `PURCHASE_REQUEST_V1_PurchaseRequestService` for the
Clean-Arch shape.

## Cross-cutting primitives chef MUST consume

Lead-maintained — chef imports, never reinvents.

| Concern | Primitive | Notes |
|---|---|---|
| Unified inbox | `Bpm.Application.Inbox.ITypedInboxProvider` + `InboxRow` | Required per feature. Both Application and Persistence DI scans auto-register impls. |
| Per-flow data access | chef-shipped `I<CODE>_V<N>_CaseStore` (Application) + EF impl (Persistence) — see SKILL §3.4 | Mandatory: Application can't reference Persistence, so the service must talk to its own table through this port |
| Org-chart reads | `Bpm.Application.Org.IOrgChartReader` | manager / primary dept / dept head / dept parent / group expansion / role assignees by code |
| Principal lookup | `Bpm.Application.Common.Directory.IPrincipalDirectory` | `GetByIdAsync`, `GetManyAsync`, `FindFirstUserInRoleAsync(roleName)` — display name / email / Kind / Active |
| Notification send | `Bpm.Application.Notifications.INotifyDispatcher` + `NotifyMessage` | POC binds to `FileNotifyDispatcher` writing `var/notifications.txt`; production swaps the binding |
| File upload (UI) | `@/components/ui/FilePicker` | Returns `{ id, fileName, contentType, sizeBytes }` |
| File storage (backend) | `Bpm.Application.Files.IFileStorageService` | Read bytes by id |
| Buttons | `@/components/ui/button` | `variant=primary\|outline\|ghost\|destructive`, `size=xs\|sm\|md` |
| Form inputs | `@/components/ui/form` | `<Input>`, `<Textarea>`, `<Select>`, `<Field>`, `<InfoBanner>` |
| Section cards | `@/components/ui/card` | `<SectionCard>`, `<SectionTitle>` |
| Action footer | `@/components/ui/action-footer/ActionFooter` | Viewport-fixed bottom bar — required for **both** case-detail decision buttons **and** create-form submit/cancel bars (no inline button rows). Portals to `<body>`; `FormShell` already supplies the `pb-24` clearance so the bar never covers content |
| Confirm dialog | `@/components/ui/ConfirmDialog` | Used by every form submit |
| Read-only field | `@/components/ui/readonly` | |
| API fetch (UI) | `@/lib/apiFetch` (+ `BPM_SVC_URL`, `getJwt`) | Wraps fetch with the JWT |
| JWT decode | `@/lib/jwt` (`decodeJwt`) | Read `sub` to identify the current viewer in CaseDetail |
| Auth (backend) | `BpmControllerBase.RequireUserId()` | JWT `sub` claim |
| Decision authorization | `Bpm.Application.Common.Authorization.IActorAuthorizer.CanActAsync(requiredUserId, caller, ct)` | **Required for every approval/assignee step.** Gate decisions with `if (c.XUserId is not { } x \|\| !await auth.CanActAsync(x, actorUserId, ct)) throw new ForbiddenException(...)` — NOT a raw `if (c.XUserId != caller)`. `CanActAsync` returns true for the assignee **or their active delegate**, so delegation (代理人) is honored. Submitter-only gates (withdraw/resubmit/cancel) stay strict (`c.SubmitterUserId != caller`). On the UI side, gate decision buttons with `assignee === viewer \|\| useDelegatedFor().includes(assignee)` (hook `@/lib/useDelegatedFor`), never a bare `=== viewer`. |
| **Parallel approval (並簽)** | `Bpm.Application.Parallel.IParallelApprovalService` | **The only way to cook a parallel gateway** (concurrent multi-approver step). Do NOT hand-roll per-approver columns or a fork/join engine. `OpenAsync(flowCode, ver, caseId, gatewayNodeId, slots, threshold)` opens the group + one Pending slot per branch; `DecideAsync(slotId, actor, approve, comment)` records + recomputes (returns `DecisionResult.GroupStatus`: `Open`/`Approved`/`Rejected`); `GetAsync(caseId, gatewayNodeId)` for display; `FindPendingForUserAsync(flowCode, userId, roleCodes)` → `PendingSlot[]` (carries `CaseId`) for the inbox. Threshold = N/N (全簽/AND) or M/N (門檻); any single reject → group Rejected + rest Skipped. **Reference cook: `CONTRACT_REVIEW` V1.** See §"Parallel gateway (並簽)". |
| Parallel checklist (UI) | `@/components/ParallelApprovalPanel` + `BpmnView` `currentNodes[]`/`rejectedNodes`/`skippedNodes` | Case-detail 並簽 checklist + multi-node BPMN highlight. |
| Logging | `ILogger<T>` | Diagnostic only — real delivery goes through `INotifyDispatcher` |

If you need a UI control or backend service not in this table, stop
and ask the operator. Lead ships the primitive; chef consumes it.

## Spec construct → render pattern

Scalar types (`text`, `number`, `date`, `select`, `textarea`,
`checkbox`, `radio`) render with the inline `components/ui/` inputs —
no entry needed. Anything more complex:

| Spec construct | Render / handle pattern |
|---|---|
| `field.type === 'file'` | UI: `<FilePicker value={…} onChange={…} accept="…" />`. Backend: store `value.id` as `Guid?` on the entity. Read bytes via `IFileStorageService.OpenReadAsync(id)`. **Case-detail read-back: render the stored id with `<AuthedFileLink id={…}>下載</AuthedFileLink>` (from `@/components/ui/FilePicker`) — NOT a plain `<a href="…/api/files/{id}" target="_blank">`. `GET /api/files/{id}` is `[Authorize]`; a bare link opens a new tab with no JWT → 401 ("找不到"). `AuthedFileLink` fetches the blob with the bearer token then opens it.** |
| `field.type === 'daterange'` | Render as two `<Input type="date" />`. Persist as two `DateOnly` columns (`StartDate`, `EndDate`). |
| `field.type === 'derived'` | Compute in TypeScript (`useMemo`) AND in C# (server-side, on submit). Don't trust the client value; recompute on the server before persisting. LEAVE V1's `days` field is the canonical example: `businessDaysBetween(start, end)` in both languages. |
| `field.conditional` (CEL) | Render the field's input conditionally on form state. Recheck on submit; reject the request when a required-conditional field is missing. |
| `layout.banner` | `<InfoBanner>` between section content. |
| `layout.row` | A 12-column grid with `colSpan` per fieldRef. |
| `layout.repeater` | Render an array section with add / remove buttons and an inline totals strip (`totals[]` formulas evaluated client-side). |
| `gateway.kind === 'parallel'` (並簽) | A concurrent multi-approver step. Do NOT model per-branch columns on the case. Use `IParallelApprovalService` — see §"Parallel gateway (並簽)". The spec shape: a gateway node `{ id, kind: 'parallel', join: { threshold } }` fanning out to N approval user-tasks (each with an `assignee`: role or user), converging on a join gateway. `threshold` = branch count for 全簽(AND), 1 for 或簽(OR), or M for 門檻 M/N. |

When the spec uses a construct that isn't here yet, **stop and ask
The operator** — lead ships the primitive (or extends this table) before
chef ships.

## Parallel gateway (並簽)

When the spec has a `gateway.kind === 'parallel'` step (concurrent
multi-approver — 並簽 / 會簽 / 或簽), **do not** hand-roll fork/join or
per-approver columns. Consume `IParallelApprovalService` and copy a reference
cook across all five layers. **Two references**: `CONTRACT_REVIEW` V1 = 全簽/AND
(2 branches, threshold N/N); `COMMITTEE_REVIEW` V1 = 門檻/quorum (3 branches,
threshold 2/3) — copy whichever join semantics matches; they differ only in the
`threshold` passed to `OpenAsync`. The recipe:

**Domain** — the case status enum has ONE parallel-pending state
(e.g. `PendingParallelReview`) plus the terminals (`Completed`/`Rejected`).
The case entity holds business data + minimal workflow state; the
concurrent decisions live in the primitive, NOT on the case. Keep the
gateway + branch node ids as `const` on the service (they must match the
`.bpmn.xml`).

**Persistence** — `I<CODE>_V<N>_CaseStore` needs `Add` / `FindByIdAsync` /
`FindMineAsync` / `FindByIdsAsync` (**no `FindPendingAsync`** — pending is
served by the primitive's slot query). EF config + migration as usual.

**Application — service:**
- `SubmitAsync`: validate → create case (`PendingParallelReview`) → `await parallel.OpenAsync(FlowCode, Ver, case.Id, GatewayNodeId, slots, threshold, ct)` where `slots` are `SlotSpec(nodeId, roleCode, userId)` from the spec's branches (`threshold` = branch count for AND / 1 for OR / M for 門檻).
- `DecideAsync(caseId, slotId, actorUserId, approve, comment)`: `var r = await parallel.DecideAsync(slotId, actorUserId, approve, comment, ct);` then advance: `Approved` → case `Completed` (+`CompletedAt`); `Rejected` → case `Rejected`; `Open` → leave pending. (Authorization is enforced inside `DecideAsync` via `IActorAuthorizer` — don't re-gate.)

**Application — inbox provider:** `GetMineAsync` from the store;
`GetPendingAsync` = `var roles = await directory.GetRoleCodesForUserAsync(userId, ct); var slots = await parallel.FindPendingForUserAsync(FlowCode, userId, roles, ct);` then load `store.FindByIdsAsync(slots.Select(s => s.CaseId).Distinct())` → rows. Skipped/resolved slots drop out automatically.

**Notifications:** inject `INotifyDispatcher` + `IPrincipalDirectory`; on submit,
notify the submitter (submitted ack) AND every holder of each branch role
(`directory.GetUsersInRoleAsync(role)` per branch → one 待簽 message to all
concurrent approvers); on resolve, notify the submitter completed / rejected.
Both reference cooks show this (`*_NotificationTemplates` + `Notify*` helpers).

**Api — controller:** `POST /api/<flow>/v<n>` (submit),
`POST /api/<flow>/v<n>/{caseId}/slots/{slotId}/decision` `{approve, comment}`,
`GET /api/<flow>/v<n>/{caseId}` — detail builds a review DTO from
`parallel.GetAsync(caseId, gatewayNodeId)`: `policyLabel` (全簽 vs 門檻 M/N),
`approvedCount`, `threshold`, and one slot view per branch (`slotId`,
`roleCode`, `state` = slot.Decision lowercased, decider name, comment, time).

**UI — case-detail:** render `<ParallelApprovalPanel>` from the review;
for each `pending` slot show 核准/退件 behind `<ConfirmDialog>` posting to the
decision endpoint (403 = not your slot). "檢視流程圖" opens `<BpmnView>` with
`completedNodes` = approved node ids, `currentNodes` = pending, `rejectedNodes`
= rejected, `skippedNodes` = skipped — so multiple nodes light simultaneously.
**Also colour the STRUCTURAL nodes** (start / fork gateway / join gateway / end —
they aren't slots, so they never colour on their own): add start + fork to
`completedNodes` once submitted, and add join + end once the case is `Completed`.
Without this a finished case leaves its end (完成) node unlit even though the flow
is done.

**`.bpmn.xml`** — a real `bpmn:parallelGateway` fork → N `bpmn:userTask`
branches → a `bpmn:parallelGateway` join → end. **Node ids MUST equal** the
slot `NodeId`s and the case `GatewayNodeId` (that's how highlight maps state to
diagram). Ship full BPMN DI (bounds + waypoints), same as any other cook.

**Register** the flow as usual; seed any new approver role (e.g. `LEGAL`) +
a holder in the admin `Seeder` if the spec references a role that doesn't exist.

## Visual baseline — crib from a model-B feature form

The shipped **model-B feature forms** under
`bpm-ui/src/features/<CODE>/V1/*Form.tsx` are the visual ground truth
for the customer-facing UI — their layout (section cards, two-column
grids, repeater header bars, currency-paired amount inputs, right-side
action gutters) is what the operator expects every chef-cooked form to look
like, and they are already wired the model-B way. (The old
`screens/forms/Reference_*.tsx` set + its `useFormRuntime` /
`useFlowSubmit` runtime were removed — the feature forms are now the
single source of visual truth.)

**Pick one before you write a line of JSX.** A bare form built only
from `<Input>` / `<Select>` will work but look amateur next to its
siblings; opening the closest feature form first is what gets the cook
past "functional" into "presentable".

> **Canonical exemplar:**
> `bpm-ui/src/features/VENDOR_EXPENSE/V1/VENDOR_EXPENSE_V1_VendorExpenseForm.tsx`.
> Sectioned cards, dark invoice header band, currency-prefixed amount,
> grouped grand-total — all on correct model-B wiring (`FormShell` +
> `apiFetch` + `ActionFooter` + `ConfirmDialog`). For invoice / repeater
> / money forms crib its structure directly.

### Shape → feature-form lookup

| Spec shape | Closest feature form | What to crib |
|---|---|---|
| Repeater + amount + currency + running total | `VENDOR_EXPENSE/V1` · `TEO/V1` | Per-row `SectionCard` with header bar, 2-col grid for paired fields, currency `<Select>` glued to `<Input type=number>`, dual NTD/USD display, right-side gutter with Plus/Copy/Trash row actions, Totals row in its own `SectionCard` at the bottom |
| Line-item purchase / PO | `PURCHASE_REQUEST/V1` · `FAP/V1` | Line-item rows + grand total; FAP also shows an auto-generated PO number milestone |
| Single form + multi-section approval-friendly | `APE/V1` · `LEAVE/V1` | Stacked `SectionCard` + `SectionTitle` blocks per logical group; `InfoBanner` for policy text; currency-paired amount (APE). Good shape for any approval-heavy single-form flow |
| File-upload / attachment field | `LEAVE/V1` (conditional cert `<FilePicker>`) | `<FilePicker>` for `field.type==='file'`; case-detail reads it back via `<AuthedFileLink>` |
| Personnel-action form (lots of name / dept / date fields) | `EOB/V1` · `ETM/V1` | Dense two-column grids, bilingual labels via `FieldLabel`, `<Field hint>` for inline help |
| Asset / disposal form | `FAD/V1` | Manager-judged single-approval form with asset fields |
| Read-mostly view (case detail style) | any `*_CaseDetail.tsx` (e.g. `APE/V1`) | `SectionCard` blocks with read-only paired data + 簽核 timeline + Stepper + BpmnView; use as the **case-detail** baseline, not the form baseline |

When the spec doesn't match any shape exactly, pick the closest from
the list above and tell the user in your cook-complete report which one
you used.

### DO copy (structural)

- `SectionCard` + `SectionTitle` from `@/components/ui/card` for every
  top-level block — never a bare `<div>`.
- 2-column `grid grid-cols-2 gap-5` for paired fields; `gap-x-6
  gap-y-2` flex rows for inline label+input chips.
- `FieldLabel required` / `FieldLabel tip="..."` (from
  `@/components/ui/form`) for every field — the asterisk + tooltip
  hint are part of the visual language.
- Repeater header bar — two flavours, both fine:
  - **Dark band (preferred for invoice/money rows, per the exemplar):**
    `flex flex-wrap items-center gap-x-3 gap-y-2 bg-slate-700 px-4 py-2
    text-white` with `Invoice #N` on the left and the per-row key
    control (currency `<select>` styled `bg-slate-600`) + remove button
    pushed right (`ml-auto`).
  - **Light band:** `… border-b border-rule bg-slate-50 px-4 py-2.5`
    with the row index left and 2-3 "summary" inputs inline.
- Right-side row-action gutter (`flex flex-col items-center gap-1.5
  border-l border-rule bg-slate-50/60 p-2`) with `Plus`, `Copy`,
  `Trash2` lucide icons stacked vertically.
- Currency-paired amount: `<Select className="w-24 flex-shrink-0">`
  glued to `<Input type=number className="text-right font-mono">`,
  with NTD/USD computed-total below.
- `InfoBanner` for policy / reminder text at the top of the form
  (Chinese + English subtitle is the established pattern).
- Totals row in its own `SectionCard` at the bottom with
  right-aligned `font-mono` amounts.

### Required model-B wiring (don't reintroduce model A)

The feature forms are already clean — keep them that way. Every cook:

- Uses `<FormShell code="<CODE>" activeStep={0} persona={persona as
  PersonaCode} mode="create">` — it draws the step rail, requestor
  summary, View-BPMN and copy-from-existing chrome that make a form
  look finished. Dropping it is what leaves a cook looking "plain".
- Submits via a plain `<form onSubmit>` / handler calling `apiFetch`
  against `/api/<flow>/v<n>/...` — there is **no** `useFormRuntime` /
  `useFlowSubmit` / `useFlowTask` (those model-A hooks were deleted).
- Wraps the submit / cancel bar in `ActionFooter` from
  `@/components/ui/action-footer/ActionFooter` — never an inline
  `SectionCard` button row, never a model-A `ActionBar`.
- Pulls `<Select>` options from `field.options[]` in the spec (or a
  real catalog API) — no `@/lib/mocks` demo catalogs.

### Workflow recipe

1. Open the closest `features/<CODE>/V1/*Form.tsx` side-by-side with
   the spec (see the shape lookup above).
2. Copy its JSX structure into your `<CODE>_V<N>_<Purpose>Form.tsx`
   and adapt fields/sections to your spec's `fields[]` + `layout[]`.
3. Swap options to `field.options` from the spec.
4. Point the submit handler at your controller's POST endpoint via
   `apiFetch`.
5. Confirm the result by eyeballing it next to the form you cribbed —
   if they don't look like cousins, the layout needs a re-pass.

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

`InboxRow.detailUrl` is the per-flow case-detail page chef provides.
It MUST be `/cases/{slug}/{caseId}` where **`{slug}` is the flowCode
lower-cased with underscores preserved** — i.e. `flowCode.ToLower()`,
NOT a kebab-case slug. The bpm-ui router resolves it via
`lookupForm(slug.replace('-','_').toUpperCase())` against the manifest
`code` (which is UPPER_SNAKE).

```
flowCode LEAVE            → /cases/leave/{id}            ✓
flowCode VENDOR_EXPENSE   → /cases/vendor_expense/{id}   ✓
flowCode PURCHASE_REQUEST → /cases/purchase_request/{id} ✓
                          → /cases/purchase-request/{id} ✗ HYPHEN — historically
                            shipped this; toUpperCase gave "PURCHASE-REQUEST"
                            which missed the registry key and rendered
                            "還沒提供 case detail view". Single-word flows hid
                            the bug; multi-word flows break. Always underscore.
```

Do NOT borrow the REST route's kebab-case (`/api/purchase-request/v1`)
for the detailUrl — the REST path and the UI `/cases/` slug use
different separators. The bpm-ui router needs a matching route — for
v0 features lead seeds it; future features should coordinate with lead
before adding new top-level routes.

## Case-detail page

Every chef-cooked feature MUST ship a read-only-by-default detail page
in `bpm-ui/src/features/<CODE>/V<N>/<CODE>_V<N>_CaseDetail.tsx`,
exported via `manifest.detailComponent`. The
`/cases/:flowCode/:caseId` route auto-binds:

```ts
import type { FormManifest } from '@/features/registry'
import { LEAVE_V1_CaseDetail } from './LEAVE_V1_CaseDetail'
// …
const manifest: FormManifest = { …, detailComponent: LEAVE_V1_CaseDetail }
```

The page renders: header (title + status pill), a **progress stepper**
(see below), field grid (every business data + spec-driven field), 簽核
timeline (one row per approval stage with state dot / actor display /
decision timestamp / comment), plus a "View BPMN" button that opens the
shared `BpmnView` modal pre-fed with status-derived markers (see next
section).

**Progress stepper (required).** The detail page MUST show the SAME
horizontal stepper the create form shows, so a case reads consistently
whether you're filling it in or tracking it. Render the shared
`<Stepper>` from `@/components/Stepper` in a slate bar near the top
(right after `<FlowStateBanner/>`), driven by the case status:

```tsx
import { Stepper } from '@/components/Stepper'
import { FORMS } from '@/lib/workflow'
// …inside the rendered case, after <FlowStateBanner/>:
<SectionCard className="!p-0">
  <div className="bg-slate-50 px-4 py-2">
    <Stepper steps={FORMS.<CODE>.steps} activeStep={activeStepFor(data.status)} withZh />
  </div>
</SectionCard>
```

Add a module-level `activeStepFor(status): number` with an **exhaustive**
switch mapping each status to the 0-based index into `FORMS.<CODE>.steps`:
a "pending <approver>" status → that approver's step; a send-back
(`ResubmitRequired`) → the first approval step; the terminal success
status (`Completed`) → the last step; reject/cancel terminals → the
approval step where they stopped. APE V1 is the canonical reference.

**⚠️ Steps MUST mirror YOUR cooked state machine 1:1 — never the cribbed
form's.** This is the #1 stepper bug: when you crib another flow's
`FORMS.<CODE>` entry, you inherit ITS stages (e.g.
`CONFIRM & PRINT`, `FIN REVIEW`, `NOTIFY ADM`, `IT JUDGE`) — but if your state
machine doesn't actually transition through them, the stepper lights those
stages green on completion even though they never ran (a lie to the user).
Define `steps` (and the parallel `ownerByStep`) as exactly: `apply` + one entry
per **real** transition your service performs (each `Pending*` gate, plus any
**automatic** milestone the service writes — e.g. FAP's auto-generated PO
number, which IS a real step) + `close`. Then `activeStepFor` maps every status
to its OWN step, with no skipped/phantom indices. If a stage label names an
actor/role, it must be the actor your service actually resolves (FAD's first
gate resolves the submitter's **manager**, so it's "MANAGER / 主管判別", not
"IT JUDGE"). Mismatch = bug; the stepper is a contract, not decoration.

**Status badge (`<StatusBadge kind=…>`) must name the real stage.** The
`StatusKind`s in `components/ui/badge.tsx` carry hardcoded EN/ZH labels. Map
each `Pending*` status to the kind whose label matches that stage — do NOT
borrow `fin_review` / `it_spec_review` for a stage that isn't finance / IT-spec
review (that was the 2nd-bug: a setup/handover/verification stage showing
"FIN Review"). If no existing kind fits your stage, add one to `badge.tsx`
(this is a lead edit — flag it) rather than reusing a wrong-labelled kind.

### Action buttons → `<ActionFooter>`

When the current viewer is the active assignee, the case-detail page
exposes the node's `actions[]` as buttons via the shared
`<ActionFooter>` primitive in `@/components/ui/action-footer`. **Inline
buttons are not allowed** — the sticky footer keeps decision controls
uniform across flows and reserves the left slot for future SLA / hint
chips.

```tsx
import { ActionFooter, type ActionFooterItem } from '@/components/ui/action-footer/ActionFooter'

const footerActions: ActionFooterItem[] = useMemo(() => {
  if (!isCurrentAssignee) return []
  // Derive from spec.actions[] for this stage. Translate kind →
  // backend route + variant per SKILL.md §3.5.
  return [
    { id: 'reject',  label: '退件 / Reject',  variant: 'destructive', pending, onClick: () => postDecision(false) },
    { id: 'approve', label: '核准 / Approve', variant: 'primary',     pending, onClick: () => postDecision(true)  },
  ]
}, [isCurrentAssignee, pending])

return (
  <div className="mx-auto max-w-screen-lg space-y-4 p-6 pb-24">
    {/* … rest of the page … */}
    <ActionFooter hint={footerHint} actions={footerActions} />
  </div>
)
```

Comment / archive note text inputs live inside in-page SectionCards
(so the user can scroll back through the case while typing); the
footer reads their state for the onClick handler. Add `pb-24` (or
similar) to the page's scroll container so the footer doesn't cover
real content.

Variants map from `TaskAction.kind`: `submit`/`approve`/`complete` →
`primary`, `reject`/`cancel`/`revoke` → `destructive`,
`save_draft`/`custom` → default. Hide actions whose `guard` evaluates
to false (or disable via `disabled` + `title`).

**Every action confirms (product requirement).** `ActionFooter` shows a
styled `ConfirmDialog` before firing `onClick` for **every** item by default —
you don't wire confirm per-action and you MUST NOT use `window.confirm`. The
auto modal derives its copy from the item `label` + `variant` (destructive →
danger tone). Tailor it by passing `confirm: { titleZh, description,
confirmText, tone }` (e.g. withdraw → `description: '撤回後無法復原。'`). Only a
genuinely safe / non-mutating action (e.g. a `resubmit` that just navigates to
the edit form, which has its own submit confirm) may opt out with
`confirm: false`. Comment inputs for reject still live in an in-page
SectionCard; the modal is the are-you-sure gate.

### Create-form submit bar → `<ActionFooter>`

The submitter form (`<CODE>_V<N>_<Purpose>Form.tsx`, create mode) uses
the **same** `<ActionFooter>` for its 取消 / 送出 bar — **not** a
trailing `<SectionCard>` button row. The footer is viewport-fixed
(portals to `<body>`), so it stays pinned at the bottom on both short
and long forms. `FormShell` already carries `pb-24`, so don't add your
own bottom spacer. Put the error / status hint in `hint`, and force the
submit-button spinner via the item's `pending` flag (no inline
`<Loader2>`):

```tsx
import { ActionFooter } from '@/components/ui/action-footer/ActionFooter'

// …inside the FormShell children, after the last field SectionCard:
<ActionFooter
  hint={error ? <span className="text-danger">{error}</span> : <span>送出後將通知您的主管。</span>}
  actions={[
    { id: 'cancel', label: '取消', variant: 'ghost', disabled: pending, onClick: () => navigate('/') },
    { id: 'submit', label: isResubmit ? '重新送出' : '送出申請', variant: 'primary', pending, disabled: !valid, onClick: attemptSubmit },
  ]}
/>
```

The `ConfirmDialog` (when `confirm=true`) stays a sibling — the footer's
`onClick` only opens it. See `APE_V1_AdvancePaymentForm.tsx` for the
canonical create-form shape.

### Retired-flow banner

Mount `<FlowStateBanner flowCode={'…'} flowVersion={N} />` from
`@/components/ui/flow-state-banner/FlowStateBanner` near the top of
the case detail (above the status grid). Renders nothing for live
(Published) flows; surfaces a warning row when the case's flow has been
retired by admin — existing cases stay actionable, but the launcher is
hidden, so the user needs the cue when they hit the case via deep
link. (Live-gate is `state === 'Published'`: a flow goes Committed →
Approved [reviewed] → Published [live in this env]; chef code never
touches this — lead owns the lifecycle + launcher gate.) Backed by a
single cached `useFlowRegistry` fetch.

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
