#!/usr/bin/env node
// Layout-quality gate for cooked BPMN diagrams.
//
// BpmnView re-layouts every diagram at render time with ELK
// (src/lib/bpmnAutoLayout.ts). This script runs the SAME layout over every
// feature .bpmn.xml and fails when the resulting geometry has:
//   - an edge segment crossing a node box it doesn't connect to
//   - an edge label overlapping a node box
//   - a node (gateway/event) label overlapping another node box
//   - the start event not in the leftmost layer (cycle-breaking gone wrong)
//
// A failure here means the diagram WILL render with overlaps — the fix is in
// the semantic XML (usually edge direction / duplicated flows), not in DI
// coordinates (those are regenerated anyway).
//
// Wired as `prebuild` next to validate-bpmn.mjs, and chef can run it directly:
//   node scripts/check-bpmn-layout.mjs
//
// ⚠ Keep the ELK options in lockstep with src/lib/bpmnAutoLayout.ts.
import { readdirSync, readFileSync } from 'node:fs'
import { join } from 'node:path'
import ELK from 'elkjs/lib/elk.bundled.js'

const elk = new ELK()

const FLOW_TAGS = ['startEvent', 'endEvent', 'intermediateThrowEvent', 'intermediateCatchEvent',
  'exclusiveGateway', 'parallelGateway', 'inclusiveGateway', 'eventBasedGateway',
  'userTask', 'serviceTask', 'sendTask', 'receiveTask', 'scriptTask',
  'businessRuleTask', 'manualTask', 'task', 'callActivity', 'subProcess']

const sizeFor = (tag) => tag.endsWith('Event') ? [36, 36] : tag.endsWith('Gateway') ? [50, 50] : [120, 80]
const outside = (tag) => tag.endsWith('Event') || tag.endsWith('Gateway')
const est = (s) => Math.max(40, [...s].reduce((w, ch) => w + (/[一-鿿　-〿＀-￯]/.test(ch) ? 14 : 7), 0) + 8)

// Same layout options as src/lib/bpmnAutoLayout.ts — see the note there about
// DEPTH_FIRST cycle breaking and OUTSIDE label placement.
const LAYOUT_OPTIONS = {
  'elk.algorithm': 'layered',
  'elk.direction': 'RIGHT',
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
}

function parse(xml) {
  const nodes = []
  for (const tag of FLOW_TAGS) {
    for (const m of xml.matchAll(new RegExp(`<bpmn:${tag}\\s+([^>]*?)/?>`, 'g'))) {
      const id = m[1].match(/id="([^"]*)"/)?.[1]
      const name = m[1].match(/name="([^"]*)"/)?.[1] ?? ''
      if (id) nodes.push({ id, tag, name })
    }
  }
  const edges = []
  for (const m of xml.matchAll(/<bpmn:sequenceFlow\s+([^>]*?)\/?>/g)) {
    edges.push({
      id: m[1].match(/id="([^"]*)"/)?.[1],
      source: m[1].match(/sourceRef="([^"]*)"/)?.[1],
      target: m[1].match(/targetRef="([^"]*)"/)?.[1],
      name: m[1].match(/name="([^"]*)"/)?.[1] ?? '',
    })
  }
  return { nodes, edges }
}

const rectsOverlap = (a, b, pad = 1) =>
  a.x1 < b.x2 - pad && a.x2 > b.x1 + pad && a.y1 < b.y2 - pad && a.y2 > b.y1 + pad

async function checkFile(file) {
  const xml = readFileSync(file, 'utf8')
  // Render-time re-layout skips these shapes and keeps shipped DI — nothing
  // for this gate to assert.
  if (/<bpmn:collaboration|<bpmn:laneSet/.test(xml)) return { skipped: 'pools/lanes' }
  if ((xml.match(/<bpmn:process\b/g) ?? []).length !== 1) return { skipped: 'multi-process' }

  const { nodes, edges } = parse(xml)
  if (!nodes.length || !edges.length) return { skipped: 'no flow nodes' }
  const ids = new Set(nodes.map(n => n.id))
  const badRef = edges.find(e => !ids.has(e.source) || !ids.has(e.target))
  if (badRef) return { issues: [`sequenceFlow ${badRef.id} references unknown node`] }

  const layout = await elk.layout({
    id: 'root',
    layoutOptions: LAYOUT_OPTIONS,
    children: nodes.map(n => {
      const [w, h] = sizeFor(n.tag)
      return {
        id: n.id, width: w, height: h,
        labels: outside(n.tag) && n.name ? [{ text: n.name, width: est(n.name), height: 14 }] : [],
        layoutOptions: outside(n.tag) ? { 'elk.nodeLabels.placement': 'OUTSIDE V_BOTTOM H_CENTER' } : undefined,
      }
    }),
    edges: edges.map(e => ({
      id: e.id, sources: [e.source], targets: [e.target],
      labels: e.name ? [{ text: e.name, width: est(e.name), height: 14 }] : [],
    })),
  })

  const boxes = layout.children.map(c => ({ id: c.id, x1: c.x, y1: c.y, x2: c.x + c.width, y2: c.y + c.height }))
  const issues = []

  const start = nodes.find(n => n.tag === 'startEvent')
  if (start) {
    const sx = boxes.find(b => b.id === start.id).x1
    const minX = Math.min(...boxes.map(b => b.x1))
    if (sx > minX + 1) issues.push(`start event not leftmost (x=${sx}, expected ${minX}) — check edge directions`)
  }

  for (const e of layout.edges ?? []) {
    const eDef = edges.find(d => d.id === e.id)
    for (const s of e.sections ?? []) {
      const pts = [s.startPoint, ...(s.bendPoints ?? []), s.endPoint]
      for (let i = 0; i < pts.length - 1; i++) {
        const seg = {
          x1: Math.min(pts[i].x, pts[i + 1].x), y1: Math.min(pts[i].y, pts[i + 1].y),
          x2: Math.max(pts[i].x, pts[i + 1].x), y2: Math.max(pts[i].y, pts[i + 1].y),
        }
        for (const b of boxes) {
          if (b.id === eDef.source || b.id === eDef.target) continue
          if (rectsOverlap(seg, b, 2)) issues.push(`edge ${e.id} crosses node ${b.id}`)
        }
      }
    }
    for (const l of e.labels ?? []) {
      const lb = { x1: l.x, y1: l.y, x2: l.x + l.width, y2: l.y + l.height }
      for (const b of boxes) if (rectsOverlap(lb, b)) issues.push(`label "${eDef.name}" of ${e.id} overlaps node ${b.id}`)
    }
  }
  for (const c of layout.children) {
    for (const l of c.labels ?? []) {
      if (l.x == null) continue
      const lb = { x1: c.x + l.x, y1: c.y + l.y, x2: c.x + l.x + l.width, y2: c.y + l.y + l.height }
      for (const b of boxes) if (b.id !== c.id && rectsOverlap(lb, b)) issues.push(`label of node ${c.id} overlaps node ${b.id}`)
    }
  }
  return { issues }
}

const featuresDir = join(import.meta.dirname, '..', 'src', 'features')
const files = readdirSync(featuresDir, { recursive: true })
  .filter((p) => String(p).endsWith('.bpmn.xml'))
  .map((p) => join(featuresDir, String(p)))

let failed = 0
for (const file of files) {
  const rel = file.split('/src/features/')[1]
  const r = await checkFile(file)
  if (r.skipped) console.log(`~ ${rel}: skipped (${r.skipped})`)
  else if (r.issues.length === 0) console.log(`✓ ${rel}`)
  else {
    failed++
    for (const i of r.issues) console.error(`✗ ${rel}: ${i}`)
  }
}

if (failed > 0) {
  console.error(`\nBPMN layout check failed: ${failed} diagram(s) render with overlaps. Fix the semantic XML (node/edge structure), not the DI coordinates.`)
  process.exit(1)
}
console.log(`BPMN layout OK (${files.length} diagram${files.length === 1 ? '' : 's'})`)
