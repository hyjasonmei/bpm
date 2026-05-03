import { Plus, Trash2, ChevronDown, ChevronRight } from 'lucide-react'
import { useState } from 'react'
import { Field, Input, Select, Textarea, Checkbox } from '@/components/ui/form'
import type {
  DraftSpec, Notification, NotifyTrigger, NotifyRecipient, NotifyTemplate,
} from '@/lib/onboarding'

const TRIGGERS: NotifyTrigger[] = [
  'on_submit', 'on_approve', 'on_reject', 'on_complete', 'on_assign', 'on_sla_breach',
]

const CHANNELS = ['email', 'in_app', 'teams'] as const

const RECIPIENT_TYPES = ['submitter', 'current_approver', 'role', 'specific_user'] as const

export function StepNotify({ draft, setDraft }: { draft: DraftSpec; setDraft: (d: DraftSpec) => void }) {
  const [expandedId, setExpandedId] = useState<string | null>(draft.notifications[0]?.id ?? null)

  const upsert = (n: Notification) => {
    const others = draft.notifications.filter(x => x.id !== n.id)
    setDraft({ ...draft, notifications: [...others, n] })
  }
  const remove = (id: string) => setDraft({ ...draft, notifications: draft.notifications.filter(x => x.id !== id) })

  const addNotification = () => {
    const n: Notification = {
      id: `notify_${Date.now().toString(36).slice(-4)}`,
      trigger: 'on_assign',
      channel: ['email'],
      recipients: [{ type: 'current_approver' }],
      template: {
        subject: { 'zh-TW': '【新通知】' },
        body: { 'zh-TW': '案件已進入下一階段。\n\n請點此查看: {{caseUrl}}' },
        variables: ['caseUrl'],
      },
    }
    upsert(n)
    setExpandedId(n.id)
  }

  return (
    <div className="flex flex-col gap-3">
      <div className="flex items-start justify-between">
        <p className="text-xs text-ink-muted max-w-xl">
          每個通知有 trigger（什麼時候觸發）/ channel（email/in_app/teams）/ recipients / 雙語 template。
          Body 用 <code className="font-mono">{'{{ variable }}'}</code> 語法，變數在「variables」欄列出。
          NOTIFY 階段非阻擋（無通知也能繼續），但生 code 時 emitter 會空。
        </p>
        <button
          onClick={addNotification}
          className="flex items-center gap-1 rounded bg-primary px-3 py-1.5 text-xs font-medium text-white hover:bg-blue-700"
        >
          <Plus className="h-3.5 w-3.5" /> Add notification
        </button>
      </div>

      {draft.notifications.length === 0 && (
        <div className="rounded border border-dashed border-rule p-10 text-center text-sm text-ink-faint">
          還沒有通知。點 Add notification 開始。
        </div>
      )}

      {[...draft.notifications].sort((a, b) => a.id.localeCompare(b.id)).map(n => {
        const expanded = expandedId === n.id
        return (
          <div key={n.id} className="rounded-md border border-rule bg-white">
            <div className="flex items-center justify-between border-b border-rule bg-slate-50 px-3 py-2">
              <button onClick={() => setExpandedId(expanded ? null : n.id)} className="flex flex-1 items-center gap-2 text-left">
                {expanded ? <ChevronDown className="h-4 w-4 text-ink-muted" /> : <ChevronRight className="h-4 w-4 text-ink-muted" />}
                <span className="font-mono text-[10px] text-ink-faint">{n.id}</span>
                <span className="text-sm font-medium text-ink">{n.trigger}</span>
                <span className="text-[11px] text-ink-muted">→ {n.recipients.map(recipientLabel).join(', ')}</span>
                <span className="text-[10px] text-ink-faint">[{n.channel.join('+')}]</span>
              </button>
              <button
                onClick={() => remove(n.id)}
                className="flex h-7 w-7 items-center justify-center rounded text-ink-faint hover:bg-rose-50 hover:text-danger"
                title="Remove"
              >
                <Trash2 className="h-4 w-4" />
              </button>
            </div>

            {expanded && (
              <div className="space-y-3 p-3">
                <div className="grid grid-cols-2 gap-2">
                  <Field label="ID" hint="snake_case">
                    <Input value={n.id} onChange={e => upsert({ ...n, id: e.target.value.toLowerCase().replace(/\s+/g, '_') })} />
                  </Field>
                  <Field label="Trigger" required>
                    <Select value={n.trigger} onChange={e => upsert({ ...n, trigger: e.target.value as NotifyTrigger })}>
                      {TRIGGERS.map(t => <option key={t} value={t}>{t}</option>)}
                    </Select>
                  </Field>
                </div>

                <div>
                  <p className="mb-1 text-xs font-semibold text-ink">Channels</p>
                  <div className="flex gap-3">
                    {CHANNELS.map(ch => (
                      <Checkbox
                        key={ch}
                        id={`${n.id}-ch-${ch}`}
                        checked={n.channel.includes(ch)}
                        onChange={e => upsert({
                          ...n,
                          channel: e.target.checked
                            ? [...n.channel, ch]
                            : n.channel.filter(c => c !== ch),
                        })}
                        label={ch}
                      />
                    ))}
                  </div>
                </div>

                <RecipientsEditor
                  recipients={n.recipients}
                  onChange={r => upsert({ ...n, recipients: r })}
                />

                <TemplateEditor
                  template={n.template}
                  onChange={t => upsert({ ...n, template: t })}
                />
              </div>
            )}
          </div>
        )
      })}
    </div>
  )
}

function recipientLabel(r: NotifyRecipient): string {
  switch (r.type) {
    case 'submitter':        return 'submitter'
    case 'current_approver': return 'current_approver'
    case 'role':             return `role:${r.role}`
    case 'specific_user':    return `user:${r.userId}`
  }
}

function emptyRecipient(type: NotifyRecipient['type']): NotifyRecipient {
  switch (type) {
    case 'submitter':        return { type: 'submitter' }
    case 'current_approver': return { type: 'current_approver' }
    case 'role':             return { type: 'role', role: '' }
    case 'specific_user':    return { type: 'specific_user', userId: '' }
  }
}

function RecipientsEditor({
  recipients, onChange,
}: { recipients: NotifyRecipient[]; onChange: (r: NotifyRecipient[]) => void }) {
  return (
    <div>
      <p className="mb-1 text-xs font-semibold text-ink">Recipients</p>
      <div className="space-y-1.5">
        {recipients.map((r, i) => (
          <div key={i} className="flex items-center gap-2 rounded border border-rule bg-slate-50 p-2">
            <Select
              className="h-7 max-w-[160px] text-xs"
              value={r.type}
              onChange={e => {
                const updated = [...recipients]
                updated[i] = emptyRecipient(e.target.value as NotifyRecipient['type'])
                onChange(updated)
              }}
            >
              {RECIPIENT_TYPES.map(t => <option key={t} value={t}>{t}</option>)}
            </Select>
            {r.type === 'role' && (
              <Input
                className="h-7 text-xs"
                placeholder="HR / VP / Finance"
                value={r.role}
                onChange={e => {
                  const updated = [...recipients]
                  updated[i] = { type: 'role', role: e.target.value }
                  onChange(updated)
                }}
              />
            )}
            {r.type === 'specific_user' && (
              <Input
                className="h-7 text-xs"
                placeholder="u_xxx"
                value={r.userId}
                onChange={e => {
                  const updated = [...recipients]
                  updated[i] = { type: 'specific_user', userId: e.target.value }
                  onChange(updated)
                }}
              />
            )}
            <button
              onClick={() => onChange(recipients.filter((_, j) => j !== i))}
              className="ml-auto flex h-6 w-6 items-center justify-center rounded text-ink-faint hover:bg-rose-50 hover:text-danger"
              title="Remove"
            >
              <Trash2 className="h-3.5 w-3.5" />
            </button>
          </div>
        ))}
      </div>
      <button
        onClick={() => onChange([...recipients, { type: 'current_approver' }])}
        className="mt-2 text-[11px] text-blue-600 hover:underline"
      >
        + Add recipient
      </button>
    </div>
  )
}

function TemplateEditor({
  template, onChange,
}: { template: NotifyTemplate; onChange: (t: NotifyTemplate) => void }) {
  return (
    <div className="space-y-2 rounded border border-rule bg-slate-50 p-2">
      <p className="text-xs font-semibold text-ink">Template (zh-TW)</p>
      <Field label="Subject" required>
        <Input
          value={template.subject['zh-TW']}
          onChange={e => onChange({ ...template, subject: { ...template.subject, 'zh-TW': e.target.value } })}
        />
      </Field>
      <Field label="Body" required hint="Mustache 變數: {{ applicant.name }}">
        <Textarea
          rows={4}
          className="font-mono text-[11px]"
          value={template.body['zh-TW']}
          onChange={e => onChange({ ...template, body: { ...template.body, 'zh-TW': e.target.value } })}
        />
      </Field>
      <Field label="Variables (newline-separated)" hint="必須等於 body 中所有 {{ var }} 的集合">
        <Textarea
          rows={2}
          className="font-mono text-[11px]"
          value={template.variables.join('\n')}
          onChange={e => onChange({
            ...template,
            variables: e.target.value.split('\n').map(s => s.trim()).filter(Boolean),
          })}
        />
      </Field>
    </div>
  )
}
