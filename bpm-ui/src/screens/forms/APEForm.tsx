import { useEffect, useState } from 'react'
import { fmtNTD, fmtUSD, NTD_RATE } from '@/lib/cn'
import { SectionCard, SectionTitle } from '@/components/ui/card'
import { Input, Textarea, Select, Checkbox, FieldLabel } from '@/components/ui/form'
import { UploadZone } from '@/components/ui/readonly'
import { FormShell, ActionBar } from './FormShell'
import { CHARGE_OPTS, CURRENCIES } from '@/lib/mocks'
import type { PersonaCode } from '@/lib/role'
import { FlowToast, useFormRuntime, type FormRuntimeProps } from '@/hooks/useFormRuntime'

interface APEFormProps extends FormRuntimeProps {
  persona: PersonaCode
}

export function APEForm({ persona, ...rt }: APEFormProps) {
  const runtime = useFormRuntime('APE', rt)
  const isTaskMode = runtime.mode === 'task'

  const [receiveDate, setReceiveDate] = useState('')
  const [returnDate, setReturnDate] = useState('')
  const [dept, setDept] = useState('TWT.1746G - Elton Yang')
  const [recharge, setRecharge] = useState(false)
  const [currency, setCurrency] = useState('NTD')
  const [amount, setAmount] = useState('')
  const [description, setDescription] = useState('')
  const [note, setNote] = useState('')

  useEffect(() => {
    if (!isTaskMode || !runtime.task) return
    const fd = (runtime.task.mergedFormData ?? {}) as {
      advance_amount?: number | string
      currency?: string
      purpose?: string
      expected_spend_date?: string
      expected_settle_date?: string
      charge_dept?: string
    }
    if (fd.expected_spend_date) setReceiveDate(fd.expected_spend_date)
    if (fd.expected_settle_date) setReturnDate(fd.expected_settle_date)
    if (fd.charge_dept) setDept(fd.charge_dept)
    if (fd.currency) setCurrency(fd.currency === 'TWD' ? 'NTD' : fd.currency)
    if (fd.advance_amount !== undefined) setAmount(String(fd.advance_amount))
    if (fd.purpose) setDescription(fd.purpose)
  }, [isTaskMode, runtime.task])

  const ntdAmt = currency === 'NTD' ? Number(amount || 0) : Number(amount || 0) * NTD_RATE
  const ro = isTaskMode

  function toSpecPayload() {
    return {
      advance_amount: Number(amount || 0),
      currency: currency === 'NTD' ? 'TWD' : currency,
      purpose: description,
      expected_spend_date: receiveDate,
      expected_settle_date: returnDate,
      charge_dept: dept,
    }
  }

  return (
    <FormShell code="APE" activeStep={0} setActiveStep={() => {}} persona={persona} mode={runtime.mode}>
      <FlowToast message={runtime.toast} />
      <SectionCard>
        <div className="space-y-4 p-4">
          <div className="grid grid-cols-2 gap-5">
            <div>
              <FieldLabel required>The date you expect to receive the cash</FieldLabel>
              <Input type="date" value={receiveDate} readOnly={ro} onChange={e => setReceiveDate(e.target.value)} />
            </div>
            <div>
              <FieldLabel required>The date you will deduct / return the advance</FieldLabel>
              <Input type="date" value={returnDate} readOnly={ro} onChange={e => setReturnDate(e.target.value)} />
            </div>
          </div>

          <div>
            <FieldLabel required>Charge Department</FieldLabel>
            <Select value={dept} disabled={ro} onChange={e => setDept(e.target.value)}>
              {CHARGE_OPTS.map(o => <option key={o}>{o}</option>)}
            </Select>
            <div className="mt-2">
              <Checkbox id="ape-recharge" label="Recharge to outside Taiwan (Taipei)" checked={recharge} disabled={ro} onChange={e => setRecharge(e.target.checked)} />
            </div>
          </div>

          <div className="grid grid-cols-2 gap-5">
            <div>
              <FieldLabel required>Description</FieldLabel>
              <Input value={description} readOnly={ro} placeholder="Enter description…" onChange={e => setDescription(e.target.value)} />
            </div>
            <div>
              <FieldLabel required>Amount</FieldLabel>
              <div className="flex gap-2">
                <Select value={currency} disabled={ro} className="w-24 flex-shrink-0" onChange={e => setCurrency(e.target.value)}>
                  {CURRENCIES.map(c => <option key={c}>{c}</option>)}
                </Select>
                <Input type="number" min={0} className="text-right font-mono" value={amount} readOnly={ro} onChange={e => setAmount(e.target.value)} />
              </div>
              {ntdAmt > 0 && (
                <div className="mt-1 text-right text-xs">
                  <span className="font-mono font-medium text-danger">{fmtNTD(ntdAmt)}</span>
                  <span className="ml-1 font-mono text-ink-faint">{fmtUSD(ntdAmt)}</span>
                </div>
              )}
            </div>
          </div>
        </div>
      </SectionCard>

      {/* Total */}
      <SectionCard>
        <div className="flex items-center justify-end gap-3 px-4 py-3">
          <span className="text-sm font-semibold text-ink">Total</span>
          <span className="font-mono text-base font-bold text-danger">{fmtNTD(ntdAmt)}</span>
          <span className="font-mono text-sm text-ink-faint">{fmtUSD(ntdAmt)}</span>
        </div>
      </SectionCard>

      <div className="grid grid-cols-2 gap-4">
        <SectionCard>
          <SectionTitle>Attachment</SectionTitle>
          <div className="p-4"><UploadZone /></div>
        </SectionCard>
        <SectionCard>
          <SectionTitle>Note</SectionTitle>
          <div className="p-4">
            <Textarea rows={4} value={note} readOnly={ro} onChange={e => setNote(e.target.value)} placeholder="Add any notes here…" />
          </div>
        </SectionCard>
      </div>

      <ActionBar code="APE" activeStep={0} persona={persona}
        mode={runtime.mode}
        nodeKind={runtime.task?.task.nodeKind}
        pending={runtime.pending}
        onSubmit={() => {
          if (isTaskMode) { void runtime.submitUserTask(); return }
          if (!Number(amount) || !receiveDate || !returnDate || !description.trim()) {
            runtime.fireToast('Amount, dates and description are required.')
            return
          }
          void runtime.submitCreate(toSpecPayload())
        }}
        onApprove={c => { void runtime.approve(c) }}
        onReject={c => { void runtime.reject(c) }}
        onReturnTask={c => { void runtime.returnTask(c) }}
      />
    </FormShell>
  )
}
