import type {
  DraftSpec, OnboardingStepId,
  Decision, Approval, Notification, NodeSLA, TestCase,
  FlowNode, FlowEdge,
  FormField, FieldType, ActorRef, NotifyTrigger, NotifyRecipient,
  UserTask, LayoutChild,
} from './onboarding'
import { ACTOR_PATH_WHITELIST, EMPTY_DRAFT, defaultApprovalActions, defaultUserTaskActions, newFieldUid, testCaseToSnapshot } from './onboarding'

const FLOW_NODE_TYPES = [
  'startEvent', 'endEvent', 'userTask', 'approval', 'gateway', 'serviceTask', 'notify',
] as const

/**
 * Per-step Anthropic tool definitions for the CoPilot chat.
 *
 * Each step contributes one tool. When the customer asks for a concrete
 * change in chat (e.g. "增加金額欄位"), Claude calls the tool with the new
 * complete state for that slice — the apply function writes it into the
 * draft and the canvas updates immediately.
 *
 * Schemas are deliberately narrow (only the fields the model needs to
 * authoritatively set). Anything missing — bilingual labels, hints, default
 * values, conditional expressions — Claude can ask the customer about and
 * leave for them to fill in by hand. Keeping schemas tight reduces the
 * blast radius of a hallucinated edit.
 *
 * "Replace" semantics: every tool overwrites the entire slice it owns.
 * Reason: lets the model see the current draft summary, decide the result,
 * and emit it whole. Patch semantics (add/remove diffs) require state-
 * threading the model frequently gets wrong over multi-turn chats.
 */

export interface AnthropicTool {
  name: string
  description: string
  input_schema: object
}

export interface StepToolBinding {
  tool: AnthropicTool
  apply: (draft: DraftSpec, input: unknown) => DraftSpec
}

const FIELD_TYPES: FieldType[] = [
  'text', 'textarea', 'number', 'date', 'daterange',
  'select', 'multiselect', 'file', 'user_picker', 'derived',
]

// Forms — one tool call replaces ONE user task's field list. Targeting per-task
// keeps the input small and makes "add X to the request form" unambiguous when
// there are multiple user tasks.
//
// Optional `repeaters` param replaces the itemFields[] of named repeaters in
// the user task's layout tree. Without it the model can only edit outer
// fields; repeater contents (e.g. line items) used to be opaque to AI.
const formsTool: StepToolBinding = {
  tool: {
    name: 'emit_form_fields',
    description: 'Update the field list for ONE user task. `taskId` MUST be one of draftSummary.availableUserTasks[].id verbatim — never invent ids like `task_1` / `task__1` / `userTask_1`. Pass the COMPLETE outer `fields` list (existing + new + modified); ids snake_case unique within the task. To edit a repeater\'s line-item fields (line items / attachments), pass `repeaters` with the repeater id from draftSummary.availableUserTasks[].repeaters[].id and the COMPLETE replacement itemFields[] for that repeater. Outer fields and repeaters are independent — omit `repeaters` if you only touch outer fields, omit `fields` (or pass it unchanged) if you only touch repeater contents.',
    input_schema: {
      type: 'object',
      required: ['taskId'],
      properties: {
        taskId: { type: 'string', description: 'Must be one of draftSummary.availableUserTasks[].id verbatim. Never invent.' },
        formCode: { type: 'string', description: 'Optional: override the form code (UPPERCASE_SNAKE)' },
        fields: {
          type: 'array',
          description: 'COMPLETE replacement for the outer fields[]. Omit (undefined) to leave outer fields untouched.',
          items: {
            type: 'object',
            required: ['id', 'labelZh', 'type', 'required'],
            properties: {
              id: { type: 'string', description: 'snake_case unique within this task' },
              labelZh: { type: 'string', description: 'Field label in 繁體中文' },
              labelEn: { type: 'string', description: 'Optional English label' },
              type: { type: 'string', enum: FIELD_TYPES },
              required: { type: 'boolean' },
              noteZh: { type: 'string', description: 'Optional free-text note for chef — use this when CEL / structured props cannot capture a business rule (e.g. "金額很大時主管要 double check 上一季預算"). chef reads it to generate the right code. Not shown to end-users.' },
              conditional: { type: 'string', description: "Optional JS-like expression, e.g. \"leave_type === '病假'\"" },
              options: {
                type: 'array',
                description: 'For select/multiselect only',
                items: {
                  type: 'object',
                  required: ['value', 'label'],
                  properties: { value: { type: 'string' }, label: { type: 'string' } },
                },
              },
            },
          },
        },
        repeaters: {
          type: 'array',
          description: 'Replace the itemFields[] of named repeaters. Each entry targets one repeater by id; the entire itemFields[] is overwritten with the supplied list.',
          items: {
            type: 'object',
            required: ['repeaterId', 'itemFields'],
            properties: {
              repeaterId: { type: 'string', description: 'Must be one of draftSummary.availableUserTasks[].repeaters[].id verbatim.' },
              itemFields: {
                type: 'array',
                description: 'COMPLETE replacement for this repeater\'s itemFields[].',
                items: {
                  type: 'object',
                  required: ['id', 'labelZh', 'type', 'required'],
                  properties: {
                    id: { type: 'string', description: 'snake_case unique within this repeater' },
                    labelZh: { type: 'string' },
                    labelEn: { type: 'string' },
                    type: { type: 'string', enum: FIELD_TYPES },
                    required: { type: 'boolean' },
                    noteZh: { type: 'string' },
                    options: {
                      type: 'array',
                      items: {
                        type: 'object',
                        required: ['value', 'label'],
                        properties: { value: { type: 'string' }, label: { type: 'string' } },
                      },
                    },
                  },
                },
              },
            },
          },
        },
      },
    },
  },
  apply: (draft, raw) => {
    type ToolField = {
      id: string
      labelZh: string
      labelEn?: string
      type: FieldType
      required: boolean
      noteZh?: string
      conditional?: string
      options?: { value: string; label: string }[]
    }
    const input = raw as {
      taskId: string
      formCode?: string
      fields?: ToolField[]
      repeaters?: Array<{ repeaterId: string; itemFields: ToolField[] }>
    }
    const node = draft.flow.nodes.find(n => n.id === input.taskId)
    if (!node) {
      const valid = draft.flow.nodes.filter(n => n.type === 'userTask').map(n => n.id)
      throw new Error(`taskId "${input.taskId}" 不存在；valid: ${valid.join(', ') || '(no user task nodes yet — draw one on the BPMN canvas first)'}`)
    }
    const existingTask = draft.userTasks.find(t => t.id === input.taskId)
    const mapField = (f: ToolField): FormField => ({
      id: f.id,
      label: { 'zh-TW': f.labelZh, ...(f.labelEn ? { en: f.labelEn } : {}) },
      type: f.type,
      required: f.required,
      _uid: newFieldUid(),
      ...(f.noteZh ? { note: { 'zh-TW': f.noteZh } } : {}),
      ...(f.conditional ? { conditional: f.conditional } : {}),
      ...(f.options ? { options: f.options } : {}),
    })

    const outerFields: FormField[] = input.fields
      ? input.fields.map(mapField)
      : existingTask?.fields ?? []

    let nextLayout = existingTask?.layout
    if (input.repeaters?.length) {
      if (!nextLayout?.length) {
        throw new Error(`task "${input.taskId}" 沒有任何 repeater 可以更新；先手動加 repeater 再叫 AI 編輯`)
      }
      const updates = new Map(input.repeaters.map(r => [r.repeaterId, r.itemFields]))
      const seen = new Set<string>()
      nextLayout = patchRepeaterFields(nextLayout, updates, mapField, seen)
      const missing = [...updates.keys()].filter(id => !seen.has(id))
      if (missing.length) {
        const valid = collectAllRepeaterIds(existingTask?.layout ?? []).join(', ')
        throw new Error(`repeaterId ${missing.join(', ')} 不存在於 task "${input.taskId}"；valid: ${valid || '(none)'}`)
      }
    }

    const nextTask: UserTask = {
      id: input.taskId,
      formCode: input.formCode ?? existingTask?.formCode ?? `${draft.meta.flowCode || 'FLOW'}_${node.label.toUpperCase().replace(/\s+/g, '_').slice(0, 12)}`,
      fields: outerFields,
      ...(nextLayout ? { layout: nextLayout } : {}),
      permissions: existingTask?.permissions ?? { submitter: 'self', viewers: ['self'] },
      // Preserve existing actions; new tasks get the standard `submit`.
      actions: existingTask?.actions ?? defaultUserTaskActions(),
    }
    return {
      ...draft,
      userTasks: [...draft.userTasks.filter(t => t.id !== input.taskId), nextTask],
    }
  },
}

function patchRepeaterFields(
  children: LayoutChild[],
  updates: Map<string, Array<{ id: string; labelZh: string; labelEn?: string; type: FieldType; required: boolean; noteZh?: string; options?: { value: string; label: string }[] }>>,
  mapField: (f: { id: string; labelZh: string; labelEn?: string; type: FieldType; required: boolean; noteZh?: string; options?: { value: string; label: string }[] }) => FormField,
  seen: Set<string>,
): LayoutChild[] {
  return children.map(c => {
    if (c.kind === 'repeater') {
      const upd = updates.get(c.id)
      if (upd) {
        seen.add(c.id)
        return { ...c, itemFields: upd.map(mapField), itemLayout: patchRepeaterFields(c.itemLayout, updates, mapField, seen) }
      }
      return { ...c, itemLayout: patchRepeaterFields(c.itemLayout, updates, mapField, seen) }
    }
    if (c.kind === 'section' || c.kind === 'row') {
      return { ...c, children: patchRepeaterFields(c.children, updates, mapField, seen) } as LayoutChild
    }
    return c
  })
}

function collectAllRepeaterIds(children: LayoutChild[]): string[] {
  const out: string[] = []
  for (const c of children) {
    if (c.kind === 'repeater') {
      out.push(c.id)
      out.push(...collectAllRepeaterIds(c.itemLayout))
    } else if (c.kind === 'section' || c.kind === 'row') {
      out.push(...collectAllRepeaterIds(c.children))
    }
  }
  return out
}

const decisionsTool: StepToolBinding = {
  tool: {
    name: 'emit_decision_rules',
    description: 'Replace the entire decisions[] array. Include one entry per gateway node, with a branch entry for every outgoing edge. **Critical**: `id` must match a node id from draftSummary.nodes[] verbatim (e.g. `gateway_days`), and every `edgeId` must match a draftSummary.edges[].id verbatim (e.g. `e4`, `e5`) — do NOT invent semantic names. `condition` references userTask field ids verbatim (snake_case, see draftSummary.userTasks[].fields[].id). For exclusive gateways, exactly one branch must have isDefault=true.',
    input_schema: {
      type: 'object',
      required: ['decisions'],
      properties: {
        decisions: {
          type: 'array',
          items: {
            type: 'object',
            required: ['id', 'type', 'branches'],
            properties: {
              id: { type: 'string', description: 'gateway node id' },
              type: { type: 'string', enum: ['exclusive', 'parallel', 'inclusive'] },
              branches: {
                type: 'array',
                items: {
                  type: 'object',
                  required: ['edgeId', 'condition'],
                  properties: {
                    edgeId: { type: 'string', description: 'flow.edges[].id, must originate from this gateway' },
                    condition: { type: 'string', description: 'Expression like "amount >= 10000". Empty for the default branch is OK.' },
                    isDefault: { type: 'boolean' },
                  },
                },
              },
            },
          },
        },
      },
    },
  },
  apply: (draft, raw) => {
    const input = raw as { decisions: Decision[] }
    return { ...draft, decisions: input.decisions }
  },
}

// approvers — emits the v2 ActorRef discriminated union (see
// flowcook-wizard spec §APPROVERS). Five types: expr / principal /
// conditional / collection / natural_language. Fallback is no longer
// a recursive ActorRef — it's `{ text: string }`. conditional.then/else
// and collection.actors[*] are still recursive. Prefer structured
// types; natural_language is the last resort.
const ACTOR_REF_DEFS = {
  fallback: {
    type: 'object',
    required: ['text'],
    properties: {
      text: { type: 'string', description: 'Natural-language description of what to do when the primary rule resolves to no one. chef reads this at build time and generates the catch-all branch.' },
    },
  },
  actorRef: {
    oneOf: [
      {
        type: 'object',
        required: ['type', 'path'],
        properties: {
          type: { const: 'expr' },
          path: { type: 'string', enum: [...ACTOR_PATH_WHITELIST], description: 'Whitelisted org-chart path. Walks from `submitter` token by token (.manager / .department / .head / .parent). Use one of the whitelisted values verbatim.' },
          fallback: { $ref: '#/$defs/fallback' },
        },
      },
      {
        type: 'object',
        required: ['type', 'ref'],
        properties: {
          type: { const: 'principal' },
          ref: { type: 'string', description: 'Prefixed principal ref `kind:id`, where kind ∈ user|dept|group|role and id is the directory uuid (or role code). Must match a real principal in draftSummary.principalsAvailable when surfaced — picker is the source of truth.' },
          fallback: { $ref: '#/$defs/fallback' },
        },
      },
      {
        type: 'object',
        required: ['type', 'condition', 'then', 'else'],
        properties: {
          type: { const: 'conditional' },
          condition: {
            type: 'object',
            required: ['field', 'op', 'value'],
            properties: {
              field: { type: 'string', description: 'A form field id from userTasks (verbatim, snake_case)' },
              op: { type: 'string', enum: ['==', '!=', '>', '>=', '<', '<=', 'in', 'not_in'] },
              value: { description: 'Literal or array (for in / not_in)' },
            },
          },
          then: { $ref: '#/$defs/actorRef' },
          else: { $ref: '#/$defs/actorRef' },
          fallback: { $ref: '#/$defs/fallback' },
        },
      },
      {
        type: 'object',
        required: ['type', 'mode', 'actors'],
        properties: {
          type: { const: 'collection' },
          mode: { type: 'string', enum: ['any', 'all'] },
          min_approvals: { type: 'integer', minimum: 1, description: 'Required when mode=any. Must be ≤ actors.length.' },
          actors: { type: 'array', minItems: 1, items: { $ref: '#/$defs/actorRef' } },
          fallback: { $ref: '#/$defs/fallback' },
        },
      },
      {
        type: 'object',
        required: ['type', 'text'],
        properties: {
          type: { const: 'natural_language' },
          text: { type: 'string', description: 'LAST RESORT only — use this only when none of expr / principal / conditional / collection can express the rule. chef will use LLM judgment to generate the resolver code.' },
          fallback: { $ref: '#/$defs/fallback' },
        },
      },
    ],
  },
} as const

const approversTool: StepToolBinding = {
  tool: {
    name: 'emit_approver_config',
    description:
      'Replace the entire approvals[] array. Include one entry per approval node `{id, approver: ActorRef}`. ActorRef discriminated by `type`: expr (org-chart path) / principal (`kind:id` ref to user|dept|group|role) / conditional (if/then/else against form field) / collection (any|all of N actors). **natural_language is the last resort** — prefer the four structured types whenever possible. Fallback is now `{ text: string }` (not recursive ActorRef); chef reads it at build time.',
    input_schema: {
      type: 'object',
      required: ['approvals'],
      properties: {
        approvals: {
          type: 'array',
          items: {
            type: 'object',
            required: ['id', 'approver'],
            properties: {
              id: { type: 'string', description: 'approval node id (matches flow.nodes[].id)' },
              approver: { $ref: '#/$defs/actorRef' },
            },
          },
        },
      },
      $defs: ACTOR_REF_DEFS,
    },
  },
  apply: (draft, raw) => {
    const input = raw as { approvals: Array<{ id: string; approver: ActorRef }> }
    const next: Approval[] = input.approvals.map(a => {
      const existing = draft.approvals.find(x => x.id === a.id)
      return {
        id: a.id,
        approver: a.approver,
        // Preserve existing actions; brand-new approvals get [approve, reject].
        actions: existing?.actions ?? defaultApprovalActions(),
      }
    })
    return { ...draft, approvals: next }
  },
}

const notifyTool: StepToolBinding = {
  tool: {
    name: 'emit_notifications',
    description: `Replace the entire notifications[] array. **Critical**: Pass the COMPLETE list — every notification that should exist after the change (existing + newly added + modified). Omitting an existing entry will DELETE it. If the user asks to "add" a notification, include all current notifications PLUS the new one. Each notification has a trigger, channels, recipients, and a 繁體中文 template with Mustache {{variables}}. Variables[] must list every {{var}} appearing in subject + body. Recipients accept three ergonomic types: submitter / current_approver / principal (\`ref\` is \`kind:id\`, kind ∈ user|dept|group|role).

**Two notification kinds — do not conflate**:
- **node-bound** (set \`nodeId\` to a BPMN \`notify\` node id): fires when the flow reaches that node. There is exactly one notification per notify node; do not create event-triggered ones for the same node.
- **event-triggered** (no \`nodeId\`): fires on the cross-cutting trigger event (on_submit / on_assign / etc.). When the user asks to "add a new notification on on_complete", they almost always mean event-triggered — do NOT attach it to an unrelated notify node.

If a notification's \`nodeId\` is already set in draftSummary, treat it as node-bound and only modify trigger/recipients/template — never reassign its nodeId.`,
    input_schema: {
      type: 'object',
      required: ['notifications'],
      properties: {
        notifications: {
          type: 'array',
          items: {
            type: 'object',
            required: ['id', 'trigger', 'channel', 'recipients', 'template'],
            properties: {
              id: { type: 'string', description: 'snake_case' },
              nodeId: { type: 'string', description: 'Optional: BPMN `notify` node id this notification is bound to. When set, the notification fires when the flow reaches that node (ignore the cross-cutting trigger for routing — trigger may stay as an admin-side label).' },
              trigger: { type: 'string', enum: ['on_submit', 'on_approve', 'on_reject', 'on_complete', 'on_assign', 'on_sla_breach'] },
              channel: { type: 'array', items: { type: 'string', enum: ['email', 'in_app', 'teams'] } },
              recipients: {
                type: 'array',
                items: {
                  oneOf: [
                    { type: 'object', required: ['type'], properties: { type: { const: 'submitter' } } },
                    { type: 'object', required: ['type'], properties: { type: { const: 'current_approver' } } },
                    {
                      type: 'object',
                      required: ['type', 'ref'],
                      properties: {
                        type: { const: 'principal' },
                        ref: { type: 'string', description: 'Prefixed `kind:id` where kind ∈ user|dept|group|role. Use `role:HR`, `role:Finance` etc. for role-based sends.' },
                      },
                    },
                  ],
                },
              },
              template: {
                type: 'object',
                required: ['subjectZh', 'bodyZh', 'variables'],
                properties: {
                  subjectZh: { type: 'string' },
                  bodyZh: { type: 'string' },
                  variables: { type: 'array', items: { type: 'string' } },
                },
              },
            },
          },
        },
      },
    },
  },
  apply: (draft, raw) => {
    const input = raw as {
      notifications: Array<{
        id: string
        nodeId?: string
        trigger: NotifyTrigger
        channel: ('email' | 'in_app' | 'teams')[]
        recipients: NotifyRecipient[]
        template: { subjectZh: string; bodyZh: string; variables: string[] }
      }>
    }
    const next: Notification[] = input.notifications.map(n => ({
      id: n.id,
      // AI tool emits the legacy event-only enum (back-compat).
      // Wizard authors who want action-scoped triggers do it in UI.
      trigger: { kind: 'event' as const, event: n.trigger },
      channel: n.channel,
      recipients: n.recipients,
      template: {
        subject: { 'zh-TW': n.template.subjectZh },
        body: { 'zh-TW': n.template.bodyZh },
        variables: n.template.variables,
      },
      ...(n.nodeId ? { nodeId: n.nodeId } : {}),
    }))
    return { ...draft, notifications: next }
  },
}

const slaTool: StepToolBinding = {
  tool: {
    name: 'emit_sla_config',
    description: `Replace sla.perNode. **Critical**: Pass the COMPLETE map — every node that should have SLA after the change (existing entries that should stay + new + modified). Omitting an existing key DELETES that node's SLA. If the user asks to "set SLA on node X", include X **plus all other existing nodes' SLA** from draftSummary.sla.perNode. Keys are node ids (only approval / userTask / serviceTask nodes are meaningful).

**v2 simplification**:
- \`duration\` and \`escalation.after\` are **absolute hours only**, e.g. "1h", "8h", "24h". Relative "%" syntax is no longer accepted — pre-compute absolute hours from draftSummary.sla.perNode if the user says "50% of duration".
- \`noteZh\` (optional) is a free-text instruction for chef when structured duration / escalation can't express the rule (譬如「夜班 SLA 算法不同」「跨時區員工放寬 2 小時」). End-users don't see this.`,
    input_schema: {
      type: 'object',
      required: ['perNode'],
      properties: {
        perNode: {
          type: 'object',
          additionalProperties: {
            type: 'object',
            required: ['duration'],
            properties: {
              duration: { type: 'string', description: 'Absolute hours only, e.g. "1h", "8h", "24h". Common: 1h / 2h / 4h / 8h / 16h / 24h / 48h / 72h.' },
              businessHoursOnly: { type: 'boolean' },
              escalation: {
                type: 'object',
                required: ['after', 'action'],
                properties: {
                  after: { type: 'string', description: 'Absolute hours only, e.g. "8h". NO percentages — convert relative forms to absolute.' },
                  action: { type: 'string', enum: ['notify', 'reassign', 'escalate_one_level', 'auto_approve', 'auto_reject'] },
                },
              },
              noteZh: { type: 'string', description: 'Optional free-text instruction for chef — use when structured rules can\'t express the case.' },
            },
          },
        },
      },
    },
  },
  apply: (draft, raw) => {
    type SlaInput = Omit<NodeSLA, 'note'> & { noteZh?: string }
    const input = raw as { perNode: Record<string, SlaInput> }
    const perNode: Record<string, NodeSLA> = {}
    for (const [nodeId, entry] of Object.entries(input.perNode)) {
      const { noteZh, ...rest } = entry
      perNode[nodeId] = {
        ...rest,
        ...(noteZh ? { note: { 'zh-TW': noteZh } } : {}),
      }
    }
    return { ...draft, sla: { perNode } }
  },
}

const testTool: StepToolBinding = {
  tool: {
    name: 'emit_test_cases',
    description: 'Replace the entire testCases[] array. Each case has inputs (an arbitrary object matching the user task fields), an expectedPath of node ids, and optional expectedApprovers / expectedNotifications.',
    input_schema: {
      type: 'object',
      required: ['testCases'],
      properties: {
        testCases: {
          type: 'array',
          items: {
            type: 'object',
            required: ['id', 'name', 'inputs'],
            properties: {
              id: { type: 'string' },
              name: { type: 'string' },
              inputs: { type: 'object', description: 'Field id → value map' },
              expectedPath: { type: 'array', items: { type: 'string' } },
              expectedApprovers: {
                type: 'array',
                items: {
                  type: 'object',
                  required: ['nodeId', 'userIds'],
                  properties: { nodeId: { type: 'string' }, userIds: { type: 'array', items: { type: 'string' } } },
                },
              },
              expectedNotifications: {
                type: 'array',
                items: {
                  type: 'object',
                  required: ['trigger', 'recipientCount'],
                  properties: { trigger: { type: 'string' }, recipientCount: { type: 'integer' } },
                },
              },
              expectedHttpStatus: { type: 'integer' },
              expectedValidationErrors: { type: 'array', items: { type: 'string' } },
            },
          },
        },
      },
    },
  },
  apply: (draft, raw) => {
    const input = raw as { testCases: TestCase[] }
    // The AI tool emits the legacy expanded shape (expectedPath /
    // expectedApprovers / etc.); the DraftSpec carries the bundle's
    // `TestCaseSnapshot` shape so the build payload is a passthrough.
    return { ...draft, testCases: input.testCases.map(testCaseToSnapshot) }
  },
}

// Source — one tool call replaces the flow skeleton (meta + nodes + edges).
// Replace semantics: AI emits the COMPLETE flow it understood from the
// customer's description, the wizard wipes the previous draft.flow / meta
// and seeds the rest of the steps off this clean baseline.
const sourceTool: StepToolBinding = {
  tool: {
    name: 'emit_flow_skeleton',
    description: 'Replace the flow skeleton (meta + nodes + edges) with the structure the customer just described. Emit the COMPLETE flow — every node and edge the customer needs. Every flow needs exactly one startEvent and at least one endEvent; gateways need ≥2 outgoing edges with conditions; ids are snake_case ASCII.',
    input_schema: {
      type: 'object',
      required: ['meta', 'nodes', 'edges'],
      properties: {
        meta: {
          type: 'object',
          required: ['tenant', 'flowName', 'flowCode'],
          properties: {
            tenant:   { type: 'string', description: 'Customer tenant code, lowercase, e.g. acme' },
            flowName: { type: 'string', description: 'Human-readable Chinese name' },
            flowCode: { type: 'string', description: 'UPPERCASE_SNAKE identifier (class / table base name)' },
          },
        },
        nodes: {
          type: 'array',
          items: {
            type: 'object',
            required: ['id', 'type', 'label'],
            properties: {
              id:    { type: 'string', description: 'snake_case unique id (start_1 / task_apply / approval_manager / gateway_amount / end_1)' },
              type:  { type: 'string', enum: [...FLOW_NODE_TYPES] },
              label: { type: 'string', description: '繁體中文 label' },
            },
          },
        },
        edges: {
          type: 'array',
          items: {
            type: 'object',
            required: ['id', 'source', 'target'],
            properties: {
              id:        { type: 'string' },
              source:    { type: 'string' },
              target:    { type: 'string' },
              label:     { type: 'string', description: 'Optional edge label (especially gateway branches)' },
              condition: { type: 'string', description: 'Optional condition for gateway branches' },
            },
          },
        },
      },
    },
  },
  apply: (draft, raw) => {
    const input = raw as {
      meta: { tenant: string; flowName: string; flowCode: string }
      nodes: FlowNode[]
      edges: FlowEdge[]
    }
    return {
      ...EMPTY_DRAFT,
      ...draft,
      meta: { ...draft.meta, ...input.meta },
      flow: { nodes: input.nodes, edges: input.edges },
    }
  },
}

// translation — patch draft.labels[locale][key]. Typical use: "AI fill
// empties" button posts a chat turn that lists every empty (key,
// zh-TW) pair; AI returns the en translations. Tool semantics: PATCH
// (partial update) — only emitted keys are written; omitted keys keep
// their current values. Diverges from the「emit COMPLETE」family
// because translation is naturally additive and the table can be
// huge.
const translationTool: StepToolBinding = {
  tool: {
    name: 'emit_translations',
    description: 'PATCH (partial merge) into draft.labels. Each entry overwrites labels[entry.locale][entry.key]; existing keys not in this payload are LEFT ALONE. Use this to fill empties or correct a handful of strings, not to replace the whole table. Locale strings: "zh-TW" (primary, usually pre-filled), "en", future "ja" / "ko" etc.',
    input_schema: {
      type: 'object',
      required: ['entries'],
      properties: {
        entries: {
          type: 'array',
          items: {
            type: 'object',
            required: ['locale', 'key', 'text'],
            properties: {
              locale: { type: 'string', description: 'e.g. "en", "zh-TW"' },
              key: { type: 'string', description: 'Label key from draftSummary.labelKeys, e.g. "node.task_apply.label" or "field.task_apply.leave_type.label"' },
              text: { type: 'string', description: 'Translation in the target locale' },
            },
          },
        },
      },
    },
  },
  apply: (draft, raw) => {
    const input = raw as { entries: Array<{ locale: string; key: string; text: string }> }
    const labels: Record<string, Record<string, string>> = {
      ...(draft.labels ?? { 'zh-TW': {} }),
    }
    for (const e of input.entries) {
      labels[e.locale] = { ...(labels[e.locale] ?? {}), [e.key]: e.text }
    }
    return { ...draft, labels }
  },
}

// notes — overwrite draft.notes. Simple replace-the-string, no merge.
const notesTool: StepToolBinding = {
  tool: {
    name: 'emit_notes_text',
    description: 'Replace draft.notes (single free-text string for chef / reviewer). Pass the new full body — this is OVERWRITE, not append. If the user says "add a paragraph about X", read draftSummary.notes (when present) and concatenate before emitting.',
    input_schema: {
      type: 'object',
      required: ['text'],
      properties: {
        text: { type: 'string', description: 'The full new notes body (overwrites). Plain text. Use \\n for line breaks.' },
      },
    },
  },
  apply: (draft, raw) => {
    const input = raw as { text: string }
    return { ...draft, notes: input.text }
  },
}

// ACCESS intentionally has no tool. The picker (modal with 4 tabs +
// search + select buffer) is already the friendliest path to assign
// principals — adding an AI editor on top brings staleness / scale
// problems (whole directory in prompt) that aren't worth it for the
// gain. Chat on this step stays advisory: answer questions, explain
// concepts, suggest who should own what. The picker does the writing.
export const STEP_TOOLS: Partial<Record<OnboardingStepId, StepToolBinding>> = {
  source:    sourceTool,
  forms:     formsTool,
  decisions: decisionsTool,
  approvers: approversTool,
  notify:      notifyTool,
  sla:         slaTool,
  translation: translationTool,
  notes:       notesTool,
  // testTool is retained on disk but no longer wired — the dedicated
  // TEST step was dropped in the 11-step refactor (Submit is now in the
  // wizard header). trigger_access / variables / integrations tools
  // remain advisory (ACCESS by design; INTEGRATIONS / VARIABLES land
  // in Phase E2 if needed).
}
void testTool;
