import { useState } from 'react'
import { ExternalLink, Check } from 'lucide-react'
import { SectionCard, SectionTitle } from '@/components/ui/card'
import { StatusBadge } from '@/components/ui/badge'
import { ReadonlyField, HistoryLog } from '@/components/ui/readonly'
import { FormShell, ActionBar } from './FormShell'
import type { PersonaCode } from '@/lib/role'

const HISTORY = [
  { time: '2021/07/15 02:23 pm', action: 'Close',   by: 'Mark Ng',    dept: 'TWT.1761G - Taiwan Regional IT' },
  { time: '2021/06/18 02:30 pm', action: 'Approve', by: 'Elton Yang', dept: 'TWT.1746G - Corp BAS-CRM' },
  { time: '2021/06/18 02:25 pm', action: 'Confirm', by: 'Wilson You', dept: 'TWT.1746G - Corp BAS-CRM' },
  { time: '2021/06/18 01:45 pm', action: 'Approve', by: 'Peter Liao', dept: 'TWT.1711G - Taiwan Corp G&A' },
  { time: '2021/06/15 01:04 pm', action: 'Approve', by: 'Alex Kuo',   dept: 'TWT.1761G - Taiwan Regional IT' },
  { time: '2021/06/11 05:13 pm', action: 'Submit',  by: 'Wilson You', dept: 'TWT.1746G - Corp BAS-CRM' },
]

export function ITPRView({ persona }: { persona: PersonaCode }) {
  const [activeStep, setActiveStep] = useState(6) // closed

  return (
    <FormShell code="ITPR" activeStep={activeStep} setActiveStep={setActiveStep} persona={persona} copySelector={false}>
      <SectionCard>
        <div className="grid grid-cols-2 gap-x-8 gap-y-3 p-4">
          <ReadonlyField label="Requestor" value="Wilson You (游上毅) - 31781" />
          <ReadonlyField label="Request Date" value="2021/06/11" />
          <ReadonlyField label="Requestor Dept." value="TWT.1746G-Corp BAS - CRM" />
          <div className="flex flex-col gap-0.5">
            <div className="text-xs text-ink-muted">Request No.</div>
            <div className="font-mono text-sm font-semibold text-ink">TW-PR-21-000794</div>
          </div>
          <ReadonlyField label="Shipping Location" value="Taipei office" />
          <ReadonlyField label="Project" value="ISP041 - ERP & Finance Cloud" />
          <ReadonlyField label="Charge to" value="TWT.1746G - Elton Yang" />
          <ReadonlyField label="Expected Date" value="2021/07/02" />
          <ReadonlyField label="Purpose" value="Additional - for Finance projects" />
          <div className="flex flex-col gap-0.5">
            <div className="text-xs text-ink-muted">PR Status</div>
            <StatusBadge kind="closed" />
          </div>
        </div>
      </SectionCard>

      <SectionCard>
        <SectionTitle>Software Purchase</SectionTitle>
        <div className="overflow-x-auto">
          <table className="w-full text-sm">
            <thead>
              <tr className="border-b border-rule bg-slate-50">
                {['#', 'Category', 'Item', 'Spec', 'Qty', 'Unit Price', 'Subtotal', 'Received', 'Delivered'].map(h => (
                  <th key={h} className="px-3 py-2 text-left text-xs font-medium text-ink-muted">{h}</th>
                ))}
              </tr>
            </thead>
            <tbody>
              <tr className="border-b border-slate-100">
                <td className="px-3 py-3 font-mono text-xs text-ink-faint">1</td>
                <td className="px-3 py-3">Software</td>
                <td className="px-3 py-3 font-medium">Power BI</td>
                <td className="px-3 py-3 text-xs text-ink-muted">3 years (2021/7/1 – 2024/6/30)</td>
                <td className="px-3 py-3 text-center font-mono">4</td>
                <td className="px-3 py-3 font-mono text-sm">NTD 6,588</td>
                <td className="px-3 py-3 font-mono text-sm">NTD 26,352</td>
                <td className="px-3 py-3"><Check className="h-4 w-4 text-good" /></td>
                <td className="px-3 py-3"><Check className="h-4 w-4 text-good" /></td>
              </tr>
            </tbody>
          </table>
        </div>
        <div className="space-y-1.5 border-t border-slate-100 p-4">
          {[['Subtotal', 'NTD 26,352', 'USD 942.66'], ['VAT', 'NTD 1,318', 'USD 47.15'], ['Total (incl. VAT)', 'NTD 27,670', 'USD 989.81']].map(([l, n, u]) => (
            <div key={l} className="flex items-center justify-between text-sm">
              <span className="font-medium text-ink-muted">{l}</span>
              <span className="font-mono"><span className="font-semibold text-danger">{n}</span> <span className="text-ink-faint">({u})</span></span>
            </div>
          ))}
        </div>
      </SectionCard>

      <SectionCard>
        <SectionTitle>Status</SectionTitle>
        <div className="p-4">
          <div className="mb-3 flex gap-3 border-b border-rule">
            <button className="border-b-2 border-blue-500 px-1 pb-2 text-sm font-medium text-ink">PO Summary</button>
            <button className="px-1 pb-2 text-sm text-blue-500 hover:underline">TW-PO-21-000982</button>
          </div>
          <div className="mb-2 text-sm font-semibold text-ink">Closed</div>
          <a className="inline-flex items-center gap-1 font-mono text-sm text-blue-600 hover:underline">
            <ExternalLink className="h-3 w-3" /> TW-PO-21-000982
          </a>
          <span className="ml-2"><StatusBadge kind="closed" /></span>
        </div>
      </SectionCard>

      <HistoryLog rows={HISTORY} />

      <ActionBar code="ITPR" activeStep={activeStep} persona={persona}
        onSubmit={() => setActiveStep(s => s + 1)}
        onApprove={() => setActiveStep(s => s + 1)}
        onReject={() => setActiveStep(0)}
      />
    </FormShell>
  )
}
