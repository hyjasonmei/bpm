# chef conventions — naming, paths, variables

Distilled from `openspec/specs/flowcook-chef` for quick lookup during
a chef session. The spec wins if these drift.

## Path map

chef writes inside csprojs the solution already references —
specifically inside `Features/<CODE>/V<N>/` subtrees of those csprojs.
Don't create new csproj files or edit `bpm-svc.slnx`.

| Allowed (write) | Why this csproj |
|---|---|
| `bpm-svc/src/Persistence/Features/<CODE>/V<N>/**` | EF entity + configuration + handlers live with the rest of Persistence so they participate in `OnModelCreating` via `ApplyConfigurationsFromAssembly` |
| `bpm-svc/src/Api/Features/<CODE>/V<N>/**` | Controllers + on-the-wire DTOs auto-pick up from Api's `MapControllers` |
| `bpm-svc/src/Persistence/Migrations/*.cs` | EF migrations stay in the existing `Migrations/` folder so `dotnet ef` finds them; file name still carries the `<CODE>_V<N>_` prefix |
| `bpm-svc/tests/Bpm.Tests/Features/<CODE>/V<N>/**` | tests folder mirrors the feature layout |
| `bpm-ui/src/features/<CODE>/V<N>/**` | the registry globs `*/V*/manifest.ts` here automatically |

| Read-only | Notes |
|---|---|
| `bpm-svc/src/{Api,Application,Domain,Persistence,Functions,SeedCli}/**` outside `Features/<CODE>/V<N>/` | runtime + framework |
| `bpm-admin-svc/**`, `bpm-admin-ui/**` | admin tooling — not chef's territory |
| `bpm-www/**`, `syncer/**`, `chef/**`, `docs/**`, `openspec/**` | docs / sibling services / self |
| `bpm-ui/src/screens/forms/Reference_*.tsx` | hand-coded visual reference set |

If you need to read something that isn't listed, it's probably fine —
the read list is "you'll need these"; the forbidden list is hard.

## Naming

`<CODE>` is the spec's `meta.flowCode` upper-cased. `<N>` is the
spec's `meta.flowVersion` integer.

| Artifact | Pattern | Example (LEAVE V1) |
|---|---|---|
| C# class | `<CODE>_V<N>_<PurposeCamel>` | `LEAVE_V1_SubmitHandler` |
| C# file | matches class | `LEAVE_V1_SubmitHandler.cs` |
| DB table | `<code>_v<n>_<purpose_snake>` | `leave_v1_leave_request` |
| EF migration | `<CODE>_V<N>_<PurposeCamel>` | `LEAVE_V1_InitialCreate` |
| React component | `<CODE>_V<N>_<PurposeCamel>` | `LEAVE_V1_LeaveForm` |
| React file | matches component | `LEAVE_V1_LeaveForm.tsx` |
| Test file (C#) | `<CODE>_V<N>_<Aspect>Tests.cs` | `LEAVE_V1_ApprovalTests.cs` |
| Test file (TS) | `<CODE>_V<N>_<aspect>.test.tsx` | `LEAVE_V1_layout.test.tsx` |

The prefix is part of the identifier — no `namespace LEAVE.V1`,
no `LeaveForm` "inside the LEAVE folder it's obvious". Flat prefix,
everywhere.

## Versioning (no feature-flag service in MVP)

`<CODE>_V<N>_` is the only isolation chef needs in MVP:

- **Backend**: prefix means `LEAVE_V1_LeaveRequest` and
  `LEAVE_V2_LeaveRequest` are different classes mapped to different
  tables (`LEAVE_V1_leave_request` vs `LEAVE_V2_leave_request`). The
  V2 controller has its own route. Old and new coexist on the same
  process without colliding; the V2 work can ship without touching
  V1's wiring at all.
- **Frontend**: `bpm-ui/src/features/registry.ts` globs every
  `features/*/V*/manifest.ts` at startup and resolves a flow code to
  its **highest** version automatically. Drop a V2 manifest folder
  and the registry picks it up on next dev-server reload.

There is no `IFeatureFlagService` and chef does NOT invent one. If
you genuinely need "toggle V2 off without redeploying" later, that's
a separate change request — not chef's call to scaffold.

## Variables

`spec.variables[]` is the only source for environment-dependent values.

```ts
spec.variables = [
  { name: 'ERP_URL',        defaultValue: 'https://erp.acme.example', sensitive: false },
  { name: 'ERP_BEARER',     defaultValue: '',                          sensitive: true  },
  { name: 'HR_REVIEW_DAYS', defaultValue: '3',                         sensitive: false },
]
```

C# side: chef generates a typed `<CODE>_V<N>_Variables` record bound
to `IConfiguration` keyed by spec.variables names. Never read
`Environment.GetEnvironmentVariable` directly — go through the
generated record so a future runtime VariableResolver can intercept.

UI side: variables that are user-relevant flow into the form via
`spec.userTasks[].fields[].derivedFrom` CEL using `${VAR}` syntax.
Secrets (`sensitive: true`) never reach the UI bundle.

Any `${ref}` chef sees in `derivedFrom` / decision branches / actor
conditions that doesn't resolve to a `spec.variables[]` name or a
`spec.userTasks[*].fields[*].id` is a **stop-and-ask** trigger.

## ActorRef DSL

Five shapes, in priority order (use the highest that fits):

1. `expr` — `submitter.manager`, `submitter.department.head` ...
   resolved by the runtime `IActorResolver`.
2. `principal` — `user:alice`, `dept:engineering`, `role:hr_lead`, etc.
3. `conditional` — `{ condition: { field, op, value }, then, else }`.
4. `collection` — `any` / `all` of N actors with optional `min_approvals`.
5. `natural_language` — last-resort string. Read it, decide. If you
   can't bake the rule cleanly into structured logic, stop and ask
   Jason; do not silently approximate.

Every shape may carry a `fallback: { text }` for the "primary
resolved to nobody" case. The fallback text is natural language for
the same reason — read it, decide, escalate if ambiguous.

## Cross-cutting concerns chef MUST NOT reinvent

Some data is **runtime-wide**, not per-flow. The core platform already
exposes it; chef-cooked feature code MUST consume the existing surface
instead of building a parallel per-flow version.

| Concern | Core surface (use this) | What chef MUST NOT build |
|---|---|---|
| "Start a new instance" | `POST /api/processes` (+ `useFlowSubmit` / `useFormRuntime.submitCreate`) | `POST /api/<flow>/v<n>/(submit\|apply\|create)` that writes a chef-owned table directly, bypassing `IProcessRuntime` |
| "What's in the current user's inbox?" | `GET /api/tasks/mine` + `useMyTasks(status)` | `GET /api/<flow>/v<n>/pending`, `<PendingTasksCard>` inside `Features/` |
| "What did the current user submit?" | `GET /api/processes/mine` + `useMyInstances(status)` | `GET /api/<flow>/v<n>/my-cases`, `<MyCasesList>` inside `Features/` |
| "What's the status of this instance?" | `GET /api/processes/{id}` + `GET /api/processes/{id}/history` | `GET /api/<flow>/v<n>/case/{id}` returning runtime state |
| "Approve / reject / return this task" | `POST /api/tasks/{id}/submit` (+ `useFormRuntime`) | Per-flow state-mutation endpoints named after a spec role (`POST /api/<flow>/v<n>/<role>-decision/{caseId}` and friends — `<role>` is spec-defined and varies) |
| "Track per-step status / current approver / per-role comments" | `ProcessTask` + `TaskHistory` (read via `useFlowTask`) | Per-flow status enum keyed by approver role (`Pending<Role>` / `Approved` / `Rejected` …), per-role assignment columns (`<Role>UserId`), per-role decision columns (`<Role>Approved`, `<Role>Comment`, …) on a chef entity. The `<Role>` placeholder is whatever the spec calls the actor — chef must NOT bake any of them into a column. |
| Task assignment, delegation, SLA | `ProcessRuntime` does this from the spec | Per-flow assignment / SLA tables |

If the form *needs* extra display data that the spec doesn't already
inline into form-data (e.g. running quota, dependency lookup), that's
a per-flow **business data** read — fine. Name the endpoint after the
business resource (`/api/leave/v1/balance`), not after a runtime
concept (`/pending`, `/inbox`, `/my-cases`).

When a regression slips in (chef-cooked code grows a `Pending`
controller or a `<PendingTasksCard>`), it's a stop-and-flag for the
next chef session — drop the file, swap callers to the global hooks.

## Core UI components chef MUST consume

Lead maintains these in `bpm-ui/src/components/ui/`. Chef forms import
from these paths — do NOT re-roll an equivalent inside `Features/`.

| Use | Canonical import |
|---|---|
| Buttons | `@/components/ui/button` (`<Button variant="primary\|outline\|ghost\|destructive" size="xs\|sm\|md" />`) |
| Form inputs | `@/components/ui/form` (`<Input>`, `<Textarea>`, `<Select>`, `<Field label hint required error>`, `<InfoBanner>`) |
| Section cards | `@/components/ui/card` (`<SectionCard>`, `<SectionTitle>`) |
| Confirm dialog | `@/components/ui/ConfirmDialog` (`<ConfirmDialog open title titleZh description tone onConfirm onCancel />`) |
| Modal primitive | `@/components/ui/Modal` (wrap Radix Dialog — use this if you need a non-confirm dialog shape, but prefer existing wrappers) |
| Read-only field | `@/components/ui/readonly` |

If you need a UI control that isn't in this table, that's a stop-and-ask
for lead — same rule as the spec-construct table below.

## Spec construct → core primitive table

This is the lookup chef MUST hit when a `userTask.fields[]` entry has a
non-scalar `type`, or when any other spec construct needs cross-cutting
machinery. **Scalar types** (`text`, `number`, `date`, `select`,
`textarea`, `checkbox`, `radio`) render with the inline `components/ui/`
inputs — no entry needed. Anything more complex MUST be in this table
before chef ships it.

| Spec construct | UI primitive (import) | Backend pattern | Form-data shape |
|---|---|---|---|
| `field.type === 'file'` | `<FilePicker value={…} onChange={…} accept="…" />` from `@/components/ui/FilePicker` | Store the returned `{id}` (Guid string). Handlers read bytes back via `IFileStorageService.OpenReadAsync(id)` (Application). Never write blob tables or filesystem code inside `Features/`. | `{ id: string, fileName: string, contentType: string, sizeBytes: number }` |

When the spec uses a construct that isn't here yet, **stop and ask
Jason** — lead will ship the primitive (see `lead/skill/SKILL.md`) and
add a row above. Do not approximate it inside `Features/<CODE>/V<N>/`.

The forms-side import path is always `@/components/ui/<Primitive>`; the
backend-side import path is always under `Bpm.Application.<Area>` (the
interface) or, if you need a concrete impl, `Bpm.Persistence.<Area>`.
Feature code never reaches into another feature folder.

## Form layout

`userTask.fields[]` is canonical (types / required / CEL).
`userTask.layout[]` is the visual tree:

- `section` — titled card; optional CEL `condition` for show/hide.
- `row` — 12-column grid; each `FieldRef` has `colSpan` 3 / 4 / 6 / 8 / 12.
- `banner` — info / warn / danger inline note; optional CEL.
- `repeater` — bounded list, each item carries its own `itemFields[]`
  + `itemLayout[]`. Optional `totals[]` aggregations
  (CEL formulas, `currency` / `number` / `percent` format).
- `fieldRef` — leaf pointing at a `fields[].id`.

Render rules:

- A `fieldRef` whose id isn't in `fields[]` is a stop-and-ask.
- A `field` not referenced anywhere in `layout[]` falls through as a
  full-width row at the end of the last section (don't drop it
  silently — surface the orphan in your summary message).
- Repeater item fields live in their own namespace; don't conflate
  `outer.amount` with `repeater.amount` even though they share a label.
- A `field.type` that isn't a scalar AND isn't in the §spec-construct
  table is a stop-and-ask — lead needs to ship the primitive first.

## Tests chef must always include

| Spec artifact | Test |
|---|---|
| `notifications[]` | One render assertion per template (variables interpolated). |
| `decisions[]` (gateway) | One branch test per `branches[]` entry, plus the default. |
| `approvals[]` | One approve + one reject test per approval node. |
| `integrations.items[]` | One happy-path mock test per item. |
| `userTasks[]` | One form test per task: required-field validation, layout structure (`screen.getByText(sectionTitle)` etc), CEL conditional visibility. |
| Flow as whole | One end-to-end happy-path test exercising every node. |

Failing tests block the commit — don't merge red.

### Spec ⇄ sampleOrg drift

The wizard's PrincipalPicker reads principals from admin's live
`Admin_Principals` table, so `ActorRef.principal.ref = user:<uuid>`
in `spec.json` carries a UUID that exists in production but is
**not guaranteed to be in `sampleOrg.users[]`** (the bundle's test
fixture). When you find such drift:

1. **Production code (handler / controller / dispatcher)** — use the
   spec's literal UUID as-is. Hand it to `IActorResolver` and let
   runtime resolve against real admin org. Do NOT rewrite the spec.
2. **Integration tests** — substitute the missing principal with the
   nearest semantic match from `sampleOrg.users[]` when seeding the
   in-memory scenario (e.g. spec wants `user:02724add-…` for the VP
   approver → test seeds `user:33333333-…` "Vera VP" from sampleOrg).
   Comment the substitution so the next reader knows why test ≠ prod.
3. **Final report** — list every drift instance under a
   "spec ⇄ sampleOrg drift" bullet. Each one is a wizard followup
   (PrincipalPicker should append picked principals into sampleOrg
   automatically so spec + sampleOrg round-trip).

This rule covers `ActorRef.principal` ⊕ `ActorRef.collection.actors`
⊕ `recipients[]` — anything that names a principal by UUID.

## Commit shape

One commit per logical step is easier for Jason to review in GitKraken
than one giant commit:

1. `feat(<CODE>_V<N>): EF entity + migration`
2. `feat(<CODE>_V<N>): submit handler + controller`
3. `feat(<CODE>_V<N>): approval / decision handlers`
4. `feat(<CODE>_V<N>): notification templates`
5. `feat(<CODE>_V<N>): React form component`
6. `feat(<CODE>_V<N>): tests`
7. `feat(<CODE>_V<N>): feature flag wiring + .env additions`

A single bundle commit is also fine if the work is genuinely small
(single section, no integrations).
