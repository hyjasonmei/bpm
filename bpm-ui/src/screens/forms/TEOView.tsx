import { useState } from 'react'
import { Info } from 'lucide-react'
import { SectionCard, SectionTitle } from '@/components/ui/card'
import { StatusBadge } from '@/components/ui/badge'
import { ReadonlyField, HistoryLog } from '@/components/ui/readonly'
import { FormShell, ActionBar } from './FormShell'
import type { PersonaCode } from '@/lib/role'

interface ExpenseRow {
  n: number
  date: string
  country: string
  category: string
  amount: string
  lcy: string
  desc: string
}

const EXPENSES: ExpenseRow[] = [
  { n: 1, date: '2023/04/14', country: 'United Arab Emirates', category: 'Airfare',                  amount: 'NTD 50,400',                          lcy: 'NTD 50,400', desc: 'GCC.1751G;Wilson You;Airfare;UAE; round-trip Airfare to Dubai' },
  { n: 2, date: '2023/05/06', country: 'United Arab Emirates', category: 'Hotel',                    amount: 'NTD 44,652',                          lcy: 'NTD 44,652', desc: 'GCC.1751G;Wilson You;Hotel;UAE; 7 days 43,992 + service fee 660' },
  { n: 3, date: '2023/04/30', country: 'United Arab Emirates', category: 'Airport tax / Taxi / Bus', amount: 'AED 163.00 (Exchange Rate 8.3781)',   lcy: 'NTD 1,367',  desc: 'GCC.1751G;Wilson You; from Dubai airport to Hotel' },
  { n: 4, date: '2023/05/06', country: 'United Arab Emirates', category: 'Airport tax / Taxi / Bus', amount: 'AED 99.00 (Exchange Rate 8.3781)',    lcy: 'NTD 830',    desc: 'GCC.1751G;Wilson You; from Hotel to Dubai airport.' },
]

const HISTORY = [
  { time: '2023/06/05 05:18 pm', action: 'Success',     by: 'Jarvis BPM', dept: 'Corp BAS - Architecture, Integration and Mobile' },
  { time: '2023/06/05 05:18 pm', action: 'Send To NAV', by: 'Jacy Wang',  dept: 'TWT.1751G - Taiwan Finance Operation' },
  { time: '2023/05/29 10:06 pm', action: 'Confirm',     by: 'Wilson You', dept: 'TWT.1746G - Corp IS-SaaS & Digital Business' },
  { time: '2023/05/29 02:03 pm', action: 'Approve',     by: 'Jean Hsu',   dept: 'TWT.1751G - Taiwan Finance Operation' },
  { time: '2023/05/29 02:00 pm', action: 'Approve',     by: 'Elton Yang', dept: 'TWT.1746G - Corp IS-SaaS & Digital Business' },
  { time: '2023/05/29 01:47 am', action: 'Re-Submit',   by: 'Wilson You', dept: 'TWT.1746G - Corp IS-SaaS & Digital Business' },
  { time: '2023/05/29 10:12 am', action: 'Return',      by: 'Jacy Wang',  dept: 'TWT.1751G - Taiwan Finance Operation', remark: 'need to modify' },
  { time: '2023/05/29 08:24 am', action: 'Submit',      by: 'Wilson You', dept: 'TWT.1746G - Corp IS-SaaS & Digital Business' },
]

export function TEOView({ persona }: { persona: PersonaCode }) {
  const [activeStep, setActiveStep] = useState(4) // closed by default

  return (
    <FormShell code="TEO" activeStep={activeStep} setActiveStep={setActiveStep} persona={persona} copySelector={false}>
      <SectionCard>
        <div className="grid grid-cols-2 gap-x-8 gap-y-3 p-4">
          <ReadonlyField label="Requestor" value="Wilson You (游上毅) - 31781" />
          <ReadonlyField label="Request Date" value="2023/05/29" />
          <ReadonlyField label="Requestor Dept." value="TWT.1746G - Corp IS-SaaS & Digital Business" />
          <div className="flex flex-col gap-0.5">
            <div className="text-xs text-ink-muted">Request No.</div>
            <div className="font-mono text-sm font-semibold text-ink">TW-TEO-23-000220</div>
          </div>
          <ReadonlyField label="Business Unit" value="Taiwan (Taipei)" />
          <div className="flex flex-col gap-0.5">
            <div className="text-xs text-ink-muted">Status</div>
            <StatusBadge kind="closed" />
          </div>
        </div>
      </SectionCard>

      <SectionCard>
        <SectionTitle>Original Travel Request Plan</SectionTitle>
        <div className="overflow-x-auto">
          <table className="w-full text-sm">
            <thead>
              <tr className="border-b border-rule bg-slate-50">
                {['Travel Request No.', 'Travel Period', 'Travel Purpose', 'Itinerary'].map(h => (
                  <th key={h} className="px-4 py-2 text-left text-xs font-medium text-ink-muted">{h}</th>
                ))}
              </tr>
            </thead>
            <tbody>
              <tr className="border-b border-slate-100">
                <td className="px-4 py-3"><a className="font-mono text-xs text-blue-600 hover:underline">TW-TRQ-23-000160</a></td>
                <td className="px-4 py-3 text-xs text-ink-muted">2023/04/29 AM – 2023/05/06 PM</td>
                <td className="px-4 py-3 text-xs text-ink-muted">For company DMCC 2023 RSM auditing.</td>
                <td className="px-4 py-3 text-xs text-ink-muted">Round trip: Taipei → Dubai office</td>
              </tr>
            </tbody>
          </table>
        </div>
        <div className="grid grid-cols-2 gap-x-8 p-4">
          <ReadonlyField label="Charge In" value="GCC.1751G - Jean Hsu" />
          <ReadonlyField label="Project Code" value="N/A" />
        </div>
      </SectionCard>

      <SectionCard>
        <SectionTitle>Travel Expense</SectionTitle>
        <div className="divide-y divide-slate-100">
          {EXPENSES.map(e => (
            <div key={e.n} className="grid grid-cols-2 gap-x-8 gap-y-2 p-4">
              <ReadonlyField label={`#${e.n} Date`} value={e.date} />
              <ReadonlyField label="Country" value={e.country} />
              <ReadonlyField label="Category" value={e.category} />
              <div className="space-y-1">
                <ReadonlyField label="Amount" value={e.amount} />
                <div className="flex items-center gap-1">
                  <span className="text-xs text-ink-muted">Amount (LCY)</span>
                  <Info className="h-3 w-3 text-ink-faint" />
                  <span className="ml-1 text-sm text-ink">{e.lcy}</span>
                </div>
              </div>
              <div className="col-span-2"><ReadonlyField label="Description" value={e.desc} /></div>
            </div>
          ))}
        </div>
        <div className="flex items-center justify-end gap-3 border-t border-rule bg-slate-50 p-4">
          <span className="text-sm font-semibold text-ink">Total Expense:</span>
          <span className="font-mono font-bold text-danger">NTD 97,249</span>
          <span className="font-mono text-sm text-ink-faint">(USD 3,131.52)</span>
        </div>
      </SectionCard>

      <SectionCard>
        <SectionTitle>Per-diem Calculation</SectionTitle>
        <div className="overflow-x-auto">
          <table className="w-full text-sm">
            <thead>
              <tr className="border-b border-rule bg-slate-50">
                {['#1 Destination', 'From', 'To', 'Per-diem (Before adjustment)'].map(h => (
                  <th key={h} className="px-4 py-2 text-left text-xs font-medium text-ink-muted">{h}</th>
                ))}
              </tr>
            </thead>
            <tbody>
              <tr className="border-b border-slate-100">
                <td className="px-4 py-2">Dubai</td>
                <td className="px-4 py-2 font-mono text-xs">2023/04/29 PM</td>
                <td className="px-4 py-2 font-mono text-xs">2023/05/06 PM</td>
                <td className="px-4 py-2 text-sm">EUR 375.00</td>
              </tr>
            </tbody>
          </table>
        </div>
        <div className="grid grid-cols-3 gap-4 border-t border-slate-100 p-4 text-sm">
          <div><span className="text-xs text-ink-muted">Business Days</span><div>7.5</div></div>
          <div><span className="text-xs text-ink-muted">No. of Hotel Breakfast</span><div>6 breakfast(s)</div></div>
          <div><span className="text-xs text-ink-muted">Deduction</span><div className="font-mono">EUR -60.00</div></div>
        </div>
        <div className="flex items-center justify-end gap-3 border-t border-rule bg-slate-50 p-4">
          <span className="text-sm font-semibold">Net Per-diem:</span>
          <span className="font-mono font-medium text-ink">EUR 315.00</span>
        </div>
        <div className="flex items-center justify-end gap-3 border-t border-rule px-4 py-2">
          <span className="text-sm font-semibold text-ink">Total Per-diem:</span>
          <span className="font-mono font-bold text-danger">NTD 10,575</span>
          <span className="font-mono text-sm text-ink-faint">(USD 340.53)</span>
        </div>
      </SectionCard>

      {/* Net */}
      <div className="flex items-center justify-end gap-3 rounded-lg border border-rule bg-card p-4">
        <span className="text-sm font-semibold text-ink">Net Amount</span>
        <span className="font-mono text-base font-bold text-danger">NTD 97,049</span>
        <span className="font-mono text-sm text-ink-faint">(USD 3,122.05)</span>
      </div>

      <HistoryLog rows={HISTORY} />

      <ActionBar code="TEO" activeStep={activeStep} persona={persona}
        onSubmit={() => setActiveStep(s => s + 1)}
        onApprove={() => setActiveStep(s => s + 1)}
        onReject={() => setActiveStep(0)}
      />
    </FormShell>
  )
}
