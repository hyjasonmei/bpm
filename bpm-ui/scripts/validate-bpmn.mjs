#!/usr/bin/env node
// Validate every cooked BPMN diagram: each <dc:Bounds> coordinate (x/y/width/
// height) must be a real number. Catches hand-authored cook mistakes such as
// height="/4" (NaN) that slip past tsc — bpm-ui's read-only viewer honours the
// DI bounds, so a NaN coordinate renders the label in the wrong place.
//
// Wired as `prebuild`, so it runs before every `npm run build` — locally AND in
// the chef deploy pipeline (PublishManager runs `npm run build`), failing a
// deploy loudly instead of shipping a broken diagram. Dependency-free (regex
// over the Bounds tags) so it needs no extra install in the cook's gate.
import { readdirSync, readFileSync } from 'node:fs'
import { join } from 'node:path'

const featuresDir = join(import.meta.dirname, '..', 'src', 'features')

const bpmnFiles = readdirSync(featuresDir, { recursive: true })
  .filter((p) => String(p).endsWith('.bpmn.xml'))
  .map((p) => join(featuresDir, String(p)))

const isNumber = (v) => /^-?\d+(\.\d+)?$/.test(v.trim())

let errors = 0
for (const file of bpmnFiles) {
  const xml = readFileSync(file, 'utf8')
  for (const tag of xml.matchAll(/<(?:\w+:)?Bounds\b([^>]*?)\/?>/g)) {
    for (const attr of tag[1].matchAll(/\b(x|y|width|height)\s*=\s*"([^"]*)"/g)) {
      const [, key, value] = attr
      if (!isNumber(value)) {
        const line = xml.slice(0, tag.index).split('\n').length
        console.error(`✗ ${file}:${line}  <Bounds ${key}="${value}"> is not a number`)
        errors++
      }
    }
  }
}

if (errors > 0) {
  console.error(`\nBPMN validation failed: ${errors} malformed Bounds coordinate(s).`)
  process.exit(1)
}
console.log(`BPMN bounds OK (${bpmnFiles.length} diagram${bpmnFiles.length === 1 ? '' : 's'})`)
