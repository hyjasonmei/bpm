## Why

The 9-step onboarding stepper (`bpm-admin-ui`) currently produces a single `spec.json` blob — either downloaded via `Export Draft Spec` or POSTed to `/api/spec` for the (now-deferred) Claude Code pipeline. That artefact is **not portable**: a colleague handed the JSON cannot reproduce the same flow on a clean instance because the spec references actors / orgs / sample cases that only exist in the originator's database.

The product moves forward only if the 9-stepper output is a **self-contained, portable design package** — what we call a **spec bundle**. The acceptance criteria, agreed during 2026-05-10 product brainstorm:

> Hand the bundle to a clean, empty `bpm-admin-ui` + `bpm-svc` instance → import it → the same form renders, the same routing fires, and an end-to-end test case completes with the same final state.

This change does three things together because they are coupled by the bundle format:

1. **Define the bundle**: a zip with ≥3 well-known files (spec.json / bpmn.xml / spec.md) plus everything needed to reproduce — sample-org, test-cases, notification templates, manifest. The bundle is the *single artefact* the 9-stepper emits going forward.
2. **Add a Flow Library** to `bpm-admin-ui`: list saved flows, view bundle contents, import (as runtime install OR as 9-stepper draft), export. Today the stepper has no concept of "the flow I designed last week" — it's a single in-progress draft in localStorage.
3. **Teach `bpm-svc` to ingest a bundle**: parse, validate, seed the sample-org, register the spec for runtime execution, and run the bundled test-cases against `add-process-runtime` to *prove* the bundle is reproducible (not just well-formed).

The Claude Code dev-pipeline previously implied by `StepGoLive`'s `POST /api/spec` is **explicitly deferred**. The 9-stepper's output is now the bundle, full stop. Pipeline work becomes a separate later proposal once we have the bundle as stable input.

## What Changes

### `StepGoLive` no longer ships to a pipeline

Replace the existing `POST /api/spec` flow with: **build bundle → save to Flow Library → optionally download zip**. The "Submit Spec → 1-2 工作天部署" copy comes out; the Go Live screen becomes "preview the bundle, save to library, optionally download."

### Bundle file layout (new capability `bpm-spec-bundle`)

A bundle is a `.zip` with this canonical layout:

```
{flowCode}_v{version}.zip
├── manifest.json              ← bundle schema version, source instance, parent bundle pointer, checksum index, exported_at
├── spec.json                  ← canonical machine-readable spec (= today's DraftSpec output)
├── bpmn.xml                   ← bpmn-js-renderable visualization
├── spec.md                    ← human-readable rendering of spec.json
├── README.md                  ← "what this flow does" — for the customer
├── walkthrough.md             ← step-by-step happy-path walk
├── CHANGELOG.md               ← if this bundle has a parent, the diff summary
├── forms/
│   ├── {userTaskId}.json      ← per-userTask form schema (extracted from spec.json for diff-friendliness)
│   └── ...
├── notifications/
│   ├── {notificationId}.json  ← per-notification template (subject / body / channel / recipients)
│   └── ...
├── sla.json                   ← SLA + escalation config
├── actors.json                ← ActorRef definitions used in this flow
├── sample-org.json            ← Org chart needed to resolve every ActorRef in the flow (Wilson/Mary/Tony… + their dept/manager edges)
├── test-cases/
│   ├── {caseId}.json          ← input form data + expected node trace + expected final status
│   └── ...
└── assets/                    ← optional: original source diagram(s) the customer uploaded
    └── source-diagram.png
```

**Minimum required**: `manifest.json`, `spec.json`, `bpmn.xml`, `spec.md`, `sample-org.json`, at least one entry under `test-cases/`. Everything else is optional but the exporter generates them when the source data exists.

`manifest.json` carries the `parentBundleChecksum` pointer so a bundle imported into the 9-stepper as draft can later re-export with `parent` set, giving us version lineage without an in-place edit model.

### Backend: bundle export + import + runtime loader (new capabilities `bpm-spec-bundle`, `bpm-spec-reproducibility`)

- `BundleBuilder` — builds the zip from a `DraftSpec` + tenant org snapshot
- `BundleParser` — opens a zip, validates manifest, reads each known file, returns a strongly-typed `Bundle` record
- `BundleValidator` — schema validation + cross-file consistency (every `actorRef` in spec.json resolves under sample-org.json; every userTask in spec.json has an entry under `forms/`; every test-case references nodes that exist)
- `BundleRuntimeLoader` — given a parsed bundle: seed the sample-org into a new isolated tenant, register the spec, return a `LoadedBundleHandle` usable by reproducibility tests and runtime APIs
- `BundleReproducibilityRunner` — execute every `test-cases/*.json` against the loaded bundle using `add-process-runtime` and assert the produced node trace matches the bundle's expected trace

### Frontend: Flow Library screen (new capability `bpm-flow-library`)

New `bpm-admin-ui` screen `Flow Library` (added to `AdminLayout` nav alongside Onboarding / Site Settings / Users & Roles / Impersonation / Audit Logs):

- **List** — saved flows with their latest bundle version, exported_at, last reproducibility-check result
- **View** — open a bundle: tabs for spec.json / bpmn.xml render / spec.md preview / forms / notifications / sla / actors / sample-org / test-cases / assets / manifest
- **Export** — download the bundle as zip
- **Import** — drop a `.zip` → choose `Install for runtime` (parse + validate + load via reproducibility runner; only on green) OR `Open in 9-stepper as draft` (parses bundle, hydrates DraftSpec, jumps into stepper at first failed validator or GO LIVE)
- **Delete** — soft-delete a flow from the library

The existing `StepGoLive` is rewired to call `BundleBuilder` → save into Flow Library → offer zip download.

### Reproducibility as the acceptance gate (new capability `bpm-spec-reproducibility`)

- An imported bundle is **not** marked `Installed` in the Flow Library until `BundleReproducibilityRunner` runs **all** test-cases and they pass
- A bundle exported from instance A and imported into instance B (clean) MUST reproduce: same form rendering, same routing decisions, same final ProcessInstance status. This is the change's product-level acceptance criterion (option `c` from the brainstorm).

## Capabilities

### New Capabilities
- `bpm-spec-bundle`: bundle file format, manifest schema, exporter, parser, cross-file validator
- `bpm-flow-library`: Admin UI screen + REST endpoints to list / view / import / export / delete bundles
- `bpm-spec-reproducibility`: runtime-loader that mounts a bundle into an isolated tenant + test-case runner that asserts node-trace equality

### Modified Capabilities
- `bpm-onboarding-stepper` (existing in `bpm-admin-ui`): `StepGoLive` switches from "POST spec to /api/spec" to "build bundle → save to library → download zip"; loses the "1-2 工作天部署" copy and the Reveal-in-Finder affordance; gains a "View saved bundle" link to Flow Library
- `bpm-admin-shell`: add Flow Library entry to `AdminLayout` nav

## Impact

- **No new project**: `bpm-admin-ui` already exists; this adds a screen + a few `lib/` modules. Backend work lives under `bpm-svc/src/Application/Spec/Bundle/` + `Persistence/Spec/Bundles/`.
- **New deps**: `jszip` (frontend, ≈26KB gz) for client-side zip creation/inspection in the View tab; `System.IO.Compression` (already in BCL) for backend.
- **New EF entity**: `SpecBundle` (Id, TenantId, FlowCode, FlowVersion, ParentBundleChecksum?, ManifestJson, ZipBlob, Status: Draft|Installed|Failed, LastReproCheckAt, LastReproCheckResultJson). New migration.
- **New API endpoints** under `/api/admin/flow-library`: GET list, GET by id, POST import (multipart), GET export (zip), POST {id}/repro-check, DELETE {id}
- **Removes** the existing `POST /api/spec` and `POST /api/spec/reveal` writeable-filesystem flow; tracking-id ack copy / Reveal-in-Finder affordance disappear from `StepGoLive`
- **Acceptance demo**: spin up two `bpm-svc` SQLite databases, design LEAVE in instance A's 9-stepper, export bundle, import into instance B, run the bundled test-case → both instances produce identical `ProcessInstance` final state. This becomes the standing E2E test for the change.
- **Out of scope**: signing / encryption (per Jason 2026-05-10: 不用 sign); MCP-fed sample-org (Phase B); in-place runtime spec edits (round-trip is via re-export, never in-place); the Claude Code dev-pipeline previously hinted at by `StepGoLive` (deferred to a separate later proposal).
