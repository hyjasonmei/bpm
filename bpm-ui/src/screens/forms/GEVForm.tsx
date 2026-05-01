import { useState } from 'react'
import { Plus } from 'lucide-react'
import { cn, fmtNTD, fmtUSD } from '@/lib/cn'
import { Button } from '@/components/ui/button'
import { SectionCard, SectionTitle } from '@/components/ui/card'
import { Input, Textarea, Select, Checkbox, FieldLabel } from '@/components/ui/form'
import { UploadZone } from '@/components/ui/readonly'
import { FormShell, ActionBar } from './FormShell'
import { CHARGE_OPTS, CURRENCIES, GEV_CATS, PAYMENT_TERMS, VAT_RATES } from '@/lib/mocks'
import type { PersonaCode } from '@/lib/role'
import { Plus as PlusIcon, Copy as CopyIcon, Trash2 as TrashIcon } from 'lucide-react'

interface GEVLine {
  id: number
  chargeTo: string
  project: string
  recharge: boolean
  amount: string
  category: string
  description: string
}
interface GEVInvoice {
  id: number
  date: string
  no: string
  currency: string
  vat: string
  lines: GEVLine[]
}

const newLine = (): GEVLine => ({
  id: Date.now() + Math.random(),
  chargeTo: 'TWT.1746G - Elton Yang',
  project: '',
  recharge: false,
  amount: '',
  category: 'Outside service',
  description: '',
})
const newInvoice = (): GEVInvoice => ({
  id: Date.now() + Math.random(),
  date: '', no: '', currency: 'NTD', vat: '5%',
  lines: [newLine()],
})

export function GEVForm({ persona }: { persona: PersonaCode }) {
  const [activeStep, setActiveStep] = useState(0)
  const [vendor, setVendor] = useState('V97162640 - 關貿網路股份有限公司')
  const [isNewVendor, setIsNewVendor] = useState(false)
  const [payTerm, setPayTerm] = useState<string>('Current month')
  const [useContract, setUseContract] = useState(true)
  const [contractNo, setContractNo] = useState('190116-AP-0003')
  const [invoices, setInvoices] = useState<GEVInvoice[]>([{
    id: 1, date: '', no: '', currency: 'NTD', vat: '5%',
    lines: [{
      id: 1,
      chargeTo: 'TWT.1746G - Elton Yang', project: '',
      recharge: false, amount: '333',
      category: 'Outside service', description: 'GUI2.0 傳輸服務費用 - May, 2025',
    }],
  }])

  const addInv = () => setInvoices(p => [...p, newInvoice()])
  const delInv = (id: number) => { if (invoices.length > 1) setInvoices(p => p.filter(i => i.id !== id)) }
  const updInv = <K extends keyof GEVInvoice>(id: number, f: K, v: GEVInvoice[K]) => setInvoices(p => p.map(i => i.id === id ? { ...i, [f]: v } : i))

  const addLine = (invId: number) => setInvoices(p => p.map(inv => inv.id === invId ? { ...inv, lines: [...inv.lines, newLine()] } : inv))
  const copyLine = (invId: number, lId: number) => setInvoices(p => p.map(inv => {
    if (inv.id !== invId) return inv
    const src = inv.lines.find(l => l.id === lId); if (!src) return inv
    return { ...inv, lines: [...inv.lines, { ...src, id: Date.now() }] }
  }))
  const delLine = (invId: number, lId: number) => setInvoices(p => p.map(inv => {
    if (inv.id !== invId || inv.lines.length <= 1) return inv
    return { ...inv, lines: inv.lines.filter(l => l.id !== lId) }
  }))
  const updLine = <K extends keyof GEVLine>(invId: number, lId: number, f: K, v: GEVLine[K]) => setInvoices(p => p.map(inv =>
    inv.id !== invId ? inv : { ...inv, lines: inv.lines.map(l => l.id === lId ? { ...l, [f]: v } : l) }
  ))

  const subtotal = (inv: GEVInvoice) => inv.lines.reduce((s, l) => s + Number(l.amount || 0), 0)
  const vatAmt = (inv: GEVInvoice) => Math.round(subtotal(inv) * (parseFloat(inv.vat) / 100))
  const invTotal = (inv: GEVInvoice) => subtotal(inv) + vatAmt(inv)
  const grandTotal = invoices.reduce((s, inv) => s + invTotal(inv), 0)
  const isReadOnly = activeStep > 0

  return (
    <FormShell code="GEV" activeStep={activeStep} setActiveStep={setActiveStep} persona={persona}>
      {/* Vendor / Payment block */}
      <SectionCard>
        <div className="grid grid-cols-2 gap-5 p-4">
          <div>
            <FieldLabel required>Vendor</FieldLabel>
            <Select value={vendor} onChange={e => setVendor(e.target.value)} disabled={isReadOnly}>
              <option>V97162640 - 關貿網路股份有限公司</option>
              <option>V00123456 - Example Vendor Ltd.</option>
              <option>V00654321 - Tech Services Co.</option>
            </Select>
            <div className="mt-2">
              <Checkbox id="gev-newvendor" label="New Vendor" checked={isNewVendor} disabled={isReadOnly} onChange={e => setIsNewVendor(e.target.checked)} />
            </div>
          </div>
          <div>
            <FieldLabel required>Payment Term</FieldLabel>
            <Select value={payTerm} onChange={e => setPayTerm(e.target.value)} disabled={isReadOnly}>
              {PAYMENT_TERMS.map(t => <option key={t}>{t}</option>)}
            </Select>
            <div className="mt-2 flex flex-wrap items-center gap-2">
              <Checkbox id="gev-contract" label="Payment based on contract" checked={useContract} disabled={isReadOnly} onChange={e => setUseContract(e.target.checked)} />
              {useContract && <Input className="w-36" value={contractNo} readOnly={isReadOnly} onChange={e => setContractNo(e.target.value)} />}
            </div>
          </div>
        </div>
      </SectionCard>

      {/* Invoice blocks */}
      {invoices.map((inv, invIdx) => {
        const sub = subtotal(inv)
        const vat = vatAmt(inv)
        const tot = invTotal(inv)
        return (
          <SectionCard key={inv.id}>
            {/* dark invoice header */}
            <div className="flex flex-wrap items-center gap-x-4 gap-y-2 bg-slate-700 px-4 py-2 text-sm text-white">
              <span className="min-w-[80px] font-semibold">Invoice #{invIdx + 1}</span>
              <div className="flex items-center gap-2">
                <span className="text-xs text-slate-300">Invoice Date/No.<span className="ml-0.5 text-red-400">*</span></span>
                <input type="date" value={inv.date} disabled={isReadOnly} onChange={e => updInv(inv.id, 'date', e.target.value)}
                  className="h-7 w-36 rounded border border-slate-500 bg-slate-600 px-2 text-sm text-white focus:outline-none focus:ring-1 focus:ring-blue-400 disabled:opacity-60" />
                <input value={inv.no} placeholder="Invoice No." disabled={isReadOnly} onChange={e => updInv(inv.id, 'no', e.target.value)}
                  className="h-7 w-32 rounded border border-slate-500 bg-slate-600 px-2 text-sm text-white placeholder:text-slate-400 focus:outline-none focus:ring-1 focus:ring-blue-400 disabled:opacity-60" />
              </div>
              <div className="ml-auto flex items-center gap-2">
                <span className="text-xs text-slate-300">Invoice Currency<span className="ml-0.5 text-red-400">*</span></span>
                <select value={inv.currency} disabled={isReadOnly} onChange={e => updInv(inv.id, 'currency', e.target.value)}
                  className="h-7 w-20 rounded border border-slate-500 bg-slate-600 px-2 text-sm text-white focus:outline-none disabled:opacity-60">
                  {CURRENCIES.map(c => <option key={c}>{c}</option>)}
                </select>
              </div>
              <div className="flex items-center gap-2">
                <span className="text-xs text-slate-300">VAT<span className="ml-0.5 text-red-400">*</span></span>
                <select value={inv.vat} disabled={isReadOnly} onChange={e => updInv(inv.id, 'vat', e.target.value)}
                  className="h-7 w-16 rounded border border-slate-500 bg-slate-600 px-2 text-sm text-white focus:outline-none disabled:opacity-60">
                  {VAT_RATES.map(r => <option key={r}>{r}</option>)}
                </select>
              </div>
            </div>

            {/* Line items */}
            {inv.lines.map((line, lIdx) => (
              <div key={line.id} className="flex border-b border-slate-100">
                <div className="flex w-8 items-start justify-center pt-5 font-mono text-xs text-ink-faint">#{lIdx + 1}</div>
                <div className="min-w-0 flex-1 space-y-3 p-4">
                  <div className="grid grid-cols-3 gap-4">
                    <div>
                      <FieldLabel required>Charge to</FieldLabel>
                      <Select value={line.chargeTo} disabled={isReadOnly} onChange={e => updLine(inv.id, line.id, 'chargeTo', e.target.value)}>
                        {CHARGE_OPTS.map(o => <option key={o}>{o}</option>)}
                      </Select>
                      <div className="mt-1.5">
                        <Checkbox id={`gev-rch-${line.id}`} label="Recharge to outside Taiwan (Taipei)" checked={line.recharge} disabled={isReadOnly} onChange={e => updLine(inv.id, line.id, 'recharge', e.target.checked)} />
                      </div>
                    </div>
                    <div>
                      <FieldLabel required>Project</FieldLabel>
                      <Input placeholder="Click or enter keyword to search" value={line.project} readOnly={isReadOnly} onChange={e => updLine(inv.id, line.id, 'project', e.target.value)} />
                    </div>
                    <div>
                      <FieldLabel required>Amount</FieldLabel>
                      <div className="flex gap-1.5">
                        <span className="flex h-8 items-center whitespace-nowrap rounded-md border border-rule bg-slate-50 px-2 text-sm text-ink-muted">{inv.currency}</span>
                        <Input type="number" min={0} className="text-right font-mono" value={line.amount} readOnly={isReadOnly} onChange={e => updLine(inv.id, line.id, 'amount', e.target.value)} />
                      </div>
                      {Number(line.amount || 0) > 0 && (
                        <div className="mt-1 text-right text-xs">
                          <span className="font-mono font-medium text-danger">{fmtNTD(Number(line.amount))}</span>
                          <span className="ml-1 font-mono text-ink-faint">{fmtUSD(Number(line.amount))}</span>
                        </div>
                      )}
                    </div>
                  </div>
                  <div className="grid grid-cols-2 gap-4">
                    <div>
                      <FieldLabel required>Category</FieldLabel>
                      <Select value={line.category} disabled={isReadOnly} onChange={e => updLine(inv.id, line.id, 'category', e.target.value)}>
                        {GEV_CATS.map(c => <option key={c}>{c}</option>)}
                      </Select>
                    </div>
                    <div>
                      <FieldLabel required>Description</FieldLabel>
                      <Textarea rows={2} value={line.description} readOnly={isReadOnly} onChange={e => updLine(inv.id, line.id, 'description', e.target.value)} />
                    </div>
                  </div>
                </div>
                {!isReadOnly && (
                  <div className="flex flex-col items-center gap-1.5 border-l border-rule bg-slate-50/60 p-2">
                    <SmallAct Icon={PlusIcon}  label="Add"    tone="blue"  onClick={() => addLine(inv.id)} />
                    <SmallAct Icon={CopyIcon}  label="Copy"   tone="slate" onClick={() => copyLine(inv.id, line.id)} />
                    <SmallAct Icon={TrashIcon} label="Delete" tone="red"   onClick={() => delLine(inv.id, line.id)} disabled={inv.lines.length <= 1} />
                  </div>
                )}
              </div>
            ))}

            {/* Invoice totals */}
            <div className="space-y-1.5 border-t border-rule bg-slate-50/60 px-4 py-3">
              <div className="flex justify-between text-sm text-ink-muted">
                <span>Subtotal</span>
                <span className="font-mono"><span className="text-danger">{fmtNTD(sub)}</span> <span className="text-ink-faint">{fmtUSD(sub)}</span></span>
              </div>
              <div className="flex justify-between text-sm text-ink-muted">
                <span>VAT</span>
                <span className="font-mono"><span className="text-danger">{fmtNTD(vat)}</span> <span className="text-ink-faint">{fmtUSD(vat)}</span></span>
              </div>
              <div className="flex justify-between border-t border-rule pt-1.5 text-sm font-semibold text-ink">
                <span>Invoice #{invIdx + 1} Total</span>
                <span className="font-mono"><span className="text-danger">{fmtNTD(tot)}</span> <span className="text-ink-faint">{fmtUSD(tot)}</span></span>
              </div>
            </div>
          </SectionCard>
        )
      })}

      {!isReadOnly && (
        <div className="flex gap-3">
          <Button variant="outline" size="md" onClick={addInv}><Plus className="h-4 w-4" />Add Invoice</Button>
          <Button variant="destructive" size="md" onClick={() => delInv(invoices[invoices.length - 1].id)} disabled={invoices.length <= 1}>Delete Invoice</Button>
        </div>
      )}

      {/* Grand total */}
      <SectionCard>
        <div className="flex items-center justify-end gap-3 px-4 py-3">
          <span className="text-sm font-semibold text-ink">Total</span>
          <span className="font-mono text-base font-bold text-danger">{fmtNTD(grandTotal)}</span>
          <span className="font-mono text-sm text-ink-faint">{fmtUSD(grandTotal)}</span>
        </div>
      </SectionCard>

      <div className="grid grid-cols-2 gap-4">
        <SectionCard>
          <SectionTitle>Attachment</SectionTitle>
          <div className="p-4"><UploadZone /></div>
        </SectionCard>
        <SectionCard>
          <SectionTitle>Note</SectionTitle>
          <div className="p-4"><Textarea rows={4} placeholder="Add any notes here…" /></div>
        </SectionCard>
      </div>

      <ActionBar code="GEV" activeStep={activeStep} persona={persona}
        onSubmit={() => setActiveStep(s => s + 1)}
        onApprove={() => setActiveStep(s => s + 1)}
        onReject={() => setActiveStep(0)}
      />
    </FormShell>
  )
}

function SmallAct({ Icon, label, tone, onClick, disabled }: {
  Icon: React.ComponentType<{ className?: string }>; label: string; tone: 'blue' | 'slate' | 'red'; onClick: () => void; disabled?: boolean
}) {
  const cls = {
    blue:  'border-blue-200 text-blue-600 hover:bg-blue-50',
    slate: 'border-rule text-ink-muted hover:bg-slate-50',
    red:   'border-red-100 text-red-400 hover:bg-red-50',
  }[tone]
  return (
    <button onClick={onClick} disabled={disabled}
      className={cn(
        'flex w-11 flex-col items-center gap-0.5 rounded-md border bg-white py-1.5 transition-colors',
        'disabled:pointer-events-none disabled:opacity-30',
        cls,
      )}>
      <Icon className="h-4 w-4" />
      <span className="text-[9px] font-medium">{label}</span>
    </button>
  )
}
