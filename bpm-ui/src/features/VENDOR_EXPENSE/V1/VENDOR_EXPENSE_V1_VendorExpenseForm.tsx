import { useEffect, useState } from 'react'
import { CalendarIcon, Loader2, Plus, Trash2 } from 'lucide-react'
import { useNavigate, useSearchParams } from 'react-router-dom'

import { Button } from '@/components/ui/button'
import { SectionCard, SectionTitle } from '@/components/ui/card'
import { Field, InfoBanner, Input, Textarea } from '@/components/ui/form'
import { ConfirmDialog } from '@/components/ui/ConfirmDialog'
import { FormShell } from '@/screens/forms/FormShell'
import type { FormComponentProps } from '@/features/registry'
import type { PersonaCode } from '@/lib/role'
import { apiFetch } from '@/lib/apiFetch'
import { emptyInvoice } from './VENDOR_EXPENSE_V1_shared'
import type {
  VENDOR_EXPENSE_V1_CaseResponse,
  VENDOR_EXPENSE_V1_InvoiceDto,
} from './VENDOR_EXPENSE_V1_types'

/**
 * VENDOR_EXPENSE V1 — submitter form (task_fill). Posts to
 * <c>POST /api/vendor-expense/v1</c> on first submit; when entered with
 * <c>?resubmit=&lt;caseId&gt;</c> on the URL, pre-fills from the existing
 * case and POSTs to <c>/{caseId}/resubmit</c>.
 *
 * Layout follows the spec's task_fill: a top-level Vendor field + the
 * rep_iyru invoice repeater (invoice date / no / charge-to / project /
 * category / currency / amount / description). Every field is optional
 * (the repeater only sets minCount: 1) and the submit comment is
 * optional per the action's promptComment: "optional".
 *
 * Approval / Resubmit decision UI lives on
 * <see cref="VENDOR_EXPENSE_V1_CaseDetail" />.
 */
export function VENDOR_EXPENSE_V1_VendorExpenseForm({
  persona, mode = 'create', onSubmitted,
}: FormComponentProps) {
  const navigate = useNavigate()
  const [params] = useSearchParams()
  const resubmitCaseId = params.get('resubmit')
  const isResubmit = !!resubmitCaseId

  const [vendor, setVendor] = useState('')
  const [submitterComment, setSubmitterComment] = useState('')
  const [invoices, setInvoices] = useState<VENDOR_EXPENSE_V1_InvoiceDto[]>([emptyInvoice()])
  const [loading, setLoading] = useState(isResubmit)
  const [pending, setPending] = useState(false)
  const [error, setError] = useState<string | null>(null)
  const [confirmOpen, setConfirmOpen] = useState(false)

  useEffect(() => {
    if (!isResubmit) return
    let cancelled = false
    void (async () => {
      try {
        const res = await apiFetch(`/api/vendor-expense/v1/${resubmitCaseId}`)
        if (!res.ok) throw new Error(`HTTP ${res.status}`)
        const body = (await res.json()) as VENDOR_EXPENSE_V1_CaseResponse
        if (cancelled) return
        setVendor(body.vendor ?? '')
        setSubmitterComment(body.submitterComment ?? '')
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

  function patchInvoice(i: number, patch: Partial<VENDOR_EXPENSE_V1_InvoiceDto>) {
    setInvoices(prev => prev.map((inv, idx) => idx === i ? { ...inv, ...patch } : inv))
  }

  if (mode !== 'create') {
    return (
      <FormShell code="VENDOR_EXPENSE" activeStep={0} persona={persona as PersonaCode} mode="task">
        <SectionCard>
          <div className="px-5 py-8 text-center text-sm text-ink-muted">
            開啟此申請的詳細頁面以審核 / 退回。請從首頁的「Pending My Approval」進入。
          </div>
        </SectionCard>
      </FormShell>
    )
  }

  // Spec sets only minCount: 1 on the repeater — submit requires at least
  // one invoice row, no other fields are mandatory.
  const valid = invoices.length >= 1

  function attemptSubmit() {
    if (invoices.length < 1) { setError('請至少填寫一筆 invoice。'); return }
    setError(null)
    setConfirmOpen(true)
  }

  async function doSubmit() {
    setConfirmOpen(false)
    setPending(true)
    setError(null)
    // Normalize empty strings to null so optional fields — notably the
    // optional invoiceDate (DateOnly? on the backend) — deserialize
    // cleanly. An empty "" date can't convert to DateOnly?, but null can.
    const norm = (s: string | null) => (s && s.trim() ? s.trim() : null)
    const cleanInvoices = invoices.map(inv => ({
      invoiceDate: norm(inv.invoiceDate),
      invoiceNo:   norm(inv.invoiceNo),
      chargeTo:    norm(inv.chargeTo),
      project:     norm(inv.project),
      category:    norm(inv.category),
      amount:      norm(inv.amount),
      currency:    norm(inv.currency),
      description: norm(inv.description),
    }))
    const payload = {
      vendor: vendor.trim() || null,
      submitterComment: submitterComment.trim() || null,
      invoices: cleanInvoices,
    }
    const url = isResubmit
      ? `/api/vendor-expense/v1/${resubmitCaseId}/resubmit`
      : '/api/vendor-expense/v1'
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
      const body = (await res.json()) as VENDOR_EXPENSE_V1_CaseResponse
      onSubmitted?.()
      navigate(`/cases/vendor_expense/${body.id}`)
    } catch (e) {
      setError(e instanceof Error ? e.message : String(e))
    } finally {
      setPending(false)
    }
  }

  return (
    <FormShell code="VENDOR_EXPENSE" activeStep={0} persona={persona as PersonaCode} mode="create">
      <SectionCard>
        <SectionTitle>採購資訊 / Vendor</SectionTitle>
        <div className="px-5 py-4">
          <Field label="廠商 / Vendor" hint="本次採購的廠商名稱">
            <Input
              value={vendor}
              onChange={e => setVendor(e.target.value)}
              disabled={pending}
              placeholder="e.g. Acme Supplies Inc."
            />
          </Field>
        </div>
      </SectionCard>

      <SectionCard>
        <SectionTitle>新多筆群組 / Invoices</SectionTitle>

        <div className="border-b border-rule px-5 py-3">
          <InfoBanner>
            請逐筆填寫採購 invoice；至少需要一筆。
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
            hint={isResubmit ? '請說明本次補件 / 修正內容（選填）' : '簡述本次採購用途（選填）'}
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

      <SectionCard>
        <div className="flex items-center justify-between gap-3 px-5 py-3">
          <div className="text-sm text-ink-muted">
            {error
              ? <span className="text-danger">{error}</span>
              : <span>{isResubmit ? '送出後將重新通知主管。' : '送出後將通知您的部門主管。'}</span>}
          </div>
          <div className="flex items-center gap-2">
            <Button variant="ghost" onClick={() => navigate('/')} disabled={pending}>取消</Button>
            <Button variant="primary" onClick={attemptSubmit} disabled={pending || !valid}>
              {pending && <Loader2 className="h-3.5 w-3.5 animate-spin" />}
              {isResubmit ? '重新送出' : '送出申請'}
            </Button>
          </div>
        </div>
      </SectionCard>

      <ConfirmDialog
        open={confirmOpen}
        title={isResubmit ? 'Resubmit vendor expense?' : 'Submit vendor expense?'}
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
  value: VENDOR_EXPENSE_V1_InvoiceDto
  disabled: boolean
  canRemove: boolean
  onChange: (patch: Partial<VENDOR_EXPENSE_V1_InvoiceDto>) => void
  onRemove: () => void
}) {
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
        <Field label="Invoice Date" className="col-span-6">
          <div className="relative">
            <Input
              type="date"
              value={value.invoiceDate ?? ''}
              onChange={e => onChange({ invoiceDate: e.target.value })}
              disabled={disabled}
            />
            <CalendarIcon className="pointer-events-none absolute right-2 top-2 h-4 w-4 text-ink-faint" />
          </div>
        </Field>
        <Field label="Invoice No" className="col-span-6">
          <Input
            value={value.invoiceNo ?? ''}
            onChange={e => onChange({ invoiceNo: e.target.value })}
            disabled={disabled}
          />
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
      </div>
    </div>
  )
}
