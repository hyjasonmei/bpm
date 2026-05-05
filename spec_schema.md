# Spec Deliverable JSON Schema v1

> 客戶完成 9 step onboarding 後，前端匯出的 JSON 結構。
> 是 Claude Code 唯一的 input source of truth——禁止 Claude Code 從 chat history 推測。
> 對應 `inovation_idea.md` §3.3。

---

## 1. 整體結構

```typescript
type SpecDeliverable = {
  meta: SpecMeta
  flow: FlowGraph              // BPMN 拓撲（Step 1-2 產出）
  userTasks: UserTask[]        // 表單（Step 3 產出）
  decisions: Decision[]        // gateway 規則（Step 4 產出）
  approvals: Approval[]        // 審核者規則（Step 5 產出）
  notifications: Notification[]  // 通知（Step 6 產出）
  sla: SLA                     // 時限（Step 7 產出）
  integrations: Integrations   // MCP / CSV 設定
  testCases: TestCase[]        // 測試案（Step 8 產出）
}
```

---

## 2. 各部分 schema

### 2.1 meta

```typescript
type SpecMeta = {
  schemaVersion: '1.0'
  tenant: string                // e.g. "acme"
  flowName: string              // e.g. "請假"
  flowCode: string              // e.g. "LEAVE"（Identifier，用於 class / table 命名）
  flowVersion: number           // 1, 2, 3... 客戶改流程時 +1
  createdAt: string             // ISO datetime
  createdBy: string             // 客戶 onboarding 用戶 id
  language: 'zh-TW' | 'en' | ...
}
```

### 2.2 flow（BPMN 拓撲）

```typescript
type FlowGraph = {
  nodes: FlowNode[]
  edges: FlowEdge[]
}

type FlowNode = {
  id: string                    // 'start_1', 'task_apply', 'gateway_amount', ...
  type: 'startEvent' | 'endEvent'
       | 'userTask'             // 對應 userTasks[id]
       | 'approval'             // 對應 approvals[id]
       | 'gateway'              // 對應 decisions[id]
       | 'serviceTask'          // 對應 integrations[id]
       | 'notify'               // 對應 notifications[id]
  label: string                 // 顯示用，'員工申請'、'主管核准'
  refId?: string                // userTasks/approvals/...裡對應的 id（同 id 也行）
}

type FlowEdge = {
  id: string
  source: string                // FlowNode.id
  target: string                // FlowNode.id
  condition?: string            // gateway 的 branch 條件，譬如 "amount >= 50000"
  label?: string                // '通過'、'退回'
}
```

### 2.3 userTasks（表單）

```typescript
type UserTask = {
  id: string                    // 對應 FlowNode.id
  formCode: string              // 'LEAVE_APPLY'，前端會用這 code 找 component
  fields: FormField[]
  permissions: {
    submitter: 'self' | 'role:HR' | 'group:applicant'
    viewers: ('self' | 'manager' | 'role:HR' | 'all')[]
  }
}

type FormField = {
  id: string                    // 'leave_type', 'date_range', 'reason'
  label: { 'zh-TW': string; 'en'?: string }
  type: 'text' | 'textarea' | 'number' | 'date' | 'daterange'
      | 'select' | 'multiselect' | 'file' | 'user_picker'
      | 'derived'               // computed by formula
  options?: { value: string; label: string }[]   // for select
  required: boolean
  conditional?: string          // JS-like expression: "leave_type === '病假'"
  validator?: string            // JS-like: "value > 0 && value <= 30"
  default?: any
  hint?: { 'zh-TW': string; 'en'?: string }
  derivedFrom?: string          // for type=derived: "days * dailySalary"
}
```

### 2.4 decisions（gateway 規則）

```typescript
type Decision = {
  id: string                    // 對應 FlowNode.id
  type: 'exclusive' | 'parallel' | 'inclusive'
  branches: DecisionBranch[]
}

type DecisionBranch = {
  edgeId: string                // 對應 FlowEdge.id
  condition: string             // "amount >= 50000"
  isDefault?: boolean
}
```

### 2.5 approvals（審核者規則）

> **v1.1 變更**：`ApprovalRule` 已下線，改用統一的 `ActorRef`（見 §2.10）。
> ActorRef 用 typed discriminated union 表達 expr / role / group / user / conditional / collection，
> 既能取代原本所有 ApprovalRule 變體，又可以複用到 notifications / userTasks 等場景。

```typescript
type Approval = {
  id: string                    // 對應 FlowNode.id
  approver: ActorRef            // 取代原本的 rule + fallback + requiresAll
                                // - 多人簽用 collection (mode=any/all + min_approvals)
                                // - 找不到人 fallback 寫在 ActorRef.fallback（限 1 層）
                                // - 條件式分流用 conditional
}
```

舊 `ApprovalRule` → `ActorRef` 對照（migration cheat-sheet）：

| 舊 ApprovalRule | 新 ActorRef |
|---|---|
| `{type:"direct_manager"}` | `{type:"expr",path:"submitter.manager"}` |
| `{type:"role",role:"VP"}` | `{type:"role",code:"VP"}` |
| `{type:"specific_user",userId:"u_x"}` | `{type:"user",id:"u_x"}` |
| `{type:"department_head",deptOf:"applicant"}` | `{type:"expr",path:"submitter.department.head"}` |
| `{type:"amount_threshold",field:"amount",thresholds:[...]}` | `{type:"conditional",condition:{field:"amount",op:">=",value:50000},then:...,else:...}` |
| `{type:"composite",all:[a,b,c]}` | `{type:"collection",mode:"all",actors:[a,b,c]}` |
| `requiresAll: true` (多人) | `{type:"collection",mode:"all",actors:[...]}` |
| `requiresAll: false` (任一) | `{type:"collection",mode:"any",min_approvals:1,actors:[...]}` |
| `fallback: rule_b` | `{ ...primary..., fallback: rule_b }` |

### 2.6 notifications

```typescript
type Notification = {
  id: string                    // 對應 FlowNode.id（如果是 notify node）或關聯到 approval
  trigger: 'on_submit' | 'on_approve' | 'on_reject' | 'on_complete'
        | 'on_assign' | 'on_sla_breach'
  channel: ('email' | 'teams' | 'in_app')[]
  recipients: NotifyRecipient[]
  template: NotifyTemplate
}

type NotifyRecipient =
  | { type: 'submitter' }                          // 仍保留 — 等同 expr "submitter"
  | { type: 'current_approver' }                   // 特殊語意，由 runtime 決定
  | ActorRef                                       // role / group / user / conditional / collection

type NotifyTemplate = {
  subject: { 'zh-TW': string; 'en'?: string }
  body: { 'zh-TW': string; 'en'?: string }
  variables: string[]           // 'applicant.name', 'leave.startDate', ...
}
```

### 2.7 sla

```typescript
type SLA = {
  perNode: { [nodeId: string]: NodeSLA }
}

type NodeSLA = {
  duration: string              // '8h', '24h', '3d'
  businessHoursOnly?: boolean
  escalation?: {
    after: string               // '8h' / '50%'
    action: 'notify' | 'reassign' | 'escalate_one_level' | 'auto_approve' | 'auto_reject'
    target?: ApprovalRule       // 給 escalate_one_level / reassign 用
  }
}
```

### 2.8 integrations

```typescript
type Integrations = {
  identityProvider: 'mcp:entra' | 'csv'
  csvSource?: { url: string }    // Phase A 用 CSV upload 後存的 URL
  mcpConfig?: {
    endpoint: string             // 客戶內網 MCP server URL
    authToken: string            // store securely
  }
  fieldMappings: {
    employeeId: string           // 'sAMAccountName'
    displayName: string          // 'displayName'
    email: string                // 'mail'
    reportsTo: string            // 'manager'
    department: string           // 'department'
    title: string                // 'title'
  }
}
```

### 2.9 testCases

```typescript
type TestCase = {
  id: string
  name: string                   // '5 天特休、直屬主管核准'
  inputs: Record<string, any>    // {leave_type:'特休', start:'2026-05-10', end:'2026-05-12'}
  expectedPath: string[]         // ['start_1','task_apply','approval_manager','task_hr_archive','end_1']
  expectedApprovers: { nodeId: string; userIds: string[] }[]
  expectedNotifications: { trigger: string; recipientCount: number }[]
}
```

---

## 3. 完整範例：請假流程

```json
{
  "meta": {
    "schemaVersion": "1.0",
    "tenant": "acme",
    "flowName": "請假",
    "flowCode": "LEAVE",
    "flowVersion": 1,
    "createdAt": "2026-05-02T03:30:00Z",
    "createdBy": "u_mary",
    "language": "zh-TW"
  },
  "flow": {
    "nodes": [
      {"id":"start_1","type":"startEvent","label":"開始"},
      {"id":"task_apply","type":"userTask","label":"員工申請"},
      {"id":"approval_manager","type":"approval","label":"主管核准"},
      {"id":"gateway_days","type":"gateway","label":"超過 7 天？"},
      {"id":"approval_vp","type":"approval","label":"副總核准"},
      {"id":"task_hr_archive","type":"userTask","label":"HR 備案"},
      {"id":"end_1","type":"endEvent","label":"完成"}
    ],
    "edges": [
      {"id":"e1","source":"start_1","target":"task_apply"},
      {"id":"e2","source":"task_apply","target":"approval_manager"},
      {"id":"e3","source":"approval_manager","target":"gateway_days"},
      {"id":"e4","source":"gateway_days","target":"approval_vp","condition":"days >= 7"},
      {"id":"e5","source":"gateway_days","target":"task_hr_archive","condition":"days < 7","isDefault":true},
      {"id":"e6","source":"approval_vp","target":"task_hr_archive"},
      {"id":"e7","source":"task_hr_archive","target":"end_1"}
    ]
  },
  "userTasks": [
    {
      "id": "task_apply",
      "formCode": "LEAVE_APPLY",
      "fields": [
        {"id":"leave_type","label":{"zh-TW":"假別"},"type":"select","required":true,
         "options":[{"value":"特休","label":"特休"},{"value":"病假","label":"病假"},{"value":"事假","label":"事假"},{"value":"公假","label":"公假"}]},
        {"id":"date_range","label":{"zh-TW":"起訖時間"},"type":"daterange","required":true},
        {"id":"days","label":{"zh-TW":"天數"},"type":"derived","required":false,
         "derivedFrom":"businessDaysBetween(date_range.start, date_range.end)"},
        {"id":"reason","label":{"zh-TW":"事由","en":"Reason"},"type":"textarea","required":true,
         "hint":{"zh-TW":"中英文皆可"}},
        {"id":"cert","label":{"zh-TW":"證明文件"},"type":"file","required":true,
         "conditional":"leave_type === '病假' || leave_type === '公假'"}
      ],
      "permissions": {
        "submitter": "self",
        "viewers": ["self","manager","role:HR"]
      }
    },
    {
      "id": "task_hr_archive",
      "formCode": "LEAVE_ARCHIVE",
      "fields": [
        {"id":"archive_note","label":{"zh-TW":"備案備註"},"type":"textarea","required":true,"hint":{"zh-TW":"HR 留下處理紀錄供日後追溯"}}
      ],
      "permissions": {
        "submitter": "role:HR",
        "viewers": ["role:HR","self"]
      }
    }
  ],
  "decisions": [
    {
      "id": "gateway_days",
      "type": "exclusive",
      "branches": [
        {"edgeId":"e4","condition":"days >= 7"},
        {"edgeId":"e5","condition":"days < 7","isDefault":true}
      ]
    }
  ],
  "approvals": [
    {
      "id": "approval_manager",
      "approver": {"type":"expr","path":"submitter.manager"}
    },
    {
      "id": "approval_vp",
      "approver": {
        "type":"expr","path":"submitter.department.head",
        "fallback": {"type":"role","code":"VP"}
      }
    }
  ],
  "notifications": [
    {
      "id":"notify_assign_manager",
      "trigger":"on_assign",
      "channel":["email","in_app"],
      "recipients":[{"type":"current_approver"}],
      "template":{
        "subject":{"zh-TW":"【請假待簽】{{applicant.name}} 申請 {{leave.days}} 天 {{leave.type}}"},
        "body":{"zh-TW":"申請人: {{applicant.name}}\n假別: {{leave.type}}\n期間: {{leave.start}} - {{leave.end}}\n事由: {{leave.reason}}\n\n請點此核准: {{caseUrl}}"},
        "variables":["applicant.name","leave.days","leave.type","leave.start","leave.end","leave.reason","caseUrl"]
      }
    },
    {
      "id":"notify_complete",
      "trigger":"on_complete",
      "channel":["email"],
      "recipients":[{"type":"submitter"}],
      "template":{
        "subject":{"zh-TW":"您的請假已備案"},
        "body":{"zh-TW":"您於 {{submitDate}} 申請的 {{leave.days}} 天 {{leave.type}} 已完成備案。"},
        "variables":["submitDate","leave.days","leave.type"]
      }
    }
  ],
  "sla": {
    "perNode": {
      "approval_manager": {
        "duration": "8h",
        "businessHoursOnly": true,
        "escalation": {"after":"8h","action":"notify"}
      },
      "approval_vp": {
        "duration": "24h",
        "businessHoursOnly": true,
        "escalation": {"after":"24h","action":"notify"}
      }
    }
  },
  "integrations": {
    "identityProvider": "csv",
    "csvSource": {"url":"s3://bpm-tenants/acme/employees-2026-05-02.csv"},
    "fieldMappings": {
      "employeeId":"empId","displayName":"name","email":"email",
      "reportsTo":"manager","department":"department","title":"title"
    }
  },
  "testCases": [
    {
      "id":"tc_1",
      "name":"5 天特休、直屬主管核准",
      "inputs":{"leave_type":"特休","date_range":{"start":"2026-05-10","end":"2026-05-12"},"reason":"家裡有事"},
      "expectedPath":["start_1","task_apply","approval_manager","gateway_days","task_hr_archive","end_1"],
      "expectedApprovers":[{"nodeId":"approval_manager","userIds":["u_wang_manager"]}],
      "expectedNotifications":[{"trigger":"on_assign","recipientCount":1},{"trigger":"on_complete","recipientCount":1}]
    },
    {
      "id":"tc_2",
      "name":"8 天事假、需副總加簽",
      "inputs":{"leave_type":"事假","date_range":{"start":"2026-06-01","end":"2026-06-10"},"reason":"出國"},
      "expectedPath":["start_1","task_apply","approval_manager","gateway_days","approval_vp","task_hr_archive","end_1"],
      "expectedApprovers":[
        {"nodeId":"approval_manager","userIds":["u_wang_manager"]},
        {"nodeId":"approval_vp","userIds":["u_chen_vp"]}
      ],
      "expectedNotifications":[]
    },
    {
      "id":"tc_3",
      "name":"病假需附證明",
      "inputs":{"leave_type":"病假","date_range":{"start":"2026-05-15","end":"2026-05-15"},"reason":"流感","cert":"certificate.pdf"},
      "expectedPath":["start_1","task_apply","approval_manager","gateway_days","task_hr_archive","end_1"],
      "expectedApprovers":[{"nodeId":"approval_manager","userIds":["u_wang_manager"]}]
    }
  ]
}
```

---

### 2.10 ActorRef — 統一的「指涉某人」DSL（v1.1 新增）

任何 spec 欄位需要表達「誰」（簽核者、收件人、表單 owner...）都用 `ActorRef`。
這是個 typed discriminated union — 每個 ActorRef 一定有 `type` 欄位，型別決定有哪些子欄位。

```typescript
type ActorRef =
  // —— 4 個 atomic 型別（單一指涉）——
  | { type: 'expr';  path: PathString;     fallback?: ActorRef }
  | { type: 'role';  code: string;         fallback?: ActorRef }
  | { type: 'group'; id: string;           fallback?: ActorRef }
  | { type: 'user';  id: string;           fallback?: ActorRef }   // ⚠️ test only — production spec 不該出現

  // —— 2 個 composite 型別（組合）——
  | { type: 'conditional'
      condition: { field: string; op: ConditionOp; value: any }
      then: ActorRef
      else: ActorRef
      fallback?: ActorRef }                                          // 嵌套深度 ≤ 3

  | { type: 'collection'
      mode: 'any' | 'all'
      min_approvals?: number                                         // mode=any 必填，≤ actors.length
      actors: ActorRef[]                                             // 不可空
      fallback?: ActorRef }

type ConditionOp = '==' | '!=' | '>' | '>=' | '<' | '<=' | 'in' | 'not_in'

type PathString =
  | 'submitter'
  | 'submitter.manager'
  | 'submitter.manager.manager'
  | 'submitter.manager.manager.manager'
  | 'submitter.department'
  | 'submitter.department.head'
  | 'submitter.department.parent'
  | 'submitter.department.parent.head'
  | 'submitter.department.parent.parent.head'
```

**重要規則**：
- `path` 是封閉集合（whitelist）。寫不在表上的字串 → spec validator 在載入時就 reject
- `fallback` 只允許 1 層（`fallback.fallback` 會被 reject）
- `conditional` 嵌套深度 ≤ 3
- `collection.actors` 至少 1 個
- `collection.mode = "any"` 時 `min_approvals` 必須 ≤ `actors.length`
- 解析失敗模式（`PathUnresolved` / `RoleEmpty` / `GroupEmpty` / `Cycle` / `ConditionalBranchEmpty` / `ValidationFailed`）會進 `ActorResolutionAudits` 表

**worked examples**：

```jsonc
// 員工的直屬主管簽
{ "type": "expr", "path": "submitter.manager" }

// 部門頭簽，找不到 fallback 給 admin
{ "type": "expr", "path": "submitter.department.head",
  "fallback": { "type": "role", "code": "admin" } }

// 金額大於 5 萬走 CEO，否則走主管
{ "type": "conditional",
  "condition": { "field": "amount", "op": ">", "value": 50000 },
  "then":  { "type": "role", "code": "CEO" },
  "else":  { "type": "expr", "path": "submitter.manager" } }

// 採購委員會 3 人中要 2 人簽
{ "type": "collection", "mode": "any", "min_approvals": 2,
  "actors": [
    { "type": "user", "id": "u_a" },
    { "type": "user", "id": "u_b" },
    { "type": "user", "id": "u_c" }
  ] }

// 「主管 + 財務」雙簽
{ "type": "collection", "mode": "all",
  "actors": [
    { "type": "expr", "path": "submitter.manager" },
    { "type": "role", "code": "finance_manager" }
  ] }

// 條件式 + 合議：金額 >= 10萬，要 CEO + CFO 雙簽；否則主管
{ "type": "conditional",
  "condition": { "field": "amount", "op": ">=", "value": 100000 },
  "then": { "type": "collection", "mode": "all",
            "actors": [ { "type": "role", "code": "CEO" },
                        { "type": "role", "code": "CFO" } ] },
  "else": { "type": "expr", "path": "submitter.manager" } }
```

---

## 4. Schema 演化策略

- `meta.schemaVersion` 是版本號，schema 改 breaking change 就 bump
- 客戶端（9 step UI）匯出時固定填當前版本
- Claude Code prompt template 跟 schema version 綁定（v1 prompt 看 v1 spec、v2 prompt 看 v2 spec）
- 新版本上線時，舊版客戶 spec 跑 migration script 升上來，再交給新 prompt

---

## 5. 給 Claude Code 的「閱讀順序」

當 Claude Code 拿到 spec.json，建議的處理順序：

1. **先讀 `meta`**——確定 tenant、flowCode（這影響所有 class / table 命名）
2. **讀 `flow.nodes` + `flow.edges`**——畫出 state machine 拓撲
3. **讀 `userTasks`**——產 form schema、entity 欄位、Form.tsx
4. **讀 `decisions`**——產 gateway logic
5. **讀 `approvals`**——產 ApprovalResolver
6. **讀 `notifications`**——產 notify template + emit logic
7. **讀 `sla`**——產 timer + escalation handler
8. **讀 `integrations`**——產 IIdentityProvider 的實作（CSV 讀檔 / MCP client）
9. **讀 `testCases`**——產 unit test + integration test

---

## 6. 給 Review Agent 的「驗證 checklist」

Review agent 要從 spec 跟產出 code 比對：

- [ ] 每個 `flow.nodes[].id` 都有對應 code 處理
- [ ] 每個 `userTasks[].fields[].id` 都在 entity / form / DB schema 中
- [ ] 每個 `approvals[].rule` 在 ApprovalResolver 中有對應 case
- [ ] 每個 `decisions[].branches[].condition` 在 gateway 中正確實作
- [ ] 每個 `notifications[]` 都在 state transition 時 emit
- [ ] `sla.perNode` 每個都有 timer
- [ ] `testCases[]` 全部 PASS

---

*Last updated: 2026-05-02*
