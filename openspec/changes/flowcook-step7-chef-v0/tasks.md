# Tasks

## 1. Service skeleton

- [ ] 1.1 `chef/` Node project (or .NET, pending Jason confirmation)
- [ ] 1.2 Claude Code SDK runner integration
- [ ] 1.3 Config schema (per-customer admin endpoint + shared secret)
- [ ] 1.4 CI

## 2. Skill files

- [ ] 2.1 `chef/skill/skill.md` — system prompt per `openspec/specs/flowcook-chef` §5
- [ ] 2.2 `chef/skill/naming.md` — naming examples
- [ ] 2.3 `chef/skill/forbidden-paths.md` — explicit allowed / forbidden paths
- [ ] 2.4 Skill linter: ensures every chef invocation cites skill version

## 3. Queue puller

- [ ] 3.1 admin exposes `GET /api/flows/next?customer=&worker_id=` returning the next `submitted` flow and atomically transitioning to `cooking`
- [ ] 3.2 chef polls per-customer (concurrent customers, serial within)
- [ ] 3.3 Lock release / re-queue if chef worker dies

## 4. Cooking loop

- [ ] 4.1 Build prompt: spec + skill + spec.notes history
- [ ] 4.2 Invoke Claude Code SDK
- [ ] 4.3 Capture file deltas (writes / modifies only inside allowed paths)
- [ ] 4.4 Validate deltas against forbidden paths
- [ ] 4.5 On invalid → on-hold callback with error explanation
- [ ] 4.6 On valid → package bundle + tests

## 5. On-hold callback

- [ ] 5.1 POST to `bpm-admin-svc /api/flows/{id}/on-hold` with question payload
- [ ] 5.2 Stop work for this flow
- [ ] 5.3 Audit event from chef side

## 6. Output bundle handoff

- [ ] 6.1 Bundle format: per `flowcook-chef` §3.5
- [ ] 6.2 Push bundle to syncer
- [ ] 6.3 On syncer ack → transition flow `cooking → committed` via admin API

## 7. Resume handling

- [ ] 7.1 When chef picks a flow that was previously on-hold, re-read full `spec.notes`
- [ ] 7.2 Skill prompts include the on-hold context

## 8. End-to-end Milestone B demo

- [ ] 8.1 Walk LEAVE preset through 11 steps → submit
- [ ] 8.2 chef picks up, cooks, pushes bundle via syncer
- [ ] 8.3 bpm runs LEAVE case end-to-end with sandbox mode
- [ ] 8.4 Audit appears on admin Audit page
- [ ] 8.5 Mark cutover ✓; legacy bpm-svc admin / bpm-admin-ui Process Admin Console removed
