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

- [ ] 4.1 Create `bpm-svc/src/Domain/Entities/Spec/SpecBundle.cs` (inherits AuditableEntity): `Id (Guid)`, `TenantId (Guid)`, `FlowCode (string)`, `FlowVersion (int)`, `ManifestChecksum (string)`, `ParentManifestChecksum (string?)`, `ManifestJson (text)`, `ZipBlob (byte[])`, `Status (SpecBundleStatus)`, `LastReproCheckAt (DateTimeOffset?)`, `LastReproCheckResultJson (text?)`
- [ ] 4.2 Create `SpecBundleStatus.cs` enum: `Draft, Pending, Installed, InstalledUnverified, Failed, SoftDeleted`
- [ ] 4.3 Create `Persistence/Configurations/Spec/SpecBundleConfiguration.cs`: index `(TenantId, FlowCode, FlowVersion DESC)`, `(ManifestChecksum)` unique, `(Status)`
- [ ] 4.4 Add `DbSet<SpecBundle> SpecBundles` to `AppDbContext`
- [ ] 4.5 EF migration: `dotnet ef migrations add AddSpecBundles -p bpm-svc/src/Persistence -s bpm-svc/src/Api`
- [ ] 4.6 Apply locally; `sqlite3 bpm-svc/src/Api/bpm.db .schema "SpecBundles"` to verify

## 5. Backend — Runtime loader (`bpm-spec-reproducibility`)

- [ ] 5.1 Create `IBundleRuntimeLoader.cs`: `Task<LoadedBundleHandle> LoadAsync(ParsedBundle b, CancellationToken ct)`
- [ ] 5.2 Create `LoadedBundleHandle.cs` record: `ScratchTenantId (Guid)`, `RegisteredSpecCode (string)`, `SeededUserCount (int)`, `Disposable cleanup`
- [ ] 5.3 Implement `BundleRuntimeLoader.cs`: create scratch tenant `repro-{flowCode}-{checksumShort}`; insert sample-org.json users/groups/depts under that tenant id; register spec.json with the runtime spec store; return handle
- [ ] 5.4 Implement scratch-tenant cleanup hook (on handle dispose, delete the tenant + all data); guard against deleting non-scratch tenants by name prefix check
- [ ] 5.5 Create `IBundleReproducibilityRunner.cs`: `Task<ReproReport> RunAsync(LoadedBundleHandle h, IReadOnlyList<TestCaseSnapshot> cases, CancellationToken ct)`
- [ ] 5.6 Create `ReproReport.cs`: `OverallStatus (Pass|Fail)`, `Cases (IReadOnlyList<CaseResult>)`; `CaseResult`: `CaseId`, `Status`, `ExpectedTrace`, `ActualTrace`, `Diff (string?)`
- [ ] 5.7 Implement `BundleReproducibilityRunner.cs`: per case → `IProcessRuntime.StartInstanceAsync` (from `add-process-runtime`); drive it by feeding test-case form data; collect node trace from TaskHistory; assert trace equality (timestamp-stripped, assignee-id-stripped); produce diff
- [ ] 5.8 Integration test: build a known LEAVE bundle → load via runtime loader → run repro → expect `OverallStatus = Pass`
- [ ] 5.9 Integration test: tamper test-case's `expectedTrace` to insert an extra node → expect `OverallStatus = Fail` with diff highlighting the spurious node

## 6. Backend — Flow Library REST endpoints

- [ ] 6.1 Create `bpm-svc/src/Api/Admin/FlowLibrary/FlowLibraryEndpoints.cs` with `MapGroup("/api/admin/flow-library")` and an `[Admin]` auth requirement
- [ ] 6.2 `GET /` — list bundles for tenant: returns `(id, flowCode, flowVersion, status, exportedAt, lastReproCheckAt)[]`
- [ ] 6.3 `GET /{id}` — bundle metadata + manifest contents (no zip blob)
- [ ] 6.4 `GET /{id}/export` — stream the zip blob with `Content-Disposition: attachment; filename={flowCode}_v{ver}.zip`
- [ ] 6.5 `GET /{id}/files/{*path}` — stream a single file extracted from the zip (for the View tab; respects path traversal hardening)
- [ ] 6.6 `POST /import?mode=install|draft` — multipart `.zip` upload; on `mode=install` triggers parse + validate + load + repro; on `mode=draft` parses, returns DraftSpec hydration payload (no persistence)
- [ ] 6.7 `POST /{id}/repro-check` — re-run reproducibility on demand; updates `LastReproCheckAt` / `LastReproCheckResultJson`
- [ ] 6.8 `DELETE /{id}` — soft-delete (set Status = SoftDeleted)
- [ ] 6.9 Integration test: round-trip — POST import → GET list shows it → GET export downloads identical bytes (manifest checksum equal)

## 7. Frontend (`bpm-admin-ui`) — Flow Library screen

- [ ] 7.1 Add `flow-library` to `AdminScreen` union in `components/AdminLayout.tsx` and add nav entry with icon `Library` from lucide-react
- [ ] 7.2 Create `screens/FlowLibrary/FlowLibrary.tsx` — list view: cards per bundle with flowCode / version / status badge / exported_at / last repro result
- [ ] 7.3 Create `screens/FlowLibrary/BundleDetail.tsx` — tabs: Manifest / spec.json / bpmn.xml render (via existing `BpmnDiagram` component) / spec.md / forms / notifications / sla / actors / sample-org / test-cases / assets
- [ ] 7.4 Create `lib/api/flowLibrary.ts` — typed wrappers around `/api/admin/flow-library` endpoints
- [ ] 7.5 Add `jszip@3.x` dep to `bpm-admin-ui/package.json` (for client-side bundle inspection without round trip)
- [ ] 7.6 Create `lib/bundle/parseBundleClientSide.ts` — open zip in browser → returns same `ParsedBundle` shape as backend (used by Import drag-drop preview)
- [ ] 7.7 Implement Import button: drag-drop `.zip` → modal showing preview (manifest + file list) + radio choice `Install for runtime` / `Open as 9-stepper draft` / `Cancel`
- [ ] 7.8 Implement Export button (per row): GET `/{id}/export` → trigger download
- [ ] 7.9 Implement Repro Check button (per row): POST `/{id}/repro-check` → poll until done → show result dialog
- [ ] 7.10 Implement Delete button (per row): confirm dialog → DELETE
- [ ] 7.11 Wire `Open as 9-stepper draft`: navigate to Onboarding screen with `?bundle=<importId>` query param

## 8. Frontend (`bpm-admin-ui`) — Onboarding rewire

- [ ] 8.1 In `Onboarding.tsx`, on mount check `?bundle=<id>` query param; if present, fetch the import-preview payload and hydrate `DraftSpec` + sample-org + test-cases
- [ ] 8.2 Determine `stepIdx` after hydration: first failing validator, or `go_live` if all pass
- [ ] 8.3 Replace `Onboarding.tsx#exportSpec` (single-JSON download) with a "Build bundle" path that POSTs the assembled payload to `/api/admin/flow-library/build` and returns a saved bundle id
- [ ] 8.4 Update `lib/onboarding.ts` `DraftSpec` to carry `sampleOrg: SampleOrgSnapshot` and `testCases: TestCaseSnapshot[]` first-class (today these are stubs)
- [ ] 8.5 Update `StepTest.tsx` to actually capture test-cases (test name, form-data inputs per userTask, expected node trace, expected final status); today it's mostly a placeholder
- [ ] 8.6 Update `StepTest.tsx` to surface a "Sample Org" editor with the curated default seeded if empty
- [ ] 8.7 Update validators in `lib/onboarding.ts`: `validators.test` requires ≥1 test-case; `validators.go_live` requires sample-org non-empty

## 9. Frontend (`bpm-admin-ui`) — `StepGoLive` rewrite

- [ ] 9.1 Remove `submitSpec` (POST to `/api/spec`) and `revealInFinder` (POST to `/api/spec/reveal`) — both endpoints go away
- [ ] 9.2 Replace the "Submit Spec → 1-2 工作天部署" amber banner with a bundle preview panel: file list with sizes, total bundle size, "Save to Flow Library" button, "Download .zip" button
- [ ] 9.3 On Save: POST `/api/admin/flow-library/build` → on success, navigate to Flow Library with the new bundle highlighted
- [ ] 9.4 On Download: same call but with `?download=1` → response is the zip stream
- [ ] 9.5 Remove the SpecAck success-state UI (Tracking ID + Reveal in Finder); replace with a simpler "Bundle saved as v{n}" confirmation linking to Flow Library

## 10. Frontend (`bpm-admin-ui`) — assorted

- [ ] 10.1 Update `AdminLayout` nav order: Onboarding → Flow Library → Site Settings → Users & Roles → Impersonation → Audit Logs
- [ ] 10.2 Add a "Saved bundles: {n}" indicator in the Onboarding header that links to Flow Library
- [ ] 10.3 Type-check: `npm --prefix bpm-admin-ui run build` (uses `tsc -p tsconfig.app.json`) passes

## 11. Cleanup of deferred Claude Code pipeline references

- [ ] 11.1 Remove the legacy `/api/spec` and `/api/spec/reveal` endpoints from `bpm-svc/src/Api/Program.cs` (after one release deprecation window)
- [ ] 11.2 Remove `bpm-svc/src/Api/incoming/` directory writes; delete leftover tracking-id files
- [ ] 11.3 Update `inovation_idea.md` §3.4 / §9 to reflect that Phase A's deliverable is "bundle in customer's Flow Library", NOT "Claude Code pipeline" (pipeline becomes Phase B)
- [ ] 11.4 Remove "AI Onboarding" subtitle text in `Onboarding.tsx` mentioning "spec 自動送至後台 Claude Code 部署管線"

## 12. End-to-end acceptance test

- [ ] 12.1 Write `bpm-svc/test/Integration/BundleE2ETests.cs` — boot two SQLite databases (`bpmA.db`, `bpmB.db`) in the same test process; instance A designs LEAVE via direct API calls; instance A exports bundle; instance B imports bundle; instance B runs the bundled test-case; assert final ProcessInstance.SpecSnapshotJson + node trace equal between A and B
- [ ] 12.2 Add the test to CI as a separate suite (it boots two contexts, slower than unit tests)
- [ ] 12.3 Document the demo script in `docs/spec-bundle-demo.md`: how to manually reproduce instance A → instance B flow during sales demos
