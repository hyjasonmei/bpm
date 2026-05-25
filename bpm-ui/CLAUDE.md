# bpm-ui

Customer-facing employee SPA — React 18 + Vite + Tailwind v4 + shadcn.

## Ownership inside this tree

| Path | Owner | Notes |
|---|---|---|
| `src/{components,hooks,lib,screens,assets,styles}/**`, `App.tsx`, `router.tsx`, `main.tsx`, `index.css` | **lead** | Top-level shell, routes, ui primitives (`components/ui/*`), JWT-fetch helper, unified inbox wiring, BpmnView modal |
| `src/features/registry.ts` and other shared registry plumbing | **lead** | The Vite eager-glob that picks up chef-cooked manifests |
| `src/features/<CODE>/V<N>/**` | **chef** | `<CODE>_V<N>_Form.tsx`, `<CODE>_V<N>_CaseDetail.tsx`, `<CODE>_V<N>.bpmn.xml`, `manifest.ts` |
| `src/screens/forms/Reference_*.tsx` | **lead may touch for visual baseline** | 11 hand-coded model A reference forms; chef reads these for layout / tone only — they are **not** the runtime path for new flows |

As of 2026-05-25 `src/features/` only contains `registry.ts` — no
flow has been cooked into model B here yet. The 11 reference forms
still serve the demo via the legacy model A runtime.

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

`useFormRuntime`, `useFlowSubmit`, `useFlowTask`, the dual-mode
`mode='create'|'task'` contract on `screens/forms/*`, and the
`/api/processes` + `/api/tasks` clients are the old spec-driven
runtime path. **Compiles, not extended.** Cleanup is separate.

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
