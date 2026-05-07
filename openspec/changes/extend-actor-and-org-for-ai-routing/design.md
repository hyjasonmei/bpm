# Design notes

## 1. Why extend rather than replace ActorRef

The ai_bpm_spec.md you sent describes resolvers as *type-based* (`relative_manager`, `functional_head`, `by_amount`, `title_match`, ...). The current `bpm-actor-dsl` is *path-based* (`expr` + whitelisted path strings).

Both representations cover the hierarchical walks (`submitter.manager`, `submitter.department.head`). But they diverge for:

- `functional_head` — needs a tag, not a walk. There is no path that says "the dept tagged `finance`" — you'd need `function_tag` as a graph edge, which it isn't (it's an attribute).
- `by_amount` — needs a runtime predicate (`approval_limit >= form.amount`), not a fixed path.
- `title_match` — needs a pattern match, not a walk.

The simplest reconciliation: **keep `expr` for hierarchical walks, add the three predicate-based resolvers as their own types**. This:

- Preserves all existing specs (no migration of `expr` paths).
- Aligns the new types' names with the spec vocabulary (`functional_head`, `by_amount`, `title_match`) so AI prompts can use the same words.
- Avoids overloading `expr.path` with sentinel strings like `"submitter.department[function_tag=finance].head"` (which would be an ad-hoc query language).

`unresolved` is its own type, not metadata, because:

- It must be a structural node (so the runtime can branch on it without parsing metadata).
- It changes resolver semantics (never resolves to users, always returns failure).
- It's a placeholder shape, not an annotation on an otherwise-valid actor.

## 2. function_tag whitelist

Function tags are bounded vocabulary, not free text. Initial set:

| Tag | Purpose |
|---|---|
| `finance` | 財務 — anything money-related |
| `hr` | 人資 — leave, hire, headcount |
| `it` | 資訊 — IT requests, access |
| `legal` | 法務 — contracts, compliance |
| `operations` | 營運 — production, supply chain |
| `procurement` | 採購 — purchase requests |
| `audit` | 稽核 — internal audit |
| `quality` | 品保 — quality control (manufacturing-specific) |
| `general_affairs` | 行政總務 — facility, travel arrangements, supplies (TRQ notify-admin uses this) |

We use `general_affairs` instead of `admin` to avoid overloading "admin" — `admin` already names the system-administrator role / persona, and reusing it for a department function would cause confusion in the codebase ("is this the IT admin or the 行政部?").

Stored as a string column for now (not a lookup table) — the set is small and stable enough that an enum-style validator suffices. If a customer needs a custom tag we can add it via config rather than table row. Validator uses a `FunctionTagWhitelist` static class, mirroring `ActorPathWhitelist`.

The whitelist is the **DSL vocabulary**. The customer's actual department name is in `Department.name` (`財務部`); `function_tag` is the bridge.

## 3. title_normalized — what's the normalization rule?

Pseudocode:

```
normalize(title):
  s = title.trim().toLowerCase()
  # strip seniority / acting prefixes
  s = strip_prefix(s, ["資深", "副", "代理", "senior", "deputy", "acting"])
  # CN/EN unification (table-driven)
  s = unify(s, {
    "副總": "vp",
    "vice president": "vp",
    "vp": "vp",
    "經理": "manager",
    "manager": "manager",
    "處長": "director",
    "director": "director",
    ...
  })
  return s
```

Run at HR sync time, not at query time. Stored on `User.title_normalized`. If a customer's title doesn't fit any unification rule, store the lowercased trimmed string as-is — `title_match` patterns can still hit it via LIKE.

This change does **not** ship a sync agent. It ships the *column* and a unit-tested normalizer function, plus a one-shot CLI command (`dotnet run -- normalize-titles`) for the seed fixture. Real sync comes later.

## 4. by_amount semantics

```jsonc
{
  "type": "by_amount",
  "amount_field": "amount",          // form field (number)
  "from": "submitter",               // or "current_approver"
  "strategy": "manager_chain",       // or "department_tree"
  "include_self": false              // do you accept the starting user themselves?
}
```

Resolution:

1. Read `amount = ctx.form_data[amount_field]`. If missing or not numeric → `Failure(ValidationFailed, "amount field missing or non-numeric")`.
2. Resolve start user (`submitter` or `current_approver`).
3. Walk up:
   - `manager_chain`: walk `User.manager_id` upward
   - `department_tree`: walk `Department.parent_id` upward, taking each dept's `head_user_id` as a candidate
4. At each candidate, check `candidate.approval_limit >= amount` (or `Department.approval_limit` for tree mode). First match wins.
5. Cap at 10 levels (existing rule).
6. No match within cap → `Failure(AmountExceedsAllAuthorities, "amount X exceeds all authorities up to N levels")`.

`include_self` defaults `false` (you usually don't want the submitter approving their own request). Combine with the global `skip_if_initiator` flag for safety.

## 5. unresolved node

```jsonc
{
  "type": "unresolved",
  "intent": "需要部門主管的下一級長官",
  "reason": "AI 無法判斷『下一級長官』是直接上級還是該部門更高層長官",
  "suggested_clarification": "請使用者澄清：(a) 申請人的直屬主管的主管 (b) 該部門部長之上的處長",
  "confidence": 0.42
}
```

Resolver: `Resolve(unresolved) → Failure(UnresolvedAiNode, ref.reason)`.

Runtime contract: when an `unresolved` node is hit:

1. Mark the task `status = 'unresolved'`.
2. Insert a placeholder Task with `actual_assignee_id = NULL`, `original_assignee = serialized ref`, `requires_admin_intervention = true`.
3. Notify the process admin via the dashboard's "Specs needing clarification" queue.
4. Do **not** apply fallback (the spec author wanted clarification, not a guess).

This change ships only the resolver behavior + audit. The runtime hooks are part of the future Process Runtime change.

## 6. Metadata fields — validator behavior

| Field | Type | Validation |
|---|---|---|
| `intent` | string | optional, max 500 chars |
| `confidence` | number | optional, 0.0 ≤ x ≤ 1.0 |
| `needs_review` | bool | optional, defaults `false` for non-unresolved types, `true` for `unresolved` |
| `skip_if_initiator` | bool | optional, defaults `true` |

Validator accepts but never uses these to decide pass/fail (except range checks). They're purely informational + behavior-flag for the resolver.

For `confidence`, no threshold logic at this layer — the AI pipeline (Stage 1) is responsible for converting low confidence into an `unresolved` node before serializing. By the time it reaches the resolver, low-confidence `expr`/`role`/etc. nodes are still treated as authoritative.

## 7. Migration — keep it additive

- All new columns nullable.
- No column drops, no renames, no type changes on existing columns.
- New indexes are non-unique (function_tag, title_normalized, approval_limit) — they're for query speed, not constraints.
- The migration name: `ExtendOrgAndActorTypes`.

If we later regret the column choices we can drop them in a separate migration. No data is being moved; the existing seed fixture still loads cleanly because all new fields default to null.

## 8. Open questions

These are deliberately deferred — flag them and decide later, not in this change.

1. **Should `function_tag` be on a join table** (`DepartmentFunction`) so a single dept can serve multiple functions (e.g., a small company where 管理部 does both HR and IT)? Current design says no — one tag per dept, simpler. Revisit if the partner finds a customer where this fails.
2. **Should `title_normalized` be a computed column** (set by EF interceptor on save) rather than a sync-time column? Probably yes for a real product; for POC, sync-time CLI is enough.
3. **Should `approval_limit` be a single number or a list of `(category, limit)` tuples** (e.g., approves up to 50K for travel but 100K for purchase)? Current design says single. If/when a customer needs categorized limits, switch to a `ApprovalAuthority` table.
4. **`title_match.scope` enum** — only `company` and `same_department` for now. We could imagine `parent_department`, `subtree`, etc.; defer until a real use case.
