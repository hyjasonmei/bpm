import type { ComponentType } from 'react'
import type { FormCode } from '@/lib/workflow'

/**
 * Contract every chef-generated form folder must export.
 *
 * `bpm-ui/src/features/<CODE>/V<N>/manifest.ts` declares the flow
 * code + version it implements and the React component to render
 * when the runtime dispatches a `'form'` screen carrying that code.
 *
 * chef writes the manifest as part of the codegen output; bpm-ui's
 * boot never imports anything from feature folders by hand — the
 * registry below globs them in via Vite, so adding a new flow does
 * NOT require editing App.tsx or any central switch.
 */
export interface FormManifest {
  code: FormCode
  version: number
  component: ComponentType<FormComponentProps>
  /** Optional read-only case-detail page. Rendered by the global
   *  `/cases/:flowCode/:caseId` route when a feature ships one.
   *  Click-through from the unified inbox lands here. */
  detailComponent?: ComponentType<CaseDetailProps>
  /** Optional bundle-authored BPMN XML — same file admin's modeler
   *  exports. Ship it (e.g. `import x from './X.bpmn.xml?raw'`) so
   *  FormShell renders the canonical diagram instead of synthesizing
   *  a linear stand-in from the FORMS step list. */
  bpmnXml?: string
}

export interface FormComponentProps {
  persona: string
  mode?: 'create' | 'task'
  taskId?: string | null
  onSubmitted?: () => void
}

export interface CaseDetailProps {
  caseId: string
  persona: string
}

/**
 * Vite eager glob: pulls every `manifest.ts` under
 * `features/<anything>/V<anything>/`. Each module's default export
 * (or named `manifest` export) is treated as a FormManifest.
 *
 * For two manifests claiming the same `code`, the highest `version`
 * wins. This keeps the legacy `LEAVE_V1` available while a
 * regenerated `LEAVE_V2` is in flight without breaking the live
 * route — switch by renaming the older folder or by flipping its
 * version number.
 */
const modules = import.meta.glob<{ default?: FormManifest; manifest?: FormManifest }>(
  './*/V*/manifest.ts',
  { eager: true },
)

const collected = new Map<FormCode, FormManifest>()
for (const path in modules) {
  const mod = modules[path]
  const m = mod.default ?? mod.manifest
  if (!m || !m.code || !m.component) {
    console.warn(`[features registry] ${path} did not export a valid FormManifest`)
    continue
  }
  const existing = collected.get(m.code)
  if (!existing || m.version > existing.version) {
    collected.set(m.code, m)
  }
}

export const formRegistry: ReadonlyMap<FormCode, FormManifest> = collected

/** Convenience lookup; returns undefined when chef hasn't shipped this flow yet. */
export function lookupForm(code: FormCode): FormManifest | undefined {
  return collected.get(code)
}
