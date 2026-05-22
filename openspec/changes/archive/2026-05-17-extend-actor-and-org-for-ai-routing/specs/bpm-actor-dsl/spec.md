## ADDED Requirements

### Requirement: functional_head ActorRef type

The `functional_head` ActorRef SHALL carry a `function_tag` string drawn from a fixed whitelist (`finance`, `hr`, `it`, `legal`, `operations`, `procurement`, `audit`, `quality`, `general_affairs`). The validator MUST reject any `function_tag` not in this set. Resolution returns the `head_user_id` of the `Department` whose `function_tag` matches; the bridge between DSL vocabulary and the customer's organic department naming.

#### Scenario: Valid functional_head accepted

- **WHEN** spec.json contains `{ "type": "functional_head", "function_tag": "finance" }`
- **THEN** the validator accepts it

#### Scenario: Off-whitelist function_tag rejected

- **WHEN** spec.json contains `{ "type": "functional_head", "function_tag": "marketing" }`
- **THEN** the validator rejects it with the allowed-tags list in the error message

#### Scenario: Empty function_tag rejected

- **WHEN** spec.json contains `{ "type": "functional_head", "function_tag": "" }`
- **THEN** the validator rejects it

### Requirement: by_amount ActorRef type

The `by_amount` ActorRef SHALL carry:
- `amount_field` (string, non-empty) — name of a numeric field in the form data
- `from` — `"submitter"` or `"current_approver"`
- `strategy` — `"manager_chain"` or `"department_tree"`
- `include_self` (bool, optional, default `false`) — whether to consider the starting user themselves

The validator MUST reject `from` or `strategy` values outside their respective sets, and MUST reject empty `amount_field`.

#### Scenario: Valid by_amount

- **WHEN** spec.json contains `{ "type": "by_amount", "amount_field": "amount", "from": "submitter", "strategy": "manager_chain" }`
- **THEN** the validator accepts it

#### Scenario: Invalid strategy rejected

- **WHEN** spec.json contains `{ "type": "by_amount", "amount_field": "amount", "from": "submitter", "strategy": "skip-list" }`
- **THEN** the validator rejects it with the allowed-strategies list

#### Scenario: Empty amount_field rejected

- **WHEN** `amount_field = ""`
- **THEN** the validator rejects it

### Requirement: title_match ActorRef type

The `title_match` ActorRef SHALL carry:
- `patterns` — non-empty array of strings (matched against `User.title_normalized` via SQL LIKE)
- `scope` — `"company"` or `"same_department"`

The validator MUST reject empty `patterns`, scope outside the allowed set, or non-string entries in `patterns`.

#### Scenario: Valid title_match

- **WHEN** spec.json contains `{ "type": "title_match", "patterns": ["副總", "VP"], "scope": "company" }`
- **THEN** the validator accepts it

#### Scenario: Empty patterns rejected

- **WHEN** `patterns = []`
- **THEN** the validator rejects it

#### Scenario: Invalid scope rejected

- **WHEN** `scope = "subtree"`
- **THEN** the validator rejects it

### Requirement: unresolved ActorRef type

The `unresolved` ActorRef SHALL carry:
- `intent` (string, non-empty, max 500 chars) — natural-language description of what the AI was trying to express
- `reason` (string, non-empty) — why the AI could not produce a confident concrete actor
- `suggested_clarification` (string, optional) — a question the spec author can answer to disambiguate

The `unresolved` node never resolves to user IDs; the resolver MUST return a structured failure (`UnresolvedAiNode`). It SHALL NOT fall back to the `fallback` field even when one is present — `unresolved` semantically asks for human clarification, not a guess.

#### Scenario: Valid unresolved

- **WHEN** spec.json contains `{ "type": "unresolved", "intent": "需要部門主管的下一級長官", "reason": "AI 無法判斷是直屬主管的主管 還是 該部門部長之上的處長" }`
- **THEN** the validator accepts it

#### Scenario: Missing intent rejected

- **WHEN** spec.json contains `{ "type": "unresolved", "reason": "..." }` (no intent)
- **THEN** the validator rejects it

#### Scenario: Missing reason rejected

- **WHEN** spec.json contains `{ "type": "unresolved", "intent": "..." }` (no reason)
- **THEN** the validator rejects it

### Requirement: ActorRef metadata fields

The system SHALL recognize four optional metadata fields on every ActorRef regardless of `type`:

- `intent` (string, max 500 chars) — natural-language business meaning
- `confidence` (number) — AI's confidence, in range `[0.0, 1.0]`
- `needs_review` (bool) — flag for human review attention; the validator MUST set it to `true` automatically when `type = "unresolved"` and the field is missing
- `skip_if_initiator` (bool) — exclude initiator from results; default `true`

The validator MUST accept these fields on any ActorRef type and MUST reject `confidence` values outside `[0.0, 1.0]`.

#### Scenario: Metadata accepted on any type

- **WHEN** an `expr` ActorRef carries `{ "type": "expr", "path": "submitter.manager", "intent": "員工的直屬主管", "confidence": 0.95, "skip_if_initiator": true }`
- **THEN** the validator accepts it

#### Scenario: Out-of-range confidence rejected

- **WHEN** an ActorRef carries `confidence = 1.5`
- **THEN** the validator rejects it with "confidence must be in [0.0, 1.0]"

#### Scenario: Metadata is optional

- **WHEN** an ActorRef carries no metadata fields
- **THEN** the validator accepts it; resolver applies defaults (`skip_if_initiator = true`, others null)

#### Scenario: needs_review defaulted on unresolved

- **WHEN** an `unresolved` ActorRef has no `needs_review` field
- **THEN** the validator parses it as `needs_review = true`
