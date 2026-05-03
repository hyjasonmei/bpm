import type { DraftSpec, FlowNode } from './onboarding'

/**
 * Convert DraftSpec.flow to BPMN 2.0 XML for bpmn-js Viewer.
 *
 * Phase A: simple horizontal layout — every node placed left-to-right at y=180.
 * Approval nodes use bpmn:userTask (BPMN doesn't have a dedicated approval).
 * `notify` nodes use bpmn:sendTask. `serviceTask` uses bpmn:serviceTask.
 *
 * The DI section is required by bpmn-js or it'll refuse the import.
 */

const NODE_W = 100
const NODE_H = 80
const GATEWAY_SIZE = 50
const X_GAP = 60
const Y = 180

function bpmnElementName(type: FlowNode['type']): string {
  switch (type) {
    case 'startEvent':  return 'bpmn:startEvent'
    case 'endEvent':    return 'bpmn:endEvent'
    case 'userTask':    return 'bpmn:userTask'
    case 'approval':    return 'bpmn:userTask'    // BPMN has no approval; render as userTask
    case 'gateway':     return 'bpmn:exclusiveGateway'
    case 'serviceTask': return 'bpmn:serviceTask'
    case 'notify':      return 'bpmn:sendTask'
  }
}

function nodeWidth(type: FlowNode['type']): number {
  if (type === 'startEvent' || type === 'endEvent') return 36
  if (type === 'gateway') return GATEWAY_SIZE
  return NODE_W
}

function nodeHeight(type: FlowNode['type']): number {
  if (type === 'startEvent' || type === 'endEvent') return 36
  if (type === 'gateway') return GATEWAY_SIZE
  return NODE_H
}

function escapeXml(s: string): string {
  return s.replace(/&/g, '&amp;').replace(/</g, '&lt;').replace(/>/g, '&gt;').replace(/"/g, '&quot;')
}

export function flowToBpmnXml(draft: DraftSpec): string {
  const { nodes, edges } = draft.flow

  // Compute x positions left-to-right by node order, centred vertically.
  const positions = new Map<string, { x: number; y: number; w: number; h: number }>()
  let cursorX = 100
  for (const n of nodes) {
    const w = nodeWidth(n.type)
    const h = nodeHeight(n.type)
    const y = Y + (NODE_H - h) / 2  // vertically centre on the same horizontal lane
    positions.set(n.id, { x: cursorX, y, w, h })
    cursorX += w + X_GAP
  }

  // <bpmn:process> elements
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

  // <bpmndi:BPMNDiagram> — required for bpmn-js to render
  const shapeDi = nodes.map(n => {
    const p = positions.get(n.id)!
    const isMarker = n.type === 'gateway'
    const markerVisible = isMarker ? ' isMarkerVisible="true"' : ''
    return `      <bpmndi:BPMNShape id="${n.id}_di" bpmnElement="${n.id}"${markerVisible}>\n        <dc:Bounds x="${p.x}" y="${p.y}" width="${p.w}" height="${p.h}" />\n      </bpmndi:BPMNShape>`
  }).join('\n')

  const edgeDi = edges.map(e => {
    const s = positions.get(e.source)!
    const t = positions.get(e.target)!
    // Connect right-edge of source to left-edge of target
    const x1 = s.x + s.w
    const y1 = s.y + s.h / 2
    const x2 = t.x
    const y2 = t.y + t.h / 2
    return `      <bpmndi:BPMNEdge id="${e.id}_di" bpmnElement="${e.id}">\n        <di:waypoint x="${x1}" y="${y1}" />\n        <di:waypoint x="${x2}" y="${y2}" />\n      </bpmndi:BPMNEdge>`
  }).join('\n')

  return `<?xml version="1.0" encoding="UTF-8"?>
<bpmn:definitions xmlns:bpmn="http://www.omg.org/spec/BPMN/20100524/MODEL"
                  xmlns:bpmndi="http://www.omg.org/spec/BPMN/20100524/DI"
                  xmlns:dc="http://www.omg.org/spec/DD/20100524/DC"
                  xmlns:di="http://www.omg.org/spec/DD/20100524/DI"
                  xmlns:xsi="http://www.w3.org/2001/XMLSchema-instance"
                  id="${draft.meta.flowCode || 'FLOW'}_def"
                  targetNamespace="https://bpm.acme.example">
  <bpmn:process id="${draft.meta.flowCode || 'FLOW'}" isExecutable="true">
${processElements}
${sequenceFlows}
  </bpmn:process>
  <bpmndi:BPMNDiagram id="diagram_1">
    <bpmndi:BPMNPlane id="plane_1" bpmnElement="${draft.meta.flowCode || 'FLOW'}">
${shapeDi}
${edgeDi}
    </bpmndi:BPMNPlane>
  </bpmndi:BPMNDiagram>
</bpmn:definitions>`
}
