## Why

Jason reported 2026-05-24 that the "View BPMN" modal on every form
(upper-right of `FormShell`) still renders a hand-rolled SVG diagram
(`components/BpmnView.tsx`) instead of the agreed-upon `bpmn-js` viewer.
The hand-rolled view diverges from BPMN 2.0 semantics, can't show
gateways / parallel branches / lanes, and forces us to keep a custom
codepath in sync with `ProcessRuntime`'s spec model — which is already
emitted as standard BPMN by the wizard (`bpmn.xml` is in every bundle).

This change swaps the renderer to `bpmn-js` (the canonical BPMN.io
viewer that bpm-admin-ui's Process Designer was already specced to use)
so both the employee form modal and the admin designer share one BPMN
runtime story.

## What Changes

### NPM dependency

Add `bpmn-js` (~600 KB minified) to `bpm-ui/package.json`. Lazy-load
the viewer via dynamic import so it only enters the chunk users actually
opening "View BPMN" pay for.

### `<BpmnView>` rewrite

`bpm-ui/src/components/BpmnView.tsx` becomes a thin wrapper around
`bpm-js`'s `NavigatedViewer`:

- Accepts the BPMN XML string (sourced from the current instance's
  `SpecSnapshot` via a new `GET /api/processes/{id}/bpmn-xml` endpoint,
  or directly from `sample_specs/<code>_v<n>.bpmn.xml` for create mode).
- Highlights the active node (current task's `nodeId`) using bpmn-js's
  `canvas.addMarker(elementId, 'bpm-active')` API.
- Pan / zoom built-in (no custom SVG math).
- Sits inside the shared `<Modal>` primitive once
  `fix-modal-fullpage-backdrop` lands; until then it keeps its current
  `fixed inset-0` wrapper.

### Active-node + history overlay

When opened from task mode (`taskId` present), the viewer also calls
`/api/processes/{id}/history` and adds `bpm-completed` markers to nodes
that already finished (TaskHistory rows of kind `TaskCompleted`).
Hovering a completed node shows a small popover with the actor + time.

### Backend: BPMN XML endpoint

`GET /api/processes/{id}/bpmn-xml` — returns the BPMN XML embedded in
the instance's `SpecSnapshot.BpmnXml` (the bundle already ships this
under `bpmn.xml`; the snapshot writer just needs to keep that copy).
For create mode, the existing `GET /api/spec/{code}` endpoint is
extended with a `?include=bpmn` flag.

### Out of scope

- BPMN editing (Process Designer in bpm-admin-ui is the modeller —
  separate `bpm-process-admin-ui` capability)
- Sequence flow conditions overlay
- Lane / pool rendering tweaks (use bpmn-js defaults)
- Switching to bpmn-js for the admin "Designer" pane — covered by the
  existing `bpm-process-admin-ui` spec

## Capabilities

### New

- `bpm-shell-ui` (UI core capability for bpm-ui shell components) —
  adds the BPMN viewer contract used by FormShell.

### Modified

- `bpm-process-runtime` — adds the `/bpmn-xml` endpoint contract.
- `bpm-spec-bundle` — already requires `bpmn.xml` in the bundle; the
  snapshot writer SHALL preserve it on `StartInstanceAsync`.

## Impact

- `bpm-ui/package.json` — `bpmn-js` dependency
- `bpm-ui/src/components/BpmnView.tsx` — rewritten
- `bpm-ui/src/components/BpmnViewer.lazy.ts` — new (dynamic import wrapper)
- `bpm-ui/src/styles/bpmn-viewer.css` — bpmn-js theme + `bpm-active` / `bpm-completed` marker styles
- `bpm-svc/src/Api/Process/ProcessQueryController.cs` — new `GET /api/processes/{id}/bpmn-xml`
- `bpm-svc/src/Api/Spec/SpecController.cs` — `?include=bpmn` query flag
- `bpm-svc/src/Domain/Entities/Process/ProcessInstance.cs` — add `BpmnXml` column (snapshot)
- EF migration: `AddBpmnXmlToProcessInstance`
- No chef-skill update (chef doesn't touch FormShell)
