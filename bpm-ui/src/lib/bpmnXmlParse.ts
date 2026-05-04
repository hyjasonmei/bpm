import type { FlowNode, FlowEdge } from './onboarding'

/**
 * BPMN 2.0 XML → DraftSpec.flow (plus inferred meta).
 *
 * Pure browser DOMParser — bpmn-js is heavy (~1MB) and we only need to walk
 * a handful of elements. Counterpart to flowToBpmnXml in bpmnXml.ts.
 *
 * Mapping is intentionally lossy: we ignore DI (positions / waypoints) since
 * ELK re-lays out on render, drop pools / lanes, and collapse all gateway
 * subtypes (parallel / inclusive / exclusive) to FlowNode.type='gateway'
 * (the Decisions step refines the type later).
 *
 * approval inference: bpmn:userTask elements get classified as 'approval' if
 * their name contains a known approval keyword (核准/簽/批准/approve), else
 * 'userTask'. We can't tell from BPMN alone — both share the same XML tag.
 */

export interface BpmnParseResult {
  flowName: string
  nodes: FlowNode[]
  edges: FlowEdge[]
  warnings: string[]
}

const APPROVAL_KEYWORDS = /核准|簽核|簽呈|批准|審核|approve|approval|sign[- ]?off/i

const TAG_TO_TYPE: Record<string, FlowNode['type'] | 'task'> = {
  'startevent':       'startEvent',
  'endevent':         'endEvent',
  'usertask':         'task',
  'task':             'task',
  'manualtask':       'task',
  'sendtask':         'notify',
  'servicetask':      'serviceTask',
  'scripttask':       'serviceTask',
  'businessruletask': 'serviceTask',
  'exclusivegateway': 'gateway',
  'parallelgateway':  'gateway',
  'inclusivegateway': 'gateway',
  'eventbasedgateway':'gateway',
}

export function parseBpmnXml(xml: string): BpmnParseResult {
  const warnings: string[] = []
  const doc = new DOMParser().parseFromString(xml, 'application/xml')

  const parseError = doc.querySelector('parsererror')
  if (parseError) {
    throw new Error(`BPMN XML 解析失敗：${parseError.textContent?.trim() ?? 'unknown'}`)
  }

  const processEl = doc.getElementsByTagNameNS('*', 'process')[0]
  if (!processEl) {
    throw new Error('找不到 <bpmn:process>，這個檔案可能不是 BPMN 2.0 XML。')
  }

  const flowName = processEl.getAttribute('name')?.trim()
                 ?? doc.getElementsByTagNameNS('*', 'definitions')[0]?.getAttribute('name')?.trim()
                 ?? ''

  const nodes: FlowNode[] = []
  const seenIds = new Set<string>()

  // Walk every direct child of <process>; classify by local tag name.
  for (const child of Array.from(processEl.children)) {
    const local = child.localName.toLowerCase()
    if (local === 'sequenceflow') continue // edges handled separately

    const mapped = TAG_TO_TYPE[local]
    if (!mapped) continue

    const id = child.getAttribute('id')
    if (!id) {
      warnings.push(`忽略缺少 id 的元素 <${child.localName}>`)
      continue
    }
    if (seenIds.has(id)) {
      warnings.push(`重複的 node id "${id}"，後者已忽略`)
      continue
    }
    seenIds.add(id)

    const label = (child.getAttribute('name') ?? id).trim()

    let type: FlowNode['type']
    if (mapped === 'task') {
      type = APPROVAL_KEYWORDS.test(label) ? 'approval' : 'userTask'
    } else {
      type = mapped
    }

    nodes.push({ id, type, label })
  }

  if (nodes.length === 0) {
    throw new Error('找不到任何節點 (startEvent / userTask / gateway / endEvent…)，請確認檔案內容。')
  }

  const edges: FlowEdge[] = []
  for (const flow of Array.from(processEl.getElementsByTagNameNS('*', 'sequenceFlow'))) {
    const id = flow.getAttribute('id')
    const source = flow.getAttribute('sourceRef')
    const target = flow.getAttribute('targetRef')
    if (!id || !source || !target) {
      warnings.push(`忽略不完整的 sequenceFlow（缺 id / sourceRef / targetRef）`)
      continue
    }
    if (!seenIds.has(source) || !seenIds.has(target)) {
      warnings.push(`sequenceFlow ${id} 指向未知節點，已忽略`)
      continue
    }

    const condEl = flow.getElementsByTagNameNS('*', 'conditionExpression')[0]
    const condition = condEl?.textContent?.trim() || undefined
    const label = flow.getAttribute('name')?.trim() || undefined

    edges.push({ id, source, target, condition, label })
  }

  if (!nodes.some(n => n.type === 'startEvent')) warnings.push('缺少 startEvent — 後續驗證會擋。')
  if (!nodes.some(n => n.type === 'endEvent'))   warnings.push('缺少 endEvent — 後續驗證會擋。')

  return { flowName, nodes, edges, warnings }
}
