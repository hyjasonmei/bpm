# Claude Code Prompt Template v1

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
- spec.approvals[] → approver resolution logic
- spec.decisions[] → gateway logic
- spec.notifications[] → email / in-app templates emitted on state transitions
- spec.sla.perNode → timeouts and escalation handlers
- spec.integrations → CSV-based IIdentityProvider in Phase A (no MCP yet)
- spec.testCases → integration tests you must generate and run

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
   - Wire into App.tsx switch on screen.code

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

6. **PR description format** (when running gh pr create):
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
- (TODO) v1.1：第 1 次 dogfood 後

---

*Last updated: 2026-05-02*
