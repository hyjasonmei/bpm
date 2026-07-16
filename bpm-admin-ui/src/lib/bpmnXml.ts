import ELK from 'elkjs/lib/elk.bundled.js'
import type { DraftSpec, FlowNode } from './onboarding'

/**
 * Convert DraftSpec.flow to BPMN 2.0 XML for bpmn-js Viewer.
 *
 * Layout is computed by ELK (Eclipse Layout Kernel, layered algorithm) so
 * branches land on separate rows, edges route around obstacles via orthogonal
 * waypoints, and crossings are minimized.
 *
 * Gateway and edge labels are passed to ELK with estimated dimensions so it
 * reserves space for them, and we emit bpmndi:BPMNLabel with the positions
 * ELK chose — otherwise bpmn-js auto-places labels at edge midpoints and they
 * collide with gateway-below labels.
 *
 * Approval nodes use bpmn:userTask (BPMN has no approval). `notify` →
 * bpmn:sendTask. `serviceTask` → bpmn:serviceTask.
 */

const elk = new ELK()

const NODE_SIZE: Record<FlowNode['type'], { w: number; h: number }> = {
  startEvent:  { w: 36,  h: 36 },
  endEvent:    { w: 36,  h: 36 },
  gateway:     { w: 50,  h: 50 },
  userTask:    { w: 100, h: 80 },
  approval:    { w: 100, h: 80 },
  serviceTask: { w: 100, h: 80 },
  notify:      { w: 100, h: 80 },
}

const LABEL_HEIGHT = 14

function bpmnElementName(type: FlowNode['type']): string {
  switch (type) {
    case 'startEvent':  return 'bpmn:startEvent'
    case 'endEvent':    return 'bpmn:endEvent'
    case 'userTask':    return 'bpmn:userTask'
    case 'approval':    return 'bpmn:userTask'
    case 'gateway':     return 'bpmn:exclusiveGateway'
    case 'serviceTask': return 'bpmn:serviceTask'
    case 'notify':      return 'bpmn:sendTask'
  }
}

function escapeXml(s: string): string {
  return s.replace(/&/g, '&amp;').replace(/</g, '&lt;').replace(/>/g, '&gt;').replace(/"/g, '&quot;')
}

// Rough text-width estimate so ELK can reserve label space. Chinese glyphs
// render ~14px wide, ASCII ~7px. Min 40px so very short labels still get
// breathing room.
function estimateLabelWidth(s: string): number {
  let w = 0
  for (const ch of s) {
    w += /[一-龥　-〿＀-￯]/.test(ch) ? 14 : 7
  }
  return Math.max(40, w + 8)
}

interface ElkPort { x: number; y: number }
interface ElkSection { startPoint: ElkPort; endPoint: ElkPort; bendPoints?: ElkPort[] }
interface ElkLabel { x?: number; y?: number; width?: number; height?: number }
interface ElkNode { id: string; x: number; y: number; width: number; height: number; labels?: ElkLabel[] }
interface ElkEdge { id: string; sections?: ElkSection[]; labels?: ElkLabel[] }
interface ElkLayout { children?: ElkNode[]; edges?: ElkEdge[] }

export async function flowToBpmnXml(draft: DraftSpec): Promise<string> {
  const { nodes, edges } = draft.flow

  const elkGraph = {
    id: 'root',
    layoutOptions: {
      'elk.algorithm': 'layered',
      'elk.direction': 'RIGHT',
      // GREEDY (default) cycle breaking may reverse forward edges on flows
      // with send-back loops, putting the start event mid-diagram.
      // DEPTH_FIRST walks from the start, keeping the happy path left→right.
      // (Kept in lockstep with bpm-ui/src/lib/bpmnAutoLayout.ts.)
      'elk.layered.cycleBreaking.strategy': 'DEPTH_FIRST',
      'elk.layered.spacing.nodeNodeBetweenLayers': '70',
      'elk.spacing.nodeNode': '40',
      'elk.layered.spacing.edgeNodeBetweenLayers': '30',
      'elk.layered.nodePlacement.strategy': 'NETWORK_SIMPLEX',
      'elk.edgeRouting': 'ORTHOGONAL',
      'elk.spacing.edgeNode': '25',
      'elk.spacing.edgeLabel': '8',
      'elk.spacing.labelLabel': '8',
      'elk.spacing.labelNode': '8',
      'elk.spacing.labelEdge': '8',
    },
    children: nodes.map(n => ({
      id: n.id,
      width: NODE_SIZE[n.type].w,
      height: NODE_SIZE[n.type].h,
      // Only gateway labels render outside the shape in bpmn-js. Other node
      // labels render inside the box and don't need ELK reservation.
      labels: (n.type === 'gateway' && n.label)
        ? [{ text: n.label, width: estimateLabelWidth(n.label), height: LABEL_HEIGHT }]
        : [],
      // Without an explicit placement ELK leaves gateway labels at (0,0) —
      // drawn on top of the diamond. Reserve space below the shape instead.
      layoutOptions: n.type === 'gateway'
        ? { 'elk.nodeLabels.placement': 'OUTSIDE V_BOTTOM H_CENTER' }
        : undefined,
    })),
    edges: edges.map(e => ({
      id: e.id,
      sources: [e.source],
      targets: [e.target],
      labels: e.label
        ? [{ text: e.label, width: estimateLabelWidth(e.label), height: LABEL_HEIGHT }]
        : [],
    })),
  }

  const layout = (await elk.layout(elkGraph)) as ElkLayout

  const nodePos = new Map<string, ElkNode>()
  for (const c of layout.children ?? []) nodePos.set(c.id, c)

  const edgeLayout = new Map<string, ElkEdge>()
  for (const e of layout.edges ?? []) edgeLayout.set(e.id, e)

  const flowCode = draft.meta.flowCode || 'FLOW'

  const processElements = nodes.map(n => {
    const tag = bpmnElementName(n.type)
    const incoming = edges.filter(e => e.target === n.id).map(e => `      <bpmn:incoming>${e.id}</bpmn:incoming>`).join('\n')
    const outgoing = edges.filter(e => e.source === n.id).map(e => `      <bpmn:outgoing>${e.id}</bpmn:outgoing>`).join('\n')
    const inner = [incoming, outgoing].filter(s => s).join('\n')
    return `    <${tag} id="${n.id}" name="${escapeXml(n.label)}">\n${inner}\n    </${tag}>`
  }).join('\n')

  const sequenceFlows = edges.map(e => {
    const labelAttr = e.label ? ` name="${escapeXml(e.label)}"` : ''
    const condElement = e.condition
      ? `\n      <bpmn:conditionExpression xsi:type="bpmn:tFormalExpression">${escapeXml(e.condition)}</bpmn:conditionExpression>`
      : ''
    return `    <bpmn:sequenceFlow id="${e.id}" sourceRef="${e.source}" targetRef="${e.target}"${labelAttr}>${condElement}\n    </bpmn:sequenceFlow>`
  }).join('\n')

  const shapeDi = nodes.map(n => {
    const p = nodePos.get(n.id)
    if (!p) return ''
    const isMarker = n.type === 'gateway'
    const markerVisible = isMarker ? ' isMarkerVisible="true"' : ''
    const label = p.labels?.[0]
    // ELK returns child label coords relative to the parent node; BPMN DI
    // expects absolute, so translate by the node's origin.
    const labelDi = (label && label.width != null && label.height != null && label.x != null && label.y != null)
      ? `\n        <bpmndi:BPMNLabel>\n          <dc:Bounds x="${p.x + label.x}" y="${p.y + label.y}" width="${label.width}" height="${label.height}" />\n        </bpmndi:BPMNLabel>`
      : ''
    return `      <bpmndi:BPMNShape id="${n.id}_di" bpmnElement="${n.id}"${markerVisible}>\n        <dc:Bounds x="${p.x}" y="${p.y}" width="${p.width}" height="${p.height}" />${labelDi}\n      </bpmndi:BPMNShape>`
  }).filter(s => s).join('\n')

  const edgeDi = edges.map(e => {
    const el = edgeLayout.get(e.id)
    const section = el?.sections?.[0]
    if (!section) return ''
    const points: ElkPort[] = [section.startPoint, ...(section.bendPoints ?? []), section.endPoint]
    const waypoints = points.map(p => `        <di:waypoint x="${p.x}" y="${p.y}" />`).join('\n')
    const label = el?.labels?.[0]
    // Edge labels from ELK are already in absolute graph coords (edges have
    // no parent-local origin in the layered algorithm).
    const labelDi = (label && label.width != null && label.height != null && label.x != null && label.y != null)
      ? `\n        <bpmndi:BPMNLabel>\n          <dc:Bounds x="${label.x}" y="${label.y}" width="${label.width}" height="${label.height}" />\n        </bpmndi:BPMNLabel>`
      : ''
    return `      <bpmndi:BPMNEdge id="${e.id}_di" bpmnElement="${e.id}">\n${waypoints}${labelDi}\n      </bpmndi:BPMNEdge>`
  }).filter(s => s).join('\n')

  return `<?xml version="1.0" encoding="UTF-8"?>
<bpmn:definitions xmlns:bpmn="http://www.omg.org/spec/BPMN/20100524/MODEL"
                  xmlns:bpmndi="http://www.omg.org/spec/BPMN/20100524/DI"
                  xmlns:dc="http://www.omg.org/spec/DD/20100524/DC"
                  xmlns:di="http://www.omg.org/spec/DD/20100524/DI"
                  xmlns:xsi="http://www.w3.org/2001/XMLSchema-instance"
                  id="${flowCode}_def"
                  targetNamespace="https://bpm.acme.example">
  <bpmn:process id="${flowCode}" isExecutable="true">
${processElements}
${sequenceFlows}
  </bpmn:process>
  <bpmndi:BPMNDiagram id="diagram_1">
    <bpmndi:BPMNPlane id="plane_1" bpmnElement="${flowCode}">
${shapeDi}
${edgeDi}
    </bpmndi:BPMNPlane>
  </bpmndi:BPMNDiagram>
</bpmn:definitions>`
}
