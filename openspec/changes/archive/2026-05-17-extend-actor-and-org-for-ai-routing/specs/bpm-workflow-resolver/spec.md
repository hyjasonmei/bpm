## ADDED Requirements

### Requirement: ResolveFunctionalHead

The resolver SHALL handle `functional_head` ActorRef by looking up the `Department` whose `function_tag` matches the ref's tag, then returning that department's `head_user_id`.

- If no Department has the tag → `Failure(FunctionTagNotMapped, "no department tagged X")`
- If the Department exists but `head_user_id` is null → empty success with structured "department head unset" reason; resolver SHALL apply the ref's `fallback` if set
- If the head is inactive → empty success with "department head inactive" reason; resolver applies fallback

#### Scenario: Functional head present

- **GIVEN** Department `財務部` has `function_tag = "finance"`, `head_user_id = u_chen`
- **AND** `u_chen` is active
- **WHEN** the resolver evaluates `{ "type": "functional_head", "function_tag": "finance" }`
- **THEN** the result is `Success({ u_chen })`

#### Scenario: Function tag unmapped

- **GIVEN** no Department has `function_tag = "audit"`
- **WHEN** the resolver evaluates `{ "type": "functional_head", "function_tag": "audit", "fallback": { "type": "role", "code": "ceo" } }`
- **THEN** the resolver returns the result of resolving the fallback `role: ceo`

#### Scenario: Inactive head triggers fallback

- **GIVEN** `財務部.head_user_id = u_chen` and `u_chen.is_active = false`
- **WHEN** the resolver evaluates the same ref with a fallback
- **THEN** fallback is invoked

### Requirement: ResolveByAmount walks up authority chain

The resolver SHALL handle `by_amount` ActorRef by:

1. Reading `ctx.form_data[amount_field]` and parsing as decimal. Missing or non-numeric → `Failure(ValidationFailed, "amount field missing or non-numeric")`.
2. Determining the start user: `ctx.initiator_user_id` if `from = "submitter"`, `ctx.current_approver_user_id` if `from = "current_approver"`.
3. Walking upward:
   - `strategy = "manager_chain"`: iterate `User.manager_id` upward
   - `strategy = "department_tree"`: iterate `Department.parent_id` upward, taking each dept's `head_user_id` as candidate
4. At each candidate, returning `Success({ candidate })` when `candidate.approval_limit >= amount` (or `Department.approval_limit >= amount` for tree strategy).
5. Including the start user themselves only when `include_self = true`.
6. Capping at 10 levels of walk.
7. Returning `Failure(AmountExceedsAllAuthorities, "amount X exceeds all authorities up to N levels")` when no candidate qualifies.

Inactive users SHALL be skipped (walked past without returning). Cycle detection MUST abort the walk with a `Cycle` failure.

#### Scenario: Manager chain finds first qualifying authority

- **GIVEN** submitter `u_emp` (`approval_limit = null`), manager `u_mgr` (`approval_limit = 30000`), grandmanager `u_dir` (`approval_limit = 200000`)
- **AND** form `amount = 50000`, ref `from = submitter`, `strategy = manager_chain`, `include_self = false`
- **WHEN** the resolver evaluates `by_amount`
- **THEN** the result is `Success({ u_dir })` (skipped u_mgr because 30000 < 50000)

#### Scenario: Amount exceeds entire chain

- **GIVEN** the same chain as above
- **AND** form `amount = 999999`
- **WHEN** the resolver evaluates `by_amount`
- **THEN** the result is `Failure(AmountExceedsAllAuthorities, ...)`

#### Scenario: Department-tree strategy

- **GIVEN** submitter is in `工程部` (`approval_limit = 50000`); parent dept `產品開發處` (`approval_limit = 500000`)
- **AND** form `amount = 100000`, ref `strategy = department_tree`
- **WHEN** the resolver evaluates `by_amount`
- **THEN** the result is `Success({ head of 產品開發處 })`

#### Scenario: include_self skips when false

- **GIVEN** submitter `u_emp` has `approval_limit = 999999` (very senior)
- **AND** ref has `include_self = false`
- **WHEN** the resolver evaluates `by_amount` for amount = 1
- **THEN** the result excludes `u_emp` and returns the next qualifying user up the chain (or fails if none)

#### Scenario: Missing amount field

- **GIVEN** form does not include the `amount_field` named in the ref
- **WHEN** the resolver evaluates `by_amount`
- **THEN** the result is `Failure(ValidationFailed, "amount field 'X' missing")`

### Requirement: ResolveTitleMatch returns all pattern matches

The resolver SHALL handle `title_match` ActorRef by querying active users whose `title_normalized` matches at least one pattern (SQL LIKE with the `%` wildcard implicit on both sides — `%pattern%`). Patterns are OR-joined.

- `scope = "company"` — query is global
- `scope = "same_department"` — query adds `WHERE department_id = ctx.submitter.department_id`
- Inactive users SHALL be excluded
- Empty result → `Failure(TitleNoMatch, "no users match patterns [...]")`; the ref's `fallback` SHALL be tried

#### Scenario: Multiple VPs across company

- **GIVEN** users `u_chen` (`title_normalized = "vp"`, dept = 業務), `u_lin` (`title_normalized = "vp"`, dept = 工程)
- **WHEN** the resolver evaluates `{ "type": "title_match", "patterns": ["vp"], "scope": "company" }`
- **THEN** the result is `Success({ u_chen, u_lin })`

#### Scenario: Same-department scope filters

- **GIVEN** the same users as above, submitter is in dept = 工程
- **WHEN** the resolver evaluates the same ref but with `scope = "same_department"`
- **THEN** the result is `Success({ u_lin })`

#### Scenario: No matches falls back

- **GIVEN** no user has `title_normalized` matching `"chairman"`
- **WHEN** the resolver evaluates `{ "type": "title_match", "patterns": ["chairman"], "scope": "company", "fallback": { "type": "role", "code": "ceo" } }`
- **THEN** the resolver invokes the fallback

### Requirement: ResolveUnresolved always fails

The resolver SHALL handle `unresolved` ActorRef by returning `Failure(UnresolvedAiNode, ref.reason)` regardless of context. The resolver MUST NOT apply `fallback` even if present — `unresolved` semantically requests human clarification, not a guess. The audit SHALL record the full `intent` and `suggested_clarification` in the audit reason text for triage.

#### Scenario: Unresolved always fails

- **WHEN** the resolver evaluates `{ "type": "unresolved", "intent": "下一級長官", "reason": "歧義" }`
- **THEN** the result is `Failure(UnresolvedAiNode, "歧義")` regardless of `ctx`

#### Scenario: Fallback ignored on unresolved

- **WHEN** the resolver evaluates `{ "type": "unresolved", "intent": "...", "reason": "...", "fallback": { "type": "role", "code": "admin" } }`
- **THEN** the result is `Failure(UnresolvedAiNode, ...)` — the fallback is NOT invoked

### Requirement: skip_if_initiator filtering

The resolver wrapper SHALL post-filter every successful resolution to drop `ctx.initiator_user_id` from the result set when the ref's `skip_if_initiator` field is `true` (or absent — the default is `true`). When `skip_if_initiator = false`, the initiator is preserved.

This filter applies uniformly across every ActorRef type, applied at the top-level `Resolve` boundary (so child resolutions inside `conditional` / `collection` are also filtered consistently with the outer ref's flag).

#### Scenario: Initiator excluded by default

- **GIVEN** `ctx.initiator_user_id = u_emp` and the resolver would otherwise return `{ u_emp, u_mgr }`
- **AND** ref omits `skip_if_initiator`
- **WHEN** the resolver runs
- **THEN** the post-filtered result is `{ u_mgr }`

#### Scenario: Initiator preserved when flag is false

- **GIVEN** the same context
- **AND** ref carries `skip_if_initiator = false`
- **WHEN** the resolver runs
- **THEN** the result is `{ u_emp, u_mgr }`

#### Scenario: All-initiator results become empty

- **GIVEN** `ctx.initiator_user_id = u_emp` and the resolver would otherwise return `{ u_emp }` (single user case)
- **AND** ref defaults `skip_if_initiator = true`
- **WHEN** the resolver runs
- **THEN** the post-filtered result is empty; the ref's `fallback` is invoked if present

### Requirement: ResolutionError.Kind enum extended

The `ResolutionError.Kind` enum SHALL include the following new variants for the new resolver types:

- `AmountExceedsAllAuthorities` — `by_amount` walked the full chain, no candidate qualified
- `FunctionTagNotMapped` — `functional_head` found no Department with that tag
- `TitleNoMatch` — `title_match` returned no rows
- `UnresolvedAiNode` — `unresolved` was hit (always)

Each new failure SHALL be recorded in `ActorResolutionAudits.ErrorKind` and SHALL include diagnostic data in `ErrorReason` (e.g., the `amount` value for `AmountExceedsAllAuthorities`, the patterns list for `TitleNoMatch`).

#### Scenario: AmountExceedsAllAuthorities reason carries amount

- **WHEN** `by_amount` fails because amount = 999999 exceeds all authorities
- **THEN** the audit row's `ErrorReason` text includes `"999999"` so triage can read it

#### Scenario: TitleNoMatch reason carries patterns

- **WHEN** `title_match` fails with no rows for patterns `["chairman"]`
- **THEN** the audit row's `ErrorReason` text includes `"chairman"`

#### Scenario: UnresolvedAiNode reason carries intent

- **WHEN** an `unresolved` node with `intent = "下一級長官"` is resolved
- **THEN** the audit row's `ErrorReason` text includes `"下一級長官"`
