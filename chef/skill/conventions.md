# chef conventions — naming, paths, flag, variables

Distilled from `openspec/specs/flowcook-chef` for quick lookup during
a chef session. The spec wins if these drift.

## Path map

| Allowed (write + read) | Read-only | Never touch |
|---|---|---|
| `bpm-svc/features/<CODE>/V<N>/**` | `bpm-svc/{Core,Runtime,Bundle,Principal,Sandbox,Application,Api}/**` | `chef/**` |
| `bpm-ui/src/features/<CODE>/V<N>/**` | `bpm-admin-svc/**`, `bpm-admin-ui/**`, `syncer/**` | anywhere else |
|   | `bpm-ui/src/screens/forms/Reference_*.tsx` (visual reference) |   |
|   | `openspec/specs/**` + `*/CLAUDE.md` (rules) |   |

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
| Feature flag | `<CODE>_V<N>` (exactly) | `LEAVE_V1` |
| Test file (C#) | `<CODE>_V<N>_<Aspect>Tests.cs` | `LEAVE_V1_ApprovalTests.cs` |
| Test file (TS) | `<CODE>_V<N>_<aspect>.test.tsx` | `LEAVE_V1_layout.test.tsx` |

The prefix is part of the identifier — no `namespace LEAVE.V1`,
no `LeaveForm` "inside the LEAVE folder it's obvious". Flat prefix,
everywhere.

## Feature flag

One flag per version: `<CODE>_V<N>`.

- Controllers and minimal-API endpoints: 404 when off.
- Event handlers and background services: no-op (still process the
  event but emit no side effects).
- UI routes: render a placeholder or `<Navigate to="/" />`.

Implementation lives in `<CODE>_V<N>_FeatureFlag.cs`. It reads from
the bpm-svc `IFeatureFlagService` (existing infra). UI side reads from
`import.meta.env.VITE_FEATURE_<CODE>_V<N>` — chef adds that env var
to `bpm-ui/.env.example` and `bpm-ui/.env.local` as part of the
generated change set.

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
