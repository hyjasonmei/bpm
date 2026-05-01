import { useState } from 'react'
import { SectionCard, SectionTitle } from '@/components/ui/card'
import { StatusBadge, Badge } from '@/components/ui/badge'
import { ReadonlyField, HistoryLog } from '@/components/ui/readonly'
import { FormShell, ActionBar } from './FormShell'
import type { PersonaCode } from '@/lib/role'

interface OnboardingTask {
  task: string
  details?: string
  remark?: string
  status: 'Complete' | 'Pending' | '-'
  action: string
  updated: string
}

const TASKS: OnboardingTask[] = [
  { task: 'Employee Account Setup', status: 'Complete', action: 'GSA-135272 Closed', updated: '2023/05/27 11:12 am' },
  { task: 'Add member into DL', details: 'Member of:\nAll of AMEA Navision Admin', status: 'Complete', action: 'GSA-135275 Closed', updated: '2023/05/17 12:07 pm' },
  { task: 'Duo', details: 'see details ↗', status: '-', action: '-', updated: '' },
]

const HISTORY = [
  { time: '2023/05/17 12:05 pm', action: 'Complete', by: 'Wilson You', dept: 'TWT.1746G - Corp IS-SaaS & Digital Business', remark: 'Created a New Account' },
  { time: '2023/05/17 11:56 am', action: 'Submit',   by: 'Wilson You', dept: 'TWT.1746G - Corp IS-SaaS & Digital Business' },
  { time: '2023/05/17 11:30 am', action: 'Submit',   by: 'Wilson You', dept: 'TWT.1746G - Corp IS-SaaS & Digital Business' },
]

export function EXTOBView({ persona }: { persona: PersonaCode }) {
  const [activeStep, setActiveStep] = useState(2) // closed

  return (
    <FormShell code="EXTOB" activeStep={activeStep} setActiveStep={setActiveStep} persona={persona} copySelector={false}>
      <SectionCard>
        <div className="grid grid-cols-2 gap-x-8 gap-y-3 p-4">
          <ReadonlyField label="Hiring Manager" value="Wilson You" />
          <ReadonlyField label="Request Date" value="2023/05/17" />
          <ReadonlyField label="Business Title" value="Cloud Platform Engineer" />
          <div className="flex flex-col gap-0.5">
            <div className="text-xs text-ink-muted">Request No.</div>
            <div className="font-mono text-sm font-semibold text-ink">TW-EXTOB-23-000019</div>
          </div>
          <ReadonlyField label="Employee Location" value="APAC" />
          <div className="flex flex-col gap-0.5">
            <div className="text-xs text-ink-muted">Status</div>
            <StatusBadge kind="closed" />
          </div>
        </div>
      </SectionCard>

      <SectionCard>
        <SectionTitle>New Hire Info</SectionTitle>
        <div className="grid grid-cols-2 gap-x-8 gap-y-3 p-4">
          <ReadonlyField label="First Name" value="Raven" />
          <ReadonlyField label="Onboard Date" value="2023/05/23" />
          <ReadonlyField label="Middle Name" value="" />
          <ReadonlyField label="Function" value="" />
          <ReadonlyField label="Last Name" value="Wang" />
          <ReadonlyField label="Nationality" value="" />
          <ReadonlyField label="Domain / Login Name" value="Trend / ext_ravenw" mono />
          <ReadonlyField label="Require Mailbox (Teams, E3 License)" value="Yes" />
          <ReadonlyField label="Cost Center" value="TWT.1746G - Corp IS-SaaS & Digital Business" />
        </div>
      </SectionCard>

      <SectionCard>
        <SectionTitle>Contract Info</SectionTitle>
        <div className="grid grid-cols-2 gap-x-8 gap-y-3 p-4">
          <ReadonlyField label="Contract Number" value="230517-AP-0001" mono />
          <ReadonlyField label="Contract Party" value="廉誠資訊有限公司" />
          <ReadonlyField label="Contract Effective Date" value="2023/05/17" />
          <ReadonlyField label="Account Expiration Date" value="2024/05/16" />
          <ReadonlyField label="Contract Expiration Date" value="2024/05/23" />
        </div>
      </SectionCard>

      <SectionCard>
        <SectionTitle>Onboarding Tasks</SectionTitle>
        <div className="overflow-x-auto">
          <table className="w-full text-sm">
            <thead>
              <tr className="border-b border-rule bg-slate-50">
                {['', 'Task', 'Task Details', 'Remark', 'Status', 'Action Remark', 'Last Update Time'].map(h => (
                  <th key={h} className="px-3 py-2 text-left text-xs font-medium text-ink-muted">{h}</th>
                ))}
              </tr>
            </thead>
            <tbody>
              {TASKS.map((t, i) => (
                <tr key={i} className="border-b border-slate-100">
                  <td className="px-3 py-2.5">
                    <input type="checkbox" checked={t.status === 'Complete'} readOnly className="h-4 w-4 accent-blue-600" />
                  </td>
                  <td className="px-3 py-2.5 font-medium text-ink">{t.task}</td>
                  <td className="whitespace-pre-line px-3 py-2.5 text-xs text-ink-muted">{t.details ?? ''}</td>
                  <td className="px-3 py-2.5 text-xs text-ink-muted">{t.remark ?? ''}</td>
                  <td className="px-3 py-2.5">
                    {t.status === 'Complete'
                      ? <Badge tone="good">Complete</Badge>
                      : <span className="text-ink-faint">-</span>}
                  </td>
                  <td className="px-3 py-2.5 text-xs text-blue-600">{t.action}</td>
                  <td className="px-3 py-2.5 font-mono text-xs text-ink-muted">{t.updated}</td>
                </tr>
              ))}
            </tbody>
          </table>
        </div>
      </SectionCard>

      <HistoryLog rows={HISTORY} />

      <ActionBar code="EXTOB" activeStep={activeStep} persona={persona}
        onSubmit={() => setActiveStep(s => s + 1)}
        onApprove={() => setActiveStep(s => s + 1)}
        onReject={() => setActiveStep(0)}
      />
    </FormShell>
  )
}
