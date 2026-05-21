# flowcook-wizard Specification

## Purpose

Define the AI Kitchen eleven-step wizard that customer admins (or flowcook internal) use to capture a flow spec. The wizard is the canonical authoring path for `wizard`-typed flows. Each completed run produces a single spec JSON that is the contract between admin (design-time) and chef + bpm (build/run-time).

## Requirements

### Requirement: Eleven canonical steps in order

The wizard SHALL present exactly the following steps in order. External system dependencies (INTEGRATIONS) come BEFORE the values they imply (VARIABLES) so the latter can show as derived defaults — pure internal flows can leave both empty.

| # | Step | Spec output |
|---|---|---|
| 1 | SOURCE | `meta`, `flow.nodes`, `flow.edges` (with BPMN preview replacing the old STRUCTURE step) |
| 2 | ACCESS | `access.launchableBy[]`, `access.watcher[]`; `triggers[0]` auto-derived from the first user task |
| 3 | FORMS | `userTasks[].formCode`, `userTasks[].fields[]` (including per-field `note` for chef) |
| 4 | DECISIONS | `decisions[].branches[].condition` (CEL) + `isDefault` per branch |
| 5 | APPROVERS | `approvals[].approver` as `ActorRef` (5-type DSL — see APPROVERS requirement) |
| 6 | NOTIFY | `notifications[]` |
| 7 | INTEGRATIONS | `integrations.items[]` (OpenAPI spec, baseUrl, auth) |
| 8 | VARIABLES | `variables[]` (mostly auto-derived from INTEGRATIONS; user MAY add custom) |
| 9 | SLA | `sla.perNode`, escalation |
| 10 | TRANSLATION | `labels[locale]` |
| 11 | NOTES | `notes` |

After step 11 the wizard SHALL present a Submit button (not a separate step) that triggers the `draft → submitted` lifecycle transition defined in `flowcook-lifecycle`.

#### Scenario: Step order is fixed
- **WHEN** an admin opens any wizard run
- **THEN** the eleven steps appear in the order above
- **AND** the Submit button replaces the legacy "GO LIVE" step

### Requirement: Each step has its own validator gate

The wizard SHALL refuse to advance to step N+1 until step N's validator passes. Each step's validator SHALL be deterministic and idempotent. INTEGRATIONS, VARIABLES, SLA, TRANSLATION, NOTES validators SHALL accept the empty state — these steps are routinely skipped on pure internal flows.

#### Scenario: Cannot skip a placeholder step
- **WHEN** the user fills steps 1-2 but leaves step 3 (FORMS) empty
- **THEN** "Next" on step 3 is disabled until at least one userTask field is defined

#### Scenario: Optional steps can be empty
- **WHEN** the user reaches step 7 INTEGRATIONS with no external systems
- **THEN** "Next" remains enabled with an empty `integrations.items[]`
- **AND** the empty-state copy reads "這關通常可空 — 純內部流程不對接外部系統，直接 Next"

### Requirement: Step 2 (ACCESS) auto-derives the trigger; UI only asks about ACCESS

The trigger SHALL be auto-derived from the first user task in the flow (the model "user submits the first form = flow starts"). The wizard SHALL display the derived trigger as a read-only summary card so the admin can confirm it, but SHALL NOT offer an edit control on this step — to change the trigger form the admin reorders nodes in SOURCE (or renames the formCode in FORMS).

The underlying schema SHALL still write `triggers[]` as an array of one form trigger, leaving room for future cron / webhook / mail entries when a power-user UI surfaces them.

#### Scenario: Trigger summary card on ACCESS
- **WHEN** the admin reaches step 2 and the flow has a first user task labelled "員工申請" with formCode `LEAVE_APPLY`
- **THEN** the page shows a read-only card「📋 員工申請  LEAVE_APPLY」+「使用者送這張表單 = 啟動這個流程」
- **AND** `draft.triggers[0]` is `{ id: 'leave-apply', type: 'form', formCode: 'LEAVE_APPLY' }`

#### Scenario: Flow with no user task surfaces a warning
- **WHEN** the admin reaches step 2 but the flow has no `userTask` node
- **THEN** the page shows a warning card「⚠ 流程還沒有送單表單」pointing back to SOURCE
- **AND** the step validator blocks Next

### Requirement: Step 2 (ACCESS) collapses launchableBy / visibleTo into one picker

The step SHALL capture access principals via the shared Principal picker (`flowcook-principal-model`). To keep the customer's mental model simple the UI SHALL show two pickers only:

- **可啟動 / launchableBy[]** — who can submit the trigger form to start a new instance
- **旁觀者 / watcher[]** — optional; who can see other people's running instances

`visibleTo[]` SHALL NOT have its own UI control — the wizard SHALL mirror `launchableBy[]` into `visibleTo[]` on every change ("能啟動的自然看得到"). The schema field is retained so a future power-user UI can split them again without migration.

Each picker uses the shared `PrincipalPicker` modal (USER / DEPT / GROUP / ROLE tabs + search + multi-select buffer + Cancel / Select footer). Stored values are prefixed refs `${kind}:${id}`.

#### Scenario: launchableBy auto-mirrors to visibleTo
- **WHEN** the admin sets `launchableBy = [dept:engineering, user:alice]`
- **THEN** `visibleTo` is silently written to the same list
- **AND** the wizard never asks about visibleTo separately

#### Scenario: Validator requires at least one launchableBy entry
- **WHEN** the admin reaches step 2 with launchableBy empty
- **THEN** Next is disabled with hint「需指定誰可啟動本流程」

### Requirement: Step 3 (FORMS) lays out user tasks with a pill row

FORMS SHALL render a pill row above the editor — one pill per user task node — with status icon (✓ when at least 1 required field, ⚠ otherwise) + label + field count. Clicking a pill activates that task's editor below. Only the active task's editor is mounted at any time.

Each field row SHALL expose: id (snake_case, auto-derived from label), label (zh-TW + optional en), type (text / textarea / number / date / daterange / select / multiselect / file / user_picker / derived), required toggle, optional conditional / validator / derivedFrom CEL expressions (each opens `ExpressionEditorModal` — see DECISIONS requirement), an options sub-modal for select / multiselect types, and an always-visible `note` row (see next requirement).

A 「預覽」button SHALL open `FormPreviewModal` rendering the active task as live HTML inputs (text / textarea / number / date / daterange / select / multiselect / file / user_picker / derived). Conditional fields are rendered with a「⚠ 依條件顯示」note (not hidden); derived fields show the formula inline. Submit is disabled — the preview never sends data.

#### Scenario: Pill row updates with field count
- **WHEN** the admin adds a field to "員工申請" user task
- **THEN** that pill's field count increments
- **AND** the pill switches from ⚠ to ✓ once a required field exists

### Requirement: FormField.note carries free-text chef instructions

Every `FormField` SHALL have a `.note` property (`{ 'zh-TW'?: string; en?: string }`). The note is **free-text guidance for chef** to read when structured props (conditional / validator / options) cannot express a business rule (e.g.「金額很大時主管要 double-check 上季預算」or「同部門連續 3 件以上要 HR review」). End-users SHALL NOT see notes.

The field SHALL render as an always-visible row below the structured props, showing a sticky-note icon + truncated preview (or "點此寫給 chef…" placeholder) + edit affordance. Clicking opens `NoteEditorModal` (textarea + Cancel / Save buffer).

The note field replaces the legacy `hint` field. `migrateDraft` SHALL copy `field.hint` → `field.note` on load so existing drafts keep their text.

#### Scenario: AI chat fills a note
- **WHEN** the customer says in chat「金額很大時主管要 double-check 上一季的預算」
- **AND** there is no structured way to express that rule
- **THEN** the AI MAY call `emit_form_fields` with `fields[*].noteZh` populated
- **AND** the field's NoteRow surfaces the AI-filled text

### Requirement: Step 4 (DECISIONS) uses CEL expressions in a guided modal editor

Gateway rules SHALL be authored in CEL (Common Expression Language). Each branch's `condition` SHALL be edited via `ExpressionEditorModal` — left pane is the inline editor + ✓ / ✗ validation chip (debounced backend call to `/api/specs/validate-expression`); right pane lists clickable example snippets by category (日期 / 時間 / 數字 / 欄位互斥 / 字串 / 變數比對) plus the available form-field ids + flow variable names, plus operator and built-in-function references.

When the flow has 2+ gateways the editor SHALL show a pill row (same pattern as FORMS) to switch between gateways. Exclusive-type gateways SHALL surface the `isDefault` branch with a primary-tinted card + ⭐ Default chip, and sort it first.

Decision type SHALL support three values:
- `exclusive` — pick the first matching branch (most common; exactly one must be `isDefault`)
- `parallel` — fire every outgoing branch in parallel (rare; used for fan-out notifications)
- `inclusive` — fire every matching branch (rarest; usually replaceable with parallel + per-task conditional)

#### Scenario: CEL expression references a variable
- **WHEN** the rule is `amount > ${MAX_AUTO_APPROVE}`
- **THEN** the runtime SHALL substitute `${MAX_AUTO_APPROVE}` with the current variable value before evaluation

#### Scenario: Exclusive gateway without a default is invalid
- **WHEN** the gateway type is `exclusive` and no branch has `isDefault = true`
- **THEN** the gateway card surfaces「⚠ 缺 default branch」
- **AND** the step validator blocks Next

### Requirement: Step 5 (APPROVERS) uses the 5-type ActorRef DSL with natural language as last resort

Each `approval` node SHALL carry one `ActorRef`. The DSL has five types — structured options first, with `natural_language` as the **emergency exit**, not a peer:

1. **`expr`** — dot-path walker from `submitter` / `instance` through the org chart. UI is an `ActorPathBuilderModal`: token-by-token picker (e.g. `submitter` → `.manager` → `.department.head`) that only allows transitions the resolver supports. Whitelist remains the source of truth.
2. **`principal`** — direct reference to a real user / dept / group / role. UI reuses the shared `PrincipalPicker` modal (same four tabs as ACCESS). Stored as `{ type: 'principal', ref: '${kind}:${id}' }`.
3. **`conditional`** — branch on a flow-field comparator (e.g. `if amount >= 100000 then ... else ...`); `then` / `else` are recursive ActorRef.
4. **`collection`** — multi-approver set with mode `any` (first to claim wins, optional `min_approvals`) or `all` (everyone must approve). `actors[]` are recursive ActorRef.
5. **`natural_language`** — `{ type: 'natural_language', text: string }`. **Last resort** when the four structured types cannot express the rule. UI surfaces this as a visually-deprioritised option in the type selector (灰底 + 「最後手段」label + a tooltip recommending the customer try a structured option first). chef sees the text and uses LLM judgement to generate the right resolver code at build time.

Every type SHALL carry an optional `fallback?: { text: string }` — a natural-language description of what to do when the primary rule resolves to no one (e.g. "主管離職時，請 chef 找代理人"). The fallback SHALL NOT be a recursive ActorRef; the previous v1.1 recursive fallback chain is dropped because (a) structured fallback chains are better expressed by extending the primary `conditional`, and (b) reserving fallback for natural-language reinforces the "structured-first, natural-language-as-exit" model.

The wizard SHALL nudge the customer toward structured options:
- The type selector SHALL list expr / principal / conditional / collection above a visual separator and natural_language below it, with "最後手段" framing
- The AI co-pilot's `emit_approver_config` tool SHALL prefer emitting a structured type when the customer's request can plausibly be mapped to one, and SHALL only emit `natural_language` when no mapping is plausible
- Pure `natural_language` actors SHALL be a wizard-quality signal: spec exports with a high `natural_language` count are a hint that the wizard's structured options need expanding, not that the customer is doing something wrong

#### Scenario: 結構化路徑優先
- **WHEN** the customer describes "請主管批"
- **THEN** the AI emits `{ type: 'expr', path: 'submitter.manager' }`, NOT `{ type: 'natural_language', text: '請主管批' }`

#### Scenario: Natural language for truly novel rules
- **WHEN** the customer describes "如果這筆採購跟近一個月內已採購的供應商不同，需 procurement 主管批；否則走主管"
- **AND** the structured `conditional` type cannot express the time-window comparison
- **THEN** the AI MAY emit `{ type: 'natural_language', text: '<rule>' }` so chef handles it at build time

#### Scenario: Fallback for missing approver
- **WHEN** the primary rule is `{ type: 'expr', path: 'submitter.manager' }`
- **AND** `fallback.text = '若主管離職，請 chef 找直屬向上 2 級代理'`
- **THEN** spec stores both fields
- **AND** at chef build time the resolver code includes a catch-all branch derived from the fallback text

#### Scenario: Pill row switches between approval nodes
- **WHEN** the flow has 3 approval nodes
- **THEN** the editor renders a 3-pill row + active editor (same shape as FORMS / DECISIONS)
- **AND** only one approval is mounted at a time

### Requirement: Step 6 (NOTIFY) carries pure-signal channels only

The NOTIFY step SHALL declare email / sms / webhook notifications meant as signals (e.g., "approved → ping #ops Slack"). Structured outbound-data integrations MUST be done in step 7 INTEGRATIONS, not here.

#### Scenario: Trying to use NOTIFY for ERP push
- **WHEN** an admin attempts to use NOTIFY to push line-item data to an ERP system
- **THEN** the design SHALL surface that this belongs to step 7 INTEGRATIONS instead

### Requirement: Step 7 (INTEGRATIONS) takes an OpenAPI spec and is routinely empty

The INTEGRATIONS step SHALL accept the customer's external system as an uploaded OpenAPI (JSON or YAML) spec. The UI SHALL parse the spec, list endpoints, and let the customer choose endpoint(s), flow trigger node(s), field mapping, and auth.

Pure internal flows (請假 / 加班 / 簽核) routinely have no external integration. The empty-state copy SHALL set this expectation ("這關通常可空 — 直接 Next") and the validator SHALL accept zero items.

#### Scenario: OpenAPI with multiple endpoints
- **WHEN** the customer uploads an OpenAPI spec listing 12 endpoints
- **THEN** the UI shows all 12 and lets the customer pick which to invoke from this flow
- **AND** records the choice in `integrations.items[]` with explicit `endpoint.operationId` reference

#### Scenario: Sensitive auth value
- **WHEN** auth requires a bearer token
- **THEN** the token is stored in a secret store
- **AND** the spec carries only a reference (`auth.config_ref = "secret://..."`)

#### Scenario: Pure internal flow skips this step
- **WHEN** the admin reaches step 7 with no external system
- **THEN** the page shows「這關通常可空」+ helper
- **AND** Next is enabled immediately with an empty `integrations.items[]`

### Requirement: Step 8 (VARIABLES) surfaces INTEGRATIONS-derived values plus optional custom

Variables are the values referenced as `${var_name}` in later steps' CEL expressions, validators, and notification templates. The step is placed AFTER INTEGRATIONS because the bulk of variables are *derived* from the integrations the user just configured (BASE_URL, API_KEY refs, header tokens).

The wizard SHALL surface auto-derived variables as a first-class list (read-only by default, editable on click) alongside the customer's manually-declared variables. Each variable SHALL carry `name`, `default_value` (text), optional `description`, and a `sensitive` flag (UI mask + redacted from audit values).

Pure internal flows with no integration usually have no variables; the empty-state copy SHALL read「這關通常可空 — 純內部流程不需要變數」and the validator SHALL accept zero items.

#### Scenario: A sensitive variable masks its value in UI
- **WHEN** a variable has `sensitive: true`
- **THEN** the admin UI SHALL display the value masked (`****`)
- **AND** audit events for changes SHALL record only the variable name, not the value

#### Scenario: INTEGRATIONS push variables on save
- **WHEN** the admin saves an INTEGRATIONS item with `baseUrl = 'https://erp.example.com'`
- **THEN** VARIABLES auto-appears with a `${INTEGRATION_<NAME>_BASE_URL}` entry pre-filled
- **AND** the entry is editable (rename / remove / lock as sensitive) but flagged as "auto from INTEGRATIONS"

### Requirement: Step 10 (TRANSLATION) supports AI fill of empty cells

The TRANSLATION step SHALL list every label (form, button, notification, error) and present a side-by-side table for zh (primary) and en (secondary). One-click AI fill SHALL fill only empty cells, never overwrite filled ones. The schema MUST be a `Record<locale, string>` shape so future N-language extension does not require migration.

#### Scenario: AI fill on a partly-filled table
- **WHEN** 80% of labels already have en translations and the admin clicks "AI fill"
- **THEN** only the empty 20% are filled
- **AND** existing filled values remain untouched

### Requirement: Step 11 (NOTES) is a single free-text textarea

NOTES SHALL be one textarea stored as `spec.notes` (single string). Future enhancement to per-step sticky sidebars is out of scope.

#### Scenario: chef reads NOTES
- **WHEN** chef begins cooking a flow
- **THEN** chef receives `spec.notes` as additional context in its system prompt

### Requirement: Submit button replaces the legacy GO LIVE step

The legacy "GO LIVE" step SHALL NOT exist as a wizard step. Instead the wizard footer SHALL show a Submit button on step 11. Clicking Submit triggers the `draft → submitted` lifecycle transition defined in `flowcook-lifecycle`.

#### Scenario: Submit only enabled when all validators pass
- **WHEN** the user reaches step 11 but earlier steps still fail validation
- **THEN** the Submit button is disabled, with a hint indicating which earlier step needs attention

### Requirement: Test step removed; Sandbox tab handles trial runs

The wizard SHALL NOT include a TEST step. Trial running of a draft / submitted flow SHALL be handled by the Sandbox feature (`flowcook-sandbox`) via the admin Sandbox tab.

#### Scenario: User wants to try a draft flow
- **WHEN** the user finishes step 11 but wants to test before submit
- **THEN** the wizard provides a link to open Sandbox with the current draft pre-loaded
- **AND** Sandbox runs the flow against bpm runtime under sandbox config

### Requirement: Modal shell is shared across editor modals

Every editor modal in the wizard (PrincipalPicker, OptionsEditor, ExpressionEditor, ActorPathBuilder, NoteEditor, FormPreview, future FallbackEditor) SHALL render through the shared `Modal` shell. Defaults:

- Backdrop click SHALL NOT close (avoids accidental buffer loss in editor modals)
- ESC SHALL close (override via `closeOnEsc={false}` for confirm-flow modals)
- Header carries title + close × (hide via `hideClose`)
- Footer slot reserved for Cancel / Save buttons; modals SHALL buffer edits and commit only on Save

#### Scenario: An editor modal survives a misclick on the backdrop
- **WHEN** the admin is editing a CEL expression and accidentally clicks the dimmed area outside the modal
- **THEN** the modal stays open and the buffer is preserved
- **AND** the admin can still close via ESC, × or Cancel

### Requirement: AI co-pilot tools land per step

The left CHAT panel SHALL surface, per step, the Anthropic tool the AI may invoke to mutate the draft directly. As of v1 the per-step tool map is:

| Step | Tool name | Mutation |
|---|---|---|
| 1 SOURCE | `emit_flow_skeleton` | full meta + nodes + edges |
| 2 ACCESS | (advisory only — picker is the writer) | — |
| 3 FORMS | `emit_form_fields` | one user task's full field list |
| 4 DECISIONS | `emit_decision_rules` | full decisions[] |
| 5 APPROVERS | `emit_approver_config` | full approvals[] (5-type ActorRef DSL) |
| 6 NOTIFY | `emit_notifications` | full notifications[] |
| 7 INTEGRATIONS | (planned) | — |
| 8 VARIABLES | (planned) | — |
| 9 SLA | `emit_sla_config` | sla.perNode |
| 10 TRANSLATION | (planned) | — |
| 11 NOTES | (planned — likely advisory) | — |

The chat footer SHALL display the tool name when one is wired ("工具：emit_decision_rules"), so the admin can tell which steps the AI can edit directly vs. which are advisory.

#### Scenario: ACCESS chat is advisory only
- **WHEN** the admin is on step 2 and asks the AI to add Engineering dept
- **THEN** the AI replies with text guidance only
- **AND** the admin uses the right-side PrincipalPicker to actually add the dept

#### Scenario: DECISIONS chat patches the canvas
- **WHEN** the admin is on step 4 and asks "超過 7 天走副總核准 否則 HR 備案 (default)"
- **THEN** the AI calls `emit_decision_rules` with the correct gateway id + edge ids + CEL conditions verbatim from `draftSummary`
- **AND** the canvas updates and `✓ 已套用到右邊 canvas` appears in the chat reply
