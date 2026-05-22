import { useEffect, useState } from 'react'
import { Plus, Copy, Trash2, Upload } from 'lucide-react'
import { cn, NTD_RATE, fmtNTD, fmtUSD } from '@/lib/cn'

import { SectionCard, SectionTitle } from '@/components/ui/card'
import { Input, Textarea, Select, Checkbox, FieldLabel, InfoBanner } from '@/components/ui/form'
import { FormShell, ActionBar } from './FormShell'
import { CHARGE_OPTS, PROJECT_OPTS, CURRENCIES, GEE_CATS } from '@/lib/mocks'
import type { PersonaCode } from '@/lib/role'
import { FlowToast, useFormRuntime, type FormRuntimeProps } from '@/hooks/useFormRuntime'

interface GEEItem {
  id: number
  date: string
  invoiceNo: string
  chargeTo: string
  project: string
  recharge: boolean
  category: string
  currency: string
  amount: string
  description: string
}

const newItem = (): GEEItem => ({
  id: Date.now() + Math.random(),
  date: '', invoiceNo: '',
  chargeTo: 'TWT.1746G - Elton Yang',
  project: 'ISP002 - .Other Expense for Employee',
  recharge: false,
  category: 'Internet Access, ADSL',
  currency: 'NTD', amount: '',
  description: '',
})

interface GEEFormProps extends FormRuntimeProps {
  persona: PersonaCode
}

export function GEEForm({ persona, ...rt }: GEEFormProps) {
  const runtime = useFormRuntime('GEE', rt)
  const isTaskMode = runtime.mode === 'task'

  const [items, setItems] = useState<GEEItem[]>([{
    ...newItem(), id: 1, amount: '779', description: '2025 May, Network fee',
  }])

  // Hydrate first line from runtime task snapshot in task mode
  useEffect(() => {
    if (!isTaskMode || !runtime.task) return
    const fd = (runtime.task.mergedFormData ?? {}) as {
      category?: string
      expense_date?: string
      amount?: number | string
      currency?: string
      invoice_no?: string
      description?: string
    }
    setItems([{
      id: 1,
      date: fd.expense_date ?? '',
      invoiceNo: fd.invoice_no ?? '',
      chargeTo: 'TWT.1746G - Elton Yang',
      project: 'ISP002 - .Other Expense for Employee',
      recharge: false,
      category: mapCategoryFromSpec(fd.category) ?? GEE_CATS[0],
      currency: fd.currency === 'TWD' ? 'NTD' : (fd.currency ?? 'NTD'),
      amount: fd.amount !== undefined ? String(fd.amount) : '',
      description: fd.description ?? '',
    }])
  }, [isTaskMode, runtime.task])

  const add = () => setItems(p => [...p, newItem()])
  const copy = (id: number) => setItems(p => {
    const src = p.find(i => i.id === id); if (!src) return p
    return [...p, { ...src, id: Date.now() }]
  })
  const del = (id: number) => setItems(p => p.filter(i => i.id !== id))
  const upd = <K extends keyof GEEItem>(id: number, f: K, v: GEEItem[K]) =>
    setItems(p => p.map(i => i.id === id ? { ...i, [f]: v } : i))

  const toNTD = (item: GEEItem) => {
    const n = Number(item.amount || 0)
    if (item.currency === 'USD') return n * NTD_RATE
    return n
  }
  const total = items.reduce((s, i) => s + toNTD(i), 0)
  const isReadOnly = isTaskMode

  /** Map first item to spec gee_v1.json field ids. */
  function toSpecPayload() {
    const first = items[0]
    return {
      category: mapCategoryToSpec(first.category),
      expense_date: first.date,
      amount: Number(first.amount || 0),
      currency: first.currency === 'NTD' ? 'TWD' : first.currency,
      invoice_no: first.invoiceNo || undefined,
      description: first.description,
    }
  }

  return (
    <FormShell code="GEE" activeStep={0} setActiveStep={() => {}} persona={persona} mode={runtime.mode}>
      <FlowToast message={runtime.toast} />

      <InfoBanner>
        <strong>Expense reimbursement application shall be submitted within one month after spending (based on invoice date).</strong>
        <div className="mt-1 text-xs text-amber-700">所有費用依規定需在憑證(發票或收據)所載日期1個月內完成費用申請，逾期依規定將退回不予受理。</div>
      </InfoBanner>

      {items.map((item, idx) => (
        <SectionCard key={item.id}>
          {/* Invoice header bar */}
          <div className="flex flex-wrap items-center gap-x-6 gap-y-2 border-b border-rule bg-slate-50 px-4 py-2.5">
            <span className="min-w-[80px] text-sm font-semibold text-ink">Invoice #{idx + 1}</span>
            <div className="flex items-center gap-2">
              <FieldLabel required>Invoice Date</FieldLabel>
              <Input type="date" className="w-36" value={item.date} readOnly={isReadOnly} onChange={e => upd(item.id, 'date', e.target.value)} />
            </div>
            <div className="flex items-center gap-2">
              <FieldLabel>Invoice No.</FieldLabel>
              <Input className="w-40" value={item.invoiceNo} readOnly={isReadOnly} onChange={e => upd(item.id, 'invoiceNo', e.target.value)} />
            </div>
          </div>

          <div className="flex">
            <div className="min-w-0 flex-1 space-y-4 p-4">
              <div className="grid grid-cols-2 gap-5">
                <div>
                  <FieldLabel required>Charge to</FieldLabel>
                  <Select value={item.chargeTo} disabled={isReadOnly} onChange={e => upd(item.id, 'chargeTo', e.target.value)}>
                    {CHARGE_OPTS.map(o => <option key={o}>{o}</option>)}
                  </Select>
                  <div className="mt-2">
                    <Checkbox
                      id={`rch-${item.id}`}
                      label="Recharge to outside Taiwan (Taipei)"
                      checked={item.recharge}
                      disabled={isReadOnly}
                      onChange={e => upd(item.id, 'recharge', e.target.checked)}
                    />
                  </div>
                </div>
                <div>
                  <FieldLabel required>Project</FieldLabel>
                  <Select value={item.project} disabled={isReadOnly} onChange={e => upd(item.id, 'project', e.target.value)}>
                    <option value="">Click or enter keyword to search</option>
                    {PROJECT_OPTS.map(o => <option key={o}>{o}</option>)}
                  </Select>
                </div>
              </div>

              <div className="grid grid-cols-2 gap-5">
                <div>
                  <FieldLabel required>Category</FieldLabel>
                  <Select value={item.category} disabled={isReadOnly} onChange={e => upd(item.id, 'category', e.target.value)}>
                    <option value="">-- Please Select --</option>
                    {GEE_CATS.map(o => <option key={o}>{o}</option>)}
                  </Select>
                </div>
                <div>
                  <FieldLabel required>Amount</FieldLabel>
                  <div className="flex gap-2">
                    <Select value={item.currency} disabled={isReadOnly} className="w-24 flex-shrink-0" onChange={e => upd(item.id, 'currency', e.target.value)}>
                      {CURRENCIES.map(c => <option key={c}>{c}</option>)}
                    </Select>
                    <Input type="number" min={0} className={cn('text-right font-mono')} value={item.amount} readOnly={isReadOnly} onChange={e => upd(item.id, 'amount', e.target.value)} />
                  </div>
                  {toNTD(item) > 0 && (
                    <div className="mt-1 text-right text-xs">
                      <span className="font-mono font-medium text-danger">{fmtNTD(toNTD(item))}</span>
                      <span className="ml-1 font-mono text-ink-faint">{fmtUSD(toNTD(item))}</span>
                    </div>
                  )}
                </div>
              </div>

              <div>
                <FieldLabel required>Description</FieldLabel>
                <Input value={item.description} readOnly={isReadOnly} onChange={e => upd(item.id, 'description', e.target.value)} placeholder="e.g. 2025 May, Network fee" />
              </div>

              <div>
                <FieldLabel tip="Please attach the original invoice/receipt">Attachment</FieldLabel>
                <label className="inline-flex cursor-pointer items-center gap-2 rounded-md border border-rule px-3 py-1.5 text-sm text-blue-600 transition-colors hover:bg-blue-50">
                  <Upload className="h-4 w-4" /> Browse
                  <input type="file" className="hidden" />
                </label>
              </div>
            </div>

            {!isReadOnly && (
              <div className="flex flex-col items-center gap-1.5 border-l border-rule bg-slate-50/60 p-2">
                <RowAction Icon={Plus} label="Add" tone="blue" onClick={add} />
                <RowAction Icon={Copy} label="Copy" tone="slate" onClick={() => copy(item.id)} />
                <RowAction Icon={Trash2} label="Delete" tone="red" onClick={() => del(item.id)} disabled={items.length <= 1} />
              </div>
            )}
          </div>
        </SectionCard>
      ))}

      {/* Total */}
      <SectionCard>
        <div className="flex items-center justify-end gap-3 px-4 py-3">
          <span className="text-sm font-semibold text-ink">Total</span>
          <span className="font-mono text-base font-bold text-danger">{fmtNTD(total)}</span>
          <span className="font-mono text-sm text-ink-faint">{fmtUSD(total)}</span>
        </div>
      </SectionCard>

      <div className="grid grid-cols-2 gap-4">
        <SectionCard>
          <SectionTitle>Attachment</SectionTitle>
          <div className="p-4">
            <label className="flex cursor-pointer flex-col items-center gap-2 rounded-md border-2 border-dashed border-rule p-6 transition-colors hover:border-blue-300 hover:bg-blue-50/30">
              <Upload className="h-8 w-8 text-ink-faint" />
              <p className="text-sm text-blue-500">Click or Drop file here</p>
              <p className="text-xs text-ink-faint">cannot exceed 10MB</p>
              <input type="file" multiple className="hidden" />
            </label>
          </div>
        </SectionCard>
        <SectionCard>
          <SectionTitle>Note</SectionTitle>
          <div className="p-4">
            <Textarea rows={4} placeholder="Add any notes here…" />
          </div>
        </SectionCard>
      </div>

      <ActionBar code="GEE" activeStep={0} persona={persona}
        mode={runtime.mode}
        nodeKind={runtime.task?.task.nodeKind}
        pending={runtime.pending}
        onSubmit={() => {
          if (isTaskMode) { void runtime.submitUserTask(); return }
          if (!items[0].date || !Number(items[0].amount) || !items[0].description.trim()) {
            runtime.fireToast('Date, amount and description are required.')
            return
          }
          void runtime.submitCreate(toSpecPayload())
        }}
        onApprove={c => { void runtime.approve(c) }}
        onReject={c => { void runtime.reject(c) }}
        onReturnTask={c => { void runtime.returnTask(c) }}
        onClose={() => {}}
      />
    </FormShell>
  )
}

/** Map free-form GEE_CATS UI label to spec category enum. */
function mapCategoryToSpec(uiCategory: string): string {
  const lc = uiCategory.toLowerCase()
  if (lc.includes('travel') || lc.includes('meal') || lc.includes('client')) return 'business'
  if (lc.includes('equipment') || lc.includes('hardware')) return 'equipment'
  if (lc.includes('misc') || lc.includes('other') || lc.includes('internet')) return 'misc'
  return 'other'
}

/** Reverse: pick a representative UI label for a spec enum value. */
function mapCategoryFromSpec(specCategory: string | undefined): string | null {
  if (!specCategory) return null
  switch (specCategory) {
    case 'business': return GEE_CATS.find(c => c.toLowerCase().includes('client') || c.toLowerCase().includes('meal')) ?? GEE_CATS[0]
    case 'equipment': return GEE_CATS.find(c => c.toLowerCase().includes('equipment')) ?? GEE_CATS[0]
    case 'misc': return GEE_CATS.find(c => c.toLowerCase().includes('internet')) ?? GEE_CATS[0]
    default: return GEE_CATS[0]
  }
}

function RowAction({ Icon, label, tone, onClick, disabled }: {
  Icon: React.ComponentType<{ className?: string }>
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
      onClick={onClick}
      disabled={disabled}
      className={cn(
        'flex w-11 flex-col items-center gap-0.5 rounded-md border bg-white py-1.5 transition-colors',
        'disabled:pointer-events-none disabled:opacity-30',
        cls,
      )}
    >
      <Icon className="h-4 w-4" />
      <span className="text-[9px] font-medium">{label}</span>
    </button>
  )
}
