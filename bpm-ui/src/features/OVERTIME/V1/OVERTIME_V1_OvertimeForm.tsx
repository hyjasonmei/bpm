import { useState } from 'react'
import { CalendarIcon } from 'lucide-react'
import { useNavigate } from 'react-router-dom'

import { SectionCard, SectionTitle } from '@/components/ui/card'
import { Field, InfoBanner, Input, Textarea } from '@/components/ui/form'
import { ConfirmDialog } from '@/components/ui/ConfirmDialog'
import { ActionFooter } from '@/components/ui/action-footer/ActionFooter'
import { FormShell } from '@/screens/forms/FormShell'
import type { FormComponentProps } from '@/features/registry'
import type { PersonaCode } from '@/lib/role'
import { apiFetch } from '@/lib/apiFetch'
import type { OVERTIME_V1_CaseResponse, OVERTIME_V1_SubmitPayload } from './OVERTIME_V1_types'

const emptyPayload = (): OVERTIME_V1_SubmitPayload => ({
  overtimeDate: '',
  startTime: '',
  endTime: '',
  estimatedHours: '',
  overtimeReason: '',
})

/**
 * OVERTIME V1 — submitter form (userTask `task_apply`). Posts to
 * <c>POST /api/overtime/v1</c>. Decision UI (主管審核 / 人資複核) lives on
 * <see cref="OVERTIME_V1_CaseDetail" />.
 */
export function OVERTIME_V1_OvertimeForm({ persona, mode = 'create', onSubmitted }: FormComponentProps) {
  const navigate = useNavigate()
  const [form, setForm] = useState<OVERTIME_V1_SubmitPayload>(emptyPayload())
  const [pending, setPending] = useState(false)
  const [error, setError] = useState<string | null>(null)
  const [confirmOpen, setConfirmOpen] = useState(false)

  function patch(p: Partial<OVERTIME_V1_SubmitPayload>) {
    setForm(prev => ({ ...prev, ...p }))
  }

  if (mode !== 'create') {
    return (
      <FormShell code="OVERTIME" activeStep={0} persona={persona as PersonaCode} mode="task">
        <SectionCard>
          <div className="px-5 py-8 text-center text-sm text-ink-muted">
            開啟此申請的詳細頁面以核准 / 退件。請從首頁的「Pending My Approval」進入。
          </div>
        </SectionCard>
      </FormShell>
    )
  }

  const hoursNum = form.estimatedHours.trim() === '' ? null : Number(form.estimatedHours)
  const hoursValid = hoursNum === null || (Number.isFinite(hoursNum) && hoursNum >= 0)
  const valid = !!form.overtimeDate && !!form.overtimeReason.trim() && hoursValid

  function attemptSubmit() {
    if (!valid) {
      setError('請填寫加班日期與加班事由；預估時數若填寫需為 0 或正數。')
      return
    }
    setError(null)
    setConfirmOpen(true)
  }

  async function doSubmit() {
    setConfirmOpen(false)
    setPending(true)
    setError(null)
    const payload = {
      overtimeDate: form.overtimeDate,
      startTime: form.startTime.trim() || null,
      endTime: form.endTime.trim() || null,
      estimatedHours: hoursNum,
      overtimeReason: form.overtimeReason.trim(),
    }
    try {
      const res = await apiFetch('/api/overtime/v1', {
        method: 'POST',
        headers: { 'Content-Type': 'application/json' },
        body: JSON.stringify(payload),
      })
      if (!res.ok) throw new Error(await res.text() || `HTTP ${res.status}`)
      const body = (await res.json()) as OVERTIME_V1_CaseResponse
      onSubmitted?.()
      navigate(`/cases/overtime/${body.id}`)
    } catch (e) {
      setError(e instanceof Error ? e.message : String(e))
    } finally {
      setPending(false)
    }
  }

  return (
    <FormShell code="OVERTIME" activeStep={0} persona={persona as PersonaCode} mode="create">
      <SectionCard>
        <SectionTitle>加班資訊 / Overtime Request</SectionTitle>
        <div className="border-b border-rule px-5 py-3">
          <InfoBanner>
            請填寫加班資訊；加班日期與加班事由為必填。送出後將通知您的直屬主管審核，
            當月累計加班時數超過 20 小時者將再交由人資複核。
          </InfoBanner>
        </div>
        <div className="grid grid-cols-12 gap-3 px-5 py-4">
          <Field label="加班日期 / Overtime Date" required className="col-span-6">
            <div className="relative">
              <Input type="date" value={form.overtimeDate} onChange={e => patch({ overtimeDate: e.target.value })} disabled={pending} />
              <CalendarIcon className="pointer-events-none absolute right-2 top-2 h-4 w-4 text-ink-faint" />
            </div>
          </Field>
          <Field label="預估時數 / Estimated Hours" className="col-span-6">
            <div className="flex gap-1.5">
              <Input type="number" min="0" step="0.5" className="text-right font-mono" value={form.estimatedHours} onChange={e => patch({ estimatedHours: e.target.value })} disabled={pending} placeholder="0" />
              <span className="flex h-8 items-center whitespace-nowrap rounded-md border border-rule bg-slate-50 px-2.5 text-sm text-ink-muted">小時</span>
            </div>
          </Field>

          <Field label="開始時間 / Start Time" className="col-span-6">
            <Input type="time" value={form.startTime} onChange={e => patch({ startTime: e.target.value })} disabled={pending} />
          </Field>
          <Field label="結束時間 / End Time" className="col-span-6">
            <Input type="time" value={form.endTime} onChange={e => patch({ endTime: e.target.value })} disabled={pending} />
          </Field>

          <Field label="加班事由 / Reason" required className="col-span-12">
            <Textarea rows={3} value={form.overtimeReason} onChange={e => patch({ overtimeReason: e.target.value })} disabled={pending} placeholder="請說明加班原因與工作內容" />
          </Field>
        </div>
      </SectionCard>

      <ActionFooter
        hint={error
          ? <span className="text-danger">{error}</span>
          : <span>送出後將通知您的直屬主管。</span>}
        actions={[
          { id: 'cancel', label: '取消', variant: 'ghost', disabled: pending, onClick: () => navigate('/') },
          { id: 'submit', label: '送出申請', variant: 'primary', pending, disabled: !valid, onClick: attemptSubmit },
        ]}
      />

      <ConfirmDialog
        open={confirmOpen}
        title="Submit overtime request?"
        titleZh="送出加班申請？"
        description="此申請將送交您的直屬主管審核。"
        tone="default"
        confirmText="確認送出"
        onCancel={() => setConfirmOpen(false)}
        onConfirm={doSubmit}
      />
    </FormShell>
  )
}
