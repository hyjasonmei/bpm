## Context

The 9-stepper produces a `DraftSpec` (typed in `bpm-admin-ui/src/lib/onboarding.ts`). Today that object's only export forms are:

1. JSON download (`exportSpec` in `Onboarding.tsx`) — a single file, no org context, no tests, no human-readable view
2. `POST /api/spec` (`StepGoLive` → `bpm-svc/src/Api/Program.cs:205`) — server saves to `incoming/` filesystem path, returns trackingId; intended as input to the (now-deferred) Claude Code pipeline

Neither is portable. The first is missing data; the second is local to one server. To make a flow design transferable between instances we need a self-contained artefact.

The reproducibility constraint is the hard one. Two ways to interpret "the same flow runs on a clean instance":

- **(a) Visual-only**: same forms render, same routing arrows show
- **(b) Executable**: an end-to-end test case fires and produces the same node trace + final state
- **(c) Both** ← chosen 2026-05-10

(c) means the bundle MUST carry sample org chart data and at least one test case; otherwise a clean instance has no users to resolve `ActorRef.expr "submitter.manager"` against.

## Goals / Non-Goals

**Goals**
- Bundle is self-contained: a clean `bpm-svc` + `bpm-admin-ui` pair with an empty database can install a bundle and run its test-cases successfully
- Bundle format is human-inspectable: each piece of state is a separate file, named for what it is, JSON or markdown — diffable in git
- The library is the home for designed flows: 9-stepper drafts can be promoted to library entries; library entries can be re-opened as drafts
- Reproducibility is the install gate: a bundle that fails its own test-cases on a clean instance MUST NOT be marked Installed

**Non-Goals**
- Signing / encryption (deferred per 2026-05-10 brainstorm)
- In-place edit of an Installed bundle's spec (always re-export with parent pointer)
- Cross-tenant org sharing (sample-org.json is intentionally a *demo* dataset, not the real customer org)
- Multi-flow bundles (one bundle = one flow; cross-flow composition is a separate problem)
- Any Claude Code pipeline integration (the existing `POST /api/spec` Phase B hook is removed, not migrated)

## Decisions

### Decision: Bundle is a zip, not a tar or directory

Zip because (1) browsers can build/read it client-side via `jszip` so View / Export do not need a backend round trip, (2) it is a single file the user can email / drop into Slack / commit to git, (3) `System.IO.Compression.ZipArchive` is in the BCL so the backend has no extra dep.

Trade-off: zip ordering is non-deterministic, so the manifest carries a `files[].sha256` index that becomes the canonical "bundle identity" rather than the zip's own checksum. Re-zipping the same logical content produces the same manifest checksum even if file ordering shifts.

### Decision: `manifest.json` is the entry point + version pointer

Every other file is reachable via `manifest.files[]`. Loaders MUST read manifest first, refuse unknown `bundleSchemaVersion`, and ignore unlisted files (so a future version adding `audit-policy.json` won't crash old loaders that don't know it — they just won't honor it).

Lineage: `manifest.parent` is the SHA-256 of the parent bundle's manifest (or null for a new flow). This lets the library show a tree without storing diffs explicitly. Re-export from a draft hydrated from bundle X automatically sets `parent = sha256(X.manifest)`.

### Decision: `sample-org.json` is mandatory

Without it, a clean instance cannot resolve any `ActorRef`, so `(c)`-level reproducibility is impossible. The 9-stepper SHALL refuse to build a bundle that has no `sample-org.json`. Concretely, when entering Go Live we synthesize a default sample-org from the actor references the spec uses (e.g., if spec references `submitter.manager`, sample-org has at least one user with a manager edge).

The author can edit / extend sample-org during Step Test (today the step is mostly a stub — this proposal makes it functional).

### Decision: At least one test-case is mandatory

Same reason: without a test-case, the reproducibility runner has nothing to assert. `BundleBuilder` SHALL refuse to emit a bundle without at least one entry in `test-cases/`. Step Test is responsible for capturing them.

### Decision: Test-case format records *expected node trace*, not just final status

Just-final-status would let two divergent flows look "reproducible" if they happen to end the same way. Recording the trace `[start → task_apply → approval_manager → end]` catches routing regressions early. Trace equality is order-sensitive but ignores timestamps and assignee user ids (we compare role/path resolution, not specific guids — sample-org users are placeholders).

### Decision: Imported bundle is loaded into an *isolated tenant*, not the active one

A library entry is `Installed` into a freshly-created scratch tenant scoped to the bundle. This isolates sample-org users from the real customer org, prevents accidental cross-flow conflicts, and lets the reproducibility runner truncate-and-reseed without touching real data. The scratch tenant is named `repro-{flowCode}-{bundleChecksumShort}`.

If the customer wants to actually *deploy* the flow, that is a separate "promote bundle to live tenant" action — out of scope for this change.

### Decision: Round-trip via "Open as Draft", not in-place edit

Importing a bundle in `Open in 9-stepper as draft` mode hydrates `DraftSpec` from `spec.json`, hydrates Step Test's sample-org and test-cases, and lands the user on the first step whose validator currently fails (or Go Live if all pass). Re-export sets `manifest.parent` to the source bundle's checksum.

This means library entries are immutable once Installed. To change a flow you re-design and re-install. Migration of in-flight `ProcessInstance`s onto a new bundle version is **not** in this change's scope; `add-process-runtime` already snapshots spec at instance start so existing instances are unaffected.

### Decision: Acceptance gate is automatic, not manual

When a bundle is imported via `Install for runtime`, `BundleReproducibilityRunner` runs synchronously (or async with progress polling for big test sets) and only on green does the library mark it `Installed`. Yellow / red leaves it `Failed` with the test report attached. No manual override — the whole point of the change is that "the bundle reproduces" is a checkable property.

### Decision: `StepGoLive` UX collapses

Today Go Live shows validator overview + spec preview + Submit button → POST to /api/spec → Tracking ID + Reveal-in-Finder. New Go Live shows: validator overview + bundle file list (with sizes) + "Save to Library" + "Download .zip". No backend writeable-filesystem path. No tracking ID. The "1-2 工作天部署" promise becomes "saved as v{n}, ready to install".

## Risks / Trade-offs

**Risk**: Reproducibility runner is slow for big test suites, blocking import.
*Mitigation*: Run async; library shows Pending state with a spinner, transitions to Installed/Failed when done. Cap per-import test-case count at 50 (validator gate at bundle build time).

**Risk**: `add-process-runtime` is not yet implemented (0/71 tasks); reproducibility runner has nothing to call.
*Mitigation*: Reproducibility capability is conceptually behind `add-process-runtime`. Tasks here are written assuming the runtime API exists; if shipping order forces this change first, BundleReproducibilityRunner ships as a stub returning "Pending — runtime not available" and the library marks bundles `InstalledUnverified`. The format / library / round-trip work is independent and ships either order.

**Risk**: Sample-org synthesis from `actorRef` references produces unrealistic data ("Manager #1", "Manager's Manager #1") that customers laugh at on demo.
*Mitigation*: Provide a curated default sample-org template (Wilson You, Mary Chen, Tony Wang, etc. matching the existing demo personas in `bpm-ui` and `OrgFixture.cs`); synthesis only fills gaps. Bundle author can edit names in Step Test.

**Risk**: Bundle size grows large if customer attaches multiple source diagrams under `assets/`.
*Mitigation*: Cap individual asset at 5MB, total bundle at 25MB. Builder rejects above limit.

**Risk**: Schema evolution will be painful — once bundles are emitted into the wild, changing manifest format breaks them.
*Mitigation*: `bundleSchemaVersion` in manifest from day 1. Loaders refuse versions they don't know. We don't ship a migrator until we actually need to bump the format the first time (YAGNI; learn from real friction).

**Trade-off**: The Flow Library duplicates concerns that may eventually live in `add-process-admin-ui` (which is also 0/42 tasks, unbuilt). For now Flow Library is admin-portal-side and aimed at the *spec author* (you / the partner / the consultant); `process-admin-ui` will be customer-side and aimed at the *operator* of running flows. Boundaries can be merged later if it turns out they're the same audience — but that's a refactor, not a blocker.

## Migration Plan

1. New code lives alongside existing — `BundleBuilder` ships before `StepGoLive` is rewired
2. `StepGoLive` rewire is a single PR: replace `submitSpec` body with `buildBundle → saveToLibrary`
3. Existing `/api/spec` and `/api/spec/reveal` endpoints stay alive for one release with deprecation log entries, then deleted in a follow-up cleanup PR
4. No data migration: there are no installed instances of the old `POST /api/spec` flow (the `incoming/` directory just accumulated tracking-id files; they can be deleted manually)

## Open Questions

- Does the reproducibility runner need to handle bundles whose `spec.json` references capabilities the target instance has not yet shipped (e.g., bundle uses CEL but target has no `add-cel-expressions`)? Probable answer: the loader detects feature flags from spec.json and refuses with a "missing capability" error. Defer the precise design until `add-cel-expressions` lands.
- How does the library handle multiple bundles for the same `flowCode`? Probable answer: latest wins for runtime install (only one Installed at a time), but all versions retained in the library for inspection / re-import. Confirm with Jason before final implementation.
- Should `chat-snapshots/*.md` (turning points from the AI conversation during 9-stepper) be in the bundle? Useful for auditing how the flow was designed; a small risk of leaking internal AI prompts. Default OFF, opt-in flag in Go Live.
