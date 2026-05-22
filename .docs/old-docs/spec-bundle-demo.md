# Spec Bundle Reproducibility Demo

A 5-minute live demo proving the "design once, run anywhere" promise of
Spec Bundles. Two completely independent BPM sites, one zip file, identical
process behaviour.

## Setup (one-time)

Two terminal panes, two browser tabs:

```bash
# Pane 1 — Instance A (the customer who designs the flow)
cd bpm-svc/src/Api
ConnectionStrings__Default="Data Source=bpm-A.db" dotnet run --urls http://localhost:5101

# Pane 2 — Instance B (the second site, clean DB)
cd bpm-svc/src/Api
ConnectionStrings__Default="Data Source=bpm-B.db" dotnet run --urls http://localhost:5102
```

Two `bpm-admin-ui` tabs (set `VITE_API_BASE` per tab via two `.env.local`
copies, or run two `npm run dev` instances on `--port 5180` and `--port 5181`):

- Tab A → `http://localhost:5180` → talks to instance A (`:5101`)
- Tab B → `http://localhost:5181` → talks to instance B (`:5102`)

Both DBs auto-seed the org fixture on first boot (`BPM_SEED_ON_STARTUP=true`
default in dev), so each site has its own users, departments, and roles.

## Demo script

1. **Tab A — design the LEAVE flow.** Open `Onboarding`. Walk through the
   9 steps (or import `sample_specs/leave_v1.json` via Source → "Use a
   sample"). Stop at the GO LIVE step.
2. **Tab A — save to Flow Library.** Click `Save to Flow Library`. The
   wizard POSTs to `/api/admin/flow-library/build`, the backend assembles
   the zip, and you land on a "Bundle saved as v1" confirmation.
3. **Tab A — export the zip.** Open Flow Library, find the new LEAVE row,
   click `Export`. Browser downloads `LEAVE_v1.zip` (manifest + spec.json
   + bpmn.xml + forms + sample-org + test-cases).
4. **Tab B — import the zip into a clean site.** Open Flow Library on
   instance B (initially empty). Drag the `LEAVE_v1.zip` into the page.
   The import modal previews the manifest. Click `Install for runtime`.
5. **Tab B — see the repro report.** Backend parses, validates, loads under
   a scratch tenant, runs each bundled test-case via the live runtime, and
   shows `PASS`. The bundled "5 day vacation" case completes node-for-node
   identical to the design intent — without anyone touching code on B.
6. **(Optional) Tab B — start a real instance.** Hit `POST /api/processes`
   with `{ "specCode": "LEAVE", ...sample form... }` (or use the runtime
   admin UI when PR-K lands). The instance runs end-to-end on B's runtime
   with B's users / managers / HR — bundle is the spec, B is the executor.

## Talking points

- **Same form rendering.** B never saw the wizard; the bundle's
  `forms/{userTaskId}.json` carries the field definitions verbatim.
- **Same routing.** The bundle's `spec.json` includes every gateway
  condition + actor expression. B's CelNet evaluator runs them against
  B's org chart and reaches the same branches.
- **Same final state.** `ProcessInstance.SpecSnapshotJson` on B is byte-
  identical to the bundle's `spec.json` — the runtime stores it inline at
  start, so even if B later edits the spec file, this instance keeps the
  original. (Provable: see `BundleE2ETests.Bundle_round_trips_between_two_instances`.)
- **No code shipped to B.** The runtime binary is the same one A is
  running. The bundle is pure data. That's the entire migration story.

## Troubleshooting

- Repro `FAIL` with a trace diff: usually a sample-org mismatch (bundle
  references a role/dept the validator missed). Read the diff for the
  diverging node id.
- Import returns 409: same manifest checksum already installed on B.
  Delete the old row or rebuild with a bumped `flowVersion`.

## Acceptance test

`bpm-svc/tests/Bpm.Tests/Integration/BundleE2ETests.cs` automates the
above against two in-memory SQLite DBs in a single test process — boot two
instances, build on A, import + run on B, assert byte-equal SpecSnapshot
+ identical node trace. Run with:

```bash
dotnet test bpm-svc/bpm-svc.slnx --filter "category=integration"
```
