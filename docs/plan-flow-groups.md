# Plan — Admin-managed flow groups for the bpm launcher

Lets the admin user organise cooked flows into groups (e.g. 人事 /
採購 / IT) so the bpm employee Home page surfaces a section-per-group
launcher instead of a flat alphabetical list.

Decisions locked 2026-05-29 via TG:

- **Group management lives in admin Site Setting** (CRUD page).
- **Assignment happens on the AI Kitchen list row** — not in the
  wizard. chef + spec are untouched; groups are a pure admin UI
  concern.
- **End-user launcher renders sections** (not tabs), ordered by
  group `SortOrder`. Unassigned flows fall under "其他".
- **5 fields per group**: id / code / displayName (bilingual) /
  sortOrder / icon (lucide name). No colour token in v1.

---

## 1. Data model

### Admin side — new `Admin_FlowGroup` table

```csharp
public class FlowGroup : ISoftDeletable
{
    public Guid Id { get; set; }
    /// <summary>Stable slug — used in URLs + as the API key bpm-ui
    /// renders against. Lowercase ASCII, unique.</summary>
    public string Code { get; set; } = "";
    /// <summary>Bilingual label JSON: { "zh-TW": "人事", "en": "HR" }.
    /// At least zh-TW required; en optional.</summary>
    public string DisplayNameJson { get; set; } = "{}";
    public int SortOrder { get; set; }
    /// <summary>Lucide-react icon name, e.g. "Users", "ShoppingCart".
    /// bpm-ui falls back to a generic "Folder" when null.</summary>
    public string? Icon { get; set; }
    public DateTime CreatedAt { get; set; }
    public DateTime UpdatedAt { get; set; }
    public DateTime? DeletedAt { get; set; }
}
```

### Flow gains `GroupId` nullable foreign key

```csharp
public class Flow : ISoftDeletable
{
    // existing fields…
    public Guid? GroupId { get; set; }
}
```

Soft-deleting a group leaves dangling `GroupId` on existing flows —
the bpm-ui renders them under "其他" until admin reassigns. No
cascade required for POC.

---

## 2. Admin-svc API

### Group CRUD

| Verb | Path | Body | Returns |
|---|---|---|---|
| GET    | `/api/flow-groups` | — | `FlowGroupDto[]` (ordered by SortOrder) |
| POST   | `/api/flow-groups` | `{ code, displayName, sortOrder, icon? }` | created `FlowGroupDto` |
| PUT    | `/api/flow-groups/{id}` | partial update | updated `FlowGroupDto` |
| DELETE | `/api/flow-groups/{id}` | — | 204 (soft-delete; dangling `GroupId` becomes null at read time) |

### Flow assignment

| Verb | Path | Body | Returns |
|---|---|---|---|
| POST | `/api/flows/{id}/group` | `{ groupId: Guid? }` | updated `FlowDetailDto` |

Setting `groupId: null` clears the assignment (back to "其他").

### DTOs

```csharp
public record FlowGroupDto(
    Guid Id,
    string Code,
    Dictionary<string, string> DisplayName,
    int SortOrder,
    string? Icon,
    int FlowCount);                   // server-side LEFT JOIN count

public record AssignFlowGroupRequest(Guid? GroupId);
```

`FlowDetailDto` + `FlowSummaryDto` gain `groupId: Guid?` and
`groupCode: string?` (denormalised) so the list view doesn't need a
second round-trip per row.

---

## 3. Admin-ui (PR-G2)

### Site Setting → Flow Groups page

- Lives at `/site-setting/flow-groups` (left nav already has Site
  Setting).
- Table: code / zh-TW name / en name / icon / sortOrder / flow count
- Inline edit; drag handle on the leftmost column rewrites SortOrder
- "+ New group" opens a small modal (code + names + icon picker)
- Soft-delete via row 3-dot menu, with confirm

Icon picker = a curated dropdown of ~12 lucide names that read well
at 16-20px: `Users`, `ShoppingCart`, `Wrench`, `Plane`, `FileText`,
`Briefcase`, `HeartPulse`, `Coffee`, `Wallet`, `Settings`, `Folder`,
`Sparkles`. Storing the string name keeps the schema flat; bpm-ui
maps name → component on render.

### AI Kitchen list row

- Add a `Group` column between `State` and `Updated`. Renders an
  icon + zh-TW name chip if assigned; otherwise a muted "未分組"
  pill.
- 3-dot menu gains `Assign group →` which expands to a sub-list of
  groups + "Unassign".
- Assignment is immediate (`POST /api/flows/{id}/group`), no
  separate confirm — easy to undo by reassigning.

---

## 4. bpm-svc registry enrich (PR-G3)

`SharedFlowGroup` mirrors `Admin_FlowGroup` (same SharedX pattern as
`SharedFlow`).

`FlowRegistryEntry` adds:

```csharp
public sealed record FlowRegistryEntry(
    string FlowCode,
    int Version,
    string State,
    string DisplayName,
    DateTime UpdatedAt,
    string? GroupCode,                              // new
    Dictionary<string, string>? GroupDisplayName,   // new
    string? GroupIcon,                              // new
    int? GroupSortOrder);                           // new
```

Server-side LEFT JOIN: rows without a group come back with `null`
group fields. End-user JOIN happens against `SharedFlowGroup`.

---

## 5. bpm-ui launcher (PR-G3)

`QuickActionsPanel` in `screens/Home.tsx`:

- Existing `useFlowRegistry` already returns entries; consumers now
  also read the group fields.
- Group by `groupCode || '__other__'`; order groups by `groupSortOrder`
  ascending (nulls/"__other__" last).
- Each section renders a small icon (resolved from lucide via
  `groupIcon`) + zh-TW display name + count badge.
- Flows inside a section keep their existing chip styling.
- Empty groups (no flows match) hide entirely.
- Loading state: existing "showing everything while registry loads"
  fallback degrades to flat list; group sections appear once data
  lands.

### Icon resolver

```tsx
import * as Icons from 'lucide-react'
type LucideIconName = keyof typeof Icons
function ResolveIcon({ name, ...rest }: { name?: string | null } & SvgAttributes<SVGElement>) {
  const I = name && (name in Icons) ? Icons[name as LucideIconName] : Icons.Folder
  return <I {...rest} />
}
```

A simple defensive lookup; if admin sends an icon string that doesn't
exist in lucide, falls back to `Folder` so the launcher never breaks.

---

## 6. Migration / seed

EF migration adds:

- `Admin_FlowGroups` (full table)
- `Admin_Flows.GroupId` (nullable Guid, indexed)

Optional seed at admin-svc startup when the table is empty (mirrors
the existing org seed): pre-populate four groups so a freshly seeded
demo system already renders sections:

| code | zh-TW | en | sortOrder | icon |
|---|---|---|---|---|
| hr | 人事 | HR | 10 | Users |
| purchase | 採購 | Purchase | 20 | ShoppingCart |
| it | IT | IT | 30 | Wrench |
| office | 行政 | Admin | 40 | FileText |

Wrapped in the same `FLOWCOOK_ADMIN_SEED_ON_STARTUP` gate the org
seed already uses.

---

## 7. PR breakdown

| PR | Scope | Effort |
|---|---|---|
| **G0** | this design | done |
| **G1** | admin-svc: FlowGroup entity + EF migration + CRUD + assignment + Flow.GroupId + seed | 2-3h |
| **G2** | admin-ui: Site Setting Groups page + AI Kitchen list group column + assign menu | 2-3h |
| **G3** | bpm-svc SharedFlowGroup + registry enrich; bpm-ui Home QuickActions grouped | 1-2h |

Total ~1 day.

---

## 8. Acceptance criteria

1. Fresh seed: 4 default groups appear in admin Site Setting + bpm
   Home shows empty group sections (or hides them if zero flows).
2. Admin creates a 5th group "差旅 (Travel)" with icon `Plane`;
   refresh AI Kitchen list → group dropdown lists it.
3. Admin opens the AI Kitchen list, picks "Assign group → 人事" on
   the LEAVE flow row → bpm-ui Home (after next 30s poll or
   manual refresh) shows LEAVE under the 人事 section with the
   Users icon.
4. Admin drags 人事 group below 採購 in Site Setting → bpm-ui
   updates the section order on next load.
5. Admin soft-deletes the 採購 group → flows previously in 採購
   fall back to "其他" on bpm Home; assigning them to another group
   moves them.
