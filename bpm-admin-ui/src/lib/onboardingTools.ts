import type {
  DraftSpec, OnboardingStepId,
  Decision, Approval, Notification, NodeSLA, TestCase,
  FormField, FieldType, ActorRef, NotifyTrigger, NotifyRecipient,
} from './onboarding'
import { ACTOR_PATH_WHITELIST, testCaseToSnapshot } from './onboarding'

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
const formsTool: StepToolBinding = {
  tool: {
    name: 'emit_form_fields',
    description: 'Replace the entire field list for one user task. Pass the COMPLETE list of fields the task should have after the change (existing + new + modified). Each field id must be unique within the task and snake_case.',
    input_schema: {
      type: 'object',
      required: ['taskId', 'fields'],
      properties: {
        taskId: { type: 'string', description: 'The user task node id (matches flow.nodes[].id)' },
        formCode: { type: 'string', description: 'Optional: override the form code (UPPERCASE_SNAKE)' },
        fields: {
          type: 'array',
          items: {
            type: 'object',
            required: ['id', 'labelZh', 'type', 'required'],
            properties: {
              id: { type: 'string', description: 'snake_case unique within this task' },
              labelZh: { type: 'string', description: 'Field label in 繁體中文' },
              labelEn: { type: 'string', description: 'Optional English label' },
              type: { type: 'string', enum: FIELD_TYPES },
              required: { type: 'boolean' },
              hintZh: { type: 'string', description: 'Optional inline hint shown beneath the field' },
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
      },
    },
  },
  apply: (draft, raw) => {
    const input = raw as {
      taskId: string
      formCode?: string
      fields: Array<{
        id: string
        labelZh: string
        labelEn?: string
        type: FieldType
        required: boolean
        hintZh?: string
        conditional?: string
        options?: { value: string; label: string }[]
      }>
    }
    const node = draft.flow.nodes.find(n => n.id === input.taskId)
    if (!node) throw new Error(`User task "${input.taskId}" 不存在於 flow.nodes`)

    const existingTask = draft.userTasks.find(t => t.id === input.taskId)
    const newFields: FormField[] = input.fields.map(f => ({
      id: f.id,
      label: { 'zh-TW': f.labelZh, ...(f.labelEn ? { en: f.labelEn } : {}) },
      type: f.type,
      required: f.required,
      ...(f.hintZh ? { hint: { 'zh-TW': f.hintZh } } : {}),
      ...(f.conditional ? { conditional: f.conditional } : {}),
      ...(f.options ? { options: f.options } : {}),
    }))

    const nextTask = {
      id: input.taskId,
      formCode: input.formCode ?? existingTask?.formCode ?? `${draft.meta.flowCode || 'FLOW'}_${node.label.toUpperCase().replace(/\s+/g, '_').slice(0, 12)}`,
      fields: newFields,
      permissions: existingTask?.permissions ?? { submitter: 'self', viewers: ['self'] },
    }
    return {
      ...draft,
      userTasks: [...draft.userTasks.filter(t => t.id !== input.taskId), nextTask],
    }
  },
}

const decisionsTool: StepToolBinding = {
  tool: {
    name: 'emit_decision_rules',
    description: 'Replace the entire decisions[] array. Include one entry per gateway node, with a branch entry for every outgoing edge. For exclusive gateways, exactly one branch must have isDefault=true.',
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

// approvers — emits the v1.1 ActorRef discriminated union (see
// spec_schema.md §2.10). The schema is recursive: conditional.then/else
// and collection.actors[*] both reference actorRef, plus any actorRef may
// carry one fallback. Depth limits (conditional ≤ 3, fallback ≤ 1) are
// validated server-side at /api/admin/flow-library/build — keeping the
// schema permissive here avoids ballooning the input_schema and works
// around tool input validation that doesn't enforce $ref recursion depth.
const ACTOR_REF_DEFS = {
  actorRef: {
    oneOf: [
      {
        type: 'object',
        required: ['type', 'path'],
        properties: {
          type: { const: 'expr' },
          path: { type: 'string', enum: [...ACTOR_PATH_WHITELIST], description: 'Whitelisted org-chart path. Use lowercase exactly as listed.' },
          fallback: { $ref: '#/$defs/actorRef' },
        },
      },
      {
        type: 'object',
        required: ['type', 'code'],
        properties: {
          type: { const: 'role' },
          code: { type: 'string', description: 'Role code, e.g. Finance / CEO / VP / HR / admin / designer / viewer' },
          fallback: { $ref: '#/$defs/actorRef' },
        },
      },
      {
        type: 'object',
        required: ['type', 'id'],
        properties: {
          type: { const: 'group' },
          id: { type: 'string', description: 'Group id (Guid)' },
          fallback: { $ref: '#/$defs/actorRef' },
        },
      },
      {
        type: 'object',
        required: ['type', 'id'],
        properties: {
          type: { const: 'user' },
          id: { type: 'string', description: 'Specific user id (Guid). TEST-ONLY — production specs should use expr/role.' },
          fallback: { $ref: '#/$defs/actorRef' },
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
              field: { type: 'string', description: 'A form field id from userTasks' },
              op: { type: 'string', enum: ['==', '!=', '>', '>=', '<', '<=', 'in', 'not_in'] },
              value: { description: 'Literal or array (for in / not_in)' },
            },
          },
          then: { $ref: '#/$defs/actorRef' },
          else: { $ref: '#/$defs/actorRef' },
          fallback: { $ref: '#/$defs/actorRef' },
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
          fallback: { $ref: '#/$defs/actorRef' },
        },
      },
    ],
  },
} as const

const approversTool: StepToolBinding = {
  tool: {
    name: 'emit_approver_config',
    description:
      'Replace the entire approvals[] array. Include one entry per approval node. Each entry has {id, approver: ActorRef}. ActorRef is a discriminated union by type: expr (org-chart path walk), role (code), group (id), user (id, test-only), conditional (if/then/else against form field), collection (any|all of N actors with optional min_approvals). Any ActorRef may carry one fallback. Use the typed-discriminator form — never strings or sigil syntax.',
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
    const next: Approval[] = input.approvals.map(a => ({ id: a.id, approver: a.approver }))
    return { ...draft, approvals: next }
  },
}

const notifyTool: StepToolBinding = {
  tool: {
    name: 'emit_notifications',
    description: 'Replace the entire notifications[] array. Each notification has a trigger, channels, recipients, and a 繁體中文 template with Mustache {{variables}}. Variables[] must list every {{var}} appearing in subject + body.',
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
              trigger: { type: 'string', enum: ['on_submit', 'on_approve', 'on_reject', 'on_complete', 'on_assign', 'on_sla_breach'] },
              channel: { type: 'array', items: { type: 'string', enum: ['email', 'in_app', 'teams'] } },
              recipients: {
                type: 'array',
                items: {
                  oneOf: [
                    { type: 'object', required: ['type'], properties: { type: { const: 'submitter' } } },
                    { type: 'object', required: ['type'], properties: { type: { const: 'current_approver' } } },
                    { type: 'object', required: ['type', 'code'], properties: { type: { const: 'role' }, code: { type: 'string', description: 'Role code, e.g. HR / Finance / VP' } } },
                    { type: 'object', required: ['type', 'id'], properties: { type: { const: 'user' }, id: { type: 'string', description: 'User Guid (test-only — production specs should use role)' } } },
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
        trigger: NotifyTrigger
        channel: ('email' | 'in_app' | 'teams')[]
        recipients: NotifyRecipient[]
        template: { subjectZh: string; bodyZh: string; variables: string[] }
      }>
    }
    const next: Notification[] = input.notifications.map(n => ({
      id: n.id,
      trigger: n.trigger,
      channel: n.channel,
      recipients: n.recipients,
      template: {
        subject: { 'zh-TW': n.template.subjectZh },
        body: { 'zh-TW': n.template.bodyZh },
        variables: n.template.variables,
      },
    }))
    return { ...draft, notifications: next }
  },
}

const slaTool: StepToolBinding = {
  tool: {
    name: 'emit_sla_config',
    description: 'Replace sla.perNode. Keys are node ids (only approval / userTask / serviceTask nodes are meaningful). Duration uses suffix h or d, e.g. "8h", "2d". escalation.after may also be a percentage like "50%" of the duration.',
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
              duration: { type: 'string', description: 'e.g. "8h", "24h", "2d"' },
              businessHoursOnly: { type: 'boolean' },
              escalation: {
                type: 'object',
                required: ['after', 'action'],
                properties: {
                  after: { type: 'string', description: 'e.g. "8h" or "50%"' },
                  action: { type: 'string', enum: ['notify', 'reassign', 'escalate_one_level', 'auto_approve', 'auto_reject'] },
                },
              },
            },
          },
        },
      },
    },
  },
  apply: (draft, raw) => {
    const input = raw as { perNode: Record<string, NodeSLA> }
    return { ...draft, sla: { perNode: input.perNode } }
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

export const STEP_TOOLS: Partial<Record<OnboardingStepId, StepToolBinding>> = {
  forms:     formsTool,
  decisions: decisionsTool,
  approvers: approversTool,
  notify:    notifyTool,
  sla:       slaTool,
  test:      testTool,
}
