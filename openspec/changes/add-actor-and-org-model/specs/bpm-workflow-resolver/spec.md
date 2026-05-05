## ADDED Requirements

### Requirement: Resolver returns Set<UserId> uniformly

The system SHALL provide an `IActorResolver.Resolve(ActorRef ref, ResolutionContext ctx)` method that returns `ResolutionResult` — a discriminated union of `Success(IReadOnlySet<Guid> userIds)` and `Failure(ResolutionError error)`. Atomic resolutions SHALL return a (possibly empty) set, not a single value.

#### Scenario: Atomic role resolves to all assignees
- **WHEN** Resolve is called with `{ "type": "role", "code": "finance_manager" }`
- **AND** three users have RoleAssignments to that role
- **THEN** the result is `Success` containing all three user ids

#### Scenario: Empty resolution is Success with empty set
- **WHEN** Resolve is called with a role that has no assignees AND no fallback
- **THEN** the result is `Failure(RoleEmpty)` (NOT `Success({})`)

### Requirement: ResolutionContext carries submitter and form data

`ResolutionContext` SHALL contain at minimum: `submitter_user_id` (Guid), `form_data` (read-only key-value map), `flow_code` (string), `step_code` (string, optional). The resolver MAY use any of these when evaluating conditional expressions.

#### Scenario: Conditional reads form_data
- **WHEN** Resolve is called with a conditional whose condition is `{ "field": "amount", "op": ">", "value": 50000 }`
- **AND** `form_data["amount"] = 75000`
- **THEN** the resolver evaluates the condition true and resolves the `then` branch

### Requirement: Path walks bounded by whitelist depth

The resolver SHALL walk only the path segments enumerated in the actor-dsl whitelist. An attempt to walk past the whitelisted depth (e.g., `submitter.manager.manager.manager.manager`) SHOULD never reach the resolver because the validator rejects it; if it does (programmer error), the resolver MUST return `Failure(ValidationFailed)`.

#### Scenario: Walk submitter.department.parent.head
- **WHEN** submitter belongs to dept D1, D1.parent_id = D0, D0.head_user_id = U_HEAD
- **AND** Resolve is called with `{ "type": "expr", "path": "submitter.department.parent.head" }`
- **THEN** the result is `Success({U_HEAD})`

#### Scenario: Path segment unresolvable
- **WHEN** submitter has `manager_id = null`
- **AND** Resolve is called with `{ "type": "expr", "path": "submitter.manager" }`
- **THEN** the result is `Failure(PathUnresolved, reason: "submitter has no manager")`

### Requirement: Cycle detection on graph walks

The resolver SHALL maintain a visited-set during group expansion, manager-chain walks, and department-parent walks. Detecting a cycle MUST abort the walk and return `Failure(Cycle, path: [...visited])`. The resolver MUST NOT enter infinite recursion under any input.

#### Scenario: Cyclic manager chain detected
- **WHEN** user A's manager is B, B's manager is A
- **AND** Resolve is called with path `submitter.manager.manager` from user A
- **THEN** the walk detects the cycle and returns `Failure(Cycle, path: [A, B, A])`

#### Scenario: Cyclic group nesting detected
- **WHEN** group g1 contains g2 as a member, g2 contains g1
- **AND** Resolve is called with `{ "type": "group", "id": g1 }`
- **THEN** the result is `Failure(Cycle, path: [g1, g2, g1])`

### Requirement: Fallback resolution on empty/error

When an ActorRef carries a `fallback` field and primary resolution returns `Failure` OR `Success` with an empty set, the resolver SHALL retry with the fallback ActorRef. Fallback chains beyond one level MUST NOT recurse (validator already rejects nested fallbacks).

#### Scenario: Fallback used on empty primary
- **WHEN** primary `{ "type": "role", "code": "finance_manager" }` has no assignees
- **AND** fallback is `{ "type": "role", "code": "admin" }` (which has assignees)
- **THEN** the result is `Success` containing the admin role's assignees

### Requirement: Conditional and collection resolution

The resolver SHALL evaluate `conditional.condition` against `ctx.form_data`, then recursively resolve the chosen branch. For `collection`, the resolver SHALL recursively resolve every entry in `actors`, then:
- For `mode = "all"`: return the union if all child resolutions succeed; otherwise `Failure`
- For `mode = "any"`: return the union of all successful child resolutions, regardless of failures (callers honor `min_approvals` at approval-gate time, not at resolve time)

#### Scenario: Conditional then-branch chosen
- **WHEN** `condition` evaluates true
- **THEN** only the `then` branch is resolved; `else` is not visited

#### Scenario: Collection mode=all with one failure
- **WHEN** a collection has 3 actors and one resolves to Failure
- **AND** `mode = "all"`
- **THEN** the overall result is `Failure(ConditionalBranchEmpty, reason: "1 of 3 collection entries failed")`

### Requirement: Audit log of every top-level resolution

The resolver SHALL write one `ActorResolutionAudit` row per top-level `Resolve` call (NOT per recursive sub-resolution). The row SHALL contain: `timestamp`, `request_id`, `actor_ref_json` (full input), `context_summary` (submitter_user_id, flow_code, step_code), `result_kind` (`Success` / `Failure`), `resolved_user_ids` (when Success, possibly empty), `error_kind` (when Failure), `error_reason` (when Failure).

#### Scenario: Successful resolution audited
- **WHEN** Resolve returns `Success({u1, u2})` for a role ActorRef
- **THEN** an audit row exists with `result_kind = "Success"`, `resolved_user_ids = [u1, u2]`, and `actor_ref_json` containing the original input

#### Scenario: Failed resolution audited
- **WHEN** Resolve returns `Failure(PathUnresolved)`
- **THEN** an audit row exists with `result_kind = "Failure"`, `error_kind = "PathUnresolved"`, and a non-empty `error_reason`
