import { useEffect, useState } from 'react'
import { SectionCard, SectionTitle } from '@/components/ui/card'
import { Input, Select, Textarea, Field } from '@/components/ui/form'
import { FormShell, ActionBar } from './FormShell'
import type { PersonaCode } from '@/lib/role'
import { FlowToast, useFormRuntime, type FormRuntimeProps } from '@/hooks/useFormRuntime'

const CATEGORY_OPTIONS = [
  { value: 'software', label: '軟體' },
  { value: 'saas', label: 'SaaS' },
  { value: 'service', label: '服務' },
] as const

const CURRENCY_OPTIONS = [
  { value: 'TWD', label: 'TWD' },
  { value: 'USD', label: 'USD' },
] as const

interface ITPRViewProps extends FormRuntimeProps {
  persona: PersonaCode
}

/**
 * IT software / service purchase. 7-step flow: apply → IT spec → quote →
 * confirm → manager approval → finance PO → close. Promoted to dual-mode
 * form so the runtime can drive each step rather than the static demo view.
 */
export function ITPRView({ persona, ...rt }: ITPRViewProps) {
  const runtime = useFormRuntime('ITPR', rt)
  const isTaskMode = runtime.mode === 'task'

  const [category, setCategory] = useState<string>('saas')
  const [itemName, setItemName] = useState('Power BI Pro')
  const [vendorName, setVendorName] = useState('Microsoft')
  const [spec, setSpec] = useState('4 seats x 3 years')
  const [estimatedAmount, setEstimatedAmount] = useState('26352')
  const [currency, setCurrency] = useState('TWD')
  const [purpose, setPurpose] = useState('')
  const [expectedDate, setExpectedDate] = useState('')

  // Per-step task fields
  const [specReview, setSpecReview] = useState('')
  const [specDecision, setSpecDecision] = useState<'approved' | 'needs_change'>('approved')
  const [quoteAmount, setQuoteAmount] = useState('')
  const [quoteCurrency, setQuoteCurrency] = useState('TWD')
  const [quoteNo, setQuoteNo] = useState('')
  const [quoteNote, setQuoteNote] = useState('')
  const [printNo, setPrintNo] = useState('')
  const [confirmNote, setConfirmNote] = useState('')
  const [poNumber, setPoNumber] = useState('')
  const [poDate, setPoDate] = useState('')
  const [poNote, setPoNote] = useState('')

  useEffect(() => {
    if (!isTaskMode || !runtime.task) return
    const fd = (runtime.task.mergedFormData ?? {}) as Record<string, unknown>
    if (typeof fd.category === 'string') setCategory(fd.category)
    if (typeof fd.item_name === 'string') setItemName(fd.item_name)
    if (typeof fd.vendor_name === 'string') setVendorName(fd.vendor_name)
    if (typeof fd.spec === 'string') setSpec(fd.spec)
    if (fd.estimated_amount !== undefined) setEstimatedAmount(String(fd.estimated_amount))
    if (typeof fd.currency === 'string') setCurrency(fd.currency)
    if (typeof fd.purpose === 'string') setPurpose(fd.purpose)
    if (typeof fd.expected_date === 'string') setExpectedDate(fd.expected_date)
    if (typeof fd.spec_review === 'string') setSpecReview(fd.spec_review)
    if (fd.spec_decision === 'approved' || fd.spec_decision === 'needs_change') setSpecDecision(fd.spec_decision)
    if (fd.quote_amount !== undefined) setQuoteAmount(String(fd.quote_amount))
    if (typeof fd.quote_currency === 'string') setQuoteCurrency(fd.quote_currency)
    if (typeof fd.quote_no === 'string') setQuoteNo(fd.quote_no)
    if (typeof fd.quote_note === 'string') setQuoteNote(fd.quote_note)
    if (typeof fd.print_no === 'string') setPrintNo(fd.print_no)
    if (typeof fd.confirm_note === 'string') setConfirmNote(fd.confirm_note)
    if (typeof fd.po_number === 'string') setPoNumber(fd.po_number)
    if (typeof fd.po_date === 'string') setPoDate(fd.po_date)
    if (typeof fd.po_note === 'string') setPoNote(fd.po_note)
  }, [isTaskMode, runtime.task])

  const nodeId = runtime.task?.task.nodeId
  const ro = isTaskMode

  function toApplyPayload() {
    return {
      category,
      item_name: itemName,
      vendor_name: vendorName,
      spec,
      estimated_amount: Number(estimatedAmount || 0),
      currency,
      purpose: purpose || undefined,
      expected_date: expectedDate || undefined,
    }
  }

  function patchForCurrentTask(): unknown {
    switch (nodeId) {
      case 'task_it_spec':
        return { spec_review: specReview, spec_decision: specDecision }
      case 'task_it_quote':
        return {
          quote_amount: Number(quoteAmount || 0),
          quote_currency: quoteCurrency,
          quote_no: quoteNo,
          quote_file: 'quote.pdf',
          quote_note: quoteNote || undefined,
        }
      case 'task_confirm':
        return { print_no: printNo, confirm_note: confirmNote || undefined }
      case 'task_finance_po':
        return { po_number: poNumber, po_date: poDate, po_note: poNote || undefined }
      default:
        return undefined
    }
  }

  return (
    <FormShell code="ITPR" activeStep={0} setActiveStep={() => {}} persona={persona} copySelector={false} mode={runtime.mode}>
      <FlowToast message={runtime.toast} />

      <SectionCard>
        <SectionTitle>Software / Service Request</SectionTitle>
        <div className="grid grid-cols-2 gap-x-8 gap-y-3 p-4">
          <Field label="Category" required>
            <Select value={category} disabled={ro} onChange={e => setCategory(e.target.value)}>
              {CATEGORY_OPTIONS.map(o => <option key={o.value} value={o.value}>{o.label}</option>)}
            </Select>
          </Field>
          <Field label="Item Name" required>
            <Input value={itemName} readOnly={ro} onChange={e => setItemName(e.target.value)} />
          </Field>
          <Field label="Vendor" required>
            <Input value={vendorName} readOnly={ro} onChange={e => setVendorName(e.target.value)} />
          </Field>
          <Field label="Estimated Amount" required>
            <div className="flex gap-2">
              <Select value={currency} disabled={ro} className="w-24" onChange={e => setCurrency(e.target.value)}>
                {CURRENCY_OPTIONS.map(o => <option key={o.value} value={o.value}>{o.label}</option>)}
              </Select>
              <Input type="number" min={0} className="text-right font-mono" value={estimatedAmount} readOnly={ro} onChange={e => setEstimatedAmount(e.target.value)} />
            </div>
          </Field>
          <div className="col-span-2">
            <Field label="Spec" required>
              <Textarea rows={2} value={spec} readOnly={ro} onChange={e => setSpec(e.target.value)} />
            </Field>
          </div>
          <Field label="Purpose">
            <Input value={purpose} readOnly={ro} onChange={e => setPurpose(e.target.value)} />
          </Field>
          <Field label="Expected Date">
            <Input type="date" value={expectedDate} readOnly={ro} onChange={e => setExpectedDate(e.target.value)} />
          </Field>
        </div>
      </SectionCard>

      {nodeId === 'task_it_spec' && (
        <SectionCard>
          <SectionTitle>IT Spec Review</SectionTitle>
          <div className="space-y-3 p-4">
            <Field label="Spec Review" required>
              <Textarea rows={3} value={specReview} onChange={e => setSpecReview(e.target.value)} />
            </Field>
            <Field label="Decision">
              <Select value={specDecision} onChange={e => setSpecDecision(e.target.value as 'approved' | 'needs_change')}>
                <option value="approved">核准</option>
                <option value="needs_change">需修改</option>
              </Select>
            </Field>
          </div>
        </SectionCard>
      )}

      {nodeId === 'task_it_quote' && (
        <SectionCard>
          <SectionTitle>Quote</SectionTitle>
          <div className="grid grid-cols-2 gap-x-8 gap-y-3 p-4">
            <Field label="Quote Amount" required>
              <Input type="number" value={quoteAmount} onChange={e => setQuoteAmount(e.target.value)} />
            </Field>
            <Field label="Quote Currency" required>
              <Select value={quoteCurrency} onChange={e => setQuoteCurrency(e.target.value)}>
                {CURRENCY_OPTIONS.map(o => <option key={o.value} value={o.value}>{o.label}</option>)}
              </Select>
            </Field>
            <Field label="Quote No" required>
              <Input value={quoteNo} onChange={e => setQuoteNo(e.target.value)} />
            </Field>
            <div className="col-span-2">
              <Field label="Note"><Textarea rows={2} value={quoteNote} onChange={e => setQuoteNote(e.target.value)} /></Field>
            </div>
          </div>
        </SectionCard>
      )}

      {nodeId === 'task_confirm' && (
        <SectionCard>
          <SectionTitle>Confirm & Print</SectionTitle>
          <div className="grid grid-cols-2 gap-x-8 gap-y-3 p-4">
            <Field label="Print No" required>
              <Input value={printNo} onChange={e => setPrintNo(e.target.value)} />
            </Field>
            <div className="col-span-2">
              <Field label="Note"><Textarea rows={2} value={confirmNote} onChange={e => setConfirmNote(e.target.value)} /></Field>
            </div>
          </div>
        </SectionCard>
      )}

      {nodeId === 'task_finance_po' && (
        <SectionCard>
          <SectionTitle>Finance PO</SectionTitle>
          <div className="grid grid-cols-2 gap-x-8 gap-y-3 p-4">
            <Field label="PO Number" required>
              <Input value={poNumber} onChange={e => setPoNumber(e.target.value)} />
            </Field>
            <Field label="PO Date" required>
              <Input type="date" value={poDate} onChange={e => setPoDate(e.target.value)} />
            </Field>
            <div className="col-span-2">
              <Field label="Note"><Textarea rows={2} value={poNote} onChange={e => setPoNote(e.target.value)} /></Field>
            </div>
          </div>
        </SectionCard>
      )}

      <ActionBar
        code="ITPR"
        activeStep={0}
        persona={persona}
        mode={runtime.mode}
        nodeKind={runtime.task?.task.nodeKind}
        pending={runtime.pending}
        onSubmit={() => {
          if (isTaskMode) { void runtime.submitUserTask(patchForCurrentTask()); return }
          if (!itemName.trim() || !vendorName.trim() || !spec.trim() || !Number(estimatedAmount)) {
            runtime.fireToast('Item, vendor, spec and estimated amount are required.')
            return
          }
          void runtime.submitCreate(toApplyPayload())
        }}
        onApprove={c => { void runtime.approve(c) }}
        onReject={c => { void runtime.reject(c) }}
        onReturnTask={c => { void runtime.returnTask(c) }}
      />
    </FormShell>
  )
}
