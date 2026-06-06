# bpm-ui

Customer-facing employee SPA — React 18 + Vite + Tailwind v4 + shadcn.

## Ownership inside this tree

| Path | Owner | Notes |
|---|---|---|
| `src/{components,hooks,lib,screens,assets,styles}/**`, `App.tsx`, `router.tsx`, `main.tsx`, `index.css` | **lead** | Top-level shell, routes, ui primitives (`components/ui/*`), JWT-fetch helper, unified inbox wiring, BpmnView modal |
| `src/features/registry.ts` and other shared registry plumbing | **lead** | The Vite eager-glob that picks up chef-cooked manifests |
| `src/features/<CODE>/V<N>/**` | **chef** | `<CODE>_V<N>_Form.tsx`, `<CODE>_V<N>_CaseDetail.tsx`, `<CODE>_V<N>.bpmn.xml`, `manifest.ts` |
| `src/screens/forms/{FormShell,NotCookedYet}.tsx` | **lead** | Shared form shell (step rail / requestor summary / View-BPMN, used by every model-B feature form) + the "還沒煮好" page. The 11 model-A `Reference_*.tsx` were removed. |

`src/features/` now holds the cooked model-B flows (APE, EOB, ETM,
FAD, FAP, LEAVE, PURCHASE_REQUEST, TEO, TRQ, VENDOR_EXPENSE, …) plus
`registry.ts`. chef cribs layout from these feature forms — they are
the visual baseline now that the `Reference_*.tsx` set is gone.

## Manifest contract (chef-side, in case lead extends it)

```ts
// src/features/registry.ts
interface FormManifest {
  code: FormCode
  version: number
  component: ComponentType<FormComponentProps>
  detailComponent?: ComponentType<CaseDetailProps>
  bpmnXml?: string
}
```

The registry globs every `features/*/V*/manifest.ts` at startup and
resolves a flow code to its highest version automatically. Drop a V2
folder and the registry picks it up on next dev-server reload — no
central switch to edit.

`detailComponent` is bound to the global `/cases/:flowCode/:caseId`
route. `bpmnXml` is the bundle's canonical BPMN imported via Vite
`?raw` and fed to the shared `BpmnView` modal.

## Model A is retired

The model-A spec-driven UI runtime has been **removed**: the
`useFormRuntime` / `useFlowSubmit` / `useFlowTask` / `useMyInstances`
/ `useMyTasks` hooks, the 11 `screens/forms/Reference_*.tsx` forms,
and `lib/api/hrFlows.ts` are deleted (they were unrouted — nothing
live imported them).

What still lingers (separate, because it backs the legacy
`/cases/:instanceId` + `/tasks/:taskId` routes in `router.tsx` and
the admin Reports/ProcessAdmin features on the bpm-svc side):
`screens/CaseDetail.tsx`, `lib/api/process.ts`, `types/hrFlows.ts`,
and `FormRoute`'s `mode='task'` path. Removing those means dropping
or migrating the Reports/Simulator/ProcessAdmin features — a product
call, not yet done.

New flows are bespoke React components per flow, plugged in via
`features/<CODE>/V<N>/manifest.ts`.

## Type-check

`npx tsc -p tsconfig.app.json --noEmit` is the canonical type-check
(without `-p tsconfig.app.json` tsc silently skips `src/`). No JS
test runner — rely on tsc + manual boot (`npm run dev`, port 5173)
+ chrome-devtools screenshots (default `fullPage=true`).

## Conventions

- Root [`../CLAUDE.md`](../CLAUDE.md) — product context + 5-project
  architecture + Clean Architecture five-layer convention for the
  backends this UI talks to
- [`../bpm-svc/CLAUDE.md`](../bpm-svc/CLAUDE.md) — backend boundary +
  SharedIdentity
- [`../chef/skill/SKILL.md`](../chef/skill/SKILL.md) +
  [`../chef/skill/conventions.md`](../chef/skill/conventions.md) —
  per-flow folder shape, manifest, BPMN passthrough, inbox provider
- [`../lead/skill/SKILL.md`](../lead/skill/SKILL.md) — shared-shell
  boundary, when to lift a per-flow concern into `components/ui/`
- [`../README.md`](../README.md) — run + ports
