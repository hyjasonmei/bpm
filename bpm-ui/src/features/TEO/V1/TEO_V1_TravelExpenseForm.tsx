import { useEffect, useState } from 'react'
import { CalendarIcon, Plus, Trash2 } from 'lucide-react'
import { useNavigate, useSearchParams } from 'react-router-dom'

import { Button } from '@/components/ui/button'
import { SectionCard, SectionTitle } from '@/components/ui/card'
import { Field, InfoBanner, Input, Textarea } from '@/components/ui/form'
import { ConfirmDialog } from '@/components/ui/ConfirmDialog'
import { ActionFooter } from '@/components/ui/action-footer/ActionFooter'
import { FormShell } from '@/screens/forms/FormShell'
import type { FormComponentProps } from '@/features/registry'
import type { PersonaCode } from '@/lib/role'
import { apiFetch } from '@/lib/apiFetch'
import { CATEGORY_OPTIONS, emptyItem } from './TEO_V1_shared'
import type { TEO_V1_CaseResponse, TEO_V1_ExpenseItemDto } from './TEO_V1_types'

/** Display-only money formatter (the backend stores amount as a free string). */
const fmtMoney = (n: number) => n.toLocaleString('en-US', { maximumFractionDigits: 2 })

/** TEO V1 — submitter form (userTask `exp`): travel-request link + expense-item repeater. */
export function TEO_V1_TravelExpenseForm({ persona, mode = 'create', onSubmitted }: FormComponentProps) {
  const navigate = useNavigate()
  const [params] = useSearchParams()
  const resubmitCaseId = params.get('resubmit')
  const isResubmit = !!resubmitCaseId

  const [travelRequestNo, setTravelRequestNo] = useState('')
  const [items, setItems] = useState<TEO_V1_ExpenseItemDto[]>([emptyItem()])
  const [loading, setLoading] = useState(isResubmit)
  const [pending, setPending] = useState(false)
  const [error, setError] = useState<string | null>(null)
  const [confirmOpen, setConfirmOpen] = useState(false)

  useEffect(() => {
    if (!isResubmit) return
    let cancelled = false
    void (async () => {
      try {
        const res = await apiFetch(`/api/teo/v1/${resubmitCaseId}`)
        if (!res.ok) throw new Error(`HTTP ${res.status}`)
        const body = (await res.json()) as TEO_V1_CaseResponse
        if (cancelled) return
        setTravelRequestNo(body.travelRequestNo)
        setItems(body.expenseItems.length > 0
          ? body.expenseItems.map(i => ({ ...i, date: i.date?.slice(0, 10) ?? '' }))
          : [emptyItem()])
      } catch (e) {
        if (!cancelled) setError(e instanceof Error ? e.message : String(e))
      } finally {
        if (!cancelled) setLoading(false)
      }
    })()
    return () => { cancelled = true }
  }, [isResubmit, resubmitCaseId])

  const addItem = () => setItems(prev => [...prev, emptyItem()])
  const removeItem = (i: number) => setItems(prev => prev.length <= 1 ? prev : prev.filter((_, idx) => idx !== i))
  const patchItem = (i: number, patch: Partial<TEO_V1_ExpenseItemDto>) =>
    setItems(prev => prev.map((it, idx) => idx === i ? { ...it, ...patch } : it))

  if (mode !== 'create') {
    return (
      <FormShell code="TEO" activeStep={0} persona={persona as PersonaCode} mode="task">
        <SectionCard>
          <div className="px-5 py-8 text-center text-sm text-ink-muted">
            開啟此申請的詳細頁面以核准 / 退件。請從首頁的「Pending My Approval」進入。
          </div>
        </SectionCard>
      </FormShell>
    )
  }

  const valid = !!travelRequestNo.trim() && items.length > 0 && items.every(i => !!i.date)

  // Grand total of the local-currency (NTD) column — display only. Amounts are
  // free strings, so non-numeric / empty entries are skipped from the sum.
  const totalLcy = items.reduce((acc, it) => {
    const n = parseFloat((it.amountLcy ?? '').toString())
    return Number.isNaN(n) ? acc : acc + n
  }, 0)

  function attemptSubmit() {
    if (!travelRequestNo.trim()) { setError('請填寫關聯差旅申請單號。'); return }
    for (let i = 0; i < items.length; i++) if (!items[i].date) { setError(`第 ${i + 1} 筆缺少日期。`); return }
    setError(null); setConfirmOpen(true)
  }

  async function doSubmit() {
    setConfirmOpen(false); setPending(true); setError(null)
    const payload = { travelRequestNo: travelRequestNo.trim(), expenseItems: items }
    const url = isResubmit ? `/api/teo/v1/${resubmitCaseId}/resubmit` : '/api/teo/v1'
    try {
      const res = await apiFetch(url, { method: 'POST', headers: { 'Content-Type': 'application/json' }, body: JSON.stringify(payload) })
      if (!res.ok) throw new Error(await res.text() || `HTTP ${res.status}`)
      const body = (await res.json()) as TEO_V1_CaseResponse
      onSubmitted?.()
      navigate(`/cases/teo/${body.id}`)
    } catch (e) {
      setError(e instanceof Error ? e.message : String(e))
    } finally {
      setPending(false)
    }
  }

  return (
    <FormShell code="TEO" activeStep={0} persona={persona as PersonaCode} mode="create">
      <SectionCard>
        <SectionTitle>關聯差旅申請 / Travel Request</SectionTitle>
        <div className="px-5 py-4">
          <Field label="關聯差旅申請單號 / Travel Request No." required>
            <Input value={travelRequestNo} onChange={e => setTravelRequestNo(e.target.value)} disabled={pending} placeholder="e.g. TW-TRQ-23-000160" />
          </Field>
        </div>
      </SectionCard>

      <SectionCard>
        <SectionTitle>差旅費用明細 / Expense Items</SectionTitle>
        <div className="border-b border-rule px-5 py-3">
          <InfoBanner>
            逐筆填寫差旅費用；每筆至少需要日期。
            {isResubmit && <span className="mt-1 block text-amber-900">此案件先前被退回，請修正後重新送出。</span>}
          </InfoBanner>
        </div>
        {loading ? (
          <div className="px-5 py-10 text-center text-sm text-ink-muted">載入中…</div>
        ) : (
          <div className="space-y-4 px-5 py-4">
            {items.map((it, i) => (
              <ItemCard key={i} index={i} value={it} disabled={pending} canRemove={items.length > 1}
                        onChange={p => patchItem(i, p)} onRemove={() => removeItem(i)} />
            ))}
            <div className="flex justify-end">
              <Button variant="outline" size="sm" onClick={addItem} disabled={pending}>
                <Plus className="h-3.5 w-3.5" /> 新增明細
              </Button>
            </div>

            {totalLcy > 0 && (
              <div className="flex flex-wrap items-center justify-end gap-x-6 gap-y-1 rounded-md border border-rule bg-slate-50 px-4 py-3">
                <span className="text-sm font-semibold text-ink">本幣合計 / Total (LCY)</span>
                <span className="font-mono text-base font-bold text-danger">NTD {fmtMoney(totalLcy)}</span>
              </div>
            )}
          </div>
        )}
      </SectionCard>

      <ActionFooter
        hint={error ? <span className="text-danger">{error}</span> : <span>送出後將通知您的主管。</span>}
        actions={[
          { id: 'cancel', label: '取消', variant: 'ghost', disabled: pending, onClick: () => navigate('/') },
          { id: 'submit', label: isResubmit ? '重新送出' : '送出申請', variant: 'primary', pending, disabled: !valid, onClick: attemptSubmit },
        ]}
      />

      <ConfirmDialog
        open={confirmOpen}
        title={isResubmit ? 'Resubmit travel expense?' : 'Submit travel expense?'}
        titleZh={isResubmit ? '重新送出差旅費用核銷？' : '送出差旅費用核銷？'}
        description={`${items.length} ${items.length === 1 ? 'item' : 'items'} will be sent for approval.`}
        tone="default"
        confirmText={isResubmit ? '確認重新送出' : '確認送出'}
        onCancel={() => setConfirmOpen(false)}
        onConfirm={doSubmit}
      />
    </FormShell>
  )
}

function ItemCard({ index, value, disabled, canRemove, onChange, onRemove }: {
  index: number
  value: TEO_V1_ExpenseItemDto
  disabled: boolean
  canRemove: boolean
  onChange: (patch: Partial<TEO_V1_ExpenseItemDto>) => void
  onRemove: () => void
}) {
  // Keep the hydrated category as a selectable option even if it is not one of
  // the canonical CATEGORY_OPTIONS (the backend stores it as a free string).
  const catOptions = Array.from(new Set([
    ...(value.category ? [value.category] : []),
    ...CATEGORY_OPTIONS,
  ]))
  const amt = parseFloat((value.amount ?? '').toString())
  const lcy = parseFloat((value.amountLcy ?? '').toString())
  return (
    <div className="overflow-hidden rounded-md border border-rule">
      {/* Dark item header band (mirrors the GEV / VENDOR_EXPENSE reference). */}
      <div className="flex flex-wrap items-center gap-x-3 gap-y-2 bg-slate-700 px-4 py-2 text-sm text-white">
        <span className="min-w-[64px] font-semibold">明細 #{index + 1}</span>
        <div className="ml-auto flex items-center gap-2">
          <span className="text-xs text-slate-300">類別 / Category</span>
          <select
            value={value.category ?? ''}
            onChange={e => onChange({ category: e.target.value })}
            disabled={disabled}
            className="h-7 w-44 rounded border border-slate-500 bg-slate-600 px-2 text-sm text-white focus:outline-none focus:ring-1 focus:ring-blue-400 disabled:opacity-60"
          >
            {catOptions.map(o => <option key={o} value={o}>{o}</option>)}
          </select>
          {canRemove && (
            <button
              type="button"
              onClick={onRemove}
              disabled={disabled}
              className="ml-1 flex items-center gap-1 rounded px-1.5 py-1 text-xs text-slate-300 hover:bg-slate-600 hover:text-white disabled:opacity-50"
            >
              <Trash2 className="h-3.5 w-3.5" /> 移除
            </button>
          )}
        </div>
      </div>

      {/* Field grid */}
      <div className="grid grid-cols-12 gap-3 bg-card p-4">
        <Field label="日期 / Date" required className="col-span-6">
          <div className="relative">
            <Input type="date" value={value.date} onChange={e => onChange({ date: e.target.value })} disabled={disabled} />
            <CalendarIcon className="pointer-events-none absolute right-2 top-2 h-4 w-4 text-ink-faint" />
          </div>
        </Field>
        <Field label="國家 / Country" className="col-span-6">
          <Input value={value.country ?? ''} onChange={e => onChange({ country: e.target.value })} disabled={disabled} />
        </Field>

        <Field label="金額 / Amount" className="col-span-6">
          <div className="flex gap-1.5">
            <span className="flex h-8 items-center whitespace-nowrap rounded-md border border-rule bg-slate-50 px-2.5 text-sm text-ink-muted">
              原幣
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
            <div className="mt-1 text-right font-mono text-xs text-ink-faint">{fmtMoney(amt)}</div>
          )}
        </Field>
        <Field label="本幣金額 / Amount (LCY)" className="col-span-6">
          <div className="flex gap-1.5">
            <span className="flex h-8 items-center whitespace-nowrap rounded-md border border-rule bg-slate-50 px-2.5 text-sm text-ink-muted">
              NTD
            </span>
            <Input
              type="number"
              min={0}
              className="text-right font-mono"
              value={value.amountLcy ?? ''}
              onChange={e => onChange({ amountLcy: e.target.value })}
              disabled={disabled}
              placeholder="0.00"
            />
          </div>
          {!Number.isNaN(lcy) && lcy > 0 && (
            <div className="mt-1 text-right font-mono text-xs text-ink-faint">NTD {fmtMoney(lcy)}</div>
          )}
        </Field>

        <Field label="說明 / Description" className="col-span-12">
          <Textarea rows={2} value={value.description ?? ''} onChange={e => onChange({ description: e.target.value })} disabled={disabled} />
        </Field>
      </div>
    </div>
  )
}
