## Why

`bpm-ui/src/screens/Search.tsx` is a UI mock — search bar + frozen results. Real product needs cross-cutting search:

- "Find all my cases mentioning '出差'"
- "Find all leave requests by Wilson in March"
- "Find IT purchases with amount > 50000 last quarter"
- "Find cases where I'm the current assignee waiting on my action"
- "Find users with 'Manager' in title"

Without search, users can't navigate hundreds of cases. Real product capability.

## What Changes

### Search capability (NEW `bpm-search`)

**Search index** — denormalized search-friendly tables:

- `ProcessInstanceSearchIndex` — one row per ProcessInstance with: id, tenant_id, spec_code, initiator_user_id, status, started_at, completed_at, **searchable_text** (concatenated form data values + spec name + initiator name), **form_amount** (extracted numeric for amount-based filtering)
- `UserSearchIndex` — full_name, email, title_normalized, dept_name (denormalized)
- `CommentSearchIndex` — body, instance reference

Index updates trigger via EF SaveChanges interceptor: when ProcessInstance / Comment / User mutates, sync the search index row.

For SQLite POC: uses FTS5 (full-text search) virtual tables — gives us snippet highlighting + ranking.

When migrating to Postgres later: switch to Postgres GIN index on tsvector. Same shape.

### Backend search service

`ISearchService`:

- `SearchInstancesAsync(query, filters, pagination)` — text + filter
- `SearchUsersAsync(query, filters)` — admin only
- `SearchCommentsAsync(query, filters)` — body text within reachable instances
- `GlobalSearchAsync(query)` — combined; returns small result snippets with type indicator

Filters available on instances:

- `spec_code`, `status`, `initiator_user_id`, `assignee_user_id`, `started_after`, `started_before`, `completed_after`, `completed_before`, `amount_min`, `amount_max`, `tags` (future field)

### Search index update strategy

EF SaveChanges interceptor approach:

- Track ProcessInstance / Comment / User entity changes
- Build the search index row content from current state
- UPSERT into the index table in the same transaction

For form data accumulation events (every TaskSubmitted), refresh the instance's `searchable_text` to include the latest field values.

### API endpoints

- `GET /api/search/instances?q=&spec_code=&status=&...&page=&size=` — returns `{ items, total, page, size }`
- `GET /api/search/users?q=` — admin only
- `GET /api/search/comments?q=&instance_id?=&author?=`
- `GET /api/search/global?q=` — returns mixed-type results

### Frontend — Search screen

`bpm-ui/src/screens/Search.tsx` rewritten:

- Top: search bar with type toggle (cases / users / comments / global)
- Filters sidebar (spec, status, date range, amount range, my-vs-all)
- Results list with snippet + highlight on match
- Click result → navigate to instance detail / user detail / comment thread

The existing Search.tsx mock is replaced — but its visual style is kept as the foundation; this is a behavior swap, not a complete rewrite of look. (`forms/*` mocks still untouched.)

### Out of scope (future changes)

- Saved searches
- Advanced query operators (`OR`, `NOT`, exact-phrase quoting beyond basic)
- Faceted search with counts per facet
- ElasticSearch / OpenSearch integration (FTS5 / GIN sufficient for SME scale)
- Search across attachments (OCR for PDFs)
- AI semantic search (deferred per "no AI experimental")
- Auto-complete suggestions
- Search history per user

## Capabilities

### New Capabilities

- `bpm-search` — search index tables (FTS5-backed), interceptor-driven sync, ISearchService, search endpoints, replaced Search.tsx behavior.

### Modified Capabilities

- None.

## Impact

- **bpm-svc/src/Domain/Entities/Search/**: 3 search index entities (or use FTS5 directly without typed entities)
- **bpm-svc/src/Persistence/Configurations/Search/**: FTS5 virtual table creation in migration
- **bpm-svc/src/Persistence/Interceptors/SearchIndexUpdateInterceptor.cs**: syncs index on entity changes
- **bpm-svc/src/Application/Search/ISearchService.cs / SearchService.cs**: query orchestration
- **bpm-svc/src/Api/Search/SearchController.cs**: 4 endpoints
- **bpm-ui/src/screens/Search.tsx**: rewritten (replaces mock)
- **bpm-ui/src/lib/search.ts**: client + types
- **DB migration**: FTS5 virtual tables; population pass for existing data
- **Demo guard**: 9 mock-up forms NOT modified; Search screen behavior swap is intentional (mock → real)
