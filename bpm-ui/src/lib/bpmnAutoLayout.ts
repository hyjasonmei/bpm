/**
 * Re-layout a BPMN XML's diagram (DI) with ELK before rendering.
 *
 * Chef-cooked `.bpmn.xml` files carry hand-authored DI coordinates whose
 * quality varies — edges cut through task boxes, labels sit on top of
 * shapes. The semantic part (nodes / sequence flows / ids) is what the
 * case-detail highlighting depends on and is always correct, so instead
 * of trusting the shipped geometry we regenerate the whole
 * `<bpmndi:BPMNDiagram>` section at render time with ELK's layered
 * algorithm (same engine + options as bpm-admin-ui's spec preview, so
 * both sides of the product draw the same picture).
 *
 * Anything this function can't confidently re-layout (pools/lanes,
 * collaborations, multiple processes, parse failures) returns the
 * original XML untouched — the viewer then shows the shipped DI.
 */

const EVENT_SIZE = 36
const GATEWAY_SIZE = 50
const TASK_W = 120
const TASK_H = 80
const LABEL_HEIGHT = 14

// Rough text-width estimate so ELK can reserve label space. CJK glyphs
// render ~14px wide, ASCII ~7px. Min 40px so short labels keep breathing room.
function estimateLabelWidth(s: string): number {
  let w = 0
  for (const ch of s) {
    w += /[一-鿿　-〿＀-￯]/.test(ch) ? 14 : 7
  }
  return Math.max(40, w + 8)
}

const FLOW_NODE_TAGS = new Set([
  'startEvent', 'endEvent', 'intermediateThrowEvent', 'intermediateCatchEvent',
  'exclusiveGateway', 'parallelGateway', 'inclusiveGateway', 'eventBasedGateway',
  'userTask', 'serviceTask', 'sendTask', 'receiveTask', 'scriptTask',
  'businessRuleTask', 'manualTask', 'task', 'callActivity', 'subProcess',
])

function sizeFor(tag: string): { w: number; h: number } {
  if (tag.endsWith('Event')) return { w: EVENT_SIZE, h: EVENT_SIZE }
  if (tag.endsWith('Gateway')) return { w: GATEWAY_SIZE, h: GATEWAY_SIZE }
  return { w: TASK_W, h: TASK_H }
}

/** Events + gateways render their name OUTSIDE the shape in bpmn-js. */
function labelsOutside(tag: string): boolean {
  return tag.endsWith('Event') || tag.endsWith('Gateway')
}

interface ElkPoint { x: number; y: number }
interface ElkLabelOut { x?: number; y?: number; width?: number; height?: number }
interface ElkNodeOut { id: string; x: number; y: number; width: number; height: number; labels?: ElkLabelOut[] }
interface ElkEdgeOut { id: string; sections?: { startPoint: ElkPoint; endPoint: ElkPoint; bendPoints?: ElkPoint[] }[]; labels?: ElkLabelOut[] }

export async function autoLayoutBpmnXml(xml: string): Promise<string> {
  try {
    const doc = new DOMParser().parseFromString(xml, 'application/xml')
    if (doc.querySelector('parsererror')) return xml

    const processes = [...doc.getElementsByTagNameNS('*', 'process')]
    if (processes.length !== 1) return xml
    const proc = processes[0]
    // Pools / lanes need lane-aware layout — out of scope, keep shipped DI.
    if (doc.getElementsByTagNameNS('*', 'collaboration').length > 0) return xml
    if (proc.getElementsByTagNameNS('*', 'laneSet').length > 0) return xml

    const nodes: { id: string; tag: string; name: string }[] = []
    for (const el of [...proc.children]) {
      const tag = el.localName
      if (!FLOW_NODE_TAGS.has(tag)) continue
      const id = el.getAttribute('id')
      if (!id) return xml
      nodes.push({ id, tag, name: el.getAttribute('name') ?? '' })
    }
    const edges: { id: string; source: string; target: string; name: string }[] = []
    for (const el of [...proc.getElementsByTagNameNS('*', 'sequenceFlow')]) {
      const id = el.getAttribute('id')
      const source = el.getAttribute('sourceRef')
      const target = el.getAttribute('targetRef')
      if (!id || !source || !target) return xml
      edges.push({ id, source, target, name: el.getAttribute('name') ?? '' })
    }
    if (nodes.length === 0 || edges.length === 0) return xml
    const nodeIds = new Set(nodes.map(n => n.id))
    if (!edges.every(e => nodeIds.has(e.source) && nodeIds.has(e.target))) return xml

    const { default: ELK } = await import('elkjs/lib/elk.bundled.js')
    const elk = new ELK()
    const layout = (await elk.layout({
      id: 'root',
      layoutOptions: {
        'elk.algorithm': 'layered',
        'elk.direction': 'RIGHT',
        // Default GREEDY cycle breaking may reverse *forward* edges when a
        // flow has send-back loops (e.g. PURCHASE_REQUEST's two 退回 edges),
        // scattering the start event into the middle of the diagram.
        // DEPTH_FIRST walks from the start event, so the happy path always
        // reads left→right and only true back edges get reversed.
        'elk.layered.cycleBreaking.strategy': 'DEPTH_FIRST',
        'elk.layered.spacing.nodeNodeBetweenLayers': '70',
        'elk.spacing.nodeNode': '45',
        'elk.layered.spacing.edgeNodeBetweenLayers': '30',
        'elk.layered.nodePlacement.strategy': 'NETWORK_SIMPLEX',
        'elk.edgeRouting': 'ORTHOGONAL',
        'elk.spacing.edgeNode': '25',
        'elk.spacing.edgeLabel': '8',
        'elk.spacing.labelLabel': '8',
        'elk.spacing.labelNode': '10',
        'elk.spacing.labelEdge': '8',
      },
      children: nodes.map(n => {
        const { w, h } = sizeFor(n.tag)
        const outside = labelsOutside(n.tag)
        return {
          id: n.id,
          width: w,
          height: h,
          // Outside labels are put into the graph so ELK reserves room and
          // hands back coordinates; inside (task) labels need no reservation.
          labels: outside && n.name
            ? [{ text: n.name, width: estimateLabelWidth(n.name), height: LABEL_HEIGHT }]
            : [],
          layoutOptions: outside
            ? { 'elk.nodeLabels.placement': 'OUTSIDE V_BOTTOM H_CENTER' }
            : undefined,
        }
      }),
      edges: edges.map(e => ({
        id: e.id,
        sources: [e.source],
        targets: [e.target],
        labels: e.name
          ? [{ text: e.name, width: estimateLabelWidth(e.name), height: LABEL_HEIGHT }]
          : [],
      })),
    })) as { children?: ElkNodeOut[]; edges?: ElkEdgeOut[] }

    const nodeOut = new Map((layout.children ?? []).map(c => [c.id, c]))
    const edgeOut = new Map((layout.edges ?? []).map(e => [e.id, e]))
    if (nodes.some(n => !nodeOut.get(n.id)) || edges.some(e => !edgeOut.get(e.id)?.sections?.length)) return xml

    const shapeDi = nodes.map(n => {
      const p = nodeOut.get(n.id)!
      const marker = n.tag === 'exclusiveGateway' ? ' isMarkerVisible="true"' : ''
      const label = p.labels?.[0]
      // ELK returns node label coords relative to the node; DI wants absolute.
      const labelDi = (label && label.x != null && label.y != null && label.width != null && label.height != null)
        ? `\n        <bpmndi:BPMNLabel>\n          <dc:Bounds x="${p.x + label.x}" y="${p.y + label.y}" width="${label.width}" height="${label.height}" />\n        </bpmndi:BPMNLabel>`
        : ''
      return `      <bpmndi:BPMNShape id="${n.id}_di" bpmnElement="${n.id}"${marker}>\n        <dc:Bounds x="${p.x}" y="${p.y}" width="${p.width}" height="${p.height}" />${labelDi}\n      </bpmndi:BPMNShape>`
    }).join('\n')

    const edgeDi = edges.map(e => {
      const el = edgeOut.get(e.id)!
      // Concatenate every section's points (multi-section edges appear on
      // reversed/long routes; dropping tail sections is how lines end up
      // slicing through boxes).
      const points: ElkPoint[] = []
      for (const s of el.sections ?? []) {
        for (const p of [s.startPoint, ...(s.bendPoints ?? []), s.endPoint]) {
          const last = points[points.length - 1]
          if (!last || last.x !== p.x || last.y !== p.y) points.push(p)
        }
      }
      const waypoints = points.map(p => `        <di:waypoint x="${p.x}" y="${p.y}" />`).join('\n')
      const label = el.labels?.[0]
      // Edge label coords from ELK are already absolute.
      const labelDi = (label && label.x != null && label.y != null && label.width != null && label.height != null)
        ? `\n        <bpmndi:BPMNLabel>\n          <dc:Bounds x="${label.x}" y="${label.y}" width="${label.width}" height="${label.height}" />\n        </bpmndi:BPMNLabel>`
        : ''
      return `      <bpmndi:BPMNEdge id="${e.id}_di" bpmnElement="${e.id}">\n${waypoints}${labelDi}\n      </bpmndi:BPMNEdge>`
    }).join('\n')

    const procId = proc.getAttribute('id') ?? 'process_1'
    const diagram = `<bpmndi:BPMNDiagram id="diagram_relayout">\n    <bpmndi:BPMNPlane id="plane_relayout" bpmnElement="${procId}">\n${shapeDi}\n${edgeDi}\n    </bpmndi:BPMNPlane>\n  </bpmndi:BPMNDiagram>`

    // Swap the whole DI section in the source string (semantic XML untouched).
    const replaced = xml.replace(/<bpmndi:BPMNDiagram[\s\S]*<\/bpmndi:BPMNDiagram>/, diagram)
    // No DI in the source at all → inject before </bpmn:definitions>.
    if (replaced === xml && !/<bpmndi:BPMNDiagram/.test(xml)) {
      return xml.replace(/<\/bpmn:definitions>/, `  ${diagram}\n</bpmn:definitions>`)
    }
    return replaced
  } catch {
    return xml
  }
}
