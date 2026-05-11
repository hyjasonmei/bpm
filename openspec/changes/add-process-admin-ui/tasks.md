# Tasks

## 1. Process admin shell

- [x] 1.1 Create `bpm-admin-ui/src/screens/processes/ProcessAdminShell.tsx` (path drift: spec said `bpm-ui/`, but BpmnEditor + onboarding wizard live in `bpm-admin-ui/` so the operational counterpart belongs there too)
- [~] 1.2 Auth guard — covered by the existing admin-app gate (App.tsx auto-mints admin JWT on load); finer-grained `flow_admin:<spec>` role check deferred to add-roles-and-permissions enhancements
- [x] 1.3 Sidebar with the 7 sections (Definitions / Designer / Simulator / Live Cases / Completed Cases / Reports / Flow Notifications); only Definitions has real content for K1, the rest mount `<ComingSoon section="…" />`

## 2. Flow definitions

- [x] 2.1 DefinitionsList — combined view of installed bundles + filesystem specs via new `GET /api/admin/process-admin/definitions` (bundle wins when flowCode collides)
- [x] 2.2 New flow → opens Designer (placeholder navigation; PR-K2 will wire the real editor entry point)
- [x] 2.3 Version history view — side panel per row, fed by `GET /api/admin/process-admin/definitions/{flowCode}/versions`
- [x] 2.4 Spec diff viewer — side-by-side text view of latest vs previous; line-diff highlighting deferred

## 3. Designer (BPMN + forms)

- [x] 3.1 Create `Designer.tsx` with three panes (tree / canvas / detail) — `bpm-admin-ui/src/screens/processes/Designer.tsx` (path drift `bpm-ui` → `bpm-admin-ui`, same as §1.1)
- [x] 3.2 Embed `BpmnEditor`; extended with `onSelect` (subscribes to bpmn-js `selection.changed` on the eventBus) + `selectedId` (tree → canvas bridge via `selection.select` on the SelectionService)
- [~] 3.3 Right-pane router mounts StepForms / StepApprovers / StepNotify / StepDecisions / StepSla (Option B per task brief — Step* mount unfiltered; per-node `focusNodeId` enhancement deferred to keep the 5-step prop surface stable). startEvent / endEvent / serviceTask get inline messages.
- [~] 3.4 Top toolbar Save / Publish / Preview / Simulate. Save persists per-flowCode to localStorage (`bpm_designer_draft_<flowCode>`). Publish runs `POST /api/admin/flow-library/build` and surfaces the new bundle id. Preview = spec.json text preview (live form preview deferred — `DynamicForm` lives in `bpm-ui` and the demo guard forbids cross-package import). Simulate = jump to PR-K3 section.
- [x] 3.5 Optimistic concurrency: checksum-idempotent publish + pre-publish `listVersions` check warns when a newer bundle landed since hydration. No in-place edit so no real 409 path; documented in Designer header.
- [x] 3.6 Backend: `GET /api/admin/process-admin/definitions/{flowCode}/spec` returns raw spec.json — bundle blob first, then filesystem fallback. 5 controller tests added (was 193 → 198).

## 4. Simulator

- [x] 4.1 Backend: `IProcessSimulator.SimulateAsync(SimulationRequest)` returning a `SimulationResult` with trace + notifications + final form/status. Records: `SimulationRequest` (FlowCode + FormData + optional SampleOrg/InitiatorUserId), `SimulationStep` (NodeId / NodeKind / Outcome ∈ {auto-advanced, completed, spawned, errored} / ResolvedAssigneeUserId / AssigneeName / Decision / Notes), `SimulationNotification`.
- [~] 4.2 Dry-mode: **delete-on-finally** (not always-rollback transaction). Picked the cleanup approach because `ProcessRuntime.SubmitTaskAsync` opens its own per-call EF transaction inside; with the simulator running its own AppDbContext separately from the runtime's they're on different SQLite connections, so an outer rollback can't undo the inner commit. Cleanup-by-instance-id is one ExecuteDelete per table (SandboxCapturedMessages → TaskHistory → ProcessTasks → ProcessInstances) and matches the BundleRuntimeLoader scratch-tenant-cleanup pattern. Sandbox-on toggle wraps the call (same pattern as PR-J6 §11.2) so notifications flow into SandboxCapturedMessages without requiring caller toggle; previous SandboxMode is restored in the finally.
- [x] 4.3 Endpoint `POST /api/admin/process-admin/simulate` (path drift: nested under `process-admin` rather than top-level `/api/admin/simulate` so the existing `[Authorize(Roles="admin")]` controller-level gate covers it; the endpoint stays a thin pass-through). Returns 200 with `SimulationResult.Success=false` on simulation error per the API design.
- [~] 4.4 SimulatorScreen UI: flow dropdown + initiator dropdown (sandbox personas) + JSON textarea ("Form data (JSON)") + Run button. DynamicForm reuse deferred — DynamicForm lives in `bpm-ui` and the demo guard forbids cross-package import. Marked in the screen header as a future enhancement.
- [~] 4.5 Trace visualization: row-level coloring v1 (green=completed, gray=auto-advanced, amber=spawned/open-at-end, red=errored), plus an outcome badge per row. Canvas-overlay highlighting on the BPMN editor deferred — that requires extending `BpmnEditor` with a status overlay prop.

## 5. Live cases

- [ ] 5.1 LiveCasesList with table + filters (spec, age, breach)
- [ ] 5.2 LiveCaseDetail with flowchart + history feed + open tasks + admin actions
- [ ] 5.3 Polling 30s list / 15s detail

## 6. Admin intervention

- [ ] 6.1 Backend endpoints in `ProcessAdminController.cs`:
  - `POST /api/admin/tasks/{id}/force-reassign` body `{ newAssigneeUserId, reason }`
  - `POST /api/admin/tasks/{id}/force-return` body `{ targetNodeId, reason }`
  - `POST /api/admin/tasks/{id}/force-submit` body `{ formDataPatch?, decision?, reason }`
  - `POST /api/admin/processes/{id}/terminate` body `{ reason }`
- [ ] 6.2 Each writes TaskHistory with `ActorUserId = admin user id, actor_role = 'admin'` (extend payload JSON), mandatory reason
- [ ] 6.3 Frontend modal flows for each action

## 7. Completed cases

- [ ] 7.1 CompletedCasesList with cycle time metrics
- [ ] 7.2 Filters + pagination
- [ ] 7.3 PDF export per case (deferred to add-pdf-export proposal — scaffold the button + 'coming soon')

## 8. Reports

- [ ] 8.1 Backend `IProcessReportingService` aggregating per-spec stats
- [ ] 8.2 Caching: 5-min TTL keyed by `{tenant}_{spec}_{period}`
- [ ] 8.3 Cache invalidation on InstanceCompleted / InstanceCancelled events
- [ ] 8.4 Endpoints: `GET /api/admin/reports/per-spec?spec_code=&period=30d`
- [ ] 8.5 ReportsDashboard UI: stat cards + cycle time histogram + bottleneck analysis + per-assignee load
- [ ] 8.6 CSV / PDF export

## 9. Flow notification audit

- [ ] 9.1 FlowNotificationAudit page filtering NotificationDispatchAudits by spec_code

## 10. End-to-end verification

- [ ] 10.1 Boot stack with seeded data
- [ ] 10.2 Admin opens /processes; verify auth gate redirects non-admins
- [ ] 10.3 Open Designer for LEAVE; modify a field; Save (draft); reload; verify draft persists
- [ ] 10.4 Run simulator with sample 8-day vacation form; verify trace shows VP path correctly
- [ ] 10.5 Open Live Cases; force-reassign one task; verify TaskHistory has admin actor_role row
- [ ] 10.6 Open Reports; verify breach rate matches DB-level count
- [ ] 10.7 **Demo guard**: 9 mock-up forms, Home, Search, Report, lib/workflow.ts NOT modified

## 11. Commit

- [ ] 11.1 Commit in chunks (shell; designer; simulator; live cases; admin actions; reports; verification)
- [ ] 11.2 Push via GitKraken
