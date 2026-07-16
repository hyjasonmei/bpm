import { useEffect, useState } from 'react'
import { CalendarIcon, Plus, Trash2 } from 'lucide-react'
import { useNavigate, useSearchParams } from 'react-router-dom'

import { Button } from '@/components/ui/button'
import { SectionCard, SectionTitle } from '@/components/ui/card'
import { Field, InfoBanner, Input, Select, Textarea } from '@/components/ui/form'
import { ConfirmDialog } from '@/components/ui/ConfirmDialog'
import { ActionFooter } from '@/components/ui/action-footer/ActionFooter'
import { FormShell } from '@/screens/forms/FormShell'
import type { FormComponentProps } from '@/features/registry'
import type { PersonaCode } from '@/lib/role'
import { apiFetch } from '@/lib/apiFetch'
import { CATEGORY_OPTIONS, PURPOSE_OPTIONS, emptyItem } from './FAP_V1_shared'
import type { FAP_V1_CaseResponse, FAP_V1_PurchaseItemDto } from './FAP_V1_types'

/** FAP V1 — submitter form (userTask `pr`): purchase-item repeater + request info. */
export function FAP_V1_PurchaseForm({ persona, mode = 'create', onSubmitted }: FormComponentProps) {
  const navigate = useNavigate()
  const [params] = useSearchParams()
  const resubmitCaseId = params.get('resubmit')
  const isResubmit = !!resubmitCaseId

  const [items, setItems] = useState<FAP_V1_PurchaseItemDto[]>([emptyItem()])
  const [shippingLocation, setShippingLocation] = useState('')
  const [chargeTo, setChargeTo] = useState('')
  const [purpose, setPurpose] = useState('New')
  const [expectedDate, setExpectedDate] = useState('')
  const [note, setNote] = useState('')
  const [loading, setLoading] = useState(isResubmit)
  const [pending, setPending] = useState(false)
  const [error, setError] = useState<string | null>(null)
  const [confirmOpen, setConfirmOpen] = useState(false)

  useEffect(() => {
    if (!isResubmit) return
    let cancelled = false
    void (async () => {
      try {
        const res = await apiFetch(`/api/fap/v1/${resubmitCaseId}`)
        if (!res.ok) throw new Error(`HTTP ${res.status}`)
        const b = (await res.json()) as FAP_V1_CaseResponse
        if (cancelled) return
        setItems(b.purchaseItems.length > 0 ? b.purchaseItems : [emptyItem()])
        setShippingLocation(b.shippingLocation); setChargeTo(b.chargeTo); setPurpose(b.purpose)
        setExpectedDate(b.expectedDate?.slice(0, 10) ?? ''); setNote(b.note ?? '')
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
  const patchItem = (i: number, patch: Partial<FAP_V1_PurchaseItemDto>) =>
    setItems(prev => prev.map((it, idx) => idx === i ? { ...it, ...patch } : it))

  if (mode !== 'create') {
    return (
      <FormShell code="FAP" activeStep={0} persona={persona as PersonaCode} mode="task">
        <SectionCard><div className="px-5 py-8 text-center text-sm text-ink-muted">請從首頁「Pending My Approval」進入以核准 / 驗收。</div></SectionCard>
      </FormShell>
    )
  }

  const valid = items.length > 0 && items.every(i => !!i.itemSpec?.trim() && i.qty > 0)
    && !!shippingLocation.trim() && !!chargeTo.trim() && !!purpose

  function attemptSubmit() {
    if (!valid) { setError('請填寫採購明細（品項 / 數量）與收貨地點 / 費用歸屬 / 用途。'); return }
    setError(null); setConfirmOpen(true)
  }

  async function doSubmit() {
    setConfirmOpen(false); setPending(true); setError(null)
    const payload = {
      purchaseItems: items.map(i => ({ ...i, qty: Number(i.qty) || 0 })),
      shippingLocation: shippingLocation.trim(), chargeTo: chargeTo.trim(), purpose,
      expectedDate: expectedDate || null, note: note.trim() || null,
    }
    const url = isResubmit ? `/api/fap/v1/${resubmitCaseId}/resubmit` : '/api/fap/v1'
    try {
      const res = await apiFetch(url, { method: 'POST', headers: { 'Content-Type': 'application/json' }, body: JSON.stringify(payload) })
      if (!res.ok) throw new Error(await res.text() || `HTTP ${res.status}`)
      const b = (await res.json()) as FAP_V1_CaseResponse
      onSubmitted?.()
      navigate(`/cases/fap/${b.id}`)
    } catch (e) {
      setError(e instanceof Error ? e.message : String(e))
    } finally {
      setPending(false)
    }
  }

  return (
    <FormShell code="FAP" activeStep={0} persona={persona as PersonaCode} mode="create">
      <SectionCard>
        <SectionTitle>採購明細 / Purchase Items</SectionTitle>
        <div className="border-b border-rule px-5 py-3">
          <InfoBanner>
            逐項填寫採購品項；每項需要品項 / 規格與數量。
            {isResubmit && <span className="mt-1 block text-amber-900">此案件先前被退回，請修正後重新送出。</span>}
          </InfoBanner>
        </div>
        {loading ? (
          <div className="px-5 py-10 text-center text-sm text-ink-muted">載入中…</div>
        ) : (
          <div className="space-y-4 px-5 py-4">
            {items.map((it, i) => (
              <PurchaseItemCard
                key={i}
                index={i}
                value={it}
                disabled={pending}
                canRemove={items.length > 1}
                onChange={patch => patchItem(i, patch)}
                onRemove={() => removeItem(i)}
              />
            ))}
            <div className="flex justify-end">
              <Button variant="outline" size="sm" onClick={addItem} disabled={pending}><Plus className="h-3.5 w-3.5" /> 新增明細</Button>
            </div>
          </div>
        )}
      </SectionCard>

      <SectionCard>
        <SectionTitle>請購資訊 / Request Info</SectionTitle>
        <div className="grid grid-cols-1 md:grid-cols-12 gap-3 px-5 py-4">
          <Field label="收貨地點 / Shipping Location" required className="md:col-span-6">
            <Input value={shippingLocation} onChange={e => setShippingLocation(e.target.value)} disabled={pending} placeholder="Taipei office" />
          </Field>
          <Field label="費用歸屬 / Charge To" required className="md:col-span-6">
            <Input value={chargeTo} onChange={e => setChargeTo(e.target.value)} disabled={pending} placeholder="TWT.1746G" />
          </Field>
          <Field label="用途 / Purpose" required className="md:col-span-4">
            <Select value={purpose} onChange={e => setPurpose(e.target.value)} disabled={pending}>
              {PURPOSE_OPTIONS.map(o => <option key={o} value={o}>{o}</option>)}
            </Select>
          </Field>
          <Field label="期望日 / Expected Date" className="md:col-span-4">
            <div className="relative">
              <Input type="date" value={expectedDate} onChange={e => setExpectedDate(e.target.value)} disabled={pending} />
              <CalendarIcon className="pointer-events-none absolute right-2 top-2 h-4 w-4 text-ink-faint" />
            </div>
          </Field>
          <Field label="備註 / Note" className="md:col-span-12">
            <Textarea rows={2} value={note} onChange={e => setNote(e.target.value)} disabled={pending} />
          </Field>
        </div>
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
        title={isResubmit ? 'Resubmit purchase?' : 'Submit purchase?'}
        titleZh={isResubmit ? '重新送出資產採購？' : '送出資產採購？'}
        description={`${items.length} ${items.length === 1 ? 'item' : 'items'} will be sent for approval.`}
        tone="default"
        confirmText={isResubmit ? '確認重新送出' : '確認送出'}
        onCancel={() => setConfirmOpen(false)}
        onConfirm={doSubmit}
      />
    </FormShell>
  )
}

/** Single purchase-item row — dark header band (index + category + remove)
 *  over a 12-col field grid, mirroring the VENDOR_EXPENSE invoice exemplar. */
function PurchaseItemCard({
  index, value, disabled, canRemove, onChange, onRemove,
}: {
  index: number
  value: FAP_V1_PurchaseItemDto
  disabled: boolean
  canRemove: boolean
  onChange: (patch: Partial<FAP_V1_PurchaseItemDto>) => void
  onRemove: () => void
}) {
  const catOptions = Array.from(new Set([...(value.category ? [value.category] : []), ...CATEGORY_OPTIONS]))
  return (
    <div className="overflow-hidden rounded-md border border-rule">
      {/* Dark item header band (mirrors the VENDOR_EXPENSE invoice reference). */}
      <div className="flex flex-wrap items-center gap-x-3 gap-y-2 bg-slate-700 px-4 py-2 text-sm text-white">
        <span className="min-w-[64px] font-semibold">#{index + 1}</span>
        <div className="ml-auto flex items-center gap-2">
          <span className="text-xs text-slate-300">Category</span>
          <select
            value={value.category ?? ''}
            onChange={e => onChange({ category: e.target.value })}
            disabled={disabled}
            className="h-7 w-32 rounded border border-slate-500 bg-slate-600 px-2 text-sm text-white focus:outline-none focus:ring-1 focus:ring-blue-400 disabled:opacity-60"
          >
            {catOptions.map(c => <option key={c} value={c}>{c}</option>)}
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
      <div className="grid grid-cols-1 md:grid-cols-12 gap-3 bg-card p-4">
        <Field label="品項 / 規格 Item / Spec" required className="md:col-span-9">
          <Input
            value={value.itemSpec ?? ''}
            onChange={e => onChange({ itemSpec: e.target.value })}
            disabled={disabled}
            placeholder="e.g. Lenovo ThinkPad X250"
          />
        </Field>
        <Field label="數量 / Qty" required className="md:col-span-3">
          <Input
            type="number"
            min="1"
            className="text-right font-mono"
            value={String(value.qty)}
            onChange={e => onChange({ qty: Number(e.target.value) })}
            disabled={disabled}
          />
        </Field>
      </div>
    </div>
  )
}
