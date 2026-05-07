## Why

Process Admin is the *business-user* admin: the HR / IT lead at the customer who designs flows and watches them run. Their concerns:

- "Edit our LEAVE flow to add a third approval step"
- "Why is this case stuck? Who's holding it up?"
- "Show me a dry-run with 8-day vacation; verify VP would be picked"
- "Force this stuck task to a different assignee"
- "Our 採購 flow just had its first SLA breach this month — show me which step"

This is distinct from System Admin (IT / ops persona). System Admin manages users / tenant config; Process Admin manages flows / running cases.

This change ships:

- BPMN designer UI integrated with the existing wizard's flow
- Form designer (the existing StepForms-style editor, hoisted to a standalone admin route)
- Process simulator (dry-run an instance with sample data; visualize node-by-node outcomes)
- Live process monitoring with admin intervention (force reassign, return, terminate, skip)
- Reports (per-spec aggregate stats, cycle time, breach rate)

## What Changes

### Process Admin shell (NEW capability `bpm-process-admin-ui`)

Route prefix: `/processes/*` (separate from `/admin/*` System Admin and the employee `/`).

Auth: requires `flow_admin:<flow_code>` role (or `tenant_admin`).

Sidebar:
- 流程定義 / Flow Definitions — list specs, edit, version history
- 流程設計 / Designer — BPMN + Form editor as a unified editor (extends wizard StepFlow + StepForms)
- 流程模擬 / Simulator — dry-run preview
- 進行中案件 / Live Cases — monitor running instances
- 已完成案件 / Completed — historical view
- 報表 / Reports — analytics
- 通知 / Notifications — flow-scoped notification audit

### Flow Definitions

`/processes/definitions`:

- List all spec.json files in `specs-incoming/` plus active versions
- "+ New flow" → opens designer
- Click → opens designer with that spec loaded
- Version history per flow (visualize diffs between versions)
- "Publish" workflow: edit creates a new version; older versions still drive in-flight instances per snapshot semantics

### Designer — BPMN + forms unified

`/processes/designer/{specCode}`:

- Left pane: node tree + properties panel
- Center: BPMN canvas (using `bpmn-js` library — already partially integrated in `bpm-ui/src/components/BpmnEditor.tsx`)
- Right pane: per-node detail editor
  - For `userTask`: opens StepForms-style editor for fields + assignee + viewers
  - For `approval`: opens StepApprovers-style editor for ActorRef
  - For `gateway`: opens StepDecisions-style editor for branches + condition expressions (CEL with live validation from `add-cel-expressions`)
  - For `notify`: opens StepNotify-style editor
- Bottom: SLA / metadata panel
- Top toolbar: Save (creates draft) / Publish (creates new version) / Preview as user / Simulate

This is essentially the wizard hoisted to a non-stepper editor — admin can jump between steps freely, doesn't follow linear progression.

### Simulator

`/processes/simulator/{specCode}`:

- Form to fill sample input (uses DynamicForm against the start-event userTask's spec)
- Pick test users (from seed personas)
- "Run simulation" button
- Output: visual flowchart of the chosen path, every gateway evaluated, every approver resolved, every notification dispatched
- For each node: which user(s) would receive the task, computed DueAt based on SLA, any expression that failed
- No actual writes — same shape as PROCESS_RUNTIME but read-only

This is dry-run essential for "trust but verify": admin simulates before publishing to catch broken expressions, missing approvers, etc.

### Live cases monitoring

`/processes/live-cases`:

- Table: running ProcessInstances (default filter: my flows)
- Columns: instance id, spec code, initiator, current node, time-in-current-node, due/breach indicator
- Click → instance detail page
- Filter / sort by spec, age, breach status

`/processes/live-cases/{instanceId}`:

- Visual flowchart with current node highlighted
- TaskHistory feed (using comments + history)
- Currently open tasks with assignee + age
- **Admin actions** (with audit):
  - **Force reassign** — pick a different user for an open task; original task cancelled, new task spawned
  - **Force return** — return current approval to a previous step, with reason
  - **Force submit** (rare) — admin acts as the assignee with a recorded actor reason
  - **Terminate instance** — cancel with reason; all tasks cancelled

Every admin action writes to TaskHistory with `actor_role = 'admin'` flag — clearly distinguishable from organic user actions.

### Completed cases

`/processes/completed`:

- Same shape as Live but for `Status IN (Completed, Cancelled)`
- Cycle time per instance, average per spec
- PDF export per case (uses future `add-pdf-export` capability)

### Reports

`/processes/reports`:

- Per-spec stats: total instances / completed / cancelled / breach rate
- Cycle time distribution (histogram + p50 / p95)
- Bottleneck analysis: which node consumes most time on average
- Per-assignee load: who has the longest queue
- Export as CSV / PDF

Implementation: aggregates compute over completed instances; cached for 5 minutes.

### Flow-scoped notification audit

`/processes/notifications/{specCode}`:

- All NotificationDispatchAudit + NotificationDelivery rows for instances of this spec
- Useful for "did all the on_assign notifications fire?"

### Out of scope (future changes)

- Real-time live-update of the case detail (WebSocket push) — polling for now (60s)
- Visual diff view of two spec versions (text diff viewer for now)
- Spec-level access control (who can edit which flow) beyond the existing flow_admin role
- Custom node types (escape from BPMN basic set)
- Branch / merge of spec versions (linear versioning only)
- A/B testing two flow versions in parallel
- Real-time collaborative editing of a spec (single-editor lock for now)
- Flow templates / marketplace
- Voice / chat AI for flow design (deferred per "no AI experimental" directive)

## Capabilities

### New Capabilities

- `bpm-process-admin-ui` — `/processes/*` SPA routes; flow definitions list + version history; unified BPMN + forms designer; simulator; live case monitoring with admin intervention; completed cases view; reports; flow-scoped notification audit; admin action audit trail with `actor_role = 'admin'`.

### Modified Capabilities

- `bpm-process-runtime` — admin intervention endpoints: `POST /api/admin/tasks/{id}/force-reassign`, `POST /api/admin/tasks/{id}/force-return`, `POST /api/admin/tasks/{id}/force-submit`, `POST /api/admin/processes/{id}/terminate`. Each writes a TaskHistory row with `actor_role = 'admin'` and a mandatory reason.

## Impact

- **bpm-ui/src/screens/processes/ProcessAdminShell.tsx**: new
- **bpm-ui/src/screens/processes/definitions/**: list, version history
- **bpm-ui/src/screens/processes/designer/Designer.tsx**: unified BPMN + form editor
- **bpm-ui/src/screens/processes/simulator/Simulator.tsx**: new
- **bpm-ui/src/screens/processes/live/LiveCasesList.tsx, LiveCaseDetail.tsx**: new
- **bpm-ui/src/screens/processes/completed/CompletedCasesList.tsx**: new
- **bpm-ui/src/screens/processes/reports/ReportsDashboard.tsx**: new
- **bpm-ui/src/screens/processes/notifications/FlowNotificationAudit.tsx**: new
- **bpm-ui/src/components/AppLayout.tsx**: route registration `/processes/*`
- **bpm-svc/src/Api/Admin/ProcessAdminController.cs**: new — force-reassign / force-return / force-submit / terminate endpoints
- **bpm-svc/src/Api/Admin/SimulatorController.cs**: simulate endpoint
- **bpm-svc/src/Application/Process/Simulator/IProcessSimulator.cs**: simulator service (dry-run engine — reuses ProcessRuntime logic without DB writes)
- **bpm-svc/src/Application/Process/Reports/IProcessReportingService.cs**: aggregate stats
- **No DB migration**
- **NPM dependencies**: `bpmn-js` already in package.json; possibly add `bpmn-js-properties-panel` for advanced editing
- **Demo guard**: 9 mock-up forms, Home, Search, Report, lib/workflow.ts NOT modified
