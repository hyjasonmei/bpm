## ADDED Requirements

### Requirement: functional_members ActorRef type

The `functional_members` ActorRef SHALL carry:

- `function_tag` (string, non-empty, in `FunctionTagWhitelist`)
- `include_subtree` (bool, optional, default `false`) — when true, members of descendant departments are included
- `active_only` (bool, optional, default `true`) — when true, inactive users are excluded

Resolution returns the set of all users whose `department_id` matches the department tagged with `function_tag` (and, when `include_subtree`, descendant departments). The validator MUST reject `function_tag` not in the whitelist.

#### Scenario: Valid functional_members

- **WHEN** spec.json contains `{ "type": "functional_members", "function_tag": "finance" }`
- **THEN** the validator accepts it

#### Scenario: Off-whitelist function_tag rejected

- **WHEN** spec.json contains `{ "type": "functional_members", "function_tag": "marketing" }`
- **THEN** the validator rejects it with the allowed-tags list

#### Scenario: Defaults applied when omitted

- **WHEN** spec.json contains `{ "type": "functional_members", "function_tag": "hr" }` with no `include_subtree` or `active_only`
- **THEN** the parsed value carries `include_subtree = false`, `active_only = true`

### Requirement: ViewerRef discriminated union

The system SHALL define `ViewerRef` as a discriminated union over four variants. Every `ViewerRef` MUST have a `type` field whose value is one of: `"self"`, `"submitter"`, `"current_assignee"`, `"actor"`. The `actor` variant SHALL wrap an `ActorRef` in its `inner` field. The validator MUST reject any object lacking `type` or carrying a `type` value outside this set.

The first three types are *runtime-scoped* — they resolve in the context of a flow instance, not at spec-load time. The `actor` variant resolves via the standard `IActorResolver`.

#### Scenario: Valid self viewer

- **WHEN** spec.json contains `{ "type": "self" }`
- **THEN** the validator accepts it

#### Scenario: Valid actor viewer

- **WHEN** spec.json contains `{ "type": "actor", "inner": { "type": "role", "code": "auditor" } }`
- **THEN** the validator accepts it (inner ActorRef validates via `ActorRefValidator`)

#### Scenario: Missing inner on actor type rejected

- **WHEN** spec.json contains `{ "type": "actor" }` with no `inner` field
- **THEN** the validator rejects it

#### Scenario: Unknown viewer type rejected

- **WHEN** spec.json contains `{ "type": "ghost" }`
- **THEN** the validator rejects it listing the allowed type values

### Requirement: UserTask carries typed assignee and viewers

A `UserTask` SHALL carry `assignee: ActorRef` (required) and `viewers: ViewerRef[]` (optional, default empty array). The `permissions` wrapper from schema v1.0 / v1.1 SHALL be removed. Importers parsing legacy specs MUST migrate per the cheat-sheet:

- `permissions.submitter = 'self'` → `assignee = { type: 'expr', path: 'submitter', skip_if_initiator: false }`
- `permissions.submitter = 'role:X'` → `assignee = { type: 'role', code: 'X' }`
- `permissions.submitter = 'group:X'` → `assignee = { type: 'group', id: 'X' }`
- `permissions.viewers[i] = 'self'` → `viewers[i] = { type: 'self' }`
- `permissions.viewers[i] = 'manager'` → `viewers[i] = { type: 'actor', inner: { type: 'expr', path: 'submitter.manager', skip_if_initiator: false } }`
- `permissions.viewers[i] = 'role:X'` → `viewers[i] = { type: 'actor', inner: { type: 'role', code: 'X' } }`
- `permissions.viewers[i] = 'all'` → omit from result (all-readable is the default; an explicit "all" can be re-added via a wider role like `authenticated_user` if/when needed)

#### Scenario: UserTask with assignee accepted

- **WHEN** a UserTask carries `{ id: 't1', formCode: 'X', fields: [...], assignee: { type: 'role', code: 'hr' }, viewers: [{ type: 'self' }] }`
- **THEN** the validator accepts it

#### Scenario: UserTask without assignee rejected

- **WHEN** a UserTask carries no `assignee` field
- **THEN** the validator rejects it with "userTask requires assignee"

#### Scenario: Legacy permissions migrated

- **WHEN** a legacy UserTask carries `permissions: { submitter: 'role:HR', viewers: ['self', 'manager'] }`
- **AND** the importer runs
- **THEN** the migrated UserTask carries `assignee: { type: 'role', code: 'HR' }`, `viewers: [{ type: 'self' }, { type: 'actor', inner: { type: 'expr', path: 'submitter.manager', skip_if_initiator: false } }]`
