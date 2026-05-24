import { useEffect, useRef } from 'react'
import { X } from 'lucide-react'

import type { Step } from '@/lib/workflow'
import type { PersonaCode } from '@/lib/role'
import { ownerLabel } from '@/lib/workflow'
import { Modal } from '@/components/ui/Modal'

interface BpmnViewProps {
  open: boolean
  steps: Step[]
  activeStep: number
  ownerByStep: (PersonaCode | null)[]
  formLabel: string
  onClose: () => void
}

/**
 * Renders the flow's BPMN diagram via `bpmn-js` (the canonical BPMN.io
 * viewer). Until the runtime ships a per-instance `bpmn.xml` snapshot
 * endpoint (`swap-form-bpmn-viewer-to-bpmnjs` follow-up), we synthesize
 * a linear-process XML from the existing `Step[]` shape so the visual
 * upgrade lands now without any backend coupling.
 *
 * Spec-driven gateways / parallel branches will arrive when the snapshot
 * endpoint lands and we feed real `bpmn.xml` here instead.
 */
export function BpmnView({ open, steps, activeStep, ownerByStep, formLabel, onClose }: BpmnViewProps) {
  const containerRef = useRef<HTMLDivElement | null>(null)
  // We instantiate NavigatedViewer dynamically so the ~600 KB chunk lives
  // outside the initial bundle. Users who never open "View BPMN" pay nothing.
  const viewerRef = useRef<{ destroy: () => void; importXML: (xml: string) => Promise<unknown>; get<T>(name: string): T } | null>(null)
  const titleId = 'bpmn-view-title'

  useEffect(() => {
    if (!open) return
    let cancelled = false

    void (async () => {
      const mod = await import('bpmn-js/lib/NavigatedViewer')
      if (cancelled || !containerRef.current) return

      const NavigatedViewer = mod.default
      const viewer = new NavigatedViewer({
        container: containerRef.current,
      }) as unknown as { destroy: () => void; importXML: (xml: string) => Promise<unknown>; get<T>(name: string): T }
      viewerRef.current = viewer

      try {
        await viewer.importXML(buildBpmnXml(steps, ownerByStep))
        const canvas = viewer.get<{ zoom: (value: string) => void; addMarker: (id: string, marker: string) => void }>('canvas')
        canvas.zoom('fit-viewport')
        // Markers for done / active nodes — styled by .bpm-* rules in index.css.
        steps.forEach((step, i) => {
          if (i < activeStep) canvas.addMarker(nodeId(step.id), 'bpm-completed')
          if (i === activeStep) canvas.addMarker(nodeId(step.id), 'bpm-active')
        })
      } catch {
        // BPMN parse / render failure — fall back to nothing; the modal still closes.
      }
    })()

    return () => {
      cancelled = true
      viewerRef.current?.destroy()
      viewerRef.current = null
    }
  }, [open, steps, activeStep, ownerByStep])

  return (
    <Modal open={open} onClose={onClose} ariaLabelledBy={titleId} panelClassName="max-w-[95vw]">
      <div className="flex items-center justify-between border-b border-rule px-5 py-3">
        <div>
          <h3 id={titleId} className="text-sm font-bold text-ink">BPMN diagram — {formLabel}</h3>
          <p className="text-xs text-ink-muted">流程圖檢視 · rendered with bpmn-js (canonical BPMN.io viewer)</p>
        </div>
        <button onClick={onClose} className="rounded p-1 text-slate-400 hover:bg-slate-100 hover:text-slate-700" aria-label="Close">
          <X className="h-4 w-4" />
        </button>
      </div>

      <div ref={containerRef} className="h-[70vh] w-[80vw] bg-white" />
    </Modal>
  )
}

// ────────────────────────────────────────────────────────────────────────────
// Linear BPMN XML synthesizer — interim while the runtime snapshot is wired
// ────────────────────────────────────────────────────────────────────────────

const SPACING = 160
const NODE_WIDTH = 130
const NODE_HEIGHT = 80
const START_X = 60
const NODE_Y = 80

function nodeId(stepId: string): string {
  return `Task_${stepId}`
}

function buildBpmnXml(steps: Step[], ownerByStep: (PersonaCode | null)[]): string {
  const taskRefs = steps.map(s => nodeId(s.id))
  const startId = 'StartEvent_1'
  const endId = 'EndEvent_1'
  const allNodes = [startId, ...taskRefs, endId]

  const flows = allNodes.slice(0, -1).map((from, i) => ({
    id: `Flow_${i + 1}`,
    from,
    to: allNodes[i + 1],
  }))

  const taskXml = steps
    .map((s, i) => {
      const owner = ownerByStep[i]
      const label = escapeXml(`${s.en}${s.zh ? ` / ${s.zh}` : ''}${owner ? ` (${ownerLabel(owner)})` : ''}`)
      return `    <bpmn:userTask id="${nodeId(s.id)}" name="${label}" />`
    })
    .join('\n')

  const flowXml = flows
    .map(f => `    <bpmn:sequenceFlow id="${f.id}" sourceRef="${f.from}" targetRef="${f.to}" />`)
    .join('\n')

  const incomingByNode = new Map<string, string[]>()
  const outgoingByNode = new Map<string, string[]>()
  for (const f of flows) {
    incomingByNode.set(f.to, [...(incomingByNode.get(f.to) ?? []), f.id])
    outgoingByNode.set(f.from, [...(outgoingByNode.get(f.from) ?? []), f.id])
  }

  // Diagram shape definitions — bpmn-js requires this section for rendering.
  const shapeXml = allNodes
    .map((id, i) => {
      const isStartOrEnd = id === startId || id === endId
      const w = isStartOrEnd ? 36 : NODE_WIDTH
      const h = isStartOrEnd ? 36 : NODE_HEIGHT
      const x = START_X + i * SPACING + (isStartOrEnd ? (NODE_WIDTH - 36) / 2 : 0)
      const y = NODE_Y + (isStartOrEnd ? (NODE_HEIGHT - 36) / 2 : 0)
      return `      <bpmndi:BPMNShape id="${id}_di" bpmnElement="${id}">
        <dc:Bounds x="${x}" y="${y}" width="${w}" height="${h}" />
      </bpmndi:BPMNShape>`
    })
    .join('\n')

  const edgeXml = flows
    .map(f => {
      const fromIdx = allNodes.indexOf(f.from)
      const toIdx = allNodes.indexOf(f.to)
      const fromIsEvent = f.from === startId
      const toIsEvent = f.to === endId
      const x1 = START_X + fromIdx * SPACING + (fromIsEvent ? 36 : NODE_WIDTH)
      const y1 = NODE_Y + NODE_HEIGHT / 2
      const x2 = START_X + toIdx * SPACING + (toIsEvent ? 0 : 0)
      const y2 = NODE_Y + NODE_HEIGHT / 2
      return `      <bpmndi:BPMNEdge id="${f.id}_di" bpmnElement="${f.id}">
        <di:waypoint x="${x1}" y="${y1}" />
        <di:waypoint x="${x2}" y="${y2}" />
      </bpmndi:BPMNEdge>`
    })
    .join('\n')

  return `<?xml version="1.0" encoding="UTF-8"?>
<bpmn:definitions xmlns:bpmn="http://www.omg.org/spec/BPMN/20100524/MODEL"
                  xmlns:bpmndi="http://www.omg.org/spec/BPMN/20100524/DI"
                  xmlns:dc="http://www.omg.org/spec/DD/20100524/DC"
                  xmlns:di="http://www.omg.org/spec/DD/20100524/DI"
                  id="Definitions_1"
                  targetNamespace="http://bpm.example.com">
  <bpmn:process id="Process_1" isExecutable="false">
    <bpmn:startEvent id="${startId}" name="Start" />
${taskXml}
    <bpmn:endEvent id="${endId}" name="End" />
${flowXml}
  </bpmn:process>
  <bpmndi:BPMNDiagram id="BPMNDiagram_1">
    <bpmndi:BPMNPlane id="BPMNPlane_1" bpmnElement="Process_1">
${shapeXml}
${edgeXml}
    </bpmndi:BPMNPlane>
  </bpmndi:BPMNDiagram>
</bpmn:definitions>`
}

function escapeXml(s: string): string {
  return s
    .replace(/&/g, '&amp;')
    .replace(/</g, '&lt;')
    .replace(/>/g, '&gt;')
    .replace(/"/g, '&quot;')
}
