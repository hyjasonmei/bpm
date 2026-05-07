# Tasks

## 1. Process admin shell

- [ ] 1.1 Create `bpm-ui/src/screens/processes/ProcessAdminShell.tsx`
- [ ] 1.2 Auth guard: requires `flow_admin:*` or `tenant_admin`
- [ ] 1.3 Sidebar with the 7 sections

## 2. Flow definitions

- [ ] 2.1 DefinitionsList showing spec.json entries with active version
- [ ] 2.2 New flow → opens designer
- [ ] 2.3 Version history view (tab in detail)
- [ ] 2.4 Spec diff viewer (text-diff for now)

## 3. Designer (BPMN + forms)

- [ ] 3.1 Create `Designer.tsx` with three panes (tree / canvas / detail)
- [ ] 3.2 Embed `BpmnEditor` (existing component); extend with selection events feeding the right pane
- [ ] 3.3 Right-pane router: based on selected node type, mount StepForms / StepApprovers / StepNotify / StepDecisions / StepSla
- [ ] 3.4 Top toolbar: Save (creates draft) / Publish (new version) / Preview / Simulate
- [ ] 3.5 Optimistic concurrency: PUT carries version; UI handles 409 conflict gracefully

## 4. Simulator

- [ ] 4.1 Backend: `IProcessSimulator.SimulateAsync(specCode, formData, sampleUsers)` returning a trace
- [ ] 4.2 Dry-mode: wrap DB ops in always-rollback transaction; in-memory dispatcher / file storage
- [ ] 4.3 Endpoint `POST /api/admin/simulate` admin-only
- [ ] 4.4 SimulatorScreen UI: input form (DynamicForm against start userTask) + trace visualizer
- [ ] 4.5 Trace visualization: each node colored by outcome (success / failure / no-recipients)

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
