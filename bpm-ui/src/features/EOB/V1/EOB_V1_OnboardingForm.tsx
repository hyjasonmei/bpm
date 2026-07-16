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
import { LOCATION_OPTIONS, YESNO_OPTIONS } from './EOB_V1_shared'
import type { EOB_V1_CaseResponse } from './EOB_V1_types'

interface FormState {
  firstName: string; lastName: string; businessTitle: string; employeeLocation: string
  onboardDate: string; requireMailbox: string; costCenter: string
  contractNumber: string; contractEffectiveDate: string; contractExpirationDate: string
}
const empty = (): FormState => ({
  firstName: '', lastName: '', businessTitle: '', employeeLocation: 'APAC',
  onboardDate: '', requireMailbox: 'Yes', costCenter: '',
  contractNumber: '', contractEffectiveDate: '', contractExpirationDate: '',
})

/** EOB V1 — submitter form (userTask `req`): new-hire request. */
export function EOB_V1_OnboardingForm({ persona, mode = 'create', onSubmitted }: FormComponentProps) {
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
        const res = await apiFetch(`/api/eob/v1/${resubmitCaseId}`)
        if (!res.ok) throw new Error(`HTTP ${res.status}`)
        const b = (await res.json()) as EOB_V1_CaseResponse
        if (cancelled) return
        setF({
          firstName: b.firstName, lastName: b.lastName, businessTitle: b.businessTitle,
          employeeLocation: b.employeeLocation, onboardDate: b.onboardDate?.slice(0, 10) ?? '',
          requireMailbox: b.requireMailbox ?? 'Yes', costCenter: b.costCenter,
          contractNumber: b.contractNumber ?? '',
          contractEffectiveDate: b.contractEffectiveDate?.slice(0, 10) ?? '',
          contractExpirationDate: b.contractExpirationDate?.slice(0, 10) ?? '',
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
      <FormShell code="EOB" activeStep={0} persona={persona as PersonaCode} mode="task">
        <SectionCard><div className="px-5 py-8 text-center text-sm text-ink-muted">請從首頁「Pending My Approval」進入以核准 / 設定。</div></SectionCard>
      </FormShell>
    )
  }

  const valid = !!f.firstName.trim() && !!f.lastName.trim() && !!f.businessTitle.trim()
    && !!f.employeeLocation && !!f.onboardDate && !!f.costCenter.trim()

  function attemptSubmit() {
    if (!valid) { setError('請填寫姓名 / 職稱 / 地點 / 到職日 / 成本中心。'); return }
    setError(null); setConfirmOpen(true)
  }

  async function doSubmit() {
    setConfirmOpen(false); setPending(true); setError(null)
    const payload = {
      firstName: f.firstName.trim(), lastName: f.lastName.trim(), businessTitle: f.businessTitle.trim(),
      employeeLocation: f.employeeLocation, onboardDate: f.onboardDate, requireMailbox: f.requireMailbox,
      costCenter: f.costCenter.trim(), contractNumber: f.contractNumber.trim() || null,
      contractEffectiveDate: f.contractEffectiveDate || null, contractExpirationDate: f.contractExpirationDate || null,
    }
    const url = isResubmit ? `/api/eob/v1/${resubmitCaseId}/resubmit` : '/api/eob/v1'
    try {
      const res = await apiFetch(url, { method: 'POST', headers: { 'Content-Type': 'application/json' }, body: JSON.stringify(payload) })
      if (!res.ok) throw new Error(await res.text() || `HTTP ${res.status}`)
      const b = (await res.json()) as EOB_V1_CaseResponse
      onSubmitted?.()
      navigate(`/cases/eob/${b.id}`)
    } catch (e) {
      setError(e instanceof Error ? e.message : String(e))
    } finally {
      setPending(false)
    }
  }

  return (
    <FormShell code="EOB" activeStep={0} persona={persona as PersonaCode} mode="create">
      <SectionCard>
        <SectionTitle>新進員工資料 / New Hire</SectionTitle>
        <div className="border-b border-rule px-5 py-3">
          <InfoBanner>
            填寫新進員工基本資料與合約資訊。
            {isResubmit && <span className="mt-1 block text-amber-900">此案件先前被退回，請修正後重新送出。</span>}
          </InfoBanner>
        </div>
        {loading ? (
          <div className="px-5 py-10 text-center text-sm text-ink-muted">載入中…</div>
        ) : (
          <div className="grid grid-cols-1 md:grid-cols-12 gap-3 px-5 py-4">
            <Field label="名 / First Name" required className="md:col-span-6"><Input value={f.firstName} onChange={e => patch({ firstName: e.target.value })} disabled={pending} placeholder="Raven" /></Field>
            <Field label="姓 / Last Name" required className="md:col-span-6"><Input value={f.lastName} onChange={e => patch({ lastName: e.target.value })} disabled={pending} placeholder="Wang" /></Field>
            <Field label="職稱 / Business Title" required className="md:col-span-12"><Input value={f.businessTitle} onChange={e => patch({ businessTitle: e.target.value })} disabled={pending} placeholder="Cloud Platform Engineer" /></Field>
            <Field label="工作地點 / Location" required className="md:col-span-6">
              <Select value={f.employeeLocation} onChange={e => patch({ employeeLocation: e.target.value })} disabled={pending}>
                {LOCATION_OPTIONS.map(o => <option key={o} value={o}>{o}</option>)}
              </Select>
            </Field>
            <Field label="需信箱 / Require Mailbox" hint="是否為新人開立公司信箱" className="md:col-span-6">
              <Select value={f.requireMailbox} onChange={e => patch({ requireMailbox: e.target.value })} disabled={pending}>
                {YESNO_OPTIONS.map(o => <option key={o} value={o}>{o}</option>)}
              </Select>
            </Field>
            <Field label="到職日 / Onboard Date" required className="md:col-span-6">
              <div className="relative">
                <Input type="date" value={f.onboardDate} onChange={e => patch({ onboardDate: e.target.value })} disabled={pending} />
                <CalendarIcon className="pointer-events-none absolute right-2 top-2 h-4 w-4 text-ink-faint" />
              </div>
            </Field>
            <Field label="成本中心 / Cost Center" required className="md:col-span-6"><Input value={f.costCenter} onChange={e => patch({ costCenter: e.target.value })} disabled={pending} placeholder="TWT.1746G" /></Field>
          </div>
        )}
      </SectionCard>

      {!loading && (
        <SectionCard>
          <SectionTitle>合約資訊 / Contract</SectionTitle>
          <div className="border-b border-rule px-5 py-3">
            <InfoBanner>合約欄位為選填，適用於約聘 / 外包人員；正職員工可留空。</InfoBanner>
          </div>
          <div className="grid grid-cols-1 md:grid-cols-12 gap-3 px-5 py-4">
            <Field label="合約編號 / Contract No." className="md:col-span-4"><Input value={f.contractNumber} onChange={e => patch({ contractNumber: e.target.value })} disabled={pending} /></Field>
            <Field label="合約生效日 / Effective" className="md:col-span-4">
              <div className="relative">
                <Input type="date" value={f.contractEffectiveDate} onChange={e => patch({ contractEffectiveDate: e.target.value })} disabled={pending} />
                <CalendarIcon className="pointer-events-none absolute right-2 top-2 h-4 w-4 text-ink-faint" />
              </div>
            </Field>
            <Field label="合約到期日 / Expiration" className="md:col-span-4">
              <div className="relative">
                <Input type="date" value={f.contractExpirationDate} onChange={e => patch({ contractExpirationDate: e.target.value })} disabled={pending} />
                <CalendarIcon className="pointer-events-none absolute right-2 top-2 h-4 w-4 text-ink-faint" />
              </div>
            </Field>
          </div>
        </SectionCard>
      )}

      <ActionFooter
        hint={error ? <span className="text-danger">{error}</span> : <span>送出後將通知您的主管。基本設定將於核准後進行。</span>}
        actions={[
          { id: 'cancel', label: '取消', variant: 'ghost', disabled: pending, onClick: () => navigate('/') },
          { id: 'submit', label: isResubmit ? '重新送出' : '送出申請', variant: 'primary', pending, disabled: !valid, onClick: attemptSubmit },
        ]}
      />

      <ConfirmDialog
        open={confirmOpen}
        title={isResubmit ? 'Resubmit onboarding?' : 'Submit onboarding?'}
        titleZh={isResubmit ? '重新送出新進員工報到？' : '送出新進員工報到？'}
        description={`${f.firstName} ${f.lastName} will be sent to your manager for approval.`}
        tone="default"
        confirmText={isResubmit ? '確認重新送出' : '確認送出'}
        onCancel={() => setConfirmOpen(false)}
        onConfirm={doSubmit}
      />
    </FormShell>
  )
}
