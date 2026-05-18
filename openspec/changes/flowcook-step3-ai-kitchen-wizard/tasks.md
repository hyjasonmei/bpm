# Tasks

## 1. Lifecycle backend in `bpm-admin-svc`

- [ ] 1.1 `FlowDraft` + `FlowVersion` entities with `lineage_id` / `version` / `state`
- [ ] 1.2 Migration + EF config
- [ ] 1.3 `IFlowLifecycleService` enforcing state transitions per `flowcook-lifecycle`
- [ ] 1.4 Audit event on every transition

## 2. Lifecycle API

- [ ] 2.1 `GET /api/flows` list + filter by state
- [ ] 2.2 `GET /api/flows/{id}` detail with spec JSON
- [ ] 2.3 `POST /api/flows/{id}/submit`
- [ ] 2.4 `POST /api/flows/{id}/cancel`
- [ ] 2.5 `POST /api/flows/{id}/resume` (for `on hold`)
- [ ] 2.6 `POST /api/flows/{id}/clone-version` (creates new draft from approved)
- [ ] 2.7 chef-facing on-hold callback `POST /api/flows/{id}/on-hold` (auth via shared secret) — used in Step 7

## 3. Wizard shell in `bpm-admin-ui`

- [ ] 3.1 11-step stepper component with progress + validation state
- [ ] 3.2 Auto-save draft to admin DB on every step transition
- [ ] 3.3 Per-step validator gate disabling Next
- [ ] 3.4 Final Submit button transitioning `draft → submitted`

## 4. Step 1 — SOURCE

- [ ] 4.1 Source picker (preset / upload / scratch)
- [ ] 4.2 LEAVE preset wired with full nodes / edges
- [ ] 4.3 BPMN preview panel (replaces legacy STRUCTURE step)

## 5. Step 2 — TRIGGER & ACCESS

- [ ] 5.1 Single form trigger config (form template reference)
- [ ] 5.2 `launchable_by` / `visible_to` principal pickers
- [ ] 5.3 Optional `watcher` field
- [ ] 5.4 Schema written to `triggers[]` array (single entry in v0)

## 6. Step 3 — VARIABLES

- [ ] 6.1 Variable list editor with `name / default / description / sensitive`
- [ ] 6.2 Sensitive mask in UI
- [ ] 6.3 Schema written to `variables[]`
- [ ] 6.4 Per-variable audit on add / edit / remove

## 7. Step 4 — FORMS

- [ ] 7.1 userTask field editor (text / number / date / select / etc.)
- [ ] 7.2 Live preview pane
- [ ] 7.3 Conditional / validator / derivedFrom expression fields support `${var}`

## 8. Step 5 — DECISIONS

- [ ] 8.1 Gateway rule editor with Cel expression input
- [ ] 8.2 `${var}` autocomplete
- [ ] 8.3 Test-expression button

## 9. Step 6 — APPROVERS

- [ ] 9.1 Principal picker (user / dept / group, integrated with User & Role API)
- [ ] 9.2 Role selector for the principal
- [ ] 9.3 `inherit_to_members` checkbox
- [ ] 9.4 Multi-approval-node support (one per spec)

## 10. Step 7 — NOTIFY

- [ ] 10.1 Channel selector (email / sms / webhook)
- [ ] 10.2 Recipient field with `${var}` support
- [ ] 10.3 Template editor with placeholder syntax

## 11. Step 8 — INTEGRATIONS

- [ ] 11.1 OpenAPI spec upload / paste
- [ ] 11.2 Endpoint chooser
- [ ] 11.3 Trigger-node selector
- [ ] 11.4 Field mapping editor
- [ ] 11.5 Auth config with sensitive masking

## 12. Step 9 — SLA

- [ ] 12.1 Per-node duration + escalation chain editor
- [ ] 12.2 `${var}` support for duration constants

## 13. Step 10 — TRANSLATION

- [ ] 13.1 Aggregate all labels from spec into table
- [ ] 13.2 zh / en columns (Record<locale, string>)
- [ ] 13.3 "AI fill empties" button calling Anthropic API
- [ ] 13.4 Manual override of any cell

## 14. Step 11 — NOTES

- [ ] 14.1 Single textarea
- [ ] 14.2 Saved to `spec.notes`

## 15. Cross-cutting: Sandbox cross-link

- [ ] 15.1 "Test in Sandbox" link on each step opens Sandbox page with draft id
- [ ] 15.2 Sandbox page reads draft id from URL (does nothing yet)

## 16. Milestone A demo

- [ ] 16.1 Walk LEAVE preset through all 11 steps end-to-end
- [ ] 16.2 Submit and confirm `state=submitted` on `GET /api/flows/{id}`
- [ ] 16.3 Inspect produced spec JSON for completeness
