import { useState } from 'react'
import { User, AlertTriangle } from 'lucide-react'
import { SectionCard, SectionTitle } from '@/components/ui/card'
import { Checkbox } from '@/components/ui/form'
import { StatusBadge, Badge } from '@/components/ui/badge'
import { ReadonlyField, HistoryLog } from '@/components/ui/readonly'
import { FormShell, ActionBar } from './FormShell'
import type { PersonaCode } from '@/lib/role'

export function TRQView({ persona }: { persona: PersonaCode }) {
  const [activeStep, setActiveStep] = useState(3) // closed by default

  const history = [
    { time: '2023/04/27 02:01 pm', action: 'Success',     by: 'Jarvis BPM',    dept: 'Corp BAS - Architecture, Integration and Mobile' },
    { time: '2023/04/27 02:01 pm', action: 'Send To NAV', by: 'Jessica Huang', dept: 'TWT.1751G - Taiwan Finance Operation' },
    { time: '2023/04/14 11:51 am', action: 'Confirm',     by: 'Wilson You',    dept: 'TWT.1746G - Corp IS-SaaS & Digital Business' },
    { time: '2023/04/14 11:48 am', action: 'Approve',     by: 'Jean Hsu',      dept: 'TWT.1751G - Taiwan Finance Operation' },
    { time: '2023/04/12 09:37 am', action: 'Approve',     by: 'Elton Yang',    dept: 'TWT.1746G - Corp IS-SaaS & Digital Business' },
    { time: '2023/04/12 09:15 am', action: 'Re-Submit',   by: 'Wilson You',    dept: 'TWT.1746G - Corp IS-SaaS & Digital Business' },
    { time: '2023/04/11 09:29 pm', action: 'Return',      by: 'Elton Yang',    dept: 'TWT.1746G - Corp IS-SaaS & Digital Business', remark: 'Return as mentioned.' },
    { time: '2023/04/11 03:36 pm', action: 'Apply',       by: 'Wilson You',    dept: 'TWT.1746G - Corp IS-SaaS & Digital Business' },
  ]

  return (
    <FormShell code="TRQ" activeStep={activeStep} setActiveStep={setActiveStep} persona={persona} copySelector={false}>
      {/* Header info */}
      <SectionCard>
        <div className="grid grid-cols-2 gap-x-8 gap-y-3 p-4">
          <ReadonlyField label="Requestor" value="Wilson You (游上毅) - 31781" />
          <ReadonlyField label="Request Date" value="2023/04/11" />
          <ReadonlyField label="Requestor Dept." value="TWT.1746G - Corp IS-SaaS & Digital Business" />
          <div className="flex flex-col gap-0.5">
            <div className="text-xs text-ink-muted">Request No.</div>
            <div className="font-mono text-sm font-semibold text-ink">TW-TRQ-23-000160</div>
          </div>
          <ReadonlyField label="Business Unit" value="Taiwan (Taipei)" />
          <div className="flex flex-col gap-0.5">
            <div className="text-xs text-ink-muted">Status</div>
            <StatusBadge kind="closed" />
          </div>
        </div>
      </SectionCard>

      <SectionCard>
        <SectionTitle>Itinerary</SectionTitle>
        <div className="grid grid-cols-2 gap-x-8 gap-y-3 p-4">
          <ReadonlyField label="Travel Type" value="Round Trip" />
          <div />
          <ReadonlyField label="Departure City" value="Taipei, Taiwan" />
          <ReadonlyField label="Destination City" value="Trend Micro Office: Dubai, United Arab Emirates" />
          <ReadonlyField label="Depart Date" value="2023/04/29 (AM)" />
          <ReadonlyField label="Return Date" value="2023/05/06 (PM)" />
          <ReadonlyField label="Charge to" value="GCC.1751G - Jean Hsu" />
          <ReadonlyField label="Project Code" value="N/A" />
          <div className="col-span-2"><ReadonlyField label="Travel Purpose" value="For company DMCC 2023 RSM auditing." /></div>
        </div>
      </SectionCard>

      <SectionCard>
        <SectionTitle>
          <span className="inline-flex items-center gap-2">
            Travel Reservation
            <span className="text-xs font-normal text-ink-muted">— ADM/Travel agent will help based on your request below.</span>
          </span>
        </SectionTitle>
        <div className="space-y-3 p-4">
          <div className="rounded-lg border border-rule p-4">
            <Checkbox id="trq-flight" label="Flight Required" checked readOnly />
            <div className="mt-2 grid grid-cols-2 gap-x-8 gap-y-3">
              <ReadonlyField label="Passport ID" value="3 ●●●●● 8" />
              <ReadonlyField label="Passport Name" value="Yu, Shang-Yi" />
              <ReadonlyField label="Personal ID" value="F ●●●●● 9" />
              <ReadonlyField label="Date of Birth" value="198●●●●●" />
              <ReadonlyField label="Passport Expiration Date" value="2025/11/06" />
              <ReadonlyField label="Special food preference" value="no beef, please." />
              <ReadonlyField label="Seat preference" value="Window Seat" />
            </div>
          </div>
          <div className="rounded-lg border border-rule p-4">
            <Checkbox id="trq-pickup" label="國內接機服務 — pick me up at the address below and take me to the departure airport." checked readOnly />
            <div className="mt-2"><ReadonlyField label="Pick-up address" value="台北市大安區" /></div>
          </div>
          <div className="flex items-start gap-2 rounded-md border border-amber-200 bg-amber-50 p-3 text-xs text-amber-800">
            <AlertTriangle className="mt-0.5 h-4 w-4 shrink-0 text-amber-500" />
            <div>If your pick-up / drop-off address is in Taipei or New Taipei, there is no service when departure / arrival is Taipei Songshan Airport.</div>
          </div>
        </div>
      </SectionCard>

      <SectionCard>
        <div className="space-y-3 p-4">
          <Checkbox id="trq-advance" label="I want to apply cash advance for my business trip." checked readOnly />
          <div className="grid grid-cols-2 gap-x-8 gap-y-2">
            <ReadonlyField label="Business Travel Days" value="7 day(s)" />
            <ReadonlyField label="Cash Advance for Per diem" value="USD 350.00" />
          </div>
          <div className="flex items-center justify-end gap-3 border-t border-rule pt-3">
            <span className="text-sm font-semibold text-ink">Total Amount:</span>
            <span className="font-mono font-bold text-danger">NTD 10,775</span>
            <span className="font-mono text-sm text-ink-faint">(USD 350.00)</span>
          </div>
        </div>
      </SectionCard>

      <HistoryLog rows={history} />

      <SectionCard>
        <SectionTitle>Expected Approvers</SectionTitle>
        <div className="flex gap-4 p-4">
          {[{ role: 'Direct Manager', name: 'Elton Yang' }, { role: 'GCC.1751G', name: 'Jean Hsu' }].map(a => (
            <div key={a.name} className="flex flex-col items-center gap-1">
              <div className="flex h-10 w-10 items-center justify-center rounded-full bg-slate-200">
                <User className="h-5 w-5 text-ink-muted" />
              </div>
              <Badge tone="default">{a.role}</Badge>
              <div className="text-xs text-ink-muted">{a.name}</div>
            </div>
          ))}
        </div>
      </SectionCard>

      <ActionBar code="TRQ" activeStep={activeStep} persona={persona}
        onSubmit={() => setActiveStep(s => s + 1)}
        onApprove={() => setActiveStep(s => s + 1)}
        onReject={() => setActiveStep(0)}
      />
    </FormShell>
  )
}
