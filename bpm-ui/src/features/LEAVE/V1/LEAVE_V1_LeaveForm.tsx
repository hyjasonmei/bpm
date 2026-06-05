import { useMemo, useState } from 'react'
import { CalendarIcon } from 'lucide-react'
import { useNavigate } from 'react-router-dom'

import { SectionCard, SectionTitle } from '@/components/ui/card'
import { Field, InfoBanner, Input, Select, Textarea } from '@/components/ui/form'
import { ConfirmDialog } from '@/components/ui/ConfirmDialog'
import { FilePicker, type FilePickerValue } from '@/components/ui/FilePicker'
import { ActionFooter } from '@/components/ui/action-footer/ActionFooter'
import { FormShell } from '@/screens/forms/FormShell'
import type { FormComponentProps } from '@/features/registry'
import type { PersonaCode } from '@/lib/role'
import { apiFetch } from '@/lib/apiFetch'
import { businessDaysBetween, certRequired, LEAVE_TYPES } from './LEAVE_V1_shared'
import type { LEAVE_V1_CaseResponse } from './LEAVE_V1_types'

/** Bilingual display labels for the spec-aligned leave types. The <option>
 *  value stays the raw backend string (from LEAVE_TYPES) so the payload shape
 *  is unchanged — only the visible label gets an English gloss. */
const LEAVE_TYPE_LABELS: Record<string, string> = {
  特休: '特休 / Annual',
  病假: '病假 / Sick',
  事假: '事假 / Personal',
  公假: '公假 / Official',
}

/**
 * LEAVE V1 — submitter form (task_apply). Posts to /api/leave/v1.
 *
 * Per-flow approval / archive actions live on the case-detail page
 * (`LEAVE_V1_CaseDetail`) — the form component is create-only.
 */
export function LEAVE_V1_LeaveForm({ persona, mode = 'create', onSubmitted }: FormComponentProps) {
  const navigate = useNavigate()

  const [leaveType, setLeaveType] = useState<string>(LEAVE_TYPES[0])
  const [start, setStart] = useState('')
  const [end, setEnd] = useState('')
  const [reason, setReason] = useState('')
  const [cert, setCert] = useState<FilePickerValue | null>(null)
  const [pending, setPending] = useState(false)
  const [error, setError] = useState<string | null>(null)
  const [confirmOpen, setConfirmOpen] = useState(false)

  const days = useMemo(() => {
    if (!start || !end) return 0
    return businessDaysBetween(start, end)
  }, [start, end])

  const requiresCert = certRequired(leaveType)
  const validRange = !start || !end ? true : new Date(start).getTime() <= new Date(end).getTime()

  if (mode !== 'create') {
    // Approvals + HR archive are driven from the case-detail page.
    return (
      <FormShell code="LEAVE" activeStep={0} persona={persona as PersonaCode} mode="task">
        <SectionCard>
          <div className="px-5 py-8 text-center text-sm text-ink-muted">
            開啟此申請的詳細頁面以核准 / 退件 / 備案。請從首頁的「Pending My Approval」進入。
          </div>
        </SectionCard>
      </FormShell>
    )
  }

  function attemptSubmit() {
    if (!validRange || !days) { setError('請選擇有效的請假起訖日期。'); return }
    if (!reason.trim()) { setError('請填寫請假事由。'); return }
    if (requiresCert && !cert) { setError(`${leaveType}必須附上證明文件。`); return }
    setError(null)
    setConfirmOpen(true)
  }

  async function doSubmit() {
    setConfirmOpen(false)
    setPending(true)
    setError(null)
    try {
      const res = await apiFetch('/api/leave/v1', {
        method: 'POST',
        headers: { 'Content-Type': 'application/json' },
        body: JSON.stringify({
          leaveType,
          dateRange: { start, end },
          reason,
          certFileId: cert?.id ?? null,
        }),
      })
      if (!res.ok) {
        const text = await res.text()
        throw new Error(text || `HTTP ${res.status}`)
      }
      const body = (await res.json()) as LEAVE_V1_CaseResponse
      onSubmitted?.()
      navigate(`/cases/leave/${body.id}`)
    } catch (e) {
      setError(e instanceof Error ? e.message : String(e))
    } finally {
      setPending(false)
    }
  }

  return (
    <FormShell code="LEAVE" activeStep={0} persona={persona as PersonaCode} mode="create">
      <SectionCard>
        <SectionTitle>請假資訊 / Leave Detail</SectionTitle>

        <div className="grid grid-cols-12 gap-4 px-5 py-4">
          <Field label="假別 / Leave Type" required className="col-span-12 sm:col-span-4">
            <Select value={leaveType} onChange={e => setLeaveType(e.target.value)} disabled={pending}>
              {LEAVE_TYPES.map(t => <option key={t} value={t}>{LEAVE_TYPE_LABELS[t] ?? t}</option>)}
            </Select>
          </Field>

          {/* Start / end form a tidy date-range pair. */}
          <Field label="起 / Start date" required className="col-span-6 sm:col-span-4">
            <div className="relative">
              <Input type="date" value={start} onChange={e => setStart(e.target.value)} disabled={pending} />
              <CalendarIcon className="pointer-events-none absolute right-2 top-2 h-4 w-4 text-ink-faint" />
            </div>
          </Field>
          <Field label="迄 / End date" required className="col-span-6 sm:col-span-4">
            <div className="relative">
              <Input type="date" value={end} onChange={e => setEnd(e.target.value)} disabled={pending} />
              <CalendarIcon className="pointer-events-none absolute right-2 top-2 h-4 w-4 text-ink-faint" />
            </div>
          </Field>
        </div>

        {/* Computed days echo — read-only summary, mirrors the exemplar's total band. */}
        <div className="flex flex-wrap items-center justify-between gap-x-6 gap-y-1 border-t border-rule bg-slate-50 px-5 py-3">
          <span className="text-sm font-medium text-ink-muted">請假天數 / Days <span className="text-ink-faint">（自動計算，不含週末）</span></span>
          <span className="font-mono text-base">
            {!validRange ? <span className="font-semibold text-danger">起訖反向 / Invalid range</span>
              : days === 0 ? <span className="text-ink-faint">—</span>
              : <span className="text-ink"><span className="font-bold tabular">{days}</span> {days === 1 ? 'day' : 'days'}</span>}
          </span>
        </div>

        <div className="border-t border-rule px-5 py-3">
          <InfoBanner>
            提示：請假超過 7 天會自動加簽 VP / 部門主管；
            <span className="font-medium">病假 / 公假</span> 需附上證明文件。
          </InfoBanner>
        </div>
      </SectionCard>

      <SectionCard>
        <SectionTitle>事由與證明 / Reason & Documents</SectionTitle>
        <div className="grid grid-cols-12 gap-4 px-5 py-4">
          <Field label="事由 / Reason" required hint="中英文皆可" className="col-span-12 md:col-span-7">
            <Textarea
              rows={4}
              value={reason}
              onChange={e => setReason(e.target.value)}
              placeholder="e.g. 家庭旅遊 / Family trip"
              disabled={pending}
            />
          </Field>
          <Field
            label={`證明文件 / Supporting Documents${requiresCert ? ' *' : ' (選填)'}`}
            hint={requiresCert ? `${leaveType}必填，請上傳 PDF / JPG / PNG` : '視假別而定（病假 / 公假需附）'}
            className="col-span-12 md:col-span-5"
          >
            <FilePicker
              value={cert}
              onChange={setCert}
              disabled={pending}
              accept=".pdf,.png,.jpg,.jpeg"
              placeholder="PDF/JPG/PNG"
            />
          </Field>
        </div>
      </SectionCard>

      <ActionFooter
        hint={error ? <span className="text-danger">{error}</span> : <span>送出後將通知您的主管。</span>}
        actions={[
          { id: 'cancel', label: '取消', variant: 'ghost', disabled: pending, onClick: () => navigate('/') },
          { id: 'submit', label: '送出申請', variant: 'primary', pending, onClick: attemptSubmit },
        ]}
      />

      <ConfirmDialog
        open={confirmOpen}
        title="Submit leave request?"
        titleZh="送出請假申請？"
        description={`${days} ${days === 1 ? 'day' : 'days'} ${leaveType} will be sent to your manager for approval.`}
        tone="default"
        confirmText="確認送出"
        onCancel={() => setConfirmOpen(false)}
        onConfirm={doSubmit}
      />
    </FormShell>
  )
}
