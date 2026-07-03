# Custom Datasets — Customer-Maintainable Reference Data & Cascading Dropdowns

**Status:** Draft for review (Jason, Telegram 2026-06-27). Phase-1 scope below.
**Owner layer:** lead (shared platform — admin-svc config + bpm-svc read + bpm-ui primitive). Not chef.

## Problem

Flow form dropdowns today are hardcoded in each chef-cooked React form (e.g. TEO
category, leave type, currency). Changing an option list — add a city, rename a
cost center, fix a typo — means a chef re-cook + redeploy of that flow. Customers
can't self-serve.

We want **customer admins to edit option *values* without touching the flow**, and
support **cascading (dependent) dropdowns** — pick 台北市 and the 行政區 dropdown
narrows to that city's districts.

## Goals (phase 1)

1. A customer admin maintains named **datasets** (reference tables) in admin-ui; edits
   take effect on the next form render — no flow re-cook, no redeploy.
2. A flow form field can **bind** to a dataset for its options (design-time binding,
   editable content — option A below).
3. **Cascading**: a child field filters its options by a parent field's selected value.
4. Support data kept as a **denormalized wide table** (one sheet, repeated parent
   values) via **distinct** and **group**, because that's the shape customers actually
   have (an Excel sheet).
5. Submitted cases stay readable forever even if the option list later changes.

## Non-goals (explicitly out of phase 1 — YAGNI)

- **Runtime re-binding** (option B): admin re-pointing a field to a different dataset,
  or converting a hardcoded select to a dataset binding, without touching flow design.
  Phase 1 binding is fixed at design time; only *content* is editable. Architecture
  leaves room to grow here (the binding is declarative, see below).
- **Multi-parent / cross-field cascading** (depth B): an option list filtered by two+
  parent fields at once. Phase 1 is single-parent chains (depth A): 縣市 → 行政區 (→ 里).
- **AI Kitchen auto-generating datasets/bindings.** Phase 1 bindings are authored by
  chef/lead in form code. AI Kitchen integration is future work.
- **Bulk Excel/CSV import** of dataset rows. Nice-to-have; track as phase 1.5.
- Retrofitting all 11 existing flows. Phase 1 ships the capability + converts a
  reference example; existing flows convert opt-in later.

## How industry does it (validation — informs the decisions below)

Research across Salesforce, Power Apps/Dataverse, ServiceNow, Appian, Pega,
Camunda/Flowable, Airtable, Retool, and the form builders found:

- **Two cascading paradigms.** Field-attached enums use an explicit *value-pair matrix*
  (Salesforce Dependent Picklists; ServiceNow choice `dependent=`). Table/relational
  reference data uses a *runtime filter/query keyed on the parent value* (Power Apps
  `Filter()`, ServiceNow reference qualifiers, Appian `queryFilter`, Pega parameterized
  data pages, Retool `WHERE x = {{parent}}`, Airtable Dynamic Filtering). **Every
  serious data/BPM tool picked the runtime-filter model.** Our `filterBy column = parent
  value` is that model — validated.
- **"Options as rows in a maintainable table" is the dividing line for edit-without-
  redeploy.** Every tool that lets admins change values live treats reference data as
  *data*, not schema/code. Our "datasets are data" premise is the consensus.
- **distinct-from-a-wide-table and optgroup grouping are rare/absent natively** — most
  tools push DISTINCT to the data layer (SELECT DISTINCT) or make you normalize first
  (Airtable). Building these in is a small differentiator and matches the Excel-sheet
  reality.
- **Snapshot = store the value, not a live pointer.** Universal default. Renames re-
  resolve labels via value/label separation; **deletion is the hazard** — deactivate,
  don't delete, and snapshot the label-as-entered for history.

## Core data model

A **dataset is a columnar table**: named columns + rows. A field binding is a tiny
declarative query over that table.

### Dataset (admin-owned config)

- `Dataset`: `Id`, `Key` (stable slug, e.g. `tw-regions`), `Name`, `Description`,
  `Columns` (ordered list of `{ key, label, type }`, stored as an owned/JSON collection —
  small config), `IsActive`, audit stamps.
- `DatasetRow`: `Id`, `DatasetId`, `Cells` (map of `columnKey → string value`, stored as
  owned/JSON — see DB portability), `IsActive`, `SortOrder`.

One `tw-regions` dataset with columns `縣市 | 行政區 | 里` and one row per 里 *is* the
denormalized wide table. A normalized "cities + districts" pair is just two datasets
where the district rows carry a `cityKey` cell — same engine, different authoring style.

### Field binding (declarative — authored in the form)

A binding is a small object the form field declares:

```ts
interface DatasetBinding {
  datasetKey: string          // 'tw-regions'
  valueColumn: string         // '行政區'  — what gets stored
  labelColumn?: string        // defaults to valueColumn — what's shown
  filterByColumn?: string     // '縣市'    — cascading: match this column…
  // …against the parent field's selected value (passed at runtime)
  distinct?: boolean          // dedupe (wide table → unique options)
  groupByColumn?: string      // optgroup headings
  sortByColumn?: string       // option order (defaults to row SortOrder)
}
```

This is "SELECT [labelColumn], [valueColumn] FROM dataset WHERE [filterByColumn] =
:parentValue [DISTINCT] [GROUP BY groupByColumn] [ORDER BY sortByColumn]" expressed as
config. Binding is fixed at design time (option A); only dataset content changes freely.

## Architecture & ownership

Mirrors the existing **SharedIdentity** pattern: admin-svc is canonical, bpm-svc reads.

```
bpm-admin-svc  ── owns Dataset/DatasetRow tables + EF migrations (new "Datasets" domain,
   (canonical)     Clean-Arch: Domain entity / Application service+DTOs / Persistence
                   config / Api controller — mirrors the Flows domain)
        │
        │ same DB file (per-customer, no multi-tenant)
        ▼
bpm-svc        ── reads via Shared DbSets (SharedDataset, SharedDatasetRow), marked
   (runtime)      ExcludeFromMigrations — exactly like SharedFlow / SharedPrincipal.
                  Exposes an option-resolution read endpoint.
        │
        ▼
bpm-admin-ui   ── new "資料集 / Datasets" page (sibling of User & Role): list datasets,
                  edit columns, edit/add/deactivate rows (Excel-like grid).
bpm-ui         ── shared <DatasetSelect> primitive in components/ui/. Chef-cooked forms
                  use it instead of a hardcoded <select>. No manifest change needed —
                  the binding lives in the form's JSX.
```

### Why option resolution is server-side + in-memory

bpm-svc exposes `POST /api/datasets/resolve` taking a `DatasetBinding` + optional
`parentValue`, and returns `[{ value, label, group? }]`:

1. Load the dataset's active rows (cached per dataset key; reference data is read-heavy,
   write-rare — invalidate cache on admin edit).
2. Apply `filterByColumn == parentValue`, then `distinct`, `groupBy`, `sortBy` **in C#
   (Application layer)**, not in SQL.

Doing filter/distinct/group **in memory** keeps us within the DB portability rules (no
`json_extract`/JSON-path SQL, rule 2/6) and is fine for reference-data sizes (hundreds–
low thousands of rows; even all TW 里 ≈ 7.7k loads + caches trivially). If a dataset ever
gets huge, we revisit with a normalized cell table + SQL — not now (YAGNI).

Resolving server-side (not shipping the whole wide table to the browser) keeps the client
light and computes distinct/group once.

### DatasetSelect (bpm-ui primitive)

```tsx
<DatasetSelect
  binding={{ datasetKey:'tw-regions', valueColumn:'行政區', filterByColumn:'縣市', distinct:true }}
  parentValue={form.city}        // cascading: re-resolves when city changes
  value={form.district}
  onChange={(v, label) => setForm({ ...form, district: v, districtLabel: label })}
/>
```

- Calls `/api/datasets/resolve` on mount and whenever `parentValue` changes; child is
  disabled/cleared until the parent is chosen (the standard cascading UX).
- `onChange` returns **both value and label** so the form can persist the label snapshot.
- Renders `groupByColumn` as `<optgroup>` headings when present.

Chef's job shrinks to: declare the binding in the form. lead owns the primitive +
backend. (Future: a manifest-level binding map could let an admin re-point fields — that's
option B, deliberately not built yet, but the declarative binding makes it reachable.)

## Snapshot & lifecycle

- **Submit captures value + label.** The form persists both the stored `value` and the
  `label` shown at submit time (`district` + `districtLabel`). Case detail renders the
  snapshot label, so renaming/removing an option never corrupts a historical case. This
  is the universal industry-safe pattern and costs one extra column per bound field.
- **Deactivate, don't delete.** Rows (and datasets) have `IsActive`. Resolution excludes
  inactive rows from *new* dropdowns; existing stored value+label are untouched. The
  admin grid offers deactivate prominently; hard delete is guarded (and irrelevant to
  history because we snapshot the label).
- **value/label separation** is what makes both cascading and snapshot work: store the
  stable value, resolve the display label live for the editor, snapshot it on submit.

## DB portability (per root CLAUDE.md 7 rules)

- EF Core only; `Cells`/`Columns` stored as EF **owned types or plain TEXT** (rule 6) —
  no in-DB JSON-path queries (rule 2).
- All filter/distinct/group is **in-memory LINQ** in the Application layer, so SQLite and
  Postgres behave identically (rules 1, 2, 5).
- admin-svc owns the EF migrations for the Dataset tables; bpm-svc Shared DbSets are
  `ExcludeFromMigrations` (same as SharedFlow).

## Phase-1 deliverables

1. **admin-svc**: `Datasets` domain — `Dataset`/`DatasetRow` entities, EF config +
   migration, `IDatasetService` (CRUD + activate/deactivate), `DatasetsController`.
2. **bpm-admin-ui**: "資料集 / Datasets" page — dataset list, column editor, editable
   row grid (add / edit / deactivate), built on **TanStack Table (headless) + shadcn**
   styling (the shadcn Data Table recipe) so it matches the existing admin visual system.
   Editable cells = inputs rendered in TanStack cells. (Upgrade path: Glide Data Grid if
   customers later need true spreadsheet-grade editing — not phase 1.)
3. **bpm-svc**: `SharedDataset`/`SharedDatasetRow` DbSets (ExcludeFromMigrations) +
   `POST /api/datasets/resolve` (filter/distinct/group/sort in-memory, cached).
4. **bpm-ui**: `<DatasetSelect>` primitive in `components/ui/` (cascading + optgroup +
   value/label onChange).
5. **Reference conversion (demo-only in phase 1)**: a seeded demo dataset incl. a
   cascading 縣市→行政區 example, and a low-risk demo field wired to `<DatasetSelect>` to
   prove the loop end-to-end. The **real production field(s) to convert are deferred to
   post-demo customer feedback** — we ship the capability, then convert what customers
   actually ask for. Do NOT mass-convert existing flows in phase 1.
6. **Tests**: resolution-service unit tests (filter/distinct/group/inactive), bind-and-
   render integration, cascading re-resolve, snapshot-on-submit.

## Resolved decisions (review 2026-06-27, Jason via Telegram)

1. **Reference conversion target deferred.** Phase 1 ships a seeded demo dataset +
   demo-field conversion only; the real production field(s) are chosen after the demo
   based on what customers ask for.
2. **Admin grid = TanStack Table (headless) + shadcn** (not hand-rolled, not AG Grid).
   Glide Data Grid is the future upgrade path only if spreadsheet-grade editing is needed.
3. **`/api/datasets/resolve` lives on bpm-svc** (confirmed) — bpm-ui talks to one backend;
   bpm-svc reads admin-owned datasets via Shared DbSets.
4. **Phase-1.5 backlog accepted** (order: Excel/CSV import → AI-Kitchen binding →
   option-B runtime re-binding). Revisit ordering when phase 1 lands.
