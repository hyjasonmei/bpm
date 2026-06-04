import { useEffect, useState } from 'react'
import { CalendarIcon, Plus, Trash2 } from 'lucide-react'
import { useNavigate, useSearchParams } from 'react-router-dom'

import { Button } from '@/components/ui/button'
import { SectionCard, SectionTitle } from '@/components/ui/card'
import { Field, InfoBanner, Input, Textarea } from '@/components/ui/form'
import { ConfirmDialog } from '@/components/ui/ConfirmDialog'
import { FilePicker, type FilePickerValue } from '@/components/ui/FilePicker'
import { ActionFooter } from '@/components/ui/action-footer/ActionFooter'
import { FormShell } from '@/screens/forms/FormShell'
import type { FormComponentProps } from '@/features/registry'
import type { PersonaCode } from '@/lib/role'
import { apiFetch } from '@/lib/apiFetch'
import { emptyInvoice } from './PURCHASE_REQUEST_V1_shared'
import type {
  PURCHASE_REQUEST_V1_CaseResponse,
  PURCHASE_REQUEST_V1_InvoiceDto,
} from './PURCHASE_REQUEST_V1_types'

/**
 * PURCHASE_REQUEST V1 — submitter form (task_apply). Posts to
 * <c>POST /api/purchase-request/v1</c> on first submit; when entered
 * with <c>?resubmit=&lt;caseId&gt;</c> on the URL, pre-fills with the
 * existing case data and POSTs to <c>/{caseId}/resubmit</c>.
 *
 * Approval / Resubmit decision UI lives on
 * <see cref="PURCHASE_REQUEST_V1_CaseDetail" />.
 */
export function PURCHASE_REQUEST_V1_PurchaseRequestForm({
  persona, mode = 'create', onSubmitted,
}: FormComponentProps) {
  const navigate = useNavigate()
  const [params] = useSearchParams()
  const resubmitCaseId = params.get('resubmit')
  const isResubmit = !!resubmitCaseId

  const [submitterComment, setSubmitterComment] = useState('')
  const [invoices, setInvoices] = useState<PURCHASE_REQUEST_V1_InvoiceDto[]>([emptyInvoice()])
  const [loading, setLoading] = useState(isResubmit)
  const [pending, setPending] = useState(false)
  const [error, setError] = useState<string | null>(null)
  const [confirmOpen, setConfirmOpen] = useState(false)

  useEffect(() => {
    if (!isResubmit) return
    let cancelled = false
    void (async () => {
      try {
        const res = await apiFetch(`/api/purchase-request/v1/${resubmitCaseId}`)
        if (!res.ok) throw new Error(`HTTP ${res.status}`)
        const body = (await res.json()) as PURCHASE_REQUEST_V1_CaseResponse
        if (cancelled) return
        setSubmitterComment(body.submitterComment)
        setInvoices(body.invoices.length > 0 ? body.invoices : [emptyInvoice()])
      } catch (e) {
        if (!cancelled) setError(e instanceof Error ? e.message : String(e))
      } finally {
        if (!cancelled) setLoading(false)
      }
    })()
    return () => { cancelled = true }
  }, [isResubmit, resubmitCaseId])

  function addInvoice() {
    setInvoices(prev => [...prev, emptyInvoice()])
  }

  function removeInvoice(i: number) {
    setInvoices(prev => prev.length <= 1 ? prev : prev.filter((_, idx) => idx !== i))
  }

  function patchInvoice(i: number, patch: Partial<PURCHASE_REQUEST_V1_InvoiceDto>) {
    setInvoices(prev => prev.map((inv, idx) => idx === i ? { ...inv, ...patch } : inv))
  }

  if (mode !== 'create') {
    return (
      <FormShell code="PURCHASE_REQUEST" activeStep={0} persona={persona as PersonaCode} mode="task">
        <SectionCard>
          <div className="px-5 py-8 text-center text-sm text-ink-muted">
            開啟此申請的詳細頁面以核准 / 退件。請從首頁的「Pending My Approval」進入。
          </div>
        </SectionCard>
      </FormShell>
    )
  }

  const valid = (() => {
    if (!submitterComment.trim()) return false
    if (invoices.length === 0) return false
    for (const inv of invoices) {
      if (!inv.invoiceDate) return false
    }
    return true
  })()

  function attemptSubmit() {
    if (!submitterComment.trim()) { setError('請填寫送出原因。'); return }
    if (invoices.length === 0) { setError('請至少填寫一筆 invoice。'); return }
    for (let i = 0; i < invoices.length; i++) {
      if (!invoices[i].invoiceDate) { setError(`第 ${i + 1} 筆 invoice 缺少日期。`); return }
    }
    setError(null)
    setConfirmOpen(true)
  }

  async function doSubmit() {
    setConfirmOpen(false)
    setPending(true)
    setError(null)
    const payload = {
      submitterComment: submitterComment.trim(),
      invoices,
    }
    const url = isResubmit
      ? `/api/purchase-request/v1/${resubmitCaseId}/resubmit`
      : '/api/purchase-request/v1'
    try {
      const res = await apiFetch(url, {
        method: 'POST',
        headers: { 'Content-Type': 'application/json' },
        body: JSON.stringify(payload),
      })
      if (!res.ok) {
        const text = await res.text()
        throw new Error(text || `HTTP ${res.status}`)
      }
      const body = (await res.json()) as PURCHASE_REQUEST_V1_CaseResponse
      onSubmitted?.()
      navigate(`/cases/purchase-request/${body.id}`)
    } catch (e) {
      setError(e instanceof Error ? e.message : String(e))
    } finally {
      setPending(false)
    }
  }

  return (
    <FormShell code="PURCHASE_REQUEST" activeStep={0} persona={persona as PersonaCode} mode="create">
      <SectionCard>
        <SectionTitle>Invoices</SectionTitle>

        <div className="border-b border-rule px-5 py-3">
          <InfoBanner>
            提示文字：請逐筆填寫採購 invoice；每筆至少需要 invoice date。
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
          <div className="space-y-3 px-5 py-4">
            {invoices.map((inv, i) => (
              <InvoiceCard
                key={i}
                index={i}
                value={inv}
                disabled={pending}
                canRemove={invoices.length > 1}
                onChange={patch => patchInvoice(i, patch)}
                onRemove={() => removeInvoice(i)}
              />
            ))}
            <div className="flex justify-end">
              <Button variant="outline" size="sm" onClick={addInvoice} disabled={pending}>
                <Plus className="h-3.5 w-3.5" /> 新增 invoice
              </Button>
            </div>
          </div>
        )}
      </SectionCard>

      <SectionCard>
        <SectionTitle>送出說明</SectionTitle>
        <div className="px-5 py-4">
          <Field
            label="送出說明 / Submission note"
            required
            hint={isResubmit ? '請說明本次補件 / 修正內容' : '簡述本次採購用途'}
          >
            <Textarea
              rows={3}
              value={submitterComment}
              onChange={e => setSubmitterComment(e.target.value)}
              disabled={pending}
              placeholder="e.g. Q2 supplier invoices for the Acme integration project."
            />
          </Field>
        </div>
      </SectionCard>

      <ActionFooter
        hint={error
          ? <span className="text-danger">{error}</span>
          : <span>{isResubmit ? '送出後將重新通知主管。' : '送出後將通知您的部門主管。'}</span>}
        actions={[
          { id: 'cancel', label: '取消', variant: 'ghost', disabled: pending, onClick: () => navigate('/') },
          { id: 'submit', label: isResubmit ? '重新送出' : '送出申請', variant: 'primary', pending, disabled: !valid, onClick: attemptSubmit },
        ]}
      />

      <ConfirmDialog
        open={confirmOpen}
        title={isResubmit ? 'Resubmit purchase request?' : 'Submit purchase request?'}
        titleZh={isResubmit ? '重新送出採購申請？' : '送出採購申請？'}
        description={`${invoices.length} ${invoices.length === 1 ? 'invoice' : 'invoices'} will be sent for approval.`}
        tone="default"
        confirmText={isResubmit ? '確認重新送出' : '確認送出'}
        onCancel={() => setConfirmOpen(false)}
        onConfirm={doSubmit}
      />
    </FormShell>
  )
}

function InvoiceCard({
  index, value, disabled, canRemove, onChange, onRemove,
}: {
  index: number
  value: PURCHASE_REQUEST_V1_InvoiceDto
  disabled: boolean
  canRemove: boolean
  onChange: (patch: Partial<PURCHASE_REQUEST_V1_InvoiceDto>) => void
  onRemove: () => void
}) {
  const attachment: FilePickerValue | null = value.attachmentFileId
    ? { id: value.attachmentFileId, fileName: '已上傳檔案', contentType: '', sizeBytes: 0 }
    : null
  return (
    <div className="rounded-md border border-rule bg-card p-4">
      <div className="mb-3 flex items-center justify-between">
        <p className="text-sm font-semibold text-ink">#{index + 1}</p>
        {canRemove && (
          <Button variant="ghost" size="xs" onClick={onRemove} disabled={disabled}>
            <Trash2 className="h-3.5 w-3.5" /> 移除
          </Button>
        )}
      </div>

      <div className="grid grid-cols-12 gap-3">
        <Field label="Invoice No" className="col-span-6">
          <Input
            value={value.invoiceNo ?? ''}
            onChange={e => onChange({ invoiceNo: e.target.value })}
            disabled={disabled}
          />
        </Field>
        <Field label="Invoice Date" required className="col-span-6">
          <div className="relative">
            <Input
              type="date"
              value={value.invoiceDate}
              onChange={e => onChange({ invoiceDate: e.target.value })}
              disabled={disabled}
            />
            <CalendarIcon className="pointer-events-none absolute right-2 top-2 h-4 w-4 text-ink-faint" />
          </div>
        </Field>

        <Field label="Charge To" className="col-span-6">
          <Input
            value={value.chargeTo ?? ''}
            onChange={e => onChange({ chargeTo: e.target.value })}
            disabled={disabled}
          />
        </Field>
        <Field label="Project" className="col-span-6">
          <Input
            value={value.project ?? ''}
            onChange={e => onChange({ project: e.target.value })}
            disabled={disabled}
          />
        </Field>

        <Field label="Category" className="col-span-6">
          <Input
            value={value.category ?? ''}
            onChange={e => onChange({ category: e.target.value })}
            disabled={disabled}
          />
        </Field>
        <Field label="Currency" className="col-span-3">
          <Input
            value={value.currency ?? ''}
            onChange={e => onChange({ currency: e.target.value })}
            disabled={disabled}
            placeholder="USD"
          />
        </Field>
        <Field label="Amount" className="col-span-3">
          <Input
            value={value.amount ?? ''}
            onChange={e => onChange({ amount: e.target.value })}
            disabled={disabled}
            placeholder="0.00"
          />
        </Field>

        <Field label="Description" className="col-span-12">
          <Textarea
            rows={2}
            value={value.description ?? ''}
            onChange={e => onChange({ description: e.target.value })}
            disabled={disabled}
          />
        </Field>

        <Field label="Attachment" className="col-span-6">
          <FilePicker
            value={attachment}
            onChange={meta => onChange({ attachmentFileId: meta?.id ?? null })}
            disabled={disabled}
            accept=".pdf,.png,.jpg,.jpeg"
            placeholder="PDF / 圖檔"
          />
        </Field>
      </div>
    </div>
  )
}
