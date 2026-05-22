## Why

The current `bpm-actor-dsl` covers six types (`expr`, `role`, `group`, `user`, `conditional`, `collection`) and the `bpm-org-model` covers User / Department / Group / Principal. Together they handle the leave-flow demo, but they do **not** cover three classes of routing that show up in nearly every real flow the partner has collected from Taiwanese mid-sized manufacturers:

1. **Functional routing** — "this needs finance / HR / IT approval" — requires identifying *which* department plays the finance role at this customer (`財務部` vs `會計暨財務處` vs `Treasury`). No `function_tag` on Department today, so the resolver has no bridge from DSL vocabulary to the customer's organic naming.
2. **Amount-based routing** — "≥ NT$ 50K needs the next-level manager up the chain who has approval authority for that amount". No `approval_limit` on User, no `by_amount` resolver type.
3. **Title-pattern routing** — "any 副總 in this department" — common in Taiwanese matrix orgs where titles are free text and there are multiple deputies. No `title_normalized` on User, no `title_match` resolver.

In addition, the AI pipeline (Stage 1: NL → DSL) has no first-class way to say "I'm uncertain". Today, low-confidence outputs would either fabricate a wrong actor or fail validation. We need a typed `unresolved` node that surfaces low-confidence chunks to the user for manual confirmation, plus per-node metadata (`intent`, `confidence`, `needs_review`) so reviewers can audit AI-generated specs.

This change closes those gaps without disturbing the existing `expr` path-walk approach (which stays intact for hierarchical walks). The path-based design and the type-based design coexist — `expr` for org-graph walks, the new types for functional / amount / title / AI-fallback semantics.

## What Changes

### Org model extensions (`bpm-svc`)

Backwards-compatible column additions to `User` and `Department`:

- `Department.function_tag` (string, nullable, indexed) — set during onboarding, mapping customer dept → DSL vocabulary (`"finance"`, `"hr"`, `"it"`, `"legal"`, `"operations"`, `"procurement"`, `"audit"`, `"quality"`, `"general_affairs"`)
- `User.title_normalized` (string, nullable, indexed) — computed from raw title at sync time. Strips prefixes (`資深`, `副`, `代理`), unifies CN/EN (`副總` ↔ `VP` ↔ `Vice President`)
- `User.approval_limit` (decimal, nullable) — threshold in NT$ for amount-based routing; null means "no authority" (resolver walks past)
- `User.is_department_head` (bool, computed/cached) — denormalized from `Department.head_user_id` for high-frequency lookups
- `User.is_executive` (bool, computed/cached) — denormalized from `title_normalized` matching exec patterns
- `User.attributes` (JSON, nullable) — tenant-specific or low-frequency fields; do not add new columns for fields used by one customer

`Department.approval_limit` (decimal, nullable) — department-level cap, used by `by_amount` walks across the dept tree.

### ActorRef DSL extensions (`bpm-actor-dsl`)

Add four new ActorRef types to the discriminated union:

- `functional_head` — `{ type: "functional_head", function_tag: "finance" }` resolves to the `head_user_id` of the Department whose `function_tag` matches.
- `by_amount` — `{ type: "by_amount", amount_field: "amount", from: "submitter" | "current_approver", strategy: "manager_chain" | "department_tree" }` walks up until finding someone with `approval_limit >= form.<amount_field>`.
- `title_match` — `{ type: "title_match", patterns: ["副總", "VP"], scope: "company" | "same_department" }` returns all active users whose `title_normalized` matches any pattern (LIKE-based).
- `unresolved` — `{ type: "unresolved", intent: "...", reason: "...", suggested_clarification: "..." }` first-class AI-fallback node; resolver returns a structured "unresolved" status that surfaces to the spec author.

Additionally, every ActorRef MAY carry these optional metadata fields (validator accepts but does not require):

- `intent` (string) — natural-language description of business meaning (set by AI in Stage 1)
- `confidence` (number, 0-1) — AI's confidence in this resolution
- `needs_review` (bool) — flag for reviewer attention; defaults true when type is `unresolved`
- `skip_if_initiator` (bool) — exclude initiator from results; default true (matches existing implicit behavior)

The `expr` path whitelist is unchanged.

### Workflow Resolver extensions (`bpm-workflow-resolver`)

- `ResolveFunctionalHead`: query `Department` where `function_tag = X`, return its `head_user_id`. Empty result → trigger `fallback`.
- `ResolveByAmount`: read `form_data[amount_field]` from context; walk manager chain (or dept tree) starting from `submitter` (or `current_approver`); return the first user/dept whose `approval_limit >= amount`. Cap at 10 levels per existing rule. No one qualifies → empty + structured failure (`AmountExceedsAllAuthorities`).
- `ResolveTitleMatch`: SQL `LIKE` over `title_normalized` with patterns; scope clause adds `WHERE department_id = ctx.submitter.department_id` for `same_department`.
- `ResolveUnresolved`: never resolves to users; always returns `Failure(Kind = UnresolvedAiNode, Reason = ref.Reason)`. The runtime treats this specially: don't retry, don't fallback, surface directly to the admin queue.

`ResolutionError.Kind` enum gains: `AmountExceedsAllAuthorities`, `FunctionTagNotMapped`, `TitleNoMatch`, `UnresolvedAiNode`.

`skip_if_initiator` semantics: applied uniformly at the resolver wrapper level — every `Resolve*` method's success result is post-filtered to drop `ctx.initiator_user_id` when the flag is true (default).

### Sample specs + docs

- `spec_schema.md` §2.10: append the four new types with worked examples and condition operators table; add metadata-fields subsection.
- `prompt_template_v1.md`: add four worked examples, especially for `unresolved` (so AI knows to use it on uncertainty rather than guessing).
- `sample_specs/expense_with_threshold_v1.json`: replace the existing `conditional` amount routing with `by_amount` to demonstrate the new type.
- New `sample_specs/it_request_v1.json`: a small flow exercising `functional_head` (IT) + `unresolved` (AI placeholder for an ambiguous step).

### Catalog (preview, not in this change)

The full Catalog (Stage 1 AI context builder) is its own future change. This proposal only ensures the *vocabulary* the Catalog will use exists in the schema (`function_tag`, `title_normalized`).

### Wizard UI

`ActorRefEditor` gains four new picker options:

- 部門功能主管 (`functional_head`) — function_tag dropdown populated from `/api/org/function-tags`
- 金額簽核 (`by_amount`) — amount-field picker (form fields of type number) + strategy toggle (chain/tree)
- 職稱比對 (`title_match`) — pattern list (multi-select tag input) + scope toggle
- 待釐清 (`unresolved`) — only inserted by AI; UI shows as a yellow card with reason + suggested clarification + "release as" button to convert to a concrete actor

Metadata fields (`intent`, `confidence`, `needs_review`) shown read-only on every ActorRef card when present (debug-style affordance for review, not for direct editing).

### Out of scope (future changes)

- ProcessDefinition / ProcessVersion / ProcessInstance / Task / TaskHistory tables (the runtime, separate change).
- CEL parser for condition expressions.
- Stage 1 (NL → DSL) AI pipeline + Catalog builder.
- Stage 2 (DSL → C#) codegen + generation contracts.
- Multi-tenancy (tenant_id columns).
- Admin UIs (System Admin / Process Admin).
- Delegation tables.
- HR sync agent.
- AD / Entra ID integration.

## Capabilities

### Modified Capabilities
- `bpm-actor-dsl`: extend ActorRef discriminated union with `functional_head`, `by_amount`, `title_match`, `unresolved`; add optional metadata fields (`intent`, `confidence`, `needs_review`, `skip_if_initiator`).
- `bpm-org-model`: extend `User` and `Department` with `function_tag`, `title_normalized`, `approval_limit`, `is_department_head`, `is_executive`, `attributes` JSON.
- `bpm-workflow-resolver`: extend resolver to handle the four new ActorRef types; expand `ResolutionError.Kind`; apply `skip_if_initiator` uniformly.
- `bpm-wizard-actor-editor`: extend type picker dropdown with the four new choices; show metadata as read-only annotations.

### New Capabilities
None — this change is purely an extension of four existing capabilities.

## Impact

- **bpm-svc/src/Domain**: `User` and `Department` gain new properties; `ActorRef.cs` gains four new derived records; `Resolution.cs` gains four new `ErrorKind` values.
- **bpm-svc/src/Persistence**: One additive migration `ExtendOrgAndActorTypes`. New indexes on `Department.FunctionTag`, `User.TitleNormalized`, `User.ApprovalLimit`. No data backfill needed (all new columns nullable).
- **bpm-svc/src/Application**: `ActorResolver` gets four new resolve methods; `ActorRefValidator` extends to validate new types. `ActorRefJsonConverter` extended for polymorphic deserialization.
- **bpm-ui/src/lib/actor-ref.ts**: TypeScript types extended; whitelist updated for the new types' shape.
- **bpm-ui/src/components/wizard/ActorRefEditor.tsx**: dropdown options + four new editor sub-components.
- **spec_schema.md, prompt_template_v1.md**: documentation updates with worked examples.
- **sample_specs/**: one updated sample, one new sample.
- **No breaking changes**: existing specs using `expr`/`role`/`group`/`user`/`conditional`/`collection` continue to validate and resolve identically. The four new types are additive.
- **Migration is purely additive** (no column drops, no NOT NULL on existing columns, no data conversion).
- **Dependencies added**: none (existing EF Core, FluentValidation, etc. cover this).
