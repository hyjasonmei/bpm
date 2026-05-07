# Tasks

## 1. Search index schema

- [ ] 1.1 Migration `AddSearchIndex` creates SQLite FTS5 virtual tables: `instance_search`, `user_search`, `comment_search`
- [ ] 1.2 Each FTS5 table has a `content` column + standard FTS5 metadata
- [ ] 1.3 Add per-row column `id` to map back to the entity
- [ ] 1.4 Indexes: `(spec_code, status, started_at)`, `(initiator_user_id)`, `(form_amount)`
- [ ] 1.5 For Postgres compatibility down the road: SQL helper view layer that abstracts FTS5 vs GIN

## 2. Index sync interceptor

- [ ] 2.1 Create `bpm-svc/src/Persistence/Interceptors/SearchIndexUpdateInterceptor.cs`
- [ ] 2.2 On SaveChanges: detect changes to ProcessInstance / Comment / User; build search index content; UPSERT in same transaction
- [ ] 2.3 Initial backfill on migration: walk existing rows, populate the indexes
- [ ] 2.4 Tests: insert ProcessInstance → verify search returns it; update form data → verify content updated

## 3. Search service

- [ ] 3.1 Create `ISearchService.cs`
- [ ] 3.2 Implement `SearchService.cs`:
  - SearchInstancesAsync: FTS5 MATCH on `instance_search.content` + filter clauses on `process_instances` (joined)
  - SearchUsersAsync: FTS5 MATCH on `user_search`
  - SearchCommentsAsync: FTS5 MATCH on `comment_search`; filter to instances the user can read
  - GlobalSearchAsync: union of three with type tag
- [ ] 3.3 Auth-filter results based on caller role
- [ ] 3.4 Snippet generation via FTS5 snippet() function
- [ ] 3.5 Tests: happy paths + auth filter

## 4. API endpoints

- [ ] 4.1 Create `bpm-svc/src/Api/Search/SearchController.cs` with 4 endpoints
- [ ] 4.2 Standard pagination (page / size) with size cap 100
- [ ] 4.3 Integration tests

## 5. Frontend Search screen

- [ ] 5.1 Rewrite `bpm-ui/src/screens/Search.tsx` to use the new search APIs
- [ ] 5.2 Type toggle (cases / users / comments / global)
- [ ] 5.3 Filter sidebar with spec / status / date range / amount range
- [ ] 5.4 Results list with snippet rendering (HTML-escaped, `<mark>` for matches)
- [ ] 5.5 Click result → navigate to instance / user / comment
- [ ] 5.6 Bilingual labels

## 6. End-to-end verification

- [ ] 6.1 `dotnet build` clean
- [ ] 6.2 Apply migration; verify FTS5 tables; backfill ran
- [ ] 6.3 Submit a ProcessInstance with form data containing "出差日本"; search "日本"; verify it appears with snippet
- [ ] 6.4 Filter by spec_code and date range; verify constraints applied
- [ ] 6.5 Login as different users; verify per-user auth scope (regular user sees only their cases)
- [ ] 6.6 Search comments containing a specific phrase; verify only authorized comments returned
- [ ] 6.7 **Demo guard**: 9 mock-up forms, Home, Report, lib/workflow.ts NOT modified; Search.tsx behavior swap intended

## 7. Commit

- [ ] 7.1 Commit in chunks (migration + interceptor; service; endpoints; frontend; verification)
- [ ] 7.2 Push via GitKraken
