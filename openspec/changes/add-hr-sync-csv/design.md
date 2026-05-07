# Design notes

## 1. Why a multi-step import flow

Single-step "POST CSV → done" is risky:
- Wrong column mapping → catastrophic data write
- Bad data (cycle, malformed row) → partial DB state
- No way to review changes before commit

Multi-step flow:
1. Upload (file goes to file storage, immutable)
2. Map columns (admin reviews)
3. Dry-run (compute diff, no DB write)
4. Apply (transactional, idempotent)

Each step is reversible until apply. Apply is wrapped in a single transaction.

## 2. Email as immutable key

Why email? Because:
- Globally unique (within tenant)
- Doesn't change unless person changes name (rare)
- Customer always has it (mandatory for SSO etc.)

Alternatives:
- `employee_id` — varies by HR system; not always present
- Composite key — fragile

If a person's email changes (legal name change, marriage), the importer treats it as a new row + soft-deactivates the old one. Admin manually merges if needed (out-of-scope tooling).

## 3. Soft-delete by default, opt-in deactivation

Default behavior on apply: missing-from-CSV rows are *not* deactivated. Why?
- HR sometimes uploads a partial CSV (e.g., only the engineering team); we shouldn't deactivate finance
- Catastrophic mistake recovery is painful

Opt-in: `?deactivate_missing=true` flag. UI shows a clear warning checkbox.

## 4. CSV format

Standard:
- UTF-8 with BOM or without
- Comma-delimited (other delimiters: defer)
- First row = headers
- Empty cells = null (not empty string)

CsvHelper handles all this. We add:
- BOM detection for backwards compat
- Trim whitespace on every cell
- Reject mixed line endings (warn, accept)

Excel `.xlsx` is OUT — CSV only. If a customer absolutely needs Excel, we send back instructions ("Save as CSV").

## 5. Cycle detection

Manager cycle:
1. Build adjacency `email → manager_email` from CSV (and existing DB rows for hybrid)
2. DFS from each user; detect back-edges
3. Report: `"cycle: a@x.com → b@x.com → a@x.com"`

For department parent cycles: same algorithm on department codes.

Cycles abort the import with the path printed. No partial application.

## 6. Dry-run report shape

```json
{
  "imports_csv_rows": 87,
  "inserts": [
    { "row_number": 5, "email": "new@x.com", "full_name": "..." },
    ...
  ],
  "updates": [
    { "row_number": 12, "email": "wilson@x.com", "changes": { "title_raw": ["VP", "Senior VP"] } },
    ...
  ],
  "deactivations": [
    { "email": "left@x.com", "reason": "missing from CSV" }   // only when flag set
  ],
  "department_inserts": [{ "code": "PROD", "name": "PROD" }],
  "errors": [
    { "row_number": 19, "error": "manager email not found" }
  ],
  "warnings": [
    { "type": "dangling_manager", "email": "wilson@x.com", "manager_email": "vp@x.com" }
  ]
}
```

Errors abort apply; warnings allow apply.

## 7. Department auto-creation strategy

Two-file mode preferred (separate departments file with hierarchy). Single-file mode supported for convenience:

- For each unique `department_code` in CSV: if no Department row exists, INSERT with `Name = department_code` (placeholder), no parent, no function_tag, no head
- Admin enriches via `/api/departments/{id}` later

This ensures no row references a non-existent department; minimal-info departments are clearly marked for follow-up.

## 8. Concurrency

Two admins importing simultaneously? Last-write-wins on Apply, but pre-apply dry-run might show different state vs actual apply state. Mitigation: at apply time, re-run the dry-run pass and compare to the persisted dry-run report; if differ, refuse and ask admin to re-dry-run.

Single-tenant for now: only one admin per tenant typically; not a hot concurrency case.

## 9. Open questions

- **Column mapping presets**: save common mappings (e.g., "Workday CSV preset") for re-use across tenants. Defer.
- **Phone number / address fields**: not in current Org model; if customer wants, push into User.attributes JSON.
- **Photo upload via CSV**: column = URL of photo? Defer; out of scope.
- **Validation rules per tenant**: e.g., "email must end with @acme.com". Defer; could be a tenant config.
