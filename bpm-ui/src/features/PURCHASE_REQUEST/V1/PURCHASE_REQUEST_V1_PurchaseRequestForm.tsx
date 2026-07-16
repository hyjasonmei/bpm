import { useEffect, useState, type ComponentType } from 'react'
import { CalendarIcon, Copy, Plus, Trash2 } from 'lucide-react'
import { useNavigate, useSearchParams } from 'react-router-dom'

import { cn } from '@/lib/cn'
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

/** Common settlement currencies offered in the invoice header band. Free-text
 *  on the backend, so an unknown hydrated value is preserved as an extra option. */
const CURRENCIES = ['TWD', 'USD', 'EUR', 'JPY', 'CNY', 'GBP']

/** Display-only money formatter (the backend stores amount as a free string). */
const fmtMoney = (n: number) => n.toLocaleString('en-US', { maximumFractionDigits: 2 })

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

  function cloneInvoice(i: number) {
    setInvoices(prev => [...prev.slice(0, i + 1), { ...prev[i] }, ...prev.slice(i + 1)])
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

  // Grand total grouped by currency (display only) — amounts are free strings
  // and currencies can differ across rows, so never sum across currencies.
  const totalsByCcy = invoices.reduce<Record<string, number>>((acc, inv) => {
    const amt = parseFloat((inv.amount ?? '').toString())
    if (!Number.isNaN(amt) && amt !== 0) {
      const c = (inv.currency ?? '').trim() || '—'
      acc[c] = (acc[c] ?? 0) + amt
    }
    return acc
  }, {})
  const totalEntries = Object.entries(totalsByCcy)

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
      navigate(`/cases/purchase_request/${body.id}`)
    } catch (e) {
      setError(e instanceof Error ? e.message : String(e))
    } finally {
      setPending(false)
    }
  }

  return (
    <FormShell code="PURCHASE_REQUEST" activeStep={0} persona={persona as PersonaCode} mode="create">
      <SectionCard>
        <SectionTitle>採購明細 / Invoices</SectionTitle>

        <div className="border-b border-rule px-5 py-3">
          <InfoBanner>
            請逐筆填寫採購 invoice；每筆至少需要 invoice date。
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
          <div className="space-y-4 px-5 py-4">
            {invoices.map((inv, i) => (
              <InvoiceCard
                key={i}
                index={i}
                value={inv}
                disabled={pending}
                canRemove={invoices.length > 1}
                onChange={patch => patchInvoice(i, patch)}
                onAdd={addInvoice}
                onClone={() => cloneInvoice(i)}
                onRemove={() => removeInvoice(i)}
              />
            ))}

            {totalEntries.length > 0 && (
              <div className="flex flex-wrap items-center justify-end gap-x-6 gap-y-1 rounded-md border border-rule bg-slate-50 px-4 py-3">
                <span className="text-sm font-semibold text-ink">合計 / Total</span>
                {totalEntries.map(([ccy, sum]) => (
                  <span key={ccy} className="font-mono text-base font-bold text-ink">
                    {ccy === '—' ? '' : `${ccy} `}{fmtMoney(sum)}
                  </span>
                ))}
              </div>
            )}
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
  index, value, disabled, canRemove, onChange, onAdd, onClone, onRemove,
}: {
  index: number
  value: PURCHASE_REQUEST_V1_InvoiceDto
  disabled: boolean
  canRemove: boolean
  onChange: (patch: Partial<PURCHASE_REQUEST_V1_InvoiceDto>) => void
  onAdd: () => void
  onClone: () => void
  onRemove: () => void
}) {
  const attachment: FilePickerValue | null = value.attachmentFileId
    ? { id: value.attachmentFileId, fileName: '已上傳檔案', contentType: '', sizeBytes: 0 }
    : null
  const amt = parseFloat((value.amount ?? '').toString())
  const ccyOptions = Array.from(new Set([...(value.currency ? [value.currency] : []), ...CURRENCIES]))
  return (
    <div className="overflow-hidden rounded-md border border-rule">
      {/* Dark invoice header band (mirrors the GEV vendor-invoice reference). */}
      <div className="flex flex-wrap items-center gap-x-3 gap-y-2 bg-slate-700 px-4 py-2 text-sm text-white">
        <span className="min-w-[84px] font-semibold">Invoice #{index + 1}</span>
        <div className="ml-auto flex items-center gap-2">
          <span className="text-xs text-slate-300">Currency</span>
          <select
            value={value.currency ?? ''}
            onChange={e => onChange({ currency: e.target.value })}
            disabled={disabled}
            className="h-7 w-24 rounded border border-slate-500 bg-slate-600 px-2 text-sm text-white focus:outline-none focus:ring-1 focus:ring-blue-400 disabled:opacity-60"
          >
            <option value="">—</option>
            {ccyOptions.map(c => <option key={c} value={c}>{c}</option>)}
          </select>
        </div>
      </div>

      {/* Field grid + GEV-style row-action gutter (Add / Clone / Delete) */}
      <div className="flex">
        <div className="grid min-w-0 flex-1 grid-cols-1 md:grid-cols-12 gap-3 bg-card p-4">
        <Field label="Invoice No" className="md:col-span-6">
          <Input
            value={value.invoiceNo ?? ''}
            onChange={e => onChange({ invoiceNo: e.target.value })}
            disabled={disabled}
          />
        </Field>
        <Field label="Invoice Date" required className="md:col-span-6">
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

        <Field label="Charge To" className="md:col-span-6">
          <Input
            value={value.chargeTo ?? ''}
            onChange={e => onChange({ chargeTo: e.target.value })}
            disabled={disabled}
          />
        </Field>
        <Field label="Project" className="md:col-span-6">
          <Input
            value={value.project ?? ''}
            onChange={e => onChange({ project: e.target.value })}
            disabled={disabled}
          />
        </Field>

        <Field label="Category" className="md:col-span-6">
          <Input
            value={value.category ?? ''}
            onChange={e => onChange({ category: e.target.value })}
            disabled={disabled}
          />
        </Field>
        <Field label="Amount" className="md:col-span-6">
          <div className="flex gap-1.5">
            <span className="flex h-8 items-center whitespace-nowrap rounded-md border border-rule bg-slate-50 px-2.5 text-sm text-ink-muted">
              {value.currency || '—'}
            </span>
            <Input
              type="number"
              min={0}
              className="text-right font-mono"
              value={value.amount ?? ''}
              onChange={e => onChange({ amount: e.target.value })}
              disabled={disabled}
              placeholder="0.00"
            />
          </div>
          {!Number.isNaN(amt) && amt > 0 && (
            <div className="mt-1 text-right font-mono text-xs text-ink-faint">
              {value.currency ? `${value.currency} ` : ''}{fmtMoney(amt)}
            </div>
          )}
        </Field>

        <Field label="Description" className="md:col-span-12">
          <Textarea
            rows={2}
            value={value.description ?? ''}
            onChange={e => onChange({ description: e.target.value })}
            disabled={disabled}
          />
        </Field>

        <Field label="Attachment" className="md:col-span-6">
          <FilePicker
            value={attachment}
            onChange={meta => onChange({ attachmentFileId: meta?.id ?? null })}
            disabled={disabled}
            accept=".pdf,.png,.jpg,.jpeg"
            placeholder="PDF / 圖檔"
          />
        </Field>
        </div>

        {!disabled && (
          <div className="flex flex-col items-center gap-1.5 border-l border-rule bg-slate-50/60 p-2">
            <SmallAct Icon={Plus} label="Add" tone="blue" onClick={onAdd} />
            <SmallAct Icon={Copy} label="Clone" tone="slate" onClick={onClone} />
            <SmallAct Icon={Trash2} label="Delete" tone="red" onClick={onRemove} disabled={!canRemove} />
          </div>
        )}
      </div>
    </div>
  )
}

function SmallAct({ Icon, label, tone, onClick, disabled }: {
  Icon: ComponentType<{ className?: string }>
  label: string
  tone: 'blue' | 'slate' | 'red'
  onClick: () => void
  disabled?: boolean
}) {
  const cls = {
    blue:  'border-blue-200 text-blue-600 hover:bg-blue-50',
    slate: 'border-rule text-ink-muted hover:bg-slate-50',
    red:   'border-red-100 text-red-400 hover:bg-red-50',
  }[tone]
  return (
    <button
      type="button"
      onClick={onClick}
      disabled={disabled}
      className={cn(
        'flex w-12 flex-col items-center gap-0.5 rounded-md border bg-white py-1.5 transition-colors',
        'disabled:pointer-events-none disabled:opacity-30',
        cls,
      )}
    >
      <Icon className="h-4 w-4" />
      <span className="text-[9px] font-medium">{label}</span>
    </button>
  )
}
