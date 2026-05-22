# Tasks

## 1. chef/ docs (✓ landed in Phase C bootstrap commit)

- [x] 1.1 `chef/README.md` — entry doc, MVP vs. v0 service contrast
- [x] 1.2 `chef/skill/SKILL.md` — system-prompt source of truth
- [x] 1.3 `chef/skill/conventions.md` — naming / paths / flag /
       variables / ActorRef / form-layout / tests / commit shape
- [x] 1.4 `chef/skill/workflow.md` — pre-flight, freeze spec,
       worktree, session, generate, report, ship, EF migration

## 2. Claude-loadable skill (✓ landed in Phase C bootstrap commit)

- [x] 2.1 `.claude/skills/chef-codegen/SKILL.md` — frontmatter +
       dispatch into `chef/skill/*`
- [ ] 2.2 Smoke-test the skill loads via `/chef-codegen` in a fresh
       Claude Code session inside the repo

## 3. Bundle contract verification (✓ done during Phase C)

- [x] 3.1 Verified `bpm-admin-svc/Bundle/BundleBuilder.cs` emits:
       `spec.json`, `bpmn.xml`, `spec.md`, `README.md`,
       `walkthrough.md`, `forms/<id>.json`, `notifications/<id>.json`,
       `sla.json`, `actors.json`, `sample-org.json`,
       `test-cases/<id>.json`, optional `CHANGELOG.md`, `manifest.json`.
- [x] 3.2 No `notes/` directory exists — free-text chef instructions
       live inline on spec nodes (`FormField.note`, `NodeSLA.note`,
       `draft.notes`, ActorRef `fallback.text`). Updated `chef/skill/SKILL.md`
       to reflect actual layout instead of relaxing the bundle writer.

## 4. First demo flow (Phase D — separate proposal / commit)

- [ ] 4.1 Author LEAVE in admin → download bundle → unzip
- [ ] 4.2 Create worktree `../bpm-chef-worktrees/LEAVE-v1`
- [ ] 4.3 Start chef session, hand over bundle path
- [ ] 4.4 chef reads skill, restates plan, gets explicit go
- [ ] 4.5 chef generates `bpm-svc/features/LEAVE/V1/**` +
       `bpm-ui/src/features/LEAVE/V1/**` per conventions
- [ ] 4.6 chef runs `dotnet test --filter LEAVE_V1` green
- [ ] 4.7 chef runs `npx tsc -p tsconfig.app.json --noEmit` clean
- [ ] 4.8 chef commits per logical chunk + final report
- [ ] 4.9 Jason diff-reviews in GitKraken, runs migration on main
       checkout post-merge, smoke-tests LEAVE flow end-to-end via
       `/apply/LEAVE` (with `LEAVE_V1` flag on)
- [ ] 4.10 Confirm behaviour matches the hand-coded
       `Reference_LeaveForm.tsx` (visual + happy path)
- [ ] 4.11 If chef hit stop-and-ask items, capture each one as a
       skill follow-up

## 5. Skill maturation (post-demo)

- [ ] 5.1 Each stop-and-ask from §4 becomes either a skill addition
       (if it's a recurring decision) or a spec clarification (if
       it's a one-off ambiguity)
- [ ] 5.2 Re-run §4 on a second flow (HWP — hardware purchase with
       a table repeater) to exercise Tier 2 patterns
- [ ] 5.3 Snapshot the v0.2 skill once the second demo passes

## 6. Bridge to step7 service version

- [ ] 6.1 Open follow-up against `flowcook-step7-chef-v0` confirming
       the skill+conventions stack from this MVP is the system
       prompt the service version will load
- [ ] 6.2 Note any divergences (e.g. on-hold callback shape) so
       step7's spec captures them
