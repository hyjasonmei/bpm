import { useEffect, useState } from 'react'
import { CalendarIcon } from 'lucide-react'
import { useNavigate, useSearchParams } from 'react-router-dom'

import { SectionCard, SectionTitle } from '@/components/ui/card'
import { Field, InfoBanner, Input, Textarea } from '@/components/ui/form'
import { FilePicker } from '@/components/ui/FilePicker'
import { ConfirmDialog } from '@/components/ui/ConfirmDialog'
import { ActionFooter } from '@/components/ui/action-footer/ActionFooter'
import { FormShell } from '@/screens/forms/FormShell'
import type { FormComponentProps } from '@/features/registry'
import type { PersonaCode } from '@/lib/role'
import { apiFetch } from '@/lib/apiFetch'
import { emptyForm, type CrCaseResponse, type CrFormState } from './CONTRACT_REVIEW_V1_types'

const fmtMoney = (n: number) => n.toLocaleString('en-US', { maximumFractionDigits: 0 })

/**
 * CONTRACT_REVIEW V1 submitter form (userTask task_apply / task_revise). Posts to
 * <c>POST /api/contract-review/v1</c>, which opens a LEGAL + FINANCE 並簽 gateway;
 * with <c>?resubmit=&lt;caseId&gt;</c> it pre-fills the returned case, shows the
 * 修改說明 field and POSTs to <c>/{caseId}/resubmit</c> (a fresh review round).
 * Decision UI lives on <see cref="CONTRACT_REVIEW_V1_CaseDetail" />.
 */
export function CONTRACT_REVIEW_V1_Form({ persona, onSubmitted }: FormComponentProps) {
  const navigate = useNavigate()
  const [params] = useSearchParams()
  const resubmitCaseId = params.get('resubmit')
  const isResubmit = !!resubmitCaseId

  const [form, setForm] = useState<CrFormState>(emptyForm())
  const [loading, setLoading] = useState(isResubmit)
  const [pending, setPending] = useState(false)
  const [error, setError] = useState<string | null>(null)
  const [confirmOpen, setConfirmOpen] = useState(false)

  useEffect(() => {
    if (!isResubmit) return
    let cancelled = false
    void (async () => {
      try {
        const res = await apiFetch(`/api/contract-review/v1/${resubmitCaseId}`)
        if (!res.ok) throw new Error(`HTTP ${res.status}`)
        const body = (await res.json()) as CrCaseResponse
        if (cancelled) return
        setForm({
          counterpartyName: body.counterpartyName,
          contractSubject: body.contractSubject,
          amount: String(body.amount ?? ''),
          periodStart: body.periodStart?.slice(0, 10) ?? '',
          periodEnd: body.periodEnd?.slice(0, 10) ?? '',
          draftFile: body.draftFileId
            ? { id: body.draftFileId, fileName: '（沿用既有草稿，可重新上傳）', contentType: '', sizeBytes: 0 }
            : null,
          remarks: body.remarks ?? '',
          revisionNote: '',
        })
      } catch (e) {
        if (!cancelled) setError(e instanceof Error ? e.message : String(e))
      } finally {
        if (!cancelled) setLoading(false)
      }
    })()
    return () => { cancelled = true }
  }, [isResubmit, resubmitCaseId])

  function patch(p: Partial<CrFormState>) {
    setForm(prev => ({ ...prev, ...p }))
  }

  const amountNum = Number(form.amount)
  const periodOk = !!form.periodStart && !!form.periodEnd && form.periodEnd >= form.periodStart
  const valid =
    !!form.counterpartyName.trim() &&
    !!form.contractSubject.trim() &&
    Number.isFinite(amountNum) && amountNum >= 0 &&
    periodOk &&
    !!form.draftFile &&
    (!isResubmit || !!form.revisionNote.trim())

  function attemptSubmit() {
    if (!valid) {
      setError(form.periodStart && form.periodEnd && form.periodEnd < form.periodStart
        ? '合約迄日不能早於起日。'
        : '請填寫所有必填欄位（對方公司 / 合約標的 / 金額 / 合約期間 / 草稿檔案' + (isResubmit ? ' / 修改說明' : '') + '）。')
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
      counterpartyName: form.counterpartyName.trim(),
      contractSubject: form.contractSubject.trim(),
      amount: Number(form.amount) || 0,
      periodStart: form.periodStart,
      periodEnd: form.periodEnd,
      draftFileId: form.draftFile?.id ?? null,
      remarks: form.remarks.trim() ? form.remarks.trim() : null,
      revisionNote: isResubmit ? form.revisionNote.trim() : null,
    }
    const url = isResubmit ? `/api/contract-review/v1/${resubmitCaseId}/resubmit` : '/api/contract-review/v1'
    try {
      const res = await apiFetch(url, {
        method: 'POST',
        headers: { 'Content-Type': 'application/json' },
        body: JSON.stringify(payload),
      })
      if (!res.ok) throw new Error(await res.text() || `HTTP ${res.status}`)
      const body = (await res.json()) as CrCaseResponse
      onSubmitted?.()
      navigate(`/cases/contract_review/${body.id}`)
    } catch (e) {
      setError(e instanceof Error ? e.message : String(e))
    } finally {
      setPending(false)
    }
  }

  return (
    <FormShell code="CONTRACT_REVIEW" activeStep={0} persona={persona as PersonaCode} mode="create">
      <SectionCard>
        <SectionTitle>合約送審 / Contract Review</SectionTitle>
        <div className="border-b border-rule px-5 py-3">
          <InfoBanner>
            送出後由 <b>法務</b> 與 <b>財務</b> 並簽（兩方都核准才通過），再交由法務主管定案歸檔。
            {isResubmit && (
              <span className="mt-1 block text-amber-900">
                此案件先前被退回，請依退回意見修改後重新送審（將開啟新的並簽回合）。
              </span>
            )}
          </InfoBanner>
        </div>

        {loading ? (
          <div className="px-5 py-10 text-center text-sm text-ink-muted">載入中…</div>
        ) : (
          <div className="grid grid-cols-12 gap-3 px-5 py-4">
            <Field label="對方公司名稱 / Counterparty" required className="col-span-6">
              <Input value={form.counterpartyName} onChange={e => patch({ counterpartyName: e.target.value })} disabled={pending} placeholder="例：ACME Corp" />
            </Field>
            <Field label="合約金額（NTD） / Amount" required className="col-span-6">
              <div className="flex gap-1.5">
                <span className="flex h-8 items-center whitespace-nowrap rounded-md border border-rule bg-slate-50 px-2.5 text-sm text-ink-muted">NT$</span>
                <Input type="number" min="0" step="1" className="text-right font-mono" value={form.amount} onChange={e => patch({ amount: e.target.value })} disabled={pending} placeholder="0" />
              </div>
              {Number.isFinite(amountNum) && amountNum > 0 && (
                <div className="mt-1 text-right font-mono text-xs text-ink-faint">NT$ {fmtMoney(amountNum)}</div>
              )}
            </Field>

            <Field label="合約標的說明 / Contract Subject" required className="col-span-12">
              <Textarea rows={2} value={form.contractSubject} onChange={e => patch({ contractSubject: e.target.value })} disabled={pending} placeholder="簡述合約標的、範圍與重點條款" />
            </Field>

            <Field label="合約起日 / Start" required className="col-span-6" hint="迄日不能早於起日">
              <div className="relative">
                <Input type="date" value={form.periodStart} onChange={e => patch({ periodStart: e.target.value })} disabled={pending} />
                <CalendarIcon className="pointer-events-none absolute right-2 top-2 h-4 w-4 text-ink-faint" />
              </div>
            </Field>
            <Field label="合約迄日 / End" required className="col-span-6"
              error={form.periodStart && form.periodEnd && form.periodEnd < form.periodStart ? '迄日不能早於起日' : null}>
              <div className="relative">
                <Input type="date" value={form.periodEnd} min={form.periodStart || undefined} onChange={e => patch({ periodEnd: e.target.value })} disabled={pending} />
                <CalendarIcon className="pointer-events-none absolute right-2 top-2 h-4 w-4 text-ink-faint" />
              </div>
            </Field>

            <Field label="合約草稿檔案 / Draft File" required className="col-span-12" hint="僅接受 PDF / Word（.pdf, .doc, .docx）">
              <FilePicker
                value={form.draftFile}
                onChange={f => patch({ draftFile: f })}
                accept=".pdf,.doc,.docx"
                disabled={pending}
                placeholder="點擊或拖曳上傳合約草稿"
              />
            </Field>

            <Field label="備註 / Remarks" className="col-span-12">
              <Textarea rows={2} value={form.remarks} onChange={e => patch({ remarks: e.target.value })} disabled={pending} placeholder="其他補充說明（選填）" />
            </Field>

            {isResubmit && (
              <Field label="修改說明 / Revision Note" required className="col-span-12" hint="說明本次修改了哪些內容，會通知審查人">
                <Textarea rows={2} value={form.revisionNote} onChange={e => patch({ revisionNote: e.target.value })} disabled={pending} placeholder="例：已依法務意見修正責任條款，並補上用印頁" />
              </Field>
            )}
          </div>
        )}
      </SectionCard>

      <ActionFooter
        hint={error
          ? <span className="text-danger">{error}</span>
          : <span>{isResubmit ? '送出後將重新通知法務與財務審查。' : '送出後將通知法務與財務並簽。'}</span>}
        actions={[
          { id: 'cancel', label: '取消', variant: 'ghost', disabled: pending, onClick: () => navigate('/') },
          { id: 'submit', label: isResubmit ? '重新送審' : '送出審查', variant: 'primary', pending, disabled: !valid, onClick: attemptSubmit },
        ]}
      />

      <ConfirmDialog
        open={confirmOpen}
        title={isResubmit ? 'Resubmit contract review?' : 'Submit contract review?'}
        titleZh={isResubmit ? '重新送審合約？' : '送出合約審查？'}
        description={`${form.counterpartyName || '此合約'} · NT$ ${form.amount || '0'} 將送交法務與財務並簽。`}
        tone="default"
        confirmText={isResubmit ? '確認重新送審' : '確認送出'}
        onCancel={() => setConfirmOpen(false)}
        onConfirm={doSubmit}
      />
    </FormShell>
  )
}
