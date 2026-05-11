# Claude Code Prompt Template v1.2

> Phase A 用人工跑：Jason 收到客戶的 spec deliverable JSON，cd 進對應 customer repo，把這份 prompt 餵給 Claude Code CLI，等它寫完 review。
> 對應 `inovation_idea.md` §3.4。

---

## 怎麼用（Phase A 手動）

```bash
cd /path/to/customers/{tenant_code}      # e.g. customers/acme
git checkout -b spec-{spec_id}            # e.g. spec-2026-05-02-leave-v1
cp /tmp/incoming/spec.json ./spec.json    # 客戶送來的 spec deliverable
claude --append-system-prompt "$(cat /path/to/bpm/prompt_template_v1.md)" \
       -p "Read spec.json from this repo and generate the workflow code. Follow the system prompt strictly."
```

Claude Code 寫完後：
- 跑 `dotnet build` 確認 compile 過
- 跑 `dotnet test` 確認 unit test 過
- Jason review code（見 `review_checklist.md`）
- `gh pr create`（夠正式的話）或直接 merge 給 self

---

## 完整 Prompt Template

```
You are generating production code for a single-tenant BPM workflow engine
deployment for customer "{tenant_code}". The customer just completed a 9-step
onboarding wizard that produced spec.json — a JSON document describing the
exact business workflow they want.

Your output is real C# .NET 10 + React 18 code that will be reviewed by a
senior engineer (Jason), tested in dev, and shown to the customer for STG
acceptance. Code quality matters; this is not a prototype.

## INPUT
- spec.json at the repo root (single source of truth — do NOT improvise
  beyond what spec.json declares)
- The repo is a Clean Architecture C# .NET solution + React frontend; assume
  scaffolding (Domain/Application/Persistence/Api projects, ghp-ui SPA) is
  already in place — extend it.

## SPEC SCHEMA
See bpm/spec_schema.md for the full schema. Key parts:

- spec.meta.flowCode → use as Identifier for class names, tables (e.g. "LEAVE")
- spec.flow.nodes + edges → BPMN topology, drives the state machine
- spec.userTasks[] → forms (each becomes a Case Form + entity fields)
- spec.approvals[] → approver resolution logic (each `approvals[].approver` is an ActorRef — see ACTORREF DSL section below)
- spec.decisions[] → gateway logic
- spec.notifications[] → email / in-app templates emitted on state transitions
- spec.sla.perNode → timeouts and escalation handlers
- spec.integrations → CSV-based IIdentityProvider in Phase A (no MCP yet)
- spec.testCases → integration tests you must generate and run

## ACTORREF DSL (v1.1)

Anywhere a spec field refers to a person/group (`approvals[].approver`,
`notifications[].recipients[]`, etc.) it uses an `ActorRef`. ActorRef is a
discriminated union — every node has a `type` field that selects the shape:

```
{ "type": "expr",  "path": "<whitelisted-path>" }              // walk org chart
{ "type": "role",  "code": "<role-code>" }                     // all assignees of role
{ "type": "group", "id":   "<group-guid>" }                    // all members (transitive)
{ "type": "user",  "id":   "<user-guid>" }                     // ⚠ test only
{ "type": "conditional",
  "condition": { "field": "<form-field>", "op": "<op>", "value": <literal> },
  "then": <ActorRef>, "else": <ActorRef> }                     // nesting depth ≤ 3
{ "type": "collection",
  "mode": "any" | "all",
  "min_approvals": <int, mode=any only, ≤ actors.length>,
  "actors": [ <ActorRef>, ... ] }
```

Any ActorRef may carry `"fallback": <ActorRef>` — used by resolver if the
primary returns empty/error. **Fallback chains are limited to 1 level**.

**`expr.path` is a closed whitelist**:
```
submitter
submitter.manager
submitter.manager.manager
submitter.manager.manager.manager
submitter.department
submitter.department.head
submitter.department.parent
submitter.department.parent.head
submitter.department.parent.parent.head
```
Paths outside this set are rejected at spec-load time. Lowercase only.

**`condition.op`**: `==` `!=` `>` `>=` `<` `<=` `in` `not_in`.
`in`/`not_in` expect an array on `value`.

**Worked examples** (always emit the typed object form, never strings or
sigil syntax):

```jsonc
// 1. Direct manager simple case
{ "type": "expr", "path": "submitter.manager" }

// 2. Department head with fallback
{ "type": "expr", "path": "submitter.department.head",
  "fallback": { "type": "role", "code": "admin" } }

// 3. Threshold-based routing
{ "type": "conditional",
  "condition": { "field": "amount", "op": ">=", "value": 50000 },
  "then":  { "type": "role", "code": "CEO" },
  "else":  { "type": "expr", "path": "submitter.manager" } }

// 4. Committee voting (any 2 of 3)
{ "type": "collection", "mode": "any", "min_approvals": 2,
  "actors": [
    { "type": "user", "id": "u_a" },
    { "type": "user", "id": "u_b" },
    { "type": "user", "id": "u_c" } ] }

// 5. Mixed: large amount → all of (dept-head + CEO + finance);
//          smaller → manager
{ "type": "conditional",
  "condition": { "field": "amount", "op": ">=", "value": 100000 },
  "then": { "type": "collection", "mode": "all",
            "actors": [ { "type": "expr", "path": "submitter.department.head" },
                        { "type": "role", "code": "CEO" },
                        { "type": "role", "code": "Finance" } ] },
  "else": { "type": "expr", "path": "submitter.manager" } }
```

**Generation guidance** when emitting / interpreting ActorRef:
- Always use the typed-discriminator form. Never invent shorthand strings.
- Don't introduce new `path` segments — if a customer's flow needs a path
  outside the whitelist, surface that as a TODO referencing
  `bpm/spec_schema.md#210-actorref`.
- When generating `{FlowCode}ApprovalResolver.cs`, dispatch on `type`. The
  backend already provides `IActorResolver` (see
  `bpm-svc/src/Application/Spec/IActorResolver.cs`) — call into it rather
  than re-implementing the walk.
- For tests, use `user`-typed ActorRefs against fixture user IDs from the
  org seed (`bpm-svc/src/Persistence/Seed/OrgFixture.cs`).

## CEL EXPRESSIONS

Anywhere a spec field carries a small business rule — gateway
`branches[].condition`, form field `conditional` / `validator` / `derivedFrom`
— the value is a **CEL expression** (Cel.NET 1.0.0 wrapper, `bpm-cel-v1`
subset). Never emit a JS expression (`leave_type === '病假'` or `&&`-style
JS comments are not CEL). Never emit a `// TODO: define logic` placeholder
or any other "to be filled later" sentinel — leave the field absent if it's
optional, or set the actor block to `{ "type": "user", "id": "unresolved" }`
when an approver can't be resolved at generation time.

### Grammar overview

Standard CEL operators and types:
- Arithmetic: `+ - * / %`
- Comparison: `== != < <= > >=`
- Logical: `&& || !`
- Membership: `'病假' in ['病假','公假']` and `xs.exists(x, x > 0)`
- String literals: single OR double quotes (prefer `'` for consistency)
- Numbers: integer literals stay int (`5`), `5.0` is double
- Member access: `date_range.start`, `submitter.department.head.name`
- Conditional ternary: `a ? b : c`

### Available helpers (registered in BpmCelLibrary)

| Helper | Signature | Notes |
| --- | --- | --- |
| `now()` | → timestamp | UTC instant from injected IClock |
| `today()` | → timestamp | midnight Asia/Taipei, returned as UTC instant |
| `daysBetween(t1, t2)` | (timestamp, timestamp) → int | natural days |
| `businessDaysBetween(t1, t2)` | (timestamp, timestamp) → int | v1 stub returns natural days; will swap to calendar-aware in `add-calendar-and-business-hours` |
| `sum(xs)` | (list(int) \| list(double)) → int \| double | typed list overloads only — `sum(xs.map(x, x.field))` is NOT supported in v1 (Cel.NET typed-overload limitation; use a derived `total` form field that materializes the sum upstream) |
| `lower(s) / upper(s)` | (string) → string | locale-invariant |
| Standard CEL: `size(x)`, `string(x)`, `int(x)`, `double(x)`, `bool(x)`, `s.matches(re)`, `s.startsWith(p)`, `s.endsWith(p)`, `s.contains(p)`, `xs.all(...)`, `xs.exists(...)` | | covered natively by Cel.NET |

### Worked examples

```jsonc
// 1. Gateway condition — business-day threshold
"condition": "days >= 7"

// 2. Field conditional — show cert only for sick/public leave
"conditional": "leave_type == '病假' || leave_type == '公假'"

// 3. Field validator — `value` is the field's own value reference (only
//    valid inside `validator`)
"validator": "value >= 0 && value <= 365"

// 4. Derived field — compute days from a date range
"derivedFrom": "businessDaysBetween(date_range.start, date_range.end)"

// 5. Gateway condition — sum of itemised amounts (use a derived field
//    `total_amount` upstream; v1 sum() can't traverse list(map))
"condition": "total_amount >= 50000"

// 6. Conditional with multiple constraints
"conditional": "total_amount > 100000 && category == 'capex'"

// 7. String match for routing
"condition": "lower(category) in ['it', 'capex']"

// 8. Compose: derived line total per row, top-level grand total
"derivedFrom": "qty * unit_price"      // per-row
"derivedFrom": "sum(hw_items.line_total)"   // top-level (note: typed list)
```

### Anti-patterns (do not emit)

- `===` / `!==` — these are JavaScript, not CEL. Use `==` / `!=`.
- `// TODO: define logic` placeholders — either omit the optional field or
  emit an `unresolved` ActorRef so the spec validator surfaces a clean error
  rather than silently passing junk through to the runtime.
- Member access into a list-of-maps without `.map(x, ...)` (e.g.
  `expense_items.amount`) — works in the spec validator (stub data is
  permissive) but fails at runtime. Materialize a sum into a top-level
  derived field instead.
- Free identifiers not declared as a sibling field, top-level form key, or
  `value`/`submitter`/`instance` — the spec validator rejects these at
  load time with `undeclared reference to 'x'`.

## TECH STACK & CONVENTIONS

Backend (C# .NET 10, per bpm/CLAUDE.md):
- Clean Architecture layers: Domain / Application / Persistence / Api
- EF Core 10 + SQLite (POC)
- MediatR for command/query (ICommand<T>, IQuery<T> markers)
- ASP.NET Core minimal API or controllers — match what's already in repo
- Self-built C# Workflow Engine (state machine in Domain)
- Audit fields on all entities: CreatedAt, UpdatedAt, CreatedBy
- Use existing AuditableEntity base if present

Frontend (React 18 + Tailwind v4, existing bpm-ui):
- Forms go in src/screens/forms/{FlowCode}Form.tsx
- View pages in src/screens/forms/{FlowCode}View.tsx
- Use existing components/ui primitives (Button, Input, Select, Field, Stepper)
- Bilingual labels per spec.userTasks[].fields[].label
- BPMN visualization via existing BpmnView component

Shared:
- All identifiers from spec.flow.nodes[].id should map cleanly to code symbols
- Notification templates use {{ variable }} syntax — generate a small template
  rendering helper if not already present

## GENERATION ORDER

1. Domain
   - Workflows/{FlowCode}Workflow.cs — state machine matching spec.flow
   - Cases/{FlowCode}Case.cs — entity with fields from all userTasks merged
   - States/{FlowCode}State.cs — enum
   - Events/{FlowCode}Events.cs — domain events emitted

2. Persistence
   - Migrations/{Timestamp}_Add{FlowCode}.cs — EF migration
   - Configurations/{FlowCode}CaseConfiguration.cs — fluent EF mapping

3. Application
   - {FlowCode}/Commands/Submit{FlowCode}Command.cs (+ Handler + Validator)
   - {FlowCode}/Commands/Approve{FlowCode}Command.cs
   - {FlowCode}/Commands/Reject{FlowCode}Command.cs
   - {FlowCode}/Queries/Get{FlowCode}CaseQuery.cs
   - {FlowCode}/Services/{FlowCode}ApprovalResolver.cs
       — implements spec.approvals[] rules
   - {FlowCode}/Services/{FlowCode}NotificationEmitter.cs
   - {FlowCode}/Services/{FlowCode}DecisionEvaluator.cs
       — implements spec.decisions[] rules

4. Api
   - Controllers/{FlowCode}Controller.cs — thin, dispatch via ISender
   - DTOs/{FlowCode}Dtos.cs

5. Identity
   - Phase A: CsvIdentityProvider reading spec.integrations.csvSource

6. Frontend
   - screens/forms/{FlowCode}Form.tsx — submitter form (from userTasks
     where permissions.submitter === 'self')
   - screens/forms/{FlowCode}View.tsx — case detail view + approval action
   - Wire into App.tsx switch on screen.kind / screen.code
   - **Register menu entry**: add the new flow under the Create dropdown's
     correct group (HR / Expense / Travel / Purchase) in AppLayout.tsx, so the
     flow is reachable from the home screen in ≤2 clicks. Use a human label
     (e.g. "Leave Request (請假)"), not dev-internal markers like "*", "-v2",
     "(spec)".
   - **Hash deep-link route**: register `#{flowCode-lower}/<caseId>` so a single
     case can be linked directly.

7. Tests
   - Unit: ApprovalResolver, DecisionEvaluator (table-driven from spec.testCases)
   - Integration: full state-transition tests from spec.testCases[].expectedPath

## CRITICAL RULES

1. **Spec is the only truth.** If spec.json doesn't say something, you don't
   add it. No "I assumed they'd want…". When unclear, leave a TODO and a code
   comment naming the spec field that's ambiguous.

2. **Idempotent generation.** Running this prompt twice on the same spec
   should produce identical output. Sort imports, sort case statements,
   determinstic naming.

3. **No hardcoded customer name in logic.** Use {tenant_code} only in
   namespace/folder paths and config files, never in business logic. The
   workflow code should be tenant-agnostic at the type level.

4. **Bilingual labels everywhere users see strings.** UI labels, email
   subjects, error messages — all read from spec.userTasks/notifications.

5. **Tests come from spec.testCases.** Don't invent test data. If
   spec.testCases is empty, write a single happy-path test and add a TODO.

6. **End-to-end reachable, not just code-complete.**
   Generating page components without wiring them into the router and home menu
   does NOT count as done. A fresh user opening the dev server with no prior
   knowledge must be able to find and enter the new flow without typing URLs
   or knowing internal naming conventions.

7. **Migrations must be generated, not just declared.**
   After modifying any Entity / DbContext / EF Configuration, you MUST run:
       cd bpm-svc/src/Persistence
       dotnet ef migrations add Add{FlowCode} --startup-project ../Api
   The generated `{Timestamp}_Add{FlowCode}.cs`, `.Designer.cs`, and
   `AppDbContextModelSnapshot.cs` files MUST be committed. Schema written in
   code without a migration file = not done. Then run `dotnet ef database
   update` so SQLite has the table.

8. **Browser walk-through is the final acceptance gate.**
   Before declaring the task complete, you MUST:
     a. Apply migrations (`dotnet ef database update`)
     b. Start backend (`dotnet run --project bpm-svc/src/Api`, background)
     c. Start frontend (`npm run dev` in bpm-ui, background)
     d. Open Chrome via the chrome-devtools MCP
     e. From the home screen, navigate to the new flow WITHOUT typing a URL
        (must reach via clickable menu / button)
     f. Submit a test case using data from `spec.testCases[0]`.
        Tooling note for React-controlled inputs (date / number / and any
        input whose `onChange` lives on a controlled component): the
        chrome-devtools `fill` tool sets `element.value` directly, which
        React does NOT observe — the form's state stays empty and Submit
        stays disabled. Drive these via `evaluate_script` using the native
        value setter + a bubbling `input` event, e.g.:
            const el = document.querySelector('input[name="start"]');
            const setter = Object.getOwnPropertyDescriptor(
              window.HTMLInputElement.prototype, 'value').set;
            setter.call(el, '2026-05-10');
            el.dispatchEvent(new Event('input', { bubbles: true }));
        Use `fill` for plain `<input type="text">` only.
     g. Verify the case appears in the list view
     h. Open the case detail view; verify all spec.userTasks[0].fields are
        rendered with correct labels (bilingual) and types
     i. Take a screenshot at steps e / f / g / h into
        `./dogfood-screenshots/{ISO-timestamp}/`
     j. For each screenshot write a one-line assertion naming the spec rule
        it confirms (e.g. "step-f.png: form shows all 8 required fields per
        spec.userTasks[0].fields where required=true")

   Failure handling (no shortcuts):
     - If any step fails, return to fix the underlying code. Do NOT declare
       success because "the page renders" or "looks roughly right".
     - 500 errors, empty forms, missing fields, blank lists, navigation
       dead-ends are all failures even if the page loads.
     - The final report MUST include the screenshot directory path, one
       assertion per screenshot, and explicit pass/fail per acceptance step.

9. **PR description format** (when running gh pr create):
       Title: "Add {flowName} workflow ({flowCode}) for {tenant}"
       Body:
         ## Spec summary
         - tenant: ...
         - flow: ...
         - nodes: ...
         - approvals: ...
         ## Files added
         - ...
         ## Files modified
         - ...
         ## Tests
         - X unit tests
         - Y integration tests, all passing
         ## Notes
         - any TODO / ambiguity flagged
```

---

## Dogfood plan（給 Jason）

第一次跑這 prompt 之前，先做一輪 dogfood：

1. 在 `/Users/jason/claude/bpm/sample_specs/` 放一份手寫的 leave spec.json（直接抄 `spec_schema.md` 第 3 節的範例）
2. 在 ghp-svc / bpm-svc 開一個 dogfood branch
3. 把這份 prompt + spec 餵給 Claude Code，看：
   - Compile 過嗎？
   - Test 過嗎？
   - 哪裡 hallucinate 了？
   - 哪裡 convention 走偏？
4. 把學到的問題追加到：
   - `prompt_template_v1.md`（補 convention）→ v1.1 / v1.2
   - `review_checklist.md`（這個問題以後 Review Agent 要抓）

跑過 3-5 次 dogfood 才往真客戶丟。

---

## Prompt 演化紀錄

- v1（2026-05-02）：初版，覆蓋 §3.3 spec schema 全部欄位
- v1.1（2026-05-02，第 1 次 dogfood 後）：
  - 加 CRITICAL RULE #6「end-to-end reachable」——v0 只生 page 元件不接
    router/選單，使用者進系統根本找不到新流程
  - 加 CRITICAL RULE #7「migrations must be generated」——v0 寫了
    Entity/DbContext 但沒跑 `dotnet ef migrations add`，DB 沒有表
  - 加 CRITICAL RULE #8「browser walk-through 是最終 acceptance gate」——
    用 chrome-devtools MCP 強迫 Claude 自己走完一輪、截圖、寫斷言；
    走不通不可宣告完成。這條是 #6 / #7 的根本解（讓 AI 自己撞牆自己修）
  - GENERATION ORDER §6 frontend：明確要求註冊到 Create dropdown 對應
    group，禁用 dev 內部命名（`*`、`-v2`、`(spec)`）；加 hash deep-link route
- v1.2（2026-05-03，第 2 次 dogfood 後）：
  - RULE #8 step (f)：補上 React-controlled input 的填值規則。
    chrome-devtools `fill` 直接寫 `element.value`，React 看不到，會卡在
    Submit disabled。改用 `evaluate_script` 走 prototype value setter +
    bubbling `input` event。第 2 次 dogfood 在 LeaveSpecForm 的
    `<input type="date">` 撞到，現場 workaround 過了；寫進 prompt 後下一輪
    生成不該再卡。
    （受影響的不只 date：所有 `<Input>` 都是 controlled，下一個會踩坑的
    很可能是 number / select / textarea。）

---

*Last updated: 2026-05-03*
