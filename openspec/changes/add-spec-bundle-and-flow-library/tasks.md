# Tasks

## 1. Bundle format — schema definition

- [x] 1.1 Document the bundle layout in `openspec/changes/add-spec-bundle-and-flow-library/specs/bpm-spec-bundle/spec.md` (already drafted as part of this change; verify file paths and required vs optional members during review)
- [x] 1.2 Create `bpm-svc/src/Domain/Spec/Bundle/BundleManifest.cs` — records: `BundleSchemaVersion (int)`, `FlowCode (string)`, `FlowVersion (int)`, `ExportedAt (DateTimeOffset)`, `SourceInstanceId (string)`, `Parent (string?)`, `Files (IReadOnlyList<BundleFileEntry>)`
- [x] 1.3 Create `BundleFileEntry.cs`: `Path (string)`, `Sha256 (string)`, `SizeBytes (long)`, `Kind (BundleFileKind)`
- [x] 1.4 Create `BundleFileKind.cs` enum: `Manifest, Spec, BpmnXml, SpecMd, Readme, Walkthrough, Changelog, Form, Notification, Sla, Actors, SampleOrg, TestCase, Asset, Other`
- [x] 1.5 Create `BundleSchemaVersion.cs` static class with `Current = 1` constant — referenced everywhere instead of magic number

## 2. Backend — BundleBuilder (export)

- [x] 2.1 Create `bpm-svc/src/Application/Spec/Bundle/IBundleBuilder.cs` interface: `Task<byte[]> BuildAsync(BundleBuildRequest req, CancellationToken ct)`
- [x] 2.2 Create `BundleBuildRequest.cs` record: `DraftSpecJson (JsonElement)`, `BpmnXml (string)`, `SampleOrg (SampleOrgSnapshot)`, `TestCases (IReadOnlyList<TestCaseSnapshot>)`, `IncludeAssets (bool)`, `IncludeChatSnapshots (bool)` (also added `ParentSpecJson?` + `SourceInstanceId`; PR-I3 will hand `ParentBundle` zip extraction here once the parser exists)
- [x] 2.3 Implement `BundleBuilder.cs`: write each file into a `ZipArchive`, compute sha256 per file, populate manifest, emit zip bytes
- [x] 2.4 Implement `SpecMdRenderer.cs`: turn `spec.json` into a human-readable markdown overview (sections: meta / flow nodes / userTasks / decisions / approvers / notifications / sla / actors)
- [x] 2.5 Implement `WalkthroughRenderer.cs`: emit a step-by-step happy-path narration generated from the spec's first test-case
- [x] 2.6 Implement `ChangelogRenderer.cs`: when parent spec is supplied, diff against parent and emit a markdown changelog (added nodes, removed nodes, changed forms, etc.) — takes `parentSpec` JsonElement directly; zip-extraction wiring deferred to PR-I3
- [x] 2.7 Implement `BundleBuildValidator.cs`: enforces `sample-org.json` present and non-empty, ≥1 test-case; throws `BundleBuildException` with errors[] (per-asset / total-bundle size limits deferred until assets pipeline lands in §7)
- [x] 2.8 Register all in `bpm-svc/src/Application/DependencyInjection.cs`
- [x] 2.9 Unit test: build a bundle from a known DraftSpec → assert manifest has correct file list, every entry has matching sha256, zip is valid via roundtrip (`BundleBuilderTests` + renderer tests, 12 new tests; total 72 → 84 green)

## 3. Backend — BundleParser + BundleValidator

- [x] 3.1 Create `IBundleParser.cs`: `Task<ParsedBundle> ParseAsync(Stream zip, CancellationToken ct)`
- [x] 3.2 Implement `BundleParser.cs`: open zip, read and verify manifest first, then load each listed file according to its `Kind`, verify sha256 matches manifest entry; ignore unlisted files (forward-compat)
- [x] 3.3 Reject `bundleSchemaVersion > BundleSchemaVersion.Current` with `BundleParseException("unknown schema version {v}")`
- [x] 3.4 Create `IBundleValidator.cs`: `BundleValidationResult Validate(ParsedBundle b)`
- [x] 3.5 Implement `BundleValidator.cs`: every `actorRef` in spec.json resolves to a path/role/group/user that exists in actors.json AND sample-org.json; every userTask in spec.json has a `forms/{userTaskId}.json`; every test-case references node ids that exist in spec.json (v1: expr paths checked against `ActorPathWhitelist`; depth-resolution against the org tree deferred to PR-I5 repro runner)
- [x] 3.6 Unit test: tampered file (bytes don't match sha256) → parse throws
- [x] 3.7 Unit test: spec references actor "submitter.manager.manager.manager.manager" but sample-org has only 2 levels → validator returns error (`EXPR_PATH_OFF_WHITELIST`)
- [x] 3.8 Unit test: forward-compat — bundle with extra unknown file under `policies/` → parser succeeds, ignored file logged at Info level

## 4. Backend — Bundle persistence (`SpecBundle` entity)

- [x] 4.1 Create `bpm-svc/src/Domain/Entities/Spec/SpecBundle.cs` (inherits AuditableEntity): `Id (Guid)`, `TenantCode (string)` (matches the rest of the codebase — `TenantId Guid` would have been an island), `FlowCode (string)`, `FlowVersion (int)`, `ManifestChecksum (string)`, `ParentManifestChecksum (string?)`, `ManifestJson (text)`, `ZipBlob (byte[])`, `Status (SpecBundleStatus)`, `LastReproCheckAt (DateTime?)`, `LastReproCheckResultJson (text?)`
- [x] 4.2 Create `SpecBundleStatus.cs` enum: `Draft, Pending, Installed, InstalledUnverified, Failed, SoftDeleted`
- [x] 4.3 Create `Persistence/Configurations/Spec/SpecBundleConfiguration.cs`: index `(TenantCode, FlowCode, FlowVersion DESC)` via per-column `IsDescending(false, false, true)`, `(ManifestChecksum)` unique, `(Status)`
- [x] 4.4 Add `DbSet<SpecBundle> SpecBundles` to `AppDbContext`
- [x] 4.5 EF migration: `dotnet ef migrations add AddSpecBundles -p bpm-svc/src/Persistence -s bpm-svc/src/Api`
- [x] 4.6 Apply locally; `sqlite3 bpm-svc/src/Api/bpm.db .schema "SpecBundles"` to verify

## 5. Backend — Runtime loader (`bpm-spec-reproducibility`)

- [x] 5.1 Create `IBundleRuntimeLoader.cs`: `Task<LoadedBundleHandle> LoadAsync(ParsedBundle b, CancellationToken ct)`
- [x] 5.2 Create `LoadedBundleHandle.cs` (sealed class, IAsyncDisposable): `ScratchTenantCode (string)` (org tables aren't tenant-scoped today; the loader namespaces by rewriting `User.Email` to `{scratch}__{email}` and tracking ids), `RegisteredSpecCode (string)`, `SeededUserCount (int)`, `SpecJson (string)` (raw spec text for inline-spec runtime overload), `InitiatorUserId (Guid)` (picks the first sample-org user with a non-null Manager so submitter.manager resolves), and a `Func<ValueTask>? OnDispose` cleanup hook
- [x] 5.3 Implement `BundleRuntimeLoader.cs`: derive scratch tenant code `repro-{flowCode}-{checksumShort}`; idempotent (second load short-circuits); seed Departments → Users → Groups + GroupMembers; resolve roles by code (synthesize Flow-scoped Role rows tagged with bundle's FlowCode when missing); seed RoleAssignments; spec.json is NOT registered with `ISpecLoader` — the runtime takes it inline via the new `IProcessRuntime.StartInstanceAsync(cmd, inlineSpecJson, ct)` overload
- [x] 5.4 Implement scratch-tenant cleanup hook: process-side (TaskHistory / ProcessTasks / ProcessInstances) deleted by `TenantCode` filter via ExecuteDelete; org-side (TPT-mapped Principals) deleted via tracked-entity RemoveRange after clearing self-referencing FKs; synthesized Role rows dropped last; defensive `AssertScratchTenant` helper refuses any tenant code that doesn't start with `repro-`
- [x] 5.5 Create `IBundleReproducibilityRunner.cs`: `Task<ReproReport> RunAsync(LoadedBundleHandle h, IReadOnlyList<TestCaseSnapshot> cases, CancellationToken ct)`
- [x] 5.6 Create `ReproReport.cs`: `OverallStatus (Pass|Fail)`, `Cases (IReadOnlyList<CaseResult>)`; `CaseResult`: `CaseId`, `Status`, `ExpectedTrace`, `ActualTrace`, `Diff (string?)`
- [x] 5.7 Implement `BundleReproducibilityRunner.cs`: per case → `IProcessRuntime.StartInstanceAsync(cmd, inlineSpecJson, ct)` (the new overload added to `IProcessRuntime` — kept the existing `(cmd, ct)` overload so PR-C's controllers don't need to change); drive forward by polling open tasks, defaulting Approve for `NodeKind.Approval` and no-decision for UserTask; build the actual trace by reconstructing the linear path from the spec's flow graph using TaskSpawned + GatewayEvaluated events as the touched-node set (sidesteps the in-flight history-row CreatedAt-tie-breaker problem flagged in `add-process-runtime`'s E2E fixture); diff is a single string `expected: [...]\nactual: [...]`
- [x] 5.8 Integration test: build a known LEAVE bundle → load via runtime loader → run repro → expect `OverallStatus = Pass`
- [x] 5.9 Integration test: tamper test-case's `expectedTrace` to insert an extra node → expect `OverallStatus = Fail` with diff highlighting the spurious node

## 6. Backend — Flow Library REST endpoints

- [x] 6.1 Create `bpm-svc/src/Api/Admin/FlowLibrary/FlowLibraryController.cs` with `[Route("api/admin/flow-library")]` and `[Authorize(Roles = "admin")]` (controller convention; matches `HrFlowsController` / `RolesAdminController`)
- [x] 6.2 `GET /` — list bundles for tenant: returns `FlowLibraryItemDto[]` (id, flowCode, flowVersion, status, manifestChecksum, parentManifestChecksum, exportedAt, lastReproCheckAt, lastReproCheckSummary). Excludes SoftDeleted, ordered by ExportedAt DESC
- [x] 6.3 `GET /{id}` — `FlowLibraryDetailDto` (summary + parsed `BundleManifest` + `ReproReport?`); 404 if not found or SoftDeleted
- [x] 6.4 `GET /{id}/export` — stream the zip blob with `Content-Disposition: attachment; filename={flowCode}_v{ver}.zip`
- [x] 6.5 `GET /{id}/files/{*path}` — stream a single file extracted from the zip (for the View tab; path traversal hardening + manifest membership check)
- [x] 6.6 `POST /import?mode=install|draft` — multipart `.zip` upload; on `mode=install` triggers parse + validate + load + repro (synchronous); on `mode=draft` parses, returns `ImportDraftResult` (no persistence). Idempotent re-import returns 409 with existing id
- [x] 6.7 `POST /{id}/repro-check` — re-run reproducibility on demand; updates `LastReproCheckAt` / `LastReproCheckResultJson` and Status
- [x] 6.8 `DELETE /{id}` — soft-delete (set Status = SoftDeleted)
- [x] 6.9 Integration test: round-trip — POST import → GET list shows it → GET export downloads identical bytes (manifest checksum equal). 15 controller tests under `tests/Bpm.Tests/Api/Admin/FlowLibrary/`
- [x] 6.10 (PR-I7) `POST /build` — accepts `{ specJson, bpmnXml, sampleOrg, testCases }`; runs `IBundleBuilder.BuildAsync` → persists `SpecBundle` (Status=Pending). Idempotent on `ManifestChecksum` — same payload returns existing id with 200 OK. No repro auto-runs (wizard surface has its own Repro Check button)
- [x] 6.11 (PR-I7) `GET /{id}/hydration` — reads persisted `ZipBlob`, parses + validates, returns same `ImportDraftResult` shape as `POST /import?mode=draft` so the wizard hand-off code path is one-shape-fits-both. 6 new controller tests; total 15 → 21

## 7. Frontend (`bpm-admin-ui`) — Flow Library screen

- [x] 7.1 Add `flow-library` to `AdminScreen` union in `components/AdminLayout.tsx` and add nav entry with icon `Library` from lucide-react
- [x] 7.2 Create `screens/FlowLibrary/FlowLibrary.tsx` — list view: cards per bundle with flowCode / version / status badge / exported_at / last repro result
- [x] 7.3 Create `screens/FlowLibrary/BundleDetail.tsx` — tabs: Manifest / spec.json / bpmn.xml (rendered as `<pre>` for now — live BpmnDiagram needs a synthesized DraftSpec; live preview deferred to PR-I7 when bundle hydration lands) / spec.md / forms / notifications / sla / actors / sample-org / test-cases / assets / last-repro
- [x] 7.4 Create `lib/api/flowLibrary.ts` — typed wrappers around `/api/admin/flow-library` endpoints (+ `types/flowLibrary.ts` mirroring backend DTOs)
- [x] 7.5 Add `jszip@3.x` dep to `bpm-admin-ui/package.json` (for client-side bundle inspection without round trip) — `jszip@3.10.1`, ships its own types
- [x] 7.6 Create `lib/bundle/parseBundleClientSide.ts` — open zip in browser → returns `{ manifest, fileList }` for the import drag-drop preview
- [x] 7.7 Implement Import button: drag-drop `.zip` → modal showing preview (manifest + file list) + actions `Install for runtime` / `Open as 9-stepper draft` / `Cancel`
- [x] 7.8 Implement Export button (per row): downloads via `apiFetch` + Blob URL so the JWT bearer rides on the request (`exportBundleUrl` also exported for callers that want a plain `window.location` redirect once a session-cookie path lands)
- [x] 7.9 Implement Repro Check button (per row): POST `/{id}/repro-check` → result dialog (`ReproReportModal`) + refresh row
- [x] 7.10 Implement Delete button (per row): `ConfirmDialog` → DELETE → refresh
- [x] 7.11 Wire `Open as 9-stepper draft`: navigate to Onboarding screen with `?bundle=<importId>` query param. PR-I7 enables this for both saved bundle rows (GUID → GET /{id}/hydration) and fresh imports (sessionStorage payload + `?bundle=draft` marker)

## 8. Frontend (`bpm-admin-ui`) — Onboarding rewire

- [x] 8.1 In `Onboarding.tsx`, on mount check `?bundle=<id>` query param; if present, fetch the import-preview payload and hydrate `DraftSpec` + sample-org + test-cases. Two paths: GUID → `getBundleDraftHydration(id)` (new GET /api/admin/flow-library/{id}/hydration endpoint), `draft` → consume `sessionStorage.bpm_draft_bundle` set by ImportModal
- [x] 8.2 Determine `stepIdx` after hydration: first failing validator, or `go_live` if all pass — `pickStepFromValidation()` walks `ONBOARDING_STEPS` in order
- [x] 8.3 Replace `Onboarding.tsx#exportSpec` (single-JSON download) with a "Build bundle" path that POSTs the assembled payload to `/api/admin/flow-library/build` and returns a saved bundle id. Live in `StepGoLive.handleSave()`; the legacy JSON download stays as `Export DraftSpec (debug)` for dev triage
- [x] 8.4 Update `lib/onboarding.ts` `DraftSpec` to carry `sampleOrg: SampleOrgSnapshot` and `testCases: TestCaseSnapshot[]` first-class. `migrateDraft()` shim brings forward old localStorage drafts (legacy `TestCase` shape mapped via `testCaseToSnapshot`); presets converted to snapshot shape
- [x] 8.5 Update `StepTest.tsx` to actually capture test-cases (id, name, JSON inputs, expectedTrace, expectedFinalStatus). Auto-walks the flow graph from startEvent on Add for a sane default; bilingual labels
- [x] 8.6 Update `StepTest.tsx` to surface a "Sample Org" editor with the curated default seeded if empty. Two tables (users + departments) with reset-to-default; cascading FK cleanup on delete
- [x] 8.7 Update validators in `lib/onboarding.ts`: `validators.test` requires ≥1 test-case; `validators.go_live` requires `sampleOrg.users.length >= 1`

## 9. Frontend (`bpm-admin-ui`) — `StepGoLive` rewrite

- [x] 9.1 Remove `submitSpec` (POST to `/api/spec`) and `revealInFinder` (POST to `/api/spec/reveal`) — both endpoints go away. PR-I7 stops calling them; PR-I8 will delete the server-side handlers
- [x] 9.2 Replace the "Submit Spec → 1-2 工作天部署" amber banner with a bundle preview panel: bundle content summary + Save to Flow Library + Download .zip. (Deviation: skipped per-file size table preview; the wizard hands off to the Flow Library detail screen for that view to keep the build path single-source-of-truth on the backend)
- [x] 9.3 On Save: POST `/api/admin/flow-library/build` → on success, set `localStorage.bpm_admin_screen_focus = id` for FlowLibrary's flash-highlight + render success state with "Open in Flow Library →"
- [x] 9.4 On Download: build then `downloadBundle(id)` (PR-I6's blob-with-bearer helper). Simpler than `?download=1` overload — server stays one path
- [x] 9.5 Remove the SpecAck success-state UI (Tracking ID + Reveal in Finder); replace with "Bundle saved as v{n}" confirmation showing manifest sha + Open-in-library / Download buttons

## 10. Frontend (`bpm-admin-ui`) — assorted

- [x] 10.1 Update `AdminLayout` nav order: Onboarding → Flow Library → Site Settings → Users & Roles → Impersonation → Audit Logs
- [x] 10.2 Add a "Saved bundles: {n}" indicator in the Onboarding header that links to Flow Library (best-effort fetch — silently hidden when the backend is unreachable so a non-essential affordance can't break the wizard)
- [x] 10.3 Type-check: `cd bpm-admin-ui && npx tsc -p tsconfig.app.json --noEmit` passes clean (note: `npm run build` runs `tsc -b` first which falls back to repo-root tsconfig where Jason hit the silently-skipped-files trap; per Jason memory, always use the explicit `-p tsconfig.app.json` form)

## 11. Cleanup of deferred Claude Code pipeline references

- [ ] 11.1 Remove the legacy `/api/spec` and `/api/spec/reveal` endpoints from `bpm-svc/src/Api/Program.cs` (after one release deprecation window)
- [ ] 11.2 Remove `bpm-svc/src/Api/incoming/` directory writes; delete leftover tracking-id files
- [ ] 11.3 Update `inovation_idea.md` §3.4 / §9 to reflect that Phase A's deliverable is "bundle in customer's Flow Library", NOT "Claude Code pipeline" (pipeline becomes Phase B)
- [x] 11.4 Remove "AI Onboarding" subtitle text in `Onboarding.tsx` mentioning "spec 自動送至後台 Claude Code 部署管線" — replaced with "9 個 step 把流程規格談清楚，最後產生可攜帶的 spec bundle，存到 Flow Library。"

## 12. End-to-end acceptance test

- [ ] 12.1 Write `bpm-svc/test/Integration/BundleE2ETests.cs` — boot two SQLite databases (`bpmA.db`, `bpmB.db`) in the same test process; instance A designs LEAVE via direct API calls; instance A exports bundle; instance B imports bundle; instance B runs the bundled test-case; assert final ProcessInstance.SpecSnapshotJson + node trace equal between A and B
- [ ] 12.2 Add the test to CI as a separate suite (it boots two contexts, slower than unit tests)
- [ ] 12.3 Document the demo script in `docs/spec-bundle-demo.md`: how to manually reproduce instance A → instance B flow during sales demos
