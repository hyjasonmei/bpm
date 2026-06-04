import { useEffect, useState } from 'react'
import { CalendarIcon } from 'lucide-react'
import { useNavigate, useSearchParams } from 'react-router-dom'

import { SectionCard, SectionTitle } from '@/components/ui/card'
import { Field, InfoBanner, Input, Select, Textarea } from '@/components/ui/form'
import { ConfirmDialog } from '@/components/ui/ConfirmDialog'
import { ActionFooter } from '@/components/ui/action-footer/ActionFooter'
import { FormShell } from '@/screens/forms/FormShell'
import type { FormComponentProps } from '@/features/registry'
import type { PersonaCode } from '@/lib/role'
import { apiFetch } from '@/lib/apiFetch'
import { CURRENCY_OPTIONS, YESNO_OPTIONS, emptyPayload } from './APE_V1_shared'
import type { APE_V1_CaseResponse, APE_V1_SubmitPayload } from './APE_V1_types'

/**
 * APE V1 — submitter form (userTask `ape`). Posts to
 * <c>POST /api/ape/v1</c>; with <c>?resubmit=&lt;caseId&gt;</c> pre-fills
 * and POSTs to <c>/{caseId}/resubmit</c>. Decision UI lives on
 * <see cref="APE_V1_CaseDetail" />.
 */
export function APE_V1_AdvancePaymentForm({ persona, mode = 'create', onSubmitted }: FormComponentProps) {
  const navigate = useNavigate()
  const [params] = useSearchParams()
  const resubmitCaseId = params.get('resubmit')
  const isResubmit = !!resubmitCaseId

  const [form, setForm] = useState<APE_V1_SubmitPayload>(emptyPayload())
  const [loading, setLoading] = useState(isResubmit)
  const [pending, setPending] = useState(false)
  const [error, setError] = useState<string | null>(null)
  const [confirmOpen, setConfirmOpen] = useState(false)

  useEffect(() => {
    if (!isResubmit) return
    let cancelled = false
    void (async () => {
      try {
        const res = await apiFetch(`/api/ape/v1/${resubmitCaseId}`)
        if (!res.ok) throw new Error(`HTTP ${res.status}`)
        const body = (await res.json()) as APE_V1_CaseResponse
        if (cancelled) return
        setForm({
          expectReceiveDate: body.expectReceiveDate?.slice(0, 10) ?? '',
          deductReturnDate: body.deductReturnDate?.slice(0, 10) ?? '',
          chargeDepartment: body.chargeDepartment,
          rechargeOutside: body.rechargeOutside ?? 'No',
          description: body.description,
          amount: String(body.amount ?? ''),
          currency: body.currency || 'NTD',
        })
      } catch (e) {
        if (!cancelled) setError(e instanceof Error ? e.message : String(e))
      } finally {
        if (!cancelled) setLoading(false)
      }
    })()
    return () => { cancelled = true }
  }, [isResubmit, resubmitCaseId])

  function patch(p: Partial<APE_V1_SubmitPayload>) {
    setForm(prev => ({ ...prev, ...p }))
  }

  if (mode !== 'create') {
    return (
      <FormShell code="APE" activeStep={0} persona={persona as PersonaCode} mode="task">
        <SectionCard>
          <div className="px-5 py-8 text-center text-sm text-ink-muted">
            開啟此申請的詳細頁面以核准 / 退件。請從首頁的「Pending My Approval」進入。
          </div>
        </SectionCard>
      </FormShell>
    )
  }

  const amountNum = Number(form.amount)
  const valid =
    !!form.expectReceiveDate &&
    !!form.deductReturnDate &&
    !!form.chargeDepartment.trim() &&
    !!form.description.trim() &&
    !!form.currency &&
    Number.isFinite(amountNum) && amountNum > 0

  function attemptSubmit() {
    if (!valid) { setError('請填寫所有必填欄位（領款日 / 沖還日 / 費用部門 / 說明 / 金額 / 幣別），金額需大於 0。'); return }
    setError(null)
    setConfirmOpen(true)
  }

  async function doSubmit() {
    setConfirmOpen(false)
    setPending(true)
    setError(null)
    const payload = {
      expectReceiveDate: form.expectReceiveDate,
      deductReturnDate: form.deductReturnDate,
      chargeDepartment: form.chargeDepartment.trim(),
      rechargeOutside: form.rechargeOutside || null,
      description: form.description.trim(),
      amount: Number(form.amount),
      currency: form.currency,
    }
    const url = isResubmit ? `/api/ape/v1/${resubmitCaseId}/resubmit` : '/api/ape/v1'
    try {
      const res = await apiFetch(url, {
        method: 'POST',
        headers: { 'Content-Type': 'application/json' },
        body: JSON.stringify(payload),
      })
      if (!res.ok) throw new Error(await res.text() || `HTTP ${res.status}`)
      const body = (await res.json()) as APE_V1_CaseResponse
      onSubmitted?.()
      navigate(`/cases/ape/${body.id}`)
    } catch (e) {
      setError(e instanceof Error ? e.message : String(e))
    } finally {
      setPending(false)
    }
  }

  return (
    <FormShell code="APE" activeStep={0} persona={persona as PersonaCode} mode="create">
      <SectionCard>
        <SectionTitle>預支資訊 / Advance Payment</SectionTitle>
        <div className="border-b border-rule px-5 py-3">
          <InfoBanner>
            請填寫預支現金資訊；領款日 / 沖還日 / 費用部門 / 說明 / 金額為必填。
            {isResubmit && (
              <span className="mt-1 block text-amber-900">
                此案件先前被退回，請依照簽核意見修正後重新送出（這將進入新的審核回合）。
              </span>
            )}
          </InfoBanner>
        </div>
        {loading ? (
          <div className="px-5 py-10 text-center text-sm text-ink-muted">載入中…</div>
        ) : (
          <div className="grid grid-cols-12 gap-3 px-5 py-4">
            <Field label="預計領款日 / Expect to Receive" required className="col-span-6">
              <div className="relative">
                <Input type="date" value={form.expectReceiveDate} onChange={e => patch({ expectReceiveDate: e.target.value })} disabled={pending} />
                <CalendarIcon className="pointer-events-none absolute right-2 top-2 h-4 w-4 text-ink-faint" />
              </div>
            </Field>
            <Field label="沖還日 / Deduct / Return Date" required className="col-span-6">
              <div className="relative">
                <Input type="date" value={form.deductReturnDate} onChange={e => patch({ deductReturnDate: e.target.value })} disabled={pending} />
                <CalendarIcon className="pointer-events-none absolute right-2 top-2 h-4 w-4 text-ink-faint" />
              </div>
            </Field>

            <Field label="費用部門 / Charge Department" required className="col-span-6">
              <Input value={form.chargeDepartment} onChange={e => patch({ chargeDepartment: e.target.value })} disabled={pending} placeholder="e.g. Backend" />
            </Field>
            <Field label="轉嫁海外 / Recharge Outside" className="col-span-6">
              <Select value={form.rechargeOutside ?? ''} onChange={e => patch({ rechargeOutside: e.target.value })} disabled={pending}>
                {YESNO_OPTIONS.map(o => <option key={o} value={o}>{o}</option>)}
              </Select>
            </Field>

            <Field label="金額 / Amount" required className="col-span-3">
              <Input type="number" min="0" step="0.01" value={form.amount} onChange={e => patch({ amount: e.target.value })} disabled={pending} placeholder="0" />
            </Field>
            <Field label="幣別 / Currency" required className="col-span-3">
              <Select value={form.currency} onChange={e => patch({ currency: e.target.value })} disabled={pending}>
                {CURRENCY_OPTIONS.map(o => <option key={o} value={o}>{o}</option>)}
              </Select>
            </Field>

            <Field label="說明 / Description" required className="col-span-12">
              <Textarea rows={3} value={form.description} onChange={e => patch({ description: e.target.value })} disabled={pending} placeholder="預支用途說明" />
            </Field>
          </div>
        )}
      </SectionCard>

      <ActionFooter
        hint={error
          ? <span className="text-danger">{error}</span>
          : <span>{isResubmit ? '送出後將重新通知主管。' : '送出後將通知您的主管。'}</span>}
        actions={[
          { id: 'cancel', label: '取消', variant: 'ghost', disabled: pending, onClick: () => navigate('/') },
          { id: 'submit', label: isResubmit ? '重新送出' : '送出申請', variant: 'primary', pending, disabled: !valid, onClick: attemptSubmit },
        ]}
      />

      <ConfirmDialog
        open={confirmOpen}
        title={isResubmit ? 'Resubmit advance payment?' : 'Submit advance payment?'}
        titleZh={isResubmit ? '重新送出預支申請？' : '送出預支申請？'}
        description={`${form.currency} ${form.amount || '0'} will be sent to your manager for approval.`}
        tone="default"
        confirmText={isResubmit ? '確認重新送出' : '確認送出'}
        onCancel={() => setConfirmOpen(false)}
        onConfirm={doSubmit}
      />
    </FormShell>
  )
}
