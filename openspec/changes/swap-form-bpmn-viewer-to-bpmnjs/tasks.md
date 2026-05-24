# Tasks

## 1. Backend: BPMN XML snapshot + endpoint

- [ ] 1.1 Add `BpmnXml` (TEXT, nullable) column to `ProcessInstance`
- [ ] 1.2 EF migration `AddBpmnXmlToProcessInstance`
- [ ] 1.3 `ProcessRuntime.StartInstanceAsync` — write `bundle.bpmnXml` (when present) into the snapshot
- [ ] 1.4 `GET /api/processes/{id}/bpmn-xml` — returns `text/xml` with the snapshotted BPMN
- [ ] 1.5 `GET /api/spec/{code}?include=bpmn` — returns BPMN alongside the spec for create mode
- [ ] 1.6 Tests: snapshot persistence + endpoint roundtrip

## 2. Frontend viewer

- [ ] 2.1 `npm install bpmn-js`
- [ ] 2.2 `components/BpmnViewer.lazy.ts` — dynamic import wrapper
- [ ] 2.3 Rewrite `BpmnView.tsx` over bpmn-js NavigatedViewer
- [ ] 2.4 Active-node marker (`bpm-active`) from current task `nodeId`
- [ ] 2.5 Completed-node markers (`bpm-completed`) from `/processes/{id}/history` (task mode only)
- [ ] 2.6 Styles in `bpmn-viewer.css`

## 3. Verify

- [ ] 3.1 `dotnet build` clean; `dotnet test` covers the new endpoint
- [ ] 3.2 `npx tsc -p tsconfig.app.json --noEmit` clean
- [ ] 3.3 Manual: open LEAVE form → "View BPMN" renders proper BPMN diagram with start / tasks / gateways
- [ ] 3.4 Manual: open the same form in task mode → active node is highlighted; completed nodes show green markers
- [ ] 3.5 chrome-devtools screenshot of viewer (create + task mode) on PR
