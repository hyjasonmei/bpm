import { useMemo, useState } from 'react'
import { Calendar as CalendarIcon, Paperclip, UserCheck } from 'lucide-react'

import { SectionCard, SectionTitle } from '@/components/ui/card'
import { Input, Textarea, Select, Field, InfoBanner } from '@/components/ui/form'
import { ConfirmDialog } from '@/components/ui/confirm-dialog'
import { FormShell, ActionBar } from './FormShell'
import { FORMS } from '@/lib/workflow'
import { PERSONAS, type PersonaCode } from '@/lib/role'
import { MOCK_LEAVE_BALANCES, MOCK_USERS } from '@/lib/mocks'

const LEAVE_TYPES = [
  { id: 'annual',     en: 'Annual',     zh: '特休' },
  { id: 'sick',       en: 'Sick',       zh: '病假' },
  { id: 'personal',   en: 'Personal',   zh: '事假' },
  { id: 'marriage',   en: 'Marriage',   zh: '婚假' },
  { id: 'bereavement',en: 'Bereavement',zh: '喪假' },
  { id: 'maternity',  en: 'Maternity',  zh: '產假' },
  { id: 'paternity',  en: 'Paternity',  zh: '陪產假' },
  { id: 'other',      en: 'Other',      zh: '其他' },
] as const

const HALF_DAY_OPTS = [
  { id: 'full', label: 'Full days / 整天' },
  { id: 'am',   label: 'Half day (AM) / 上午半天' },
  { id: 'pm',   label: 'Half day (PM) / 下午半天' },
] as const

interface LeaveFormProps { persona: PersonaCode }

export function LeaveForm({ persona }: LeaveFormProps) {
  const def = FORMS.LEAVE
  const [activeStep, setActiveStep] = useState(def.initialActive)

  // Form fields
  const [leaveType, setLeaveType] = useState<string>('annual')
  const [start, setStart] = useState('')
  const [end, setEnd] = useState('')
  const [halfDay, setHalfDay] = useState<'full' | 'am' | 'pm'>('full')
  const [reason, setReason] = useState('')
  const [proxy, setProxy] = useState<string>('')
  const [contact, setContact] = useState('')

  // Manager step fields
  const [managerComment, setManagerComment] = useState('')

  // Toast / dialog
  const [toast, setToast] = useState<string | null>(null)
  const [confirm, setConfirm] = useState<null | { title: string; titleZh?: string; description?: string; tone?: 'danger' | 'default'; onConfirm: () => void }>(null)

  const totalDays = useMemo(() => calcWorkingDays(start, end, halfDay), [start, end, halfDay])
  const validRange = !start || !end ? null : new Date(start).getTime() <= new Date(end).getTime()
  const days = validRange === false ? null : totalDays

  const fireToast = (m: string) => { setToast(m); setTimeout(() => setToast(null), 2400) }

  const goNext = () => setActiveStep(s => Math.min(def.steps.length - 1, s + 1))
  const goReject = () => { fireToast('Returned to applicant — they will revise & re-submit'); setActiveStep(0) }

  return (
    <FormShell code="LEAVE" activeStep={activeStep} setActiveStep={setActiveStep} persona={persona}>
      {toast && (
        <div className="fixed bottom-6 right-6 z-50 rounded-lg bg-slate-800 px-4 py-2.5 text-sm text-white shadow-2xl">
          {toast}
        </div>
      )}

      {/* APPLY step content (always rendered for visibility — read-only after submit) */}
      <SectionCard>
        <SectionTitle>Leave Detail / 假別資訊</SectionTitle>
        <div className="grid grid-cols-3 gap-4 p-5">
          <Field label="Leave Type / 假別" required>
            <Select value={leaveType} onChange={e => setLeaveType(e.target.value)} disabled={activeStep > 0}>
              {LEAVE_TYPES.map(t => <option key={t.id} value={t.id}>{t.en} / {t.zh}</option>)}
            </Select>
          </Field>
          <Field label="Half day option / 假時長度">
            <Select value={halfDay} onChange={e => setHalfDay(e.target.value as 'full' | 'am' | 'pm')} disabled={activeStep > 0}>
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
              <Input type="date" value={start} onChange={e => setStart(e.target.value)} readOnly={activeStep > 0} />
              <CalendarIcon className="pointer-events-none absolute right-2 top-2 h-4 w-4 text-ink-faint" />
            </div>
          </Field>
          <Field label="End date / 結束日期" required>
            <div className="relative">
              <Input type="date" value={end} onChange={e => setEnd(e.target.value)} readOnly={activeStep > 0} />
              <CalendarIcon className="pointer-events-none absolute right-2 top-2 h-4 w-4 text-ink-faint" />
            </div>
          </Field>
          <Field label="Contact during leave / 假期聯絡方式" hint="Phone, email, or 'unreachable'">
            <Input value={contact} onChange={e => setContact(e.target.value)} placeholder="e.g. +886 9xx xxx xxx / WeChat: ..." readOnly={activeStep > 0} />
          </Field>
        </div>

        <div className="border-t border-rule px-5 py-4">
          <Field label="Reason / 請假事由" required>
            <Textarea
              rows={3}
              value={reason}
              onChange={e => setReason(e.target.value)}
              placeholder="e.g. 家庭旅遊 / Family trip with kids during school break"
              readOnly={activeStep > 0}
            />
          </Field>
        </div>

        <div className="grid grid-cols-2 gap-4 border-t border-rule px-5 py-4">
          <Field label="Proxy / Delegated approver during leave / 代理人">
            <Select value={proxy} onChange={e => setProxy(e.target.value)} disabled={activeStep > 0}>
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
              <input type="file" className="hidden" disabled={activeStep > 0} />
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

      {/* Manager step — comment + decision */}
      {activeStep === 1 && persona === 'manager' && (
        <SectionCard>
          <SectionTitle>
            <span className="inline-flex items-center gap-2"><UserCheck className="h-4 w-4 text-amber-600" /> Manager Decision / 主管簽核</span>
          </SectionTitle>
          <div className="space-y-3 p-5">
            <p className="text-sm text-ink-muted">
              Reviewing on behalf of <span className="font-semibold">{PERSONAS.manager.user.name}</span>.
              You can <strong className="text-good">approve</strong>, <strong className="text-amber-600">return</strong> for revision, or <strong className="text-danger">reject</strong>.
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

      {/* HR step */}
      {activeStep === 2 && persona === 'hr' && (
        <SectionCard>
          <SectionTitle>HR Record / 人資登錄</SectionTitle>
          <div className="space-y-3 p-5">
            <p className="text-sm text-ink-muted">
              Manager has approved. Confirm and post to attendance system to deduct from the requestor's balance, then close.
            </p>
            <Field label="HR comment / 人資備註">
              <Textarea rows={2} placeholder="Optional. Internal note for HR archive." />
            </Field>
          </div>
        </SectionCard>
      )}

      {/* Action bar */}
      <ActionBar
        code="LEAVE"
        activeStep={activeStep}
        persona={persona}
        onSubmit={() => {
          if (days === null || !days) { fireToast('Pick a valid date range first.'); return }
          if (!reason.trim()) { fireToast('Reason is required.'); return }
          setConfirm({
            title: 'Submit leave request?',
            titleZh: '送出請假申請？',
            description: `${days} ${days === 1 ? 'day' : 'days'} of ${LEAVE_TYPES.find(t => t.id === leaveType)?.en} leave will be sent to your manager for approval.`,
            tone: 'default',
            onConfirm: () => { goNext(); fireToast('Submitted. Awaiting manager approval.') },
          })
        }}
        onApprove={() => { goNext(); fireToast(activeStep === 1 ? 'Approved. Sent to HR.' : 'Recorded. Case closed.') }}
        onReject={() => setConfirm({
          title: activeStep === 1 ? 'Reject this leave?' : 'Reject and return?',
          titleZh: '退回此申請？',
          description: 'Applicant will be notified. They can revise and re-submit.',
          tone: 'danger',
          onConfirm: () => { goReject() },
        })}
        onClose={() => fireToast('Use the Create menu to start a fresh request.')}
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
