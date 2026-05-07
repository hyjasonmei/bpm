## ADDED Requirements

### Requirement: Department carries a function_tag

The system SHALL persist `Department` with an optional `function_tag` string column, drawn from a fixed whitelist (`finance`, `hr`, `it`, `legal`, `operations`, `procurement`, `audit`, `quality`, `general_affairs`). At most one Department MAY hold each tag (the bridge between DSL vocabulary and the customer's specific department naming). The column SHALL be indexed for single-row lookup. `function_tag` is set during onboarding by the system administrator, NOT synced from HR.

#### Scenario: Lookup by function_tag

- **WHEN** Department `財務部` has `function_tag = "finance"`
- **AND** the resolver looks up `functional_head` with `function_tag = "finance"`
- **THEN** the system finds `財務部` and returns its `head_user_id`

#### Scenario: Unmapped function_tag

- **WHEN** no Department has `function_tag = "audit"`
- **AND** the resolver looks up `functional_head` with `function_tag = "audit"`
- **THEN** the system returns a structured `FunctionTagNotMapped` failure

### Requirement: User carries title_normalized

The system SHALL persist `User` with two title-related columns:
- `title_raw` (string, nullable) — the title exactly as it arrives from HR or onboarding entry
- `title_normalized` (string, nullable, indexed) — a normalized form computed from `title_raw` at sync time, suitable for LIKE matching

The normalization SHALL strip seniority/acting prefixes (`資深`, `副`, `代理`, `Senior`, `Deputy`, `Acting`) and unify common CN/EN equivalents (`副總` ↔ `VP` ↔ `Vice President` etc.) via a table-driven rule. Titles that don't match any unification rule SHALL be stored as the lowercased trimmed input.

#### Scenario: Normalize Chinese senior VP

- **WHEN** a User has `title_raw = "資深副總"`
- **AND** the normalizer runs
- **THEN** `title_normalized = "vp"`

#### Scenario: Normalize English deputy director

- **WHEN** a User has `title_raw = "Deputy Director"`
- **AND** the normalizer runs
- **THEN** `title_normalized = "director"`

#### Scenario: Unknown title preserved as fallback

- **WHEN** a User has `title_raw = "首席布偶設計師"` (no rule matches)
- **AND** the normalizer runs
- **THEN** `title_normalized = "首席布偶設計師"` (lowercased trimmed)

### Requirement: User and Department carry approval_limit

The system SHALL persist `User.approval_limit` (decimal, nullable) and `Department.approval_limit` (decimal, nullable). A null value means "no specified authority" — the `by_amount` resolver walks past such candidates. A zero value means "explicitly zero authority" — also walked past. Only candidates whose `approval_limit >= form.<amount_field>` qualify.

#### Scenario: User with sufficient authority

- **WHEN** a User has `approval_limit = 100000`
- **AND** the resolver evaluates `by_amount` with `amount = 50000`
- **THEN** the User qualifies

#### Scenario: User with null limit

- **WHEN** a User has `approval_limit = null`
- **AND** the resolver evaluates `by_amount`
- **THEN** the User does not qualify, the resolver continues walking up

#### Scenario: User with zero limit

- **WHEN** a User has `approval_limit = 0`
- **AND** the resolver evaluates `by_amount` with `amount = 1`
- **THEN** the User does not qualify, the resolver continues walking up

### Requirement: User carries denormalized role flags

The system SHALL persist on `User`:
- `is_department_head` (bool, default false) — denormalized from `Department.head_user_id` matching this user's id, refreshed by sync logic or at write time
- `is_executive` (bool, default false) — denormalized from `title_normalized` matching exec patterns (`vp`, `cxo`, `director`, etc.), refreshed at title-normalize time

These flags exist to avoid recursive joins on high-frequency lookups. They are NOT canonical — the canonical sources are `Department.head_user_id` and `User.title_normalized`. Any divergence between flag and source MUST be reconciled by the next sync run.

#### Scenario: Flags refreshed on title change

- **WHEN** a User's `title_raw` is updated and `TitleNormalizer.Normalize` produces `"vp"`
- **THEN** `is_executive` is set to `true` in the same write

#### Scenario: Flags refreshed on dept-head change

- **WHEN** a Department's `head_user_id` is changed from User A to User B
- **THEN** `is_department_head` is updated on both A (false) and B (true) in the same write

### Requirement: User carries an attributes JSON column

The system SHALL persist `User.attributes` (string, nullable, containing JSON object) for tenant-specific or low-frequency fields. Validators MUST NOT reject specs that read fields out of `attributes` (the attribute structure is per-tenant). New columns SHALL NOT be added for fields used by only one customer; place them in `attributes` instead.

#### Scenario: Custom field in attributes

- **WHEN** a customer needs to track `cost_center_code` per user
- **AND** that field appears only for this customer
- **THEN** the field is stored in `User.attributes` as JSON, not as a new column

#### Scenario: Empty attributes

- **WHEN** a User has `attributes = null`
- **THEN** queries against attributes paths return empty without throwing
