import { useEffect, useState } from 'react'
import { CalendarIcon } from 'lucide-react'
import { useNavigate, useSearchParams } from 'react-router-dom'

import { SectionCard, SectionTitle } from '@/components/ui/card'
import { Field, InfoBanner, Input, Select, Textarea } from '@/components/ui/form'
import { FilePicker } from '@/components/ui/FilePicker'
import { ConfirmDialog } from '@/components/ui/ConfirmDialog'
import { ActionFooter } from '@/components/ui/action-footer/ActionFooter'
import { FormShell } from '@/screens/forms/FormShell'
import type { FormComponentProps } from '@/features/registry'
import type { PersonaCode } from '@/lib/role'
import { apiFetch } from '@/lib/apiFetch'
import { CATEGORY_OPTIONS, emptyForm, type CmCaseResponse, type CmFormState } from './COMMITTEE_REVIEW_V1_types'

const fmtMoney = (n: number) => n.toLocaleString('en-US', { maximumFractionDigits: 0 })

/**
 * COMMITTEE_REVIEW V1 submitter form (userTask task_apply / task_revise). Posts to
 * <c>POST /api/committee-review/v1</c>, which opens a 財務 / 採購 / 資訊 三委員並簽
 * gateway (門檻 2/3). With <c>?resubmit=&lt;caseId&gt;</c> it pre-fills the returned
 * case, shows the 修改說明 field and POSTs to <c>/{caseId}/resubmit</c> (a fresh
 * review round). Decision UI lives on <see cref="COMMITTEE_REVIEW_V1_CaseDetail" />.
 */
export function COMMITTEE_REVIEW_V1_Form({ persona, onSubmitted }: FormComponentProps) {
  const navigate = useNavigate()
  const [params] = useSearchParams()
  const resubmitCaseId = params.get('resubmit')
  const isResubmit = !!resubmitCaseId

  const [form, setForm] = useState<CmFormState>(emptyForm())
  const [loading, setLoading] = useState(isResubmit)
  const [pending, setPending] = useState(false)
  const [error, setError] = useState<string | null>(null)
  const [confirmOpen, setConfirmOpen] = useState(false)

  useEffect(() => {
    if (!isResubmit) return
    let cancelled = false
    void (async () => {
      try {
        const res = await apiFetch(`/api/committee-review/v1/${resubmitCaseId}`)
        if (!res.ok) throw new Error(`HTTP ${res.status}`)
        const body = (await res.json()) as CmCaseResponse
        if (cancelled) return
        setForm({
          caseTitle: body.caseTitle,
          reviewCategory: body.reviewCategory,
          applyAmount: String(body.applyAmount ?? ''),
          benefitDescription: body.benefitDescription,
          execStart: body.execStart?.slice(0, 10) ?? '',
          execEnd: body.execEnd?.slice(0, 10) ?? '',
          attachment: body.attachmentFileId
            ? { id: body.attachmentFileId, fileName: '（沿用既有附件，可重新上傳）', contentType: '', sizeBytes: 0 }
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

  function patch(p: Partial<CmFormState>) {
    setForm(prev => ({ ...prev, ...p }))
  }

  const amountNum = Number(form.applyAmount)
  const periodOk = !!form.execStart && !!form.execEnd && form.execEnd >= form.execStart
  const valid =
    !!form.caseTitle.trim() &&
    !!form.reviewCategory &&
    Number.isFinite(amountNum) && amountNum >= 0 &&
    !!form.benefitDescription.trim() &&
    periodOk &&
    !!form.attachment &&
    (!isResubmit || !!form.revisionNote.trim())

  function attemptSubmit() {
    if (!valid) {
      setError(form.execStart && form.execEnd && form.execEnd < form.execStart
        ? '執行迄日不能早於起日。'
        : '請填寫所有必填欄位（案由 / 審議類別 / 金額 / 效益說明 / 執行期間 / 附件' + (isResubmit ? ' / 修改說明' : '') + '）。')
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
      caseTitle: form.caseTitle.trim(),
      reviewCategory: form.reviewCategory,
      applyAmount: Number(form.applyAmount) || 0,
      benefitDescription: form.benefitDescription.trim(),
      execStart: form.execStart,
      execEnd: form.execEnd,
      attachmentFileId: form.attachment?.id ?? null,
      remarks: form.remarks.trim() ? form.remarks.trim() : null,
      revisionNote: isResubmit ? form.revisionNote.trim() : null,
    }
    const url = isResubmit ? `/api/committee-review/v1/${resubmitCaseId}/resubmit` : '/api/committee-review/v1'
    try {
      const res = await apiFetch(url, {
        method: 'POST',
        headers: { 'Content-Type': 'application/json' },
        body: JSON.stringify(payload),
      })
      if (!res.ok) throw new Error(await res.text() || `HTTP ${res.status}`)
      const body = (await res.json()) as CmCaseResponse
      onSubmitted?.()
      navigate(`/cases/committee_review/${body.id}`)
    } catch (e) {
      setError(e instanceof Error ? e.message : String(e))
    } finally {
      setPending(false)
    }
  }

  return (
    <FormShell code="COMMITTEE_REVIEW" activeStep={0} persona={persona as PersonaCode} mode="create">
      <SectionCard>
        <SectionTitle>委員會審議申請 / Committee Review</SectionTitle>
        <div className="border-b border-rule px-5 py-3">
          <InfoBanner>
            送出後由 <b>財務</b>、<b>採購</b>、<b>資訊</b> 三位委員並簽，達 <b>門檻 2/3</b> 核准後交由 <b>執行長</b> 最終裁決。
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
          <div className="grid grid-cols-1 md:grid-cols-12 gap-3 px-5 py-4">
            <Field label="案由標題 / Case Title" required className="md:col-span-8">
              <Input value={form.caseTitle} onChange={e => patch({ caseTitle: e.target.value })} disabled={pending} placeholder="例：新一代 ERP 系統採購案" />
            </Field>
            <Field label="審議類別 / Category" required className="md:col-span-4">
              <Select value={form.reviewCategory} onChange={e => patch({ reviewCategory: e.target.value })} disabled={pending}>
                <option value="">請選擇</option>
                {CATEGORY_OPTIONS.map(o => <option key={o.value} value={o.value}>{o.label}</option>)}
              </Select>
            </Field>

            <Field label="申請金額（NTD） / Amount" required className="md:col-span-6">
              <div className="flex gap-1.5">
                <span className="flex h-8 items-center whitespace-nowrap rounded-md border border-rule bg-slate-50 px-2.5 text-sm text-ink-muted">NT$</span>
                <Input type="number" min="0" step="1" className="text-right font-mono" value={form.applyAmount} onChange={e => patch({ applyAmount: e.target.value })} disabled={pending} placeholder="0" />
              </div>
              {Number.isFinite(amountNum) && amountNum > 0 && (
                <div className="mt-1 text-right font-mono text-xs text-ink-faint">NT$ {fmtMoney(amountNum)}</div>
              )}
            </Field>

            <Field label="效益說明 / Benefit" required className="md:col-span-12">
              <Textarea rows={2} value={form.benefitDescription} onChange={e => patch({ benefitDescription: e.target.value })} disabled={pending} placeholder="簡述本案預期效益、必要性與重點" />
            </Field>

            <Field label="預計執行起日 / Start" required className="md:col-span-6" hint="迄日不能早於起日">
              <div className="relative">
                <Input type="date" value={form.execStart} onChange={e => patch({ execStart: e.target.value })} disabled={pending} />
                <CalendarIcon className="pointer-events-none absolute right-2 top-2 h-4 w-4 text-ink-faint" />
              </div>
            </Field>
            <Field label="預計執行迄日 / End" required className="md:col-span-6"
              error={form.execStart && form.execEnd && form.execEnd < form.execStart ? '迄日不能早於起日' : null}>
              <div className="relative">
                <Input type="date" value={form.execEnd} min={form.execStart || undefined} onChange={e => patch({ execEnd: e.target.value })} disabled={pending} />
                <CalendarIcon className="pointer-events-none absolute right-2 top-2 h-4 w-4 text-ink-faint" />
              </div>
            </Field>

            <Field label="附件 / Attachment" required className="md:col-span-12" hint="請上傳審議相關文件（PDF / Word / Excel）">
              <FilePicker
                value={form.attachment}
                onChange={f => patch({ attachment: f })}
                accept=".pdf,.doc,.docx,.xls,.xlsx"
                disabled={pending}
                placeholder="點擊或拖曳上傳附件"
              />
            </Field>

            <Field label="備註 / Remarks" className="md:col-span-12">
              <Textarea rows={2} value={form.remarks} onChange={e => patch({ remarks: e.target.value })} disabled={pending} placeholder="其他補充說明（選填）" />
            </Field>

            {isResubmit && (
              <Field label="修改說明 / Revision Note" required className="md:col-span-12" hint="說明本次修改了哪些內容，會通知委員">
                <Textarea rows={2} value={form.revisionNote} onChange={e => patch({ revisionNote: e.target.value })} disabled={pending} placeholder="例：已依委員意見補充效益量化數據並調整金額" />
              </Field>
            )}
          </div>
        )}
      </SectionCard>

      <ActionFooter
        hint={error
          ? <span className="text-danger">{error}</span>
          : <span>{isResubmit ? '送出後將重新通知三位委員審議。' : '送出後將通知財務、採購、資訊三位委員並簽。'}</span>}
        actions={[
          { id: 'cancel', label: '取消', variant: 'ghost', disabled: pending, onClick: () => navigate('/') },
          { id: 'submit', label: isResubmit ? '重新送審' : '送出審議', variant: 'primary', pending, disabled: !valid, onClick: attemptSubmit },
        ]}
      />

      <ConfirmDialog
        open={confirmOpen}
        title={isResubmit ? 'Resubmit committee review?' : 'Submit committee review?'}
        titleZh={isResubmit ? '重新送審審議案？' : '送出委員會審議？'}
        description={`${form.caseTitle || '此審議案'} · NT$ ${form.applyAmount || '0'} 將送交三位委員並簽（門檻 2/3）。`}
        tone="default"
        confirmText={isResubmit ? '確認重新送審' : '確認送出'}
        onCancel={() => setConfirmOpen(false)}
        onConfirm={doSubmit}
      />
    </FormShell>
  )
}
