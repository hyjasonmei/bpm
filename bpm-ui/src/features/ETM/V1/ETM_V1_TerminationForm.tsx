import { useEffect, useState } from 'react'
import { CalendarIcon } from 'lucide-react'
import { useNavigate, useSearchParams } from 'react-router-dom'

import { SectionCard, SectionTitle } from '@/components/ui/card'
import { Field, InfoBanner, Input, Select } from '@/components/ui/form'
import { ConfirmDialog } from '@/components/ui/ConfirmDialog'
import { ActionFooter } from '@/components/ui/action-footer/ActionFooter'
import { FormShell } from '@/screens/forms/FormShell'
import type { FormComponentProps } from '@/features/registry'
import type { PersonaCode } from '@/lib/role'
import { apiFetch } from '@/lib/apiFetch'
import { REASON_OPTIONS, YESNO_OPTIONS } from './ETM_V1_shared'
import type { ETM_V1_CaseResponse } from './ETM_V1_types'

interface FormState { employeeName: string; employeeId: string; lastWorkingDate: string; reason: string; provideCertificate: string }
const empty = (): FormState => ({ employeeName: '', employeeId: '', lastWorkingDate: '', reason: 'Resignation', provideCertificate: 'Yes' })

/** ETM V1 — submitter form (userTask `req`): termination request. */
export function ETM_V1_TerminationForm({ persona, mode = 'create', onSubmitted }: FormComponentProps) {
  const navigate = useNavigate()
  const [params] = useSearchParams()
  const resubmitCaseId = params.get('resubmit')
  const isResubmit = !!resubmitCaseId

  const [f, setF] = useState<FormState>(empty())
  const [loading, setLoading] = useState(isResubmit)
  const [pending, setPending] = useState(false)
  const [error, setError] = useState<string | null>(null)
  const [confirmOpen, setConfirmOpen] = useState(false)

  useEffect(() => {
    if (!isResubmit) return
    let cancelled = false
    void (async () => {
      try {
        const res = await apiFetch(`/api/etm/v1/${resubmitCaseId}`)
        if (!res.ok) throw new Error(`HTTP ${res.status}`)
        const b = (await res.json()) as ETM_V1_CaseResponse
        if (cancelled) return
        setF({
          employeeName: b.employeeName, employeeId: b.employeeId,
          lastWorkingDate: b.lastWorkingDate?.slice(0, 10) ?? '', reason: b.reason,
          provideCertificate: b.provideCertificate ?? 'Yes',
        })
      } catch (e) {
        if (!cancelled) setError(e instanceof Error ? e.message : String(e))
      } finally {
        if (!cancelled) setLoading(false)
      }
    })()
    return () => { cancelled = true }
  }, [isResubmit, resubmitCaseId])

  const patch = (p: Partial<FormState>) => setF(prev => ({ ...prev, ...p }))

  if (mode !== 'create') {
    return (
      <FormShell code="ETM" activeStep={0} persona={persona as PersonaCode} mode="task">
        <SectionCard><div className="px-5 py-8 text-center text-sm text-ink-muted">請從首頁「Pending My Approval」進入以核准 / 交接。</div></SectionCard>
      </FormShell>
    )
  }

  const valid = !!f.employeeName.trim() && !!f.employeeId.trim() && !!f.lastWorkingDate && !!f.reason

  function attemptSubmit() {
    if (!valid) { setError('請填寫員工姓名 / 編號 / 最後工作日 / 離職原因。'); return }
    setError(null); setConfirmOpen(true)
  }

  async function doSubmit() {
    setConfirmOpen(false); setPending(true); setError(null)
    const payload = {
      employeeName: f.employeeName.trim(), employeeId: f.employeeId.trim(),
      lastWorkingDate: f.lastWorkingDate, reason: f.reason, provideCertificate: f.provideCertificate,
    }
    const url = isResubmit ? `/api/etm/v1/${resubmitCaseId}/resubmit` : '/api/etm/v1'
    try {
      const res = await apiFetch(url, { method: 'POST', headers: { 'Content-Type': 'application/json' }, body: JSON.stringify(payload) })
      if (!res.ok) throw new Error(await res.text() || `HTTP ${res.status}`)
      const b = (await res.json()) as ETM_V1_CaseResponse
      onSubmitted?.()
      navigate(`/cases/etm/${b.id}`)
    } catch (e) {
      setError(e instanceof Error ? e.message : String(e))
    } finally {
      setPending(false)
    }
  }

  if (loading) {
    return (
      <FormShell code="ETM" activeStep={0} persona={persona as PersonaCode} mode="create">
        <SectionCard>
          <SectionTitle>離職申請 / Termination</SectionTitle>
          <div className="px-5 py-10 text-center text-sm text-ink-muted">載入中…</div>
        </SectionCard>
      </FormShell>
    )
  }

  return (
    <FormShell code="ETM" activeStep={0} persona={persona as PersonaCode} mode="create">
      <SectionCard>
        <SectionTitle>申請說明 / Overview</SectionTitle>
        <div className="px-5 py-4">
          <InfoBanner>
            填寫離職員工資訊與最後工作日；送出後將通知您的主管核准，交接 / 物品返還將於核准後進行。
            {isResubmit && <span className="mt-1 block text-amber-900">此案件先前被退回，請依照退件意見修正後重新送出（將進入新的審核回合）。</span>}
          </InfoBanner>
        </div>
      </SectionCard>

      <SectionCard>
        <SectionTitle>員工資訊 / Employee</SectionTitle>
        <div className="grid grid-cols-12 gap-3 px-5 py-4">
          <Field label="員工姓名 / Employee Name" required className="col-span-6">
            <Input value={f.employeeName} onChange={e => patch({ employeeName: e.target.value })} disabled={pending} placeholder="e.g. 王小明" />
          </Field>
          <Field label="員工編號 / Employee ID" required className="col-span-6">
            <Input value={f.employeeId} onChange={e => patch({ employeeId: e.target.value })} disabled={pending} placeholder="EMP-001" />
          </Field>
        </div>
      </SectionCard>

      <SectionCard>
        <SectionTitle>離職資訊 / Separation</SectionTitle>
        <div className="grid grid-cols-12 gap-3 px-5 py-4">
          <Field label="最後工作日 / Last Working Date" required className="col-span-6" hint="員工最後一個實際工作日">
            <div className="relative">
              <Input type="date" value={f.lastWorkingDate} onChange={e => patch({ lastWorkingDate: e.target.value })} disabled={pending} />
              <CalendarIcon className="pointer-events-none absolute right-2 top-2 h-4 w-4 text-ink-faint" />
            </div>
          </Field>
          <Field label="離職原因 / Reason" required className="col-span-3">
            <Select value={f.reason} onChange={e => patch({ reason: e.target.value })} disabled={pending}>
              {REASON_OPTIONS.map(o => <option key={o} value={o}>{o}</option>)}
            </Select>
          </Field>
          <Field label="提供離職證明 / Certificate" className="col-span-3" hint="是否開立離職證明">
            <Select value={f.provideCertificate} onChange={e => patch({ provideCertificate: e.target.value })} disabled={pending}>
              {YESNO_OPTIONS.map(o => <option key={o} value={o}>{o}</option>)}
            </Select>
          </Field>
        </div>
      </SectionCard>

      <ActionFooter
        hint={error ? <span className="text-danger">{error}</span> : <span>送出後將通知您的主管。交接將於核准後進行。</span>}
        actions={[
          { id: 'cancel', label: '取消', variant: 'ghost', disabled: pending, onClick: () => navigate('/') },
          { id: 'submit', label: isResubmit ? '重新送出' : '送出申請', variant: 'primary', pending, disabled: !valid, onClick: attemptSubmit },
        ]}
      />

      <ConfirmDialog
        open={confirmOpen}
        title={isResubmit ? 'Resubmit termination?' : 'Submit termination?'}
        titleZh={isResubmit ? '重新送出員工離職？' : '送出員工離職？'}
        description={`${f.employeeName} will be sent to your manager for approval.`}
        tone="default"
        confirmText={isResubmit ? '確認重新送出' : '確認送出'}
        onCancel={() => setConfirmOpen(false)}
        onConfirm={doSubmit}
      />
    </FormShell>
  )
}
