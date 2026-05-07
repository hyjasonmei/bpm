# Design notes

## 1. Why DeletedAt vs IsActive vs Status enum

Three patterns exist in the codebase already:

- **IsActive bool** — User
- **Status enum** — StoredFile, BusinessCalendar
- **DeletedAt nullable** — Comment

Unifying on one is tempting but requires breaking changes. Pragmatic choice:

- Keep User.IsActive (HR-driven semantics, distinct from "deleted")
- StoredFile.Status (multi-state: Pending / Attached / Orphaned / Deleted)
- BusinessCalendar.Status (Active / Archived — admin curation, distinct from deletion)
- New entities use DeletedAt (consistent with Comment)

For the entities that currently hard-delete (Role / Department / etc.), add DeletedAt. Two patterns coexisting is an inconsistency we accept; consolidating is a future tech-debt issue.

## 2. EF query filters

For each soft-deletable entity in OnModelCreating:

```csharp
modelBuilder.Entity<Department>().HasQueryFilter(d => d.DeletedAt == null);
```

This makes default queries (`_db.Departments.ToListAsync()`) exclude deleted. Admin views explicitly opt-in via `_db.Departments.IgnoreQueryFilters()`.

Caveat: cross-entity joins propagate filters. A query joining Department with User might omit Users in deleted Departments. Test critical paths.

## 3. Why no cascading soft-delete

Imagine an admin soft-deletes a Department containing 5 users. Cascade options:

- **Cascade**: users' department_id set to null; they become "orphan" employees
- **Block**: forbid delete if dept has members; admin must reassign first
- **Soft-link**: members keep pointing to the deleted dept; admin manually fixes

Decision: **block**. UI prevents the action with "Reassign 5 members first". Cascade is too dangerous; soft-link is confusing.

Same pattern for Role / Group / others.

## 4. Restore semantics

Restore is idempotent: `DeletedAt = null`. Side effect: role permissions immediately re-active; users in restored dept regain context. Admin should confirm.

Audit captures both deletes and restores via the auto-interceptor (`<entity>.deleted` + `<entity>.restored`).

## 5. Performance

EF query filter adds `WHERE DeletedAt IS NULL` to every default query. Index on DeletedAt for entities with high read frequency:

```sql
CREATE INDEX idx_departments_active ON Departments(DeletedAt) WHERE DeletedAt IS NULL;
```

(SQLite supports filtered indexes; Postgres does too.)

## 6. Open questions

- **Visibility of deleted-entity references**: when a completed instance's task assignee is now in a deleted dept, do reports show "Wilson (Engineering — deleted)" or just "Wilson"? Decision: show with deleted indicator, allow drill-into to see context.
- **Search index**: deleted users / cases shouldn't appear in search by default; they should appear when admin filters "show deleted".
- **Backup-restore safety**: hard-deleting is irreversible from app; soft-delete preserves recovery. Any production restore should respect soft-delete state from backup.
