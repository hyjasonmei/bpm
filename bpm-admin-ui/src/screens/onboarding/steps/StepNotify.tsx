/**
 * StepNotify (v2) — pill row alignment with FORMS / DECISIONS /
 * APPROVERS, PrincipalSinglePicker for principal recipients, and
 * preset notification cards for common patterns. Template body /
 * subject expose clickable variable chips (form fields + draft
 * variables) so authors don't have to memorise field ids.
 */
import { AlertCircle, CheckCircle2, Mail, Plus, Sparkles, Trash2 } from 'lucide-react'
import { useState } from 'react'
import { cn } from '@/lib/cn'
import { Field, Input, Select, Textarea, Checkbox } from '@/components/ui/form'
import { PrincipalSinglePickerField } from '@/components/principal-picker/PrincipalPicker'
import type {
  DraftSpec, Notification, NotifyTrigger, NotifyRecipient, NotifyTemplate,
} from '@/lib/onboarding'

const TRIGGERS: NotifyTrigger[] = [
  'on_submit', 'on_approve', 'on_reject', 'on_complete', 'on_assign', 'on_sla_breach',
]

const CHANNELS = ['email', 'in_app', 'teams'] as const

// v2 NotifyRecipient supports 3 ergonomic types + falls through to
// other ActorRef shapes (rare). The list below drives the type select.
const RECIPIENT_TYPES = ['submitter', 'current_approver', 'principal'] as const

interface PresetNotification {
  label: string
  desc: string
  build: () => Omit<Notification, 'id'>
}

const PRESETS: PresetNotification[] = [
  {
    label: '提交時通知申請人',
    desc: 'on_submit → submitter / email',
    build: () => ({
      trigger: 'on_submit',
      channel: ['email', 'in_app'],
      recipients: [{ type: 'submitter' }],
      template: {
        subject: { 'zh-TW': '【已收到】您的申請已送出' },
        body: { 'zh-TW': '您的申請已送出，等待主管核准中。\n查看進度：{{caseUrl}}' },
        variables: ['caseUrl'],
      },
    }),
  },
  {
    label: '指派時通知 approver',
    desc: 'on_assign → current_approver',
    build: () => ({
      trigger: 'on_assign',
      channel: ['email', 'in_app'],
      recipients: [{ type: 'current_approver' }],
      template: {
        subject: { 'zh-TW': '【待簽】{{applicant.name}} 的申請' },
        body: { 'zh-TW': '申請人：{{applicant.name}}\n摘要：{{summary}}\n\n請點此核准：{{caseUrl}}' },
        variables: ['applicant.name', 'summary', 'caseUrl'],
      },
    }),
  },
  {
    label: '通過時通知 HR',
    desc: 'on_approve → role:HR',
    build: () => ({
      trigger: 'on_approve',
      channel: ['email'],
      recipients: [{ type: 'principal', ref: 'role:HR' }],
      template: {
        subject: { 'zh-TW': '【已核准】{{applicant.name}} 的申請' },
        body: { 'zh-TW': '案件已核准完畢。\n申請人：{{applicant.name}}\n\n詳細：{{caseUrl}}' },
        variables: ['applicant.name', 'caseUrl'],
      },
    }),
  },
  {
    label: '完成通知 finance',
    desc: 'on_complete → role:Finance',
    build: () => ({
      trigger: 'on_complete',
      channel: ['email'],
      recipients: [{ type: 'principal', ref: 'role:Finance' }],
      template: {
        subject: { 'zh-TW': '【流程完成】{{summary}}' },
        body: { 'zh-TW': '案件已完成所有審核步驟，請依結果處理。\n\n{{caseUrl}}' },
        variables: ['summary', 'caseUrl'],
      },
    }),
  },
  {
    label: '駁回通知申請人',
    desc: 'on_reject → submitter',
    build: () => ({
      trigger: 'on_reject',
      channel: ['email', 'in_app'],
      recipients: [{ type: 'submitter' }],
      template: {
        subject: { 'zh-TW': '【被駁回】您的申請需修改' },
        body: { 'zh-TW': '駁回原因：{{rejectReason}}\n請修正後重新送件：{{caseUrl}}' },
        variables: ['rejectReason', 'caseUrl'],
      },
    }),
  },
]

export function StepNotify({ draft, setDraft }: { draft: DraftSpec; setDraft: (d: DraftSpec) => void }) {
  const notifications = [...draft.notifications].sort((a, b) => a.id.localeCompare(b.id))
  const [activeId, setActiveId] = useState<string>(notifications[0]?.id ?? '')

  const safeActiveId = notifications.find(n => n.id === activeId)?.id ?? notifications[0]?.id ?? ''

  const upsert = (n: Notification) => {
    const others = draft.notifications.filter(x => x.id !== n.id)
    setDraft({ ...draft, notifications: [...others, n] })
  }
  const remove = (id: string) => {
    setDraft({ ...draft, notifications: draft.notifications.filter(x => x.id !== id) })
    if (activeId === id) {
      const next = draft.notifications.find(n => n.id !== id)
      if (next) setActiveId(next.id)
    }
  }
  const addPreset = (p: PresetNotification) => {
    const id = `notify_${Date.now().toString(36).slice(-4)}`
    const n: Notification = { id, ...p.build() }
    upsert(n)
    setActiveId(id)
  }
  const addBlank = () => addPreset({
    label: '',
    desc: '',
    build: () => ({
      trigger: 'on_assign',
      channel: ['email'],
      recipients: [{ type: 'current_approver' }],
      template: {
        subject: { 'zh-TW': '【新通知】' },
        body: { 'zh-TW': '案件已進入下一階段。\n\n請點此查看：{{caseUrl}}' },
        variables: ['caseUrl'],
      },
    }),
  })

  // Variable suggestion pool — union of all form-field ids + flow variables.
  // Templates often reference them via {{var}}; surface as clickable chips
  // beside the template editor.
  const fieldIds = Array.from(new Set(draft.userTasks.flatMap(t => t.fields.map(f => f.id)).filter(Boolean)))
  const variableNames = draft.variables.map(v => v.name).filter(Boolean)

  const activeNotification = notifications.find(n => n.id === safeActiveId)

  return (
    <div className="flex flex-col gap-4">
      <p className="text-xs text-ink-muted max-w-xl">
        每個通知有 trigger（什麼時候觸發）/ channel（email/in_app/teams）/ recipients / 雙語 template。
        NOTIFY 是 pure signal — 結構化 outbound data 走 INTEGRATIONS。
        本步驟非阻擋，可以全空。
      </p>

      {/* Preset strip — always visible to encourage common patterns */}
      <PresetStrip onPick={addPreset} onBlank={addBlank} />

      {notifications.length === 0 && (
        <div className="rounded border border-dashed border-rule p-10 text-center text-sm text-ink-faint">
          還沒有通知。上面選一個常見範本一鍵新增，或自訂空白通知。
        </div>
      )}

      {/* Pill row — one per notification */}
      {notifications.length > 1 && (
        <div className="flex flex-wrap gap-1.5">
          {notifications.map(n => {
            const isActive = n.id === safeActiveId
            const ok = notificationValid(n)
            return (
              <button
                key={n.id}
                onClick={() => setActiveId(n.id)}
                className={cn(
                  'flex items-center gap-2 rounded-md border px-3 py-1.5 text-xs font-medium transition-colors',
                  isActive
                    ? 'border-primary bg-primary/5 text-ink'
                    : 'border-rule bg-card text-ink-muted hover:border-primary/40 hover:text-ink',
                )}
              >
                {ok
                  ? <CheckCircle2 className={cn('h-3.5 w-3.5', isActive ? 'text-good' : 'text-good/70')} />
                  : <AlertCircle className={cn('h-3.5 w-3.5', isActive ? 'text-warn' : 'text-warn/70')} />}
                <Mail className={cn('h-3 w-3', isActive ? 'text-primary' : 'text-ink-faint')} />
                <span>{n.trigger}</span>
                <span className={cn('font-mono text-[10px]', isActive ? 'text-ink-muted' : 'text-ink-faint')}>
                  → {n.recipients.map(recipientLabel).join('+')}
                </span>
              </button>
            )
          })}
        </div>
      )}

      {/* Active notification editor */}
      {activeNotification && (
        <div className="rounded-md border border-rule bg-white">
          <div className="flex items-center justify-between gap-3 border-b border-rule bg-slate-50 px-3 py-2">
            <div className="flex items-center gap-2">
              <Mail className="h-4 w-4 text-primary" />
              <span className="font-mono text-[11px] text-ink-muted">{activeNotification.id}</span>
              <span className="text-sm font-semibold text-ink">{activeNotification.trigger}</span>
              <span className="text-[10px] text-ink-faint">[{activeNotification.channel.join('+')}]</span>
            </div>
            <button
              onClick={() => remove(activeNotification.id)}
              className="flex h-7 w-7 items-center justify-center rounded text-ink-faint hover:bg-rose-50 hover:text-danger"
              title="移除這個通知"
            >
              <Trash2 className="h-4 w-4" />
            </button>
          </div>

          <div className="space-y-3 p-3">
            <div className="grid grid-cols-2 gap-2">
              <Field label="ID" hint="snake_case">
                <Input
                  value={activeNotification.id}
                  onChange={e => upsert({ ...activeNotification, id: e.target.value.toLowerCase().replace(/\s+/g, '_') })}
                />
              </Field>
              <Field label="Trigger" required>
                <Select
                  value={activeNotification.trigger}
                  onChange={e => upsert({ ...activeNotification, trigger: e.target.value as NotifyTrigger })}
                >
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
                    id={`${activeNotification.id}-ch-${ch}`}
                    checked={activeNotification.channel.includes(ch)}
                    onChange={e => upsert({
                      ...activeNotification,
                      channel: e.target.checked
                        ? [...activeNotification.channel, ch]
                        : activeNotification.channel.filter(c => c !== ch),
                    })}
                    label={ch}
                  />
                ))}
              </div>
            </div>

            <RecipientsEditor
              recipients={activeNotification.recipients}
              onChange={r => upsert({ ...activeNotification, recipients: r })}
            />

            <TemplateEditor
              template={activeNotification.template}
              onChange={t => upsert({ ...activeNotification, template: t })}
              fieldIds={fieldIds}
              variableNames={variableNames}
            />
          </div>
        </div>
      )}
    </div>
  )
}

function notificationValid(n: Notification): boolean {
  if (n.recipients.length === 0) return false
  if (n.channel.length === 0) return false
  if (!n.template.subject['zh-TW'] || !n.template.body['zh-TW']) return false
  return true
}

function PresetStrip({
  onPick, onBlank,
}: { onPick: (p: PresetNotification) => void; onBlank: () => void }) {
  return (
    <div className="rounded-md border border-dashed border-rule bg-slate-50/40 p-2">
      <div className="mb-1.5 flex items-center gap-1.5">
        <Sparkles className="h-3.5 w-3.5 text-primary" />
        <p className="font-mono text-[10px] tracking-[0.14em] uppercase text-ink-muted">常見通知 — 一鍵新增</p>
      </div>
      <div className="flex flex-wrap gap-1">
        {PRESETS.map((p, i) => (
          <button
            key={i}
            type="button"
            onClick={() => onPick(p)}
            title={p.desc}
            className="flex flex-col items-start rounded border border-rule bg-white px-2 py-1 text-left hover:border-primary"
          >
            <span className="text-[11px] font-medium text-ink">{p.label}</span>
            <span className="font-mono text-[10px] text-ink-faint">{p.desc}</span>
          </button>
        ))}
        <button
          type="button"
          onClick={onBlank}
          className="flex items-center gap-1 rounded border border-dashed border-rule bg-white px-2 py-1 text-[11px] font-medium text-ink-muted hover:border-primary hover:text-primary"
        >
          <Plus className="h-3 w-3" />
          空白通知
        </button>
      </div>
    </div>
  )
}

function recipientLabel(r: NotifyRecipient): string {
  switch (r.type) {
    case 'submitter':        return 'submitter'
    case 'current_approver': return 'approver'
    case 'principal':        return r.ref || '(unset)'
    case 'expr':             return r.path
    case 'conditional':      return 'conditional'
    case 'collection':       return `${r.mode}(${r.actors.length})`
    case 'natural_language': return `nl:${r.text.slice(0, 12) || '(empty)'}`
  }
}

function emptyRecipient(type: 'submitter' | 'current_approver' | 'principal'): NotifyRecipient {
  switch (type) {
    case 'submitter':        return { type: 'submitter' }
    case 'current_approver': return { type: 'current_approver' }
    case 'principal':        return { type: 'principal', ref: '' }
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
              value={r.type === 'submitter' || r.type === 'current_approver' || r.type === 'principal' ? r.type : 'principal'}
              onChange={e => {
                const updated = [...recipients]
                updated[i] = emptyRecipient(e.target.value as 'submitter' | 'current_approver' | 'principal')
                onChange(updated)
              }}
            >
              {RECIPIENT_TYPES.map(t => <option key={t} value={t}>{t}</option>)}
            </Select>
            {r.type === 'principal' && (
              <PrincipalSinglePickerField
                value={r.ref}
                onChange={ref => {
                  const updated = [...recipients]
                  updated[i] = { type: 'principal', ref }
                  onChange(updated)
                }}
                modalTitle="挑一個收件人"
                placeholder="尚未選"
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
  template, onChange, fieldIds, variableNames,
}: {
  template: NotifyTemplate
  onChange: (t: NotifyTemplate) => void
  fieldIds: string[]
  variableNames: string[]
}) {
  // Common "system" template vars chef will always inject. Spell out
  // so the customer doesn't have to guess.
  const SYSTEM_VARS = ['caseUrl', 'applicant.name', 'summary', 'rejectReason']
  const allCandidates = [...SYSTEM_VARS, ...fieldIds, ...variableNames]
    .filter((v, i, arr) => arr.indexOf(v) === i)

  function insertVar(name: string) {
    const next = `${template.body['zh-TW']}{{${name}}}`
    const varsList = template.variables.includes(name)
      ? template.variables
      : [...template.variables, name]
    onChange({
      ...template,
      body: { ...template.body, 'zh-TW': next },
      variables: varsList,
    })
  }

  return (
    <div className="space-y-2 rounded border border-rule bg-slate-50 p-2">
      <p className="text-xs font-semibold text-ink">Template (zh-TW)</p>
      <Field label="Subject" required>
        <Input
          value={template.subject['zh-TW']}
          onChange={e => onChange({ ...template, subject: { ...template.subject, 'zh-TW': e.target.value } })}
        />
      </Field>
      <Field label="Body" required hint="用 {{var}} 引用變數；點下方 chip 自動插入到結尾">
        <Textarea
          rows={5}
          className="font-mono text-[11px]"
          value={template.body['zh-TW']}
          onChange={e => onChange({ ...template, body: { ...template.body, 'zh-TW': e.target.value } })}
        />
      </Field>

      {/* Variable chip strip */}
      <div>
        <p className="mb-1 font-mono text-[10px] tracking-[0.14em] uppercase text-ink-muted">
          可插入變數
        </p>
        <div className="flex flex-wrap gap-1">
          {allCandidates.map(name => (
            <button
              key={name}
              type="button"
              onClick={() => insertVar(name)}
              className="rounded border border-rule bg-white px-1.5 py-0.5 font-mono text-[10.5px] text-ink hover:border-primary hover:text-primary"
              title={`插入 {{${name}}} 並加進 variables 清單`}
            >
              {name}
            </button>
          ))}
          {allCandidates.length === 0 && (
            <span className="text-[10.5px] text-ink-faint">沒有可引用的欄位 / 變數</span>
          )}
        </div>
      </div>

      <Field label="Variables (newline-separated)" hint="必須涵蓋 body 用到的所有 {{var}}；點上面 chip 自動補">
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
