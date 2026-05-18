## ADDED Requirements

### Requirement: Search modal + dedicated page

The system SHALL provide a Search affordance from the top nav. Clicking "Search" SHALL open a modal overlay (Esc / outside-click to dismiss) over the current screen, with filter inputs and a results table. A separate `/search` screen SHALL also exist for users who want a full-page search experience. Both surfaces SHALL operate over the same mock dataset (`MOCK_CASES` extended to ~30 entries spanning all 9 form types and all statuses).

#### Scenario: Open + close
- **WHEN** the user clicks the Search nav item, then presses Escape
- **THEN** the modal opens, then closes returning focus to the prior screen

### Requirement: Search filters

Search SHALL accept: free-text Keyword (matches request no. / type label / requestor / dept), Request No. exact-prefix, multi-select Form Types, multi-select Statuses, Requestor, and a date range (Submitted From / To). The "Clear All" action SHALL reset every filter and the results.

#### Scenario: Filter combination
- **WHEN** the user selects Form Type "GEE", Status "Pending Approval", and clicks Search
- **THEN** results show only GEE rows whose status is Pending Approval

#### Scenario: Empty filters
- **WHEN** the user opens Search and clicks Search without filters
- **THEN** the full mock dataset (~30 rows) is returned, paginated 10 per page

### Requirement: Results pagination

Results SHALL paginate at user-selectable page sizes (10 / 25 / 50). Page count and current page SHALL be displayed; navigation SHALL provide first / prev / next / last buttons.

#### Scenario: Pagination renders
- **WHEN** results exceed the page size
- **THEN** a paginator below the table shows `Page 1 of N · Showing 1-10 of T` with the four nav buttons
