## Why

Today `spec.userTasks[].permissions.submitter` is typed as `'self' | 'role:HR' | 'group:applicant'` — a free-form string that the wizard does not actually expose for editing. `StepForms` only configures fields; there is no UI for "who fills this task." Every userTask ends up implicitly `submitter = 'self'` (the workflow initiator), which means a userTask intended for HR / Finance / IT can only be filled by the original applicant — wrong for 6 of the 9 mock-up flows the partner brought:

| Flow | Non-self userTask | Who should fill it |
|---|---|---|
| LEAVE | HR 備案 (`task_hr_archive`) | HR team |
| GEE / GEV / APE / TEO | confirm & print + fin_review | Finance team |
| HWP / ITPR | it_spec, quote, po (3 stages) | IT, Procurement, Finance |
| TRQ | notify_admin | General Affairs / 行政 |
| EXTOB | account creation | HR |

In other words, **every flow except a hypothetical "self-only" one needs cross-role userTasks**. Without an `assignee` concept on userTasks, the AI onboarding can document the flow but cannot capture who *executes* the work — and the generated backend will never assemble a working task queue.

This change introduces a typed `assignee` field on `UserTask` using the existing `ActorRef` DSL (no new vocabulary), wires the `StepForms` wizard with a picker UI, and extends the resolver / runtime contract so a userTask spawns a Task scoped to the resolved candidate set.

## What Changes

### Spec schema (`spec_schema.md`)

Replace the loose `permissions.submitter` string with a typed structure on `UserTask`:

```typescript
type UserTask = {
  id: string
  formCode: string
  fields: FormField[]
  assignee: ActorRef          // NEW — who fills this task
  viewers: ViewerRef[]        // NEW — who can read (subset of ActorRef + 'self', 'submitter')
}

type ViewerRef =
  | { type: 'self' }                    // the user who originally submitted the flow
  | { type: 'submitter' }               // alias of self for symmetry with notifications
  | { type: 'current_assignee' }        // whoever currently holds an open task in this flow
  | ActorRef                            // role / group / function_tag / expr / collection
```

Migration semantics: when an existing v1.0 spec carries `permissions.submitter = 'self'`, importer rewrites to `assignee = { type: 'expr', path: 'submitter', skip_if_initiator: false }` — preserves identical behavior. `'role:HR'` becomes `{ type: 'role', code: 'hr' }`. `'group:applicant'` becomes `{ type: 'group', id: 'applicant' }`.

The `permissions` wrapper object is dropped; `assignee` and `viewers` are siblings of `fields`. (This is a v1.2 schema bump — existing specs upgraded by importer.)

### Backend domain (`bpm-svc`)

This change defines the *spec field* but does not yet ship the runtime Task entity (deferred to the future Process Runtime change). Concretely we add:

- `Bpm.Domain.Spec.UserTaskSpec` record with `Assignee: ActorRef` and `Viewers: IReadOnlyList<ViewerRef>`
- `Bpm.Application.Spec.UserTaskSpecValidator` — verifies `assignee` is a valid ActorRef (delegates to existing `ActorRefValidator`); for `viewers`, allows the special `self` / `submitter` / `current_assignee` types plus any ActorRef
- A `IUserTaskAssigneeResolver` service that thinly wraps `IActorResolver`, applying userTask-specific defaults (e.g., `skip_if_initiator = false` when `assignee` is `expr:submitter`)

Persistence-wise this change is read-only against existing tables; specs stored in `specs-incoming/` carry the new shape.

### Wizard UI (`bpm-form-stepper`)

`StepForms` gains a per-userTask "誰來填" panel above the field editor:

- **Type picker** (re-uses `ActorRefEditor` with the four extra types from `extend-actor-and-org-for-ai-routing`):
  - 申請人本人 (`expr:submitter`) — default for `task_apply` / `submit` style steps
  - 角色 (`role`) — pick from existing seeded roles
  - 部門功能主管 (`functional_head`) — pick from function_tag whitelist
  - 部門功能成員 (`functional_members`) — *new helper* that resolves to "all active users in the dept tagged X"; spec form below
  - 群組 (`group`) — pick from existing groups
  - 條件式 / 合議 (`conditional` / `collection`) — for advanced cases
- **Viewers section** — multi-select of `self` / `submitter` / `current_assignee` plus optional ActorRef rows for additional readers

The wizard's `StepForms.tsx` already lists every `userTask` node from `flow.nodes`; this change adds the assignee panel inside each node's card, persisted into `draft.userTasks[].assignee`.

### New ActorRef type: `functional_members`

`functional_head` returns one user (the head). For "the finance team" / "the IT team" we need *all* members of a tagged department. Add a new ActorRef type:

```jsonc
{
  "type": "functional_members",
  "function_tag": "finance",
  "include_subtree": false,    // optional, default false; if true, walks Department.parent_id downward
  "active_only": true          // optional, default true
}
```

Resolution: find Department by `function_tag`, return all `User` rows where `department_id = dept.id` (and optionally same for descendant depts when `include_subtree = true`). Inactive users excluded by default.

This pairs with `functional_head` for the common SME pattern: most tasks are routed to "the finance team" (any of them can claim) and a few are routed to "the head of finance" (only the head). One tag → two resolution shapes.

### Workflow Resolver (`bpm-workflow-resolver`)

Add `ResolveFunctionalMembers` (similar shape to existing `ResolveTitleMatch`):

- Look up Department by tag; if missing → `Failure(FunctionTagNotMapped, ...)`
- Query active users with `department_id = dept.id`
- If `include_subtree = true`, BFS down the department tree, accumulating users
- Empty result → `Failure(FunctionalMembersEmpty, ...)`; respects ref's `fallback`

`ResolutionError.Kind` gains `FunctionalMembersEmpty`.

### Sample specs

Update the migrated samples (`leave_v1`, `purchase_v1`, `expense_with_threshold_v1`) plus add `it_request_v1` from the previous proposal — all userTasks now carry `assignee`. New behavior visible in samples:

- `task_hr_archive.assignee = { type: 'functional_members', function_tag: 'hr' }`
- `task_purchase_exec.assignee = { type: 'functional_members', function_tag: 'procurement' }`
- IT spec / quote / PO each get tagged appropriately

### Out of scope (future changes)

- `Task` entity / `TaskHistory` table / runtime task spawning (Process Runtime change)
- Claim-from-pool semantics ("any of finance team grabs the task") — deferred to runtime
- Out-of-office / delegation handling
- "Dynamic team" assignee that picks based on round-robin or load — explicit out
- AI auto-suggestion of `assignee` based on userTask label (e.g., AI sees "HR 備案" and suggests `functional_members:hr`) — Stage 1 AI work, separate change

## Capabilities

### Modified Capabilities

- `bpm-form-stepper`: `StepForms` gains an assignee/viewers picker per userTask card (uses `ActorRefEditor`). Persists into `draft.userTasks[].assignee` / `viewers`.
- `bpm-actor-dsl`: extend ActorRef discriminated union with `functional_members`; introduce `ViewerRef` discriminated union for userTask viewers.
- `bpm-workflow-resolver`: extend resolver with `ResolveFunctionalMembers`; add `FunctionalMembersEmpty` to `ResolutionError.Kind`.

### New Capabilities

None.

## Impact

- **spec_schema.md**: §2.3 (UserTask) restructured; old `permissions.submitter` string format documented as v1.0 legacy with importer migration path
- **bpm-ui/src/lib/onboarding.ts**: `UserTask` TypeScript shape gains `assignee` / `viewers`; `EMPTY_DRAFT` updated to assign `expr:submitter` by default for new userTask nodes
- **bpm-ui/src/screens/onboarding/steps/StepForms.tsx**: per-userTask assignee panel + viewer section above the existing field editor
- **bpm-ui/src/components/wizard/ActorRefEditor.tsx**: add `functional_members` to type picker (pulls in cleanly from the previous proposal's editor extensions)
- **bpm-svc/src/Domain/Spec/ActorRef.cs**: add `FunctionalMembersActorRef` record
- **bpm-svc/src/Domain/Spec/Resolution.cs**: add `FunctionalMembersEmpty` to `ErrorKind`
- **bpm-svc/src/Application/Spec/ActorResolver.cs**: add `ResolveFunctionalMembers` method
- **bpm-svc/src/Application/Spec/ActorRefValidator.cs**: add validation rules for `functional_members`
- **bpm-svc/src/Application/Spec/UserTaskSpecValidator.cs**: NEW — wraps ActorRefValidator + ViewerRef rules
- **sample_specs/**: 3 updated, 1 new (`it_request_v1.json`)
- **prompt_template_v1.md**: add UserTask assignee section + worked examples for `functional_members`
- **No DB migration** in this change — runtime tables defer to the Process Runtime change
- **No breaking change to running 9-flow demo** — these are spec/wizard/backend additions; the existing mock screens (`Home`, `forms/*`, `Search`, `Report`) and `workflow.ts` are not touched
- **Dependencies**: none (no new packages)

## Coverage check vs the 9 mock-up flows

After this change + `extend-actor-and-org-for-ai-routing`:

| Flow | userTask assignees expressible? |
|---|---|
| LEAVE | ✅ `task_apply.assignee = expr:submitter`, `task_hr_archive.assignee = functional_members:hr` |
| GEE / GEV / APE / TEO | ✅ `task_apply = expr:submitter`, `confirm/fin_review = functional_members:finance` |
| HWP / ITPR | ✅ `task_apply = expr:submitter`, `it_spec = functional_members:it`, `quote/po = functional_members:procurement`, `approve = expr:submitter.manager`, `confirm = functional_members:finance` |
| TRQ | ✅ `task_apply = expr:submitter`, `notify_admin = functional_members:general_affairs` |
| EXTOB | ✅ `submit.assignee = expr:submitter.manager` (manager submits for the new hire), `account = functional_members:hr` |

All nine flow shapes become expressible without inventing per-customer roles in the seed fixture.
