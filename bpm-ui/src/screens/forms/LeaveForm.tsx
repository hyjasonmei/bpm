import { useEffect, useMemo, useState } from 'react'
import { Calendar as CalendarIcon, Paperclip, UserCheck } from 'lucide-react'

import { SectionCard, SectionTitle } from '@/components/ui/card'
import { Input, Textarea, Select, Field, InfoBanner } from '@/components/ui/form'
import { ConfirmDialog } from '@/components/ui/confirm-dialog'
import { FormShell, ActionBar } from './FormShell'
import { PERSONAS, type PersonaCode } from '@/lib/role'
import { MOCK_LEAVE_BALANCES, MOCK_USERS } from '@/lib/mocks'
import { FlowToast, useFormRuntime, type FormRuntimeProps } from '@/hooks/useFormRuntime'

const LEAVE_TYPES = [
  { id: '特休',  en: 'Annual',     zh: '特休' },
  { id: '病假',  en: 'Sick',       zh: '病假' },
  { id: '事假',  en: 'Personal',   zh: '事假' },
  { id: '婚假',  en: 'Marriage',   zh: '婚假' },
  { id: '喪假',  en: 'Bereavement',zh: '喪假' },
  { id: '產假',  en: 'Maternity',  zh: '產假' },
  { id: '陪產假',en: 'Paternity',  zh: '陪產假' },
  { id: '公假',  en: 'Other',      zh: '公假' },
] as const

const HALF_DAY_OPTS = [
  { id: 'full', label: 'Full days / 整天' },
  { id: 'am',   label: 'Half day (AM) / 上午半天' },
  { id: 'pm',   label: 'Half day (PM) / 下午半天' },
] as const

interface LeaveFormProps extends FormRuntimeProps {
  persona: PersonaCode
}

export function LeaveForm({ persona, ...rt }: LeaveFormProps) {
  const runtime = useFormRuntime('LEAVE', rt)
  const isTaskMode = runtime.mode === 'task'

  // Form fields (in task mode, populated from runtime.task.mergedFormData and read-only)
  const [leaveType, setLeaveType] = useState<string>('特休')
  const [start, setStart] = useState('')
  const [end, setEnd] = useState('')
  const [halfDay, setHalfDay] = useState<'full' | 'am' | 'pm'>('full')
  const [reason, setReason] = useState('')
  const [proxy, setProxy] = useState<string>('')
  const [contact, setContact] = useState('')

  // Hydrate from runtime task snapshot
  useEffect(() => {
    if (!isTaskMode || !runtime.task) return
    const fd = (runtime.task.mergedFormData ?? {}) as {
      leave_type?: string
      date_range?: { start?: string; end?: string }
      reason?: string
    }
    if (fd.leave_type) setLeaveType(fd.leave_type)
    if (fd.date_range?.start) setStart(fd.date_range.start)
    if (fd.date_range?.end) setEnd(fd.date_range.end)
    if (fd.reason) setReason(fd.reason)
  }, [isTaskMode, runtime.task])

  // Manager step fields (create-mode demo only)
  const [managerComment, setManagerComment] = useState('')

  const [confirm, setConfirm] = useState<null | { title: string; titleZh?: string; description?: string; tone?: 'danger' | 'default'; onConfirm: () => void }>(null)

  const totalDays = useMemo(() => calcWorkingDays(start, end, halfDay), [start, end, halfDay])
  const validRange = !start || !end ? null : new Date(start).getTime() <= new Date(end).getTime()
  const days = validRange === false ? null : totalDays

  // Read-only when we're in task mode (the runtime owns the form data) — independent of step
  const fieldsReadOnly = isTaskMode

  // The legacy demo's per-step active highlight; in task mode we just show step 1 (or
  // the runtime task's nodeId if it maps cleanly — keep simple for now).
  const activeStep = isTaskMode ? 0 : 0

  /** Build the JSON payload that matches sample_specs/leave_v1.json field ids. */
  function toSpecPayload() {
    return {
      leave_type: leaveType,
      date_range: { start, end },
      days: days ?? 0,
      reason,
      // Optional fields — included for downstream use even if spec marks them optional
      half_day: halfDay,
      proxy_user: proxy || undefined,
      contact_during_leave: contact || undefined,
    }
  }

  return (
    <FormShell code="LEAVE" activeStep={activeStep} setActiveStep={() => {}} persona={persona} mode={runtime.mode}>
      <FlowToast message={runtime.toast} />

      {/* APPLY step content (always rendered for visibility — read-only after submit) */}
      <SectionCard>
        <SectionTitle>Leave Detail / 假別資訊</SectionTitle>
        <div className="grid grid-cols-3 gap-4 p-5">
          <Field label="Leave Type / 假別" required>
            <Select value={leaveType} onChange={e => setLeaveType(e.target.value)} disabled={fieldsReadOnly}>
              {LEAVE_TYPES.map(t => <option key={t.id} value={t.id}>{t.en} / {t.zh}</option>)}
            </Select>
          </Field>
          <Field label="Half day option / 假時長度">
            <Select value={halfDay} onChange={e => setHalfDay(e.target.value as 'full' | 'am' | 'pm')} disabled={fieldsReadOnly}>
              {HALF_DAY_OPTS.map(t => <option key={t.id} value={t.id}>{t.label}</option>)}
            </Select>
          </Field>
          <Field label="Total days / 總請假天數">
            <div className="flex h-8 items-center rounded-md border border-rule bg-slate-50 px-3 text-sm font-mono">
              {days === null ? <span className="text-danger">Invalid range</span>
                : days === 0 ? <span className="text-ink-faint">—</span>
                : <span className="text-ink"><span className="font-bold tabular">{days}</span> {days === 1 ? 'day' : 'days'}</span>}
            </div>
          </Field>
          <Field label="Start date / 開始日期" required>
            <div className="relative">
              <Input type="date" value={start} onChange={e => setStart(e.target.value)} readOnly={fieldsReadOnly} />
              <CalendarIcon className="pointer-events-none absolute right-2 top-2 h-4 w-4 text-ink-faint" />
            </div>
          </Field>
          <Field label="End date / 結束日期" required>
            <div className="relative">
              <Input type="date" value={end} onChange={e => setEnd(e.target.value)} readOnly={fieldsReadOnly} />
              <CalendarIcon className="pointer-events-none absolute right-2 top-2 h-4 w-4 text-ink-faint" />
            </div>
          </Field>
          <Field label="Contact during leave / 假期聯絡方式" hint="Phone, email, or 'unreachable'">
            <Input value={contact} onChange={e => setContact(e.target.value)} placeholder="e.g. +886 9xx xxx xxx / WeChat: ..." readOnly={fieldsReadOnly} />
          </Field>
        </div>

        <div className="border-t border-rule px-5 py-4">
          <Field label="Reason / 請假事由" required>
            <Textarea
              rows={3}
              value={reason}
              onChange={e => setReason(e.target.value)}
              placeholder="e.g. 家庭旅遊 / Family trip with kids during school break"
              readOnly={fieldsReadOnly}
            />
          </Field>
        </div>

        <div className="grid grid-cols-2 gap-4 border-t border-rule px-5 py-4">
          <Field label="Proxy / Delegated approver during leave / 代理人">
            <Select value={proxy} onChange={e => setProxy(e.target.value)} disabled={fieldsReadOnly}>
              <option value="">— Select a colleague —</option>
              {MOCK_USERS.filter(u => u.role === 'employee' && u.id !== 'wilson').map(u => (
                <option key={u.id} value={u.id}>{u.name} — {u.dept.split(' - ')[1] ?? u.dept}</option>
              ))}
            </Select>
          </Field>
          <Field label="Attachment / 附件" hint="Optional — medical certificate, etc.">
            <label className="flex h-8 cursor-pointer items-center gap-2 rounded-md border border-dashed border-rule bg-white px-3 text-sm text-ink-muted hover:bg-slate-50">
              <Paperclip className="h-3.5 w-3.5" />
              Click or drop file here (≤ 10MB)
              <input type="file" className="hidden" disabled={fieldsReadOnly} />
            </label>
          </Field>
        </div>
      </SectionCard>

      {/* HR-only — annual leave balance (read-only) */}
      {persona === 'hr' && (
        <SectionCard>
          <SectionTitle>HR — Annual Leave Balance / 年假餘額</SectionTitle>
          <div className="grid grid-cols-3 gap-4 p-5">
            <BalanceTile label="Annual / 特休"  value={MOCK_LEAVE_BALANCES.wilson.annual}  unit="days" tone="green" />
            <BalanceTile label="Sick / 病假"    value={MOCK_LEAVE_BALANCES.wilson.sick}    unit="days" tone="blue" />
            <BalanceTile label="Personal / 事假" value={MOCK_LEAVE_BALANCES.wilson.personal} unit="days" tone="amber" />
          </div>
          <InfoBanner>
            Pulled from HR records. After "Record & Close", balance updates SHALL be persisted by the workflow engine.
          </InfoBanner>
        </SectionCard>
      )}

      {/* Manager step — create-mode demo only (task mode collects comment via ActionBar dialog) */}
      {!isTaskMode && persona === 'manager' && (
        <SectionCard>
          <SectionTitle>
            <span className="inline-flex items-center gap-2"><UserCheck className="h-4 w-4 text-amber-600" /> Manager Decision / 主管簽核</span>
          </SectionTitle>
          <div className="space-y-3 p-5">
            <p className="text-sm text-ink-muted">
              Reviewing on behalf of <span className="font-semibold">{PERSONAS.manager.user.name}</span>.
              In create mode this is a visual mock — the runtime drives real decisions when opened from inbox.
            </p>
            <Field label="Comment to applicant / 簽核意見">
              <Textarea
                rows={3}
                value={managerComment}
                onChange={e => setManagerComment(e.target.value)}
                placeholder="Optional. Explain any condition / context for the decision."
              />
            </Field>
          </div>
        </SectionCard>
      )}

      {/* Action bar */}
      <ActionBar
        code="LEAVE"
        activeStep={activeStep}
        persona={persona}
        mode={runtime.mode}
        nodeKind={runtime.task?.task.nodeKind}
        pending={runtime.pending}
        onSubmit={() => {
          if (isTaskMode) {
            void runtime.submitUserTask()
            return
          }
          if (days === null || !days) { runtime.fireToast('Pick a valid date range first.'); return }
          if (!reason.trim()) { runtime.fireToast('Reason is required.'); return }
          setConfirm({
            title: 'Submit leave request?',
            titleZh: '送出請假申請？',
            description: `${days} ${days === 1 ? 'day' : 'days'} of ${LEAVE_TYPES.find(t => t.id === leaveType)?.en} leave will be sent to your manager for approval.`,
            tone: 'default',
            onConfirm: () => { void runtime.submitCreate(toSpecPayload()) },
          })
        }}
        onApprove={c => { void runtime.approve(c) }}
        onReject={c => { void runtime.reject(c) }}
        onReturnTask={c => { void runtime.returnTask(c) }}
        onClose={() => runtime.fireToast('Use the Create menu to start a fresh request.')}
      />

      <ConfirmDialog
        open={!!confirm}
        title={confirm?.title ?? ''}
        titleZh={confirm?.titleZh}
        description={confirm?.description}
        tone={confirm?.tone}
        confirmText={confirm?.tone === 'danger' ? 'Confirm reject' : 'Confirm'}
        onCancel={() => setConfirm(null)}
        onConfirm={() => { confirm?.onConfirm(); setConfirm(null) }}
      />
    </FormShell>
  )
}

function BalanceTile({ label, value, unit, tone }: { label: string; value: number; unit: string; tone: 'green' | 'blue' | 'amber' }) {
  const ring = { green: 'border-green-200 bg-green-50/30', blue: 'border-blue-200 bg-blue-50/30', amber: 'border-amber-200 bg-amber-50/30' }[tone]
  const text = { green: 'text-green-700', blue: 'text-blue-700', amber: 'text-amber-700' }[tone]
  return (
    <div className={`rounded-lg border p-4 ${ring}`}>
      <div className="text-[11px] font-semibold uppercase tracking-wider text-ink-muted">{label}</div>
      <div className="mt-1 flex items-baseline gap-1.5">
        <span className={`text-3xl font-bold tabular ${text}`}>{value}</span>
        <span className="text-xs text-ink-faint">{unit}</span>
      </div>
    </div>
  )
}

/* Working days excluding weekends; supports half-day off the start. */
function calcWorkingDays(start: string, end: string, half: 'full' | 'am' | 'pm'): number {
  if (!start || !end) return 0
  const s = new Date(start)
  const e = new Date(end)
  if (Number.isNaN(s.getTime()) || Number.isNaN(e.getTime())) return 0
  if (s.getTime() > e.getTime()) return 0
  let count = 0
  const cur = new Date(s)
  while (cur.getTime() <= e.getTime()) {
    const wd = cur.getDay()
    if (wd !== 0 && wd !== 6) count += 1
    cur.setDate(cur.getDate() + 1)
  }
  if (half !== 'full' && count > 0) count -= 0.5
  return count
}
