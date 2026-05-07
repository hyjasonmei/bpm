# Design notes

## 1. Why FTS5 for SQLite, GIN for Postgres

SQLite ships FTS5 — full-text search virtual tables with built-in tokenization, snippet, ranking. Free, fast, no setup.

For Postgres (when we migrate): GIN index on tsvector gives equivalent capability. Same query shape semantically.

Both natively handle Chinese tokenization adequately when configured correctly. CJK characters are typically tokenized character-by-character (n-grams via the FTS5 `unicode61` tokenizer) — not perfect but workable for SME case search. Customer with hard requirements can plug in JIEBA later.

## 2. Why interceptor-driven sync vs background job

Trade-off:

- **Interceptor (synchronous)**: search index always consistent with primary data. Cost: small write overhead per save.
- **Background job (eventual)**: zero write impact on hot path. Cost: stale indexes during gap, complexity of catch-up.

For SME write volume (10s of TaskSubmitted per minute peak), the interceptor cost is invisible. Pick consistency over throughput.

## 3. Search index content per entity

`ProcessInstanceSearchIndex.searchable_text` content:
- spec_code (e.g., "LEAVE")
- spec.flowName (e.g., "請假")
- initiator full_name
- joined form data values: walk the form data JSON, extract all string values, concat with spaces (numbers ignored — use form_amount for those)
- task assignees' names (current open tasks' actualAssigneeUserId names)
- comment bodies (concatenated, with author names)

This single text blob makes a query like `"出差 5天"` find an instance whose form mentions both. Length cap: 50 KB per row.

## 4. Per-user filter scope

Search results are auth-filtered:
- Regular user: instances they initiated OR are/were assignee of
- Admin: all in tenant

The search service applies the auth filter as a WHERE clause on the underlying tables; FTS5 just provides text matching.

## 5. Numeric filtering on amount

`form_amount` is denormalized — extracted from form data when an `amount` field exists. Range queries (`amount_min`, `amount_max`) use this column. Multiple amount fields per spec? Pick the canonical one (heuristic: first numeric field named `amount` / `total` / `total_amount`); if more nuance needed, future per-spec config.

## 6. Pagination

Simple offset-based: `?page=&size=` with size capped at 100. For very large result sets a cursor approach is better; SME scale doesn't need it.

## 7. Highlighting / snippets

FTS5's `snippet()` function provides snippet generation around match terms. Frontend renders the snippet with `<mark>` for matches.

## 8. Open questions

- **Search across spec definitions**: search inside the spec.json (label text) — useful for "find specs that include leave_type = 病假". Defer; fold into Process Admin search later.
- **Case-insensitive Chinese**: FTS5 handles via the unicode61 tokenizer; no special config needed.
- **Synonym expansion**: 副總 ↔ VP; not in v1. If a customer asks, add a synonym lookup pass before FTS query.
