## ADDED Requirements

### Requirement: ActorRef discriminated union

A spec.json field that refers to an actor (approver, assignee, notify recipient) SHALL hold an `ActorRef` JSON value. Every `ActorRef` MUST have a `type` field whose value is one of: `"expr"`, `"role"`, `"group"`, `"user"`, `"conditional"`, `"collection"`. The validator MUST reject any object missing `type` or carrying a `type` value outside this set.

#### Scenario: Valid expr ActorRef
- **WHEN** spec.json contains `{ "type": "expr", "path": "submitter.manager" }`
- **THEN** the validator accepts it

#### Scenario: Missing type field rejected
- **WHEN** spec.json contains `{ "path": "submitter.manager" }` (no `type`)
- **THEN** the validator rejects it with a clear "ActorRef requires `type` field" error

#### Scenario: Unknown type rejected
- **WHEN** spec.json contains `{ "type": "magic", "spell": "..." }`
- **THEN** the validator rejects it listing the allowed type values

### Requirement: expr type carries a whitelisted path

The `expr` ActorRef SHALL carry a `path` string drawn from a fixed whitelist. The whitelist members are:
- `"submitter"`
- `"submitter.manager"`, `"submitter.manager.manager"`, `"submitter.manager.manager.manager"`
- `"submitter.department"`, `"submitter.department.head"`
- `"submitter.department.parent"`, `"submitter.department.parent.head"`
- `"submitter.department.parent.parent.head"`

Paths SHALL be lowercase. The validator MUST reject any path not exactly matching one of these strings.

#### Scenario: Whitelisted path accepted
- **WHEN** an expr ActorRef has `path = "submitter.department.head"`
- **THEN** the validator accepts it

#### Scenario: Off-whitelist path rejected
- **WHEN** an expr ActorRef has `path = "submitter.manager.manager.manager.manager"` (4 levels)
- **THEN** the validator rejects it with a list of allowed paths

#### Scenario: Wrong casing rejected
- **WHEN** an expr ActorRef has `path = "Submitter.Manager"`
- **THEN** the validator rejects it

### Requirement: role / group / user atomic types

The `role`, `group`, and `user` ActorRef types SHALL each carry a single identifier field referencing a row in the corresponding entity:
- `role`: `code` (string, matches `Role.code`)
- `group`: `id` or `code` (matches `Group.id` or `Group.code`)
- `user`: `id` (Guid, matches `User.id`)

The validator MUST verify the referenced row exists at spec-load time when given access to the org-chart reader (a "lint" pass), and SHALL warn (not reject) on `user` type usage outside of test fixtures.

#### Scenario: Role reference verified at lint
- **WHEN** an ActorRef has `{ "type": "role", "code": "finance_manager" }` and a Role with that code exists
- **THEN** the validator passes

#### Scenario: User reference warned outside tests
- **WHEN** an ActorRef has `{ "type": "user", "id": "u_123" }` in a non-test spec
- **THEN** the validator emits a warning ("hardcoded user references should not be used in production specs")

### Requirement: conditional composite

A `conditional` ActorRef SHALL carry `condition`, `then` (an ActorRef), and `else` (an ActorRef) fields. The `condition` SHALL be `{ "field": <form-field-path>, "op": <one of "==" "!=" ">" ">=" "<" "<=" "in" "not_in">, "value": <literal or array> }`. Nesting depth (a `conditional` inside another `conditional`'s `then`/`else`) SHALL be capped at 3.

#### Scenario: Simple conditional
- **WHEN** an ActorRef is `{ "type": "conditional", "condition": { "field": "amount", "op": ">", "value": 50000 }, "then": { "type": "role", "code": "CEO" }, "else": { "type": "expr", "path": "submitter.manager" } }`
- **THEN** the validator accepts it

#### Scenario: Excess nesting rejected
- **WHEN** a conditional's `then` is itself a conditional whose `then` is a conditional whose `then` is a conditional (depth 4)
- **THEN** the validator rejects with "conditional nesting capped at 3 levels"

### Requirement: collection composite

A `collection` ActorRef SHALL carry `mode` (`"any"` or `"all"`), `actors` (non-empty array of ActorRef), and `min_approvals` (positive integer; required when `mode = "any"`, optional when `mode = "all"`). When `mode = "any"`, `min_approvals` MUST be `<= actors.length`.

#### Scenario: Any-of-N with min_approvals
- **WHEN** a collection ActorRef has `mode = "any"`, `min_approvals = 2`, `actors` of length 3
- **THEN** the validator accepts it

#### Scenario: min_approvals exceeds actors length
- **WHEN** a collection ActorRef has `mode = "any"`, `min_approvals = 5`, `actors` of length 3
- **THEN** the validator rejects with "min_approvals (5) exceeds actors length (3)"

#### Scenario: Empty actors rejected
- **WHEN** a collection ActorRef has `actors = []`
- **THEN** the validator rejects

### Requirement: optional fallback field

Any ActorRef MAY carry an optional `fallback: ActorRef` field. The fallback SHALL be tried by the resolver only when primary resolution returns empty or errors. Fallback chains SHALL be capped at one level deep — a `fallback`'s `fallback` field SHALL be rejected by the validator.

#### Scenario: Single-level fallback accepted
- **WHEN** an ActorRef carries `{ ..., "fallback": { "type": "role", "code": "admin" } }`
- **THEN** the validator accepts it

#### Scenario: Two-level fallback chain rejected
- **WHEN** an ActorRef's fallback itself contains a fallback field
- **THEN** the validator rejects with "fallback chain limited to one level"
