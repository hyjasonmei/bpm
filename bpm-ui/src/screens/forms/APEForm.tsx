import { useState } from 'react'
import { fmtNTD, fmtUSD, NTD_RATE } from '@/lib/cn'
import { SectionCard, SectionTitle } from '@/components/ui/card'
import { Input, Textarea, Select, Checkbox, FieldLabel } from '@/components/ui/form'
import { UploadZone } from '@/components/ui/readonly'
import { FormShell, ActionBar } from './FormShell'
import { CHARGE_OPTS, CURRENCIES } from '@/lib/mocks'
import type { PersonaCode } from '@/lib/role'

export function APEForm({ persona }: { persona: PersonaCode }) {
  const [activeStep, setActiveStep] = useState(0)
  const [receiveDate, setReceiveDate] = useState('')
  const [returnDate, setReturnDate] = useState('')
  const [dept, setDept] = useState('TWT.1746G - Elton Yang')
  const [recharge, setRecharge] = useState(false)
  const [currency, setCurrency] = useState('NTD')
  const [amount, setAmount] = useState('')
  const [description, setDescription] = useState('')
  const [note, setNote] = useState('')

  const ntdAmt = currency === 'NTD' ? Number(amount || 0) : Number(amount || 0) * NTD_RATE
  const ro = activeStep > 0

  return (
    <FormShell code="APE" activeStep={activeStep} setActiveStep={setActiveStep} persona={persona}>
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

      <ActionBar code="APE" activeStep={activeStep} persona={persona}
        onSubmit={() => setActiveStep(s => s + 1)}
        onApprove={() => setActiveStep(s => s + 1)}
        onReject={() => setActiveStep(0)}
      />
    </FormShell>
  )
}
