import { useEffect, useRef, useState } from 'react'
import BpmnModeler from 'bpmn-js/lib/Modeler'
import 'bpmn-js/dist/assets/diagram-js.css'
import 'bpmn-js/dist/assets/bpmn-font/css/bpmn.css'
import 'bpmn-js/dist/assets/bpmn-js.css'

import type { DraftSpec, FlowNode, FlowEdge } from '@/lib/onboarding'
import { flowToBpmnXml } from '@/lib/bpmnXml'

interface Props {
  draft: DraftSpec
  onChange: (draft: DraftSpec) => void
  height?: number
  /**
   * Optional selection sink. Fires whenever the user clicks a shape on the
   * canvas; the first selected element's id is reported (or null when the
   * selection is cleared / a multi-select). PR-K2 §3.2 — the Designer's
   * right pane uses this to swap in a per-node editor.
   */
  onSelect?: (nodeId: string | null) => void
  /**
   * If supplied (and changed), sets the modeler's current selection to
   * the matching element. Bridges the left-pane node tree → canvas
   * direction so clicking a row in the tree highlights the shape too.
   */
  selectedId?: string | null
}

interface BpmnElement {
  id: string
  type?: string
  businessObject?: { $type?: string; name?: string; sourceRef?: { id?: string }; targetRef?: { id?: string } }
  source?: { id?: string }
  target?: { id?: string }
  waypoints?: unknown
}

interface BpmnRegistry {
  filter: (fn: (el: BpmnElement) => boolean) => BpmnElement[]
}

interface BpmnEventBus {
  on: (event: string, fn: (e?: { newSelection?: BpmnElement[] }) => void) => void
}

interface BpmnSelectionService {
  select: (element: BpmnElement | BpmnElement[] | null) => void
  get: () => BpmnElement[]
}

/**
 * Converts a bpmn-js business object type to our FlowNode["type"]. We can't
 * tell userTask vs approval from bpmn alone (BPMN has no "approval"), so the
 * caller preserves the original type when the node id already exists in the
 * draft.
 */
function bpmnTypeToFlowType(t: string | undefined): FlowNode['type'] | null {
  switch (t) {
    case 'bpmn:StartEvent':       return 'startEvent'
    case 'bpmn:EndEvent':         return 'endEvent'
    case 'bpmn:UserTask':         return 'userTask'
    case 'bpmn:Task':             return 'userTask'
    case 'bpmn:ExclusiveGateway': return 'gateway'
    case 'bpmn:ParallelGateway':  return 'gateway'
    case 'bpmn:InclusiveGateway': return 'gateway'
    case 'bpmn:ServiceTask':      return 'serviceTask'
    case 'bpmn:SendTask':         return 'notify'
    default:                      return null
  }
}

/**
 * Editable bpmn-js Modeler. Renders the draft, lets the user drag / add /
 * delete / reconnect nodes, and syncs structural changes back to the parent
 * via onChange. Layout (positions, edge waypoints) lives only in the modeler
 * — the next time DraftSpec re-renders from outside the editor (eg preset
 * reload), ELK relayouts from scratch.
 *
 * Sync loop guard: lastEmittedRef holds the JSON we just bubbled up. When the
 * incoming draft prop equals that snapshot, we skip the re-import (which
 * would otherwise wipe the user's mid-edit modeler state on every keystroke).
 */
export function BpmnEditor({ draft, onChange, height = 420, onSelect, selectedId }: Props) {
  const containerRef = useRef<HTMLDivElement>(null)
  const modelerRef = useRef<BpmnModeler | null>(null)
  const lastEmittedRef = useRef<string>('')
  const draftRef = useRef(draft)
  const onSelectRef = useRef(onSelect)
  const [error, setError] = useState<string | null>(null)

  useEffect(() => { draftRef.current = draft }, [draft])
  // We keep onSelect in a ref so the one-time-mount eventBus subscription
  // always invokes the latest callback without re-registering on every
  // parent re-render (which would also tear down the modeler).
  useEffect(() => { onSelectRef.current = onSelect }, [onSelect])

  // One-time mount: spin up modeler + listen for changes.
  useEffect(() => {
    if (!containerRef.current) return

    const modeler = new BpmnModeler({ container: containerRef.current })
    modelerRef.current = modeler
    if (import.meta.env.DEV) {
      // eslint-disable-next-line @typescript-eslint/no-explicit-any
      ;(window as unknown as { __bpmnModeler: BpmnModeler }).__bpmnModeler = modeler
    }

    const eventBus = modeler.get<BpmnEventBus>('eventBus')
    let timer: ReturnType<typeof setTimeout> | null = null
    eventBus.on('commandStack.changed', () => {
      if (timer) clearTimeout(timer)
      // Debounce so a multi-step interaction (drag end + label rename) only
      // syncs once.
      timer = setTimeout(() => syncToDraft(modeler), 80)
    })

    // PR-K2 §3.2 — bridge bpmn-js selection events out to the parent.
    // bpmn-js' selection.changed payload is `{ oldSelection, newSelection }`
    // where each side is an array of elements. We surface only the first
    // selected element's id (or null for empty / multi-select), which
    // matches the right pane's "one shape at a time" model.
    eventBus.on('selection.changed', (e) => {
      const cb = onSelectRef.current
      if (!cb) return
      const sel = e?.newSelection ?? []
      // Filter out the synthetic root / process shapes — selecting the
      // canvas background otherwise emits id="Process_1" which confuses
      // the right-pane router.
      const first = sel.find(el => {
        const t = el.businessObject?.$type
        return t !== undefined && t !== 'bpmn:Process' && t !== 'bpmn:Definitions' && !el.id?.endsWith('_label')
      })
      cb(first?.id ?? null)
    })

    return () => {
      if (timer) clearTimeout(timer)
      modeler.destroy()
      modelerRef.current = null
    }
  }, [])

  // Re-import whenever the draft from above changes — except when it's our
  // own emission echoing back through React state.
  useEffect(() => {
    const modeler = modelerRef.current
    if (!modeler || draft.flow.nodes.length === 0) return

    const incomingKey = JSON.stringify({
      nodes: draft.flow.nodes,
      edges: draft.flow.edges,
    })
    if (incomingKey === lastEmittedRef.current) return

    let cancelled = false
    flowToBpmnXml(draft).then(xml => {
      if (cancelled) return
      return modeler.importXML(xml)
    }).then(() => {
      if (cancelled) return
      const canvas = modeler.get<{ zoom: (mode: string | number, center?: 'auto') => void }>('canvas')
      try { canvas.zoom('fit-viewport', 'auto') } catch { /* ignore */ }
      lastEmittedRef.current = incomingKey
      setError(null)
    }).catch((e: unknown) => {
      if (cancelled) return
      setError(e instanceof Error ? e.message : String(e))
    })
    return () => { cancelled = true }
  }, [draft])

  // Tree → canvas: when a parent passes a selectedId we don't already have
  // in the modeler's current selection, push it into bpmn-js. We guard the
  // assignment with the live selection so the click-on-canvas path doesn't
  // bounce its own event back through React → modeler → no-op.
  useEffect(() => {
    const modeler = modelerRef.current
    if (!modeler || selectedId === undefined) return
    let selection: BpmnSelectionService
    let registry: BpmnRegistry
    try {
      selection = modeler.get<BpmnSelectionService>('selection')
      registry = modeler.get<BpmnRegistry>('elementRegistry')
    } catch {
      return
    }
    const current = selection.get()
    const currentId = current[0]?.id ?? null
    if (currentId === selectedId) return
    if (selectedId === null) {
      selection.select(null)
      return
    }
    const target = registry.filter(el => el.id === selectedId)[0]
    if (target) selection.select(target)
  }, [selectedId])

  function syncToDraft(modeler: BpmnModeler) {
    const registry = modeler.get<BpmnRegistry>('elementRegistry')
    const all = registry.filter(() => true)

    const shapes = all.filter(el => {
      const t = el.businessObject?.$type
      return t !== undefined && t !== 'bpmn:Process' && t !== 'bpmn:Definitions' && !el.waypoints && !el.id?.endsWith('_label')
    })
    const connections = all.filter(el => Array.isArray(el.waypoints))

    const currentDraft = draftRef.current
    const oldNodesById = new Map(currentDraft.flow.nodes.map(n => [n.id, n]))

    const newNodes: FlowNode[] = []
    for (const s of shapes) {
      const bpmnType = s.businessObject?.$type
      const fromBpmn = bpmnTypeToFlowType(bpmnType)
      if (!fromBpmn) continue
      const existing = oldNodesById.get(s.id)
      newNodes.push({
        id: s.id,
        // Preserve approval distinction across round-trips when id is unchanged.
        type: existing?.type ?? fromBpmn,
        label: s.businessObject?.name ?? existing?.label ?? s.id,
      })
    }

    const oldEdgesById = new Map(currentDraft.flow.edges.map(e => [e.id, e]))
    const newEdges: FlowEdge[] = []
    for (const c of connections) {
      const sourceId = c.source?.id ?? c.businessObject?.sourceRef?.id
      const targetId = c.target?.id ?? c.businessObject?.targetRef?.id
      if (!sourceId || !targetId) continue
      const existing = oldEdgesById.get(c.id)
      newEdges.push({
        id: c.id,
        source: sourceId,
        target: targetId,
        condition: existing?.condition,
        isDefault: existing?.isDefault,
        label: c.businessObject?.name ?? existing?.label,
      })
    }

    // Only emit when something structural actually changed — otherwise every
    // mouse drag would dirty the draft.
    const changed = JSON.stringify(newNodes) !== JSON.stringify(currentDraft.flow.nodes)
      || JSON.stringify(newEdges) !== JSON.stringify(currentDraft.flow.edges)
    if (!changed) return

    const next: DraftSpec = {
      ...currentDraft,
      flow: { nodes: newNodes, edges: newEdges },
    }
    lastEmittedRef.current = JSON.stringify({ nodes: newNodes, edges: newEdges })
    onChange(next)
  }

  if (draft.flow.nodes.length === 0) {
    return (
      <div className="rounded border border-dashed border-rule p-10 text-center text-sm text-ink-faint" style={{ height }}>
        尚未從 SOURCE step 載入流程。回上一步、選範本或上傳。
      </div>
    )
  }

  return (
    <div className="relative">
      {error && (
        <div className="absolute left-2 top-2 z-10 rounded border border-amber-300 bg-amber-50 px-2 py-1 text-[11px] text-amber-800">
          BPMN render error: {error}
        </div>
      )}
      <div
        ref={containerRef}
        className="w-full rounded border border-rule bg-card"
        style={{ height }}
      />
    </div>
  )
}
