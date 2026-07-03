# Plan — Site Setting Feature Tables (archive via rename)

Lets the admin user spot chef-cooked tables that have no matching
`Admin_Flows` row (orphans), as well as archive linked features —
freeing the `<CODE>_V<N>` namespace for a fresh cook of the same
flow code/version. Triggered by 開發者's TG thread today: LEAVE V1
testbed left chef tables in the DB with no admin row backing them.

Decisions locked 2026-05-29 via TG:

- **Archive = rename, not drop.** Data stays in the DB; the tables
  carry a `__arch_<8hex>` suffix so the original namespace is free.
- **Independent `ArchivedAt` column on Flow.** Orthogonal to the
  state machine — admin can archive an Approved flow, a Retired
  flow, or even an orphan with no Admin_Flow row at all.
- **Detection is scan-based for now.** sqlite_master + naming
  convention; chef-side MCP registration tool is deferred (LEAVE
  V1's existing orphan tables can't have been registered anyway).
- **`Site Setting → Feature Tables` is the surface.** Same tab
  layout as the new Flow Groups tab.
- **FlowCode unique among "active" Admin_Flows rows.** "Active" =
  not Retired, not Archived; Retired flows can coexist with a new
  cook of the same code because their tables stay live.

---

## 1. Schema

### Flow gains 2 fields

```csharp
public DateTime? ArchivedAt { get; set; }
public string? ArchivedTableNamesJson { get; set; }  // JSON array of renamed tables
```

`ArchivedAt` is orthogonal to `State` and `DeletedAt`. Admin UI
shows a 🗄 archive chip alongside the state pill when set; archived
flows never appear in the bpm-ui launcher (independent filter on
the registry endpoint).

### `Admin_FeatureRegistration` (forward-compat; populated later)

```csharp
public class FeatureRegistration : ISoftDeletable
{
    public Guid Id { get; set; }
    public Guid? FlowId { get; set; }       // null for orphan registrations
    public string FlowCode { get; set; } = "";
    public int Version { get; set; }
    public string TableNamesJson { get; set; } = "[]";
    public DateTime RegisteredAt { get; set; }
    public DateTime? DeletedAt { get; set; }
}
```

POC stays scan-driven; the table exists so a future chef
`chef_register_feature_tables` MCP tool can write straight into it
without a migration shuffle.

### Migration

`20260529000000_AddFlowArchive.cs` — Flow columns +
Admin_FeatureRegistration table.

---

## 2. Detection — naming convention

`<CODE>_V<N>_*` where CODE = uppercase ASCII / digits, N = digits.
Excluded explicitly:

- `Admin_*` (admin's own schema)
- `__bpmEFMigrationsHistory`, `__AdminEFMigrationsHistory`,
  `sqlite_*` (EF + SQLite metadata)
- `*__arch_*` (already-archived tables — they show in a separate
  archived bucket)

For each `<CODE>_V<N>` group, lookup `Admin_Flows`:

- **Linked**: row exists, `ArchivedAt is null` → render with the
  flow detail + Archive action
- **Orphan tables**: no admin row → render with Archive action; admin
  can rename them anyway to free the namespace
- **Archived**: `ArchivedAt is not null` OR table names match the
  `__arch_<hash>` pattern → render in a separate "Archived"
  collapsible section with a Restore action (when the original
  namespace is still free) and a future Drop action (later PR)

### Dangling flow (separate category)

Linked Admin_Flow rows whose `<CODE>_V<N>` namespace has no chef
tables → labeled "Not yet cooked", not actionable here (chef cooks
later). Surface as informational.

---

## 3. API

| Verb | Path | Body | Returns | Auth |
|---|---|---|---|---|
| GET    | `/api/feature-tables` | — | classified list | user JWT |
| POST   | `/api/feature-tables/archive` | `{ flowCode, version, flowId? }` | renamed table list + updated Flow row | user JWT |
| POST   | `/api/feature-tables/restore` | `{ flowCode, version }` | restored table list | user JWT |

`archive` flow:

1. Find tables matching `<CODE>_V<N>_*` (excluding already
   archived) — these are the candidates.
2. Compute hash = first 8 hex of `FlowId` (or random when no
   admin row).
3. For each table, `ALTER TABLE name RENAME TO name__arch_<hash>`.
4. If `flowId` provided AND the row exists:
   - `Flow.ArchivedAt = utcnow`
   - `Flow.ArchivedTableNamesJson = JSON.encode(newNames)`
5. Single audit row `feature_archived` with the renamed list.

`restore` flow:

1. Find tables matching `<CODE>_V<N>_*__arch_<hash>` (a
   well-known hash recovered from the Flow row OR scanned).
2. For each, `ALTER TABLE name__arch_<hash> RENAME TO name` —
   BUT first check the original name isn't already taken by a
   newer cook. If taken, abort with a conflict.
3. Clear `Flow.ArchivedAt` / `ArchivedTableNamesJson`.
4. Audit row `feature_restored`.

### DTOs

```csharp
public record FeatureTableGroupDto(
    string FlowCode,
    int Version,
    string Status,           // 'Linked' | 'Orphan' | 'Archived' | 'Dangling'
    Guid? FlowId,
    string? FlowDisplayName,
    string? FlowState,
    DateTime? ArchivedAt,
    IReadOnlyList<string> TableNames,
    IReadOnlyList<string> ArchivedTableNames);
```

---

## 4. FlowCode uniqueness rule

`SubmitAsync` (Draft → Submitted) now refuses when another Admin_Flow
row with the same `FlowCode` exists AND is `state != Retired` AND
`ArchivedAt is null` AND `DeletedAt is null`. Error message points
to the conflicting row.

Retired co-exists because its chef tables stay live; new cook lands
on V+1 via clone-as-new-version (PR-R1).

---

## 5. Admin UI — Site Setting → Feature Tables

Mirrors the Flow Groups tab layout (added in PR-G2). Three sections:

### Linked / Orphan (collapsible, expanded by default)

Table:

| Status | Flow Code | Version | Admin Flow | Tables | Actions |
|---|---|---|---|---|---|
| 🔗 Linked | LEAVE | 1 | "請假" / Approved | 3 | Archive |
| ⚠ Orphan | LEAVE | 1 | — | 3 | Archive |

Click Archive → confirm modal asks user to type the flow code +
version (e.g. `LEAVE_V1`) to proceed.

### Archived (collapsible, collapsed by default)

Table:

| Flow Code | Version | Archived At | Tables | Actions |
|---|---|---|---|---|
| LEAVE | 1 | 2026-05-29 | 3 | Restore |

Restore button: enabled only if original namespace is free; otherwise
shows a tooltip explaining the conflict.

### Dangling (Admin_Flow without chef tables)

Informational chip row: "LEAVE v2 — Admin row exists but no chef
tables yet. Cook to materialize."

---

## 6. PR breakdown

| PR | Scope | Effort |
|---|---|---|
| **F0** | this design | done |
| **F1** | admin-svc: Flow.ArchivedAt / FeatureRegistration table / FeatureTablesService scan + archive/restore + FlowCode uniqueness check + audit + EF migration | 4-5h |
| **F2** | admin-ui: Site Setting Feature Tables tab + confirm modal | 2-3h |
| **F3** (deferred) | chef skill + MCP tool `chef_register_feature_tables` — writes to Admin_FeatureRegistration after cook completes | 0.5d |

Total F1+F2 ≈ 1 day.

---

## 7. Acceptance criteria

1. Run `dotnet ef database update` on a fresh DB → migration adds
   Flow.ArchivedAt + ArchivedTableNamesJson + Admin_FeatureRegistration.
2. Open Site Setting → Feature Tables → see LEAVE V1 listed under
   **Orphan tables** because it has chef tables (`LEAVE_V1_*`) but
   no Admin_Flows row.
3. Click Archive on the LEAVE V1 orphan row → type `LEAVE_V1` to
   confirm → tables rename to `LEAVE_V1_*__arch_<hash>` → page
   reloads → orphan row gone, new entry appears under
   **Archived** with a Restore button.
4. Click Restore → tables rename back → orphan row reappears under
   "Orphan tables".
5. Try to Submit a new flow with FlowCode=LEAVE while another
   active (non-Retired, non-Archived) LEAVE flow exists → 409
   Conflict with a message pointing to the conflicting row.
