## ADDED Requirements

### Requirement: ResolveFunctionalMembers

The resolver SHALL handle `functional_members` ActorRef by:

1. Looking up the Department whose `function_tag` matches the ref's tag.
2. If no Department has the tag → `Failure(FunctionTagNotMapped, "no department tagged X")`.
3. Querying `User` rows where `department_id = dept.id`. When `active_only = true` (default), filter `is_active = true`.
4. When `include_subtree = true`, walking the department tree downward (BFS) and accumulating users from each descendant. Cap recursion depth at 5 to bound query cost.
5. If the result set is empty → `Failure(FunctionalMembersEmpty, "department X has no qualifying members")`; the ref's `fallback` SHALL be tried if present.

The resolver MUST honor `skip_if_initiator` post-filter as for every other ActorRef type.

#### Scenario: Functional members of finance dept

- **GIVEN** Department `財務部` has `function_tag = "finance"` and 3 active users (`u_a`, `u_b`, `u_c`) plus 1 inactive (`u_d`)
- **WHEN** the resolver evaluates `{ "type": "functional_members", "function_tag": "finance" }`
- **THEN** the result is `Success({ u_a, u_b, u_c })` (inactive excluded by default)

#### Scenario: include_subtree expands descendants

- **GIVEN** Department `財務部` (head of subtree) has 2 active users; child department `會計組` has 4 active users
- **WHEN** the resolver evaluates `{ "type": "functional_members", "function_tag": "finance", "include_subtree": true }`
- **THEN** the result includes all 6 users

#### Scenario: active_only false includes inactive

- **GIVEN** the same dept with 3 active + 1 inactive
- **WHEN** the resolver evaluates `{ "type": "functional_members", "function_tag": "finance", "active_only": false }`
- **THEN** the result is `Success({ u_a, u_b, u_c, u_d })`

#### Scenario: Empty members triggers fallback

- **GIVEN** Department `稽核部` is tagged `audit` but has no active users
- **WHEN** the resolver evaluates `{ "type": "functional_members", "function_tag": "audit", "fallback": { "type": "role", "code": "auditor_pool" } }`
- **THEN** the resolver invokes the fallback `role:auditor_pool`

#### Scenario: Tag missing returns FunctionTagNotMapped

- **GIVEN** no Department has `function_tag = "general_affairs"`
- **WHEN** the resolver evaluates `{ "type": "functional_members", "function_tag": "general_affairs" }`
- **THEN** the result is `Failure(FunctionTagNotMapped, ...)` (distinct from `FunctionalMembersEmpty`)

### Requirement: ResolutionError.Kind extended for functional_members

The `ResolutionError.Kind` enum SHALL include `FunctionalMembersEmpty` for the case where a tag maps to a Department but the Department has no qualifying members. This is distinct from `FunctionTagNotMapped` (the tag has no Department at all). Audit rows SHALL include the function_tag value in the error reason text.

#### Scenario: FunctionalMembersEmpty audit reason

- **WHEN** `functional_members:audit` resolves but the audit dept has no active members
- **THEN** the audit row's `ErrorReason` text includes `"audit"` for triage

### Requirement: UserTask assignee resolution semantics

When a userTask is being prepared for runtime spawning (deferred to the Process Runtime change), the runtime SHALL invoke `IActorResolver.Resolve(userTask.Assignee, ctx)` and treat the resulting `Set<UserId>` as the candidate pool. The runtime SHALL spawn exactly one `Task` row carrying both the candidate set and a `claimed_by_user_id` (initially null). Members of the candidate set MAY claim the task; the first to claim wins.

This requirement documents the contract; the implementation lives in the future Process Runtime change. This change ships only the resolver behavior (returning a set), not the Task spawning logic.

#### Scenario: Assignee resolves to a candidate set

- **GIVEN** a userTask with `assignee = { type: 'functional_members', function_tag: 'hr' }`
- **AND** `ctx.initiator_user_id = u_emp` (HR is not in the same dept as initiator)
- **WHEN** the resolver runs at task-creation time
- **THEN** the result is the set of HR-dept active users (not filtered by skip_if_initiator since initiator is outside the set)

#### Scenario: Empty assignee resolution requires admin attention

- **GIVEN** an `unresolved` assignee or a `functional_members` with `FunctionalMembersEmpty` failure
- **WHEN** the runtime would spawn a Task
- **THEN** the runtime instead enqueues an admin-intervention notice (deferred contract — not implemented in this change but documented for future compliance)
