import { useEffect, useState } from 'react'
import { SectionCard, SectionTitle } from '@/components/ui/card'
import { Input, Select, Textarea, Field } from '@/components/ui/form'
import { FormShell, ActionBar } from './FormShell'
import type { PersonaCode } from '@/lib/role'
import { FlowToast, useFormRuntime, type FormRuntimeProps } from '@/hooks/useFormRuntime'

const DEPT_OPTIONS = [
  { value: 'engineering', label: 'Engineering' },
  { value: 'operations', label: 'Operations' },
  { value: 'finance', label: 'Finance' },
  { value: 'it', label: 'IT' },
  { value: 'hr', label: 'HR' },
  { value: 'other', label: '其他' },
] as const

const MAILBOX_OPTIONS = [
  { value: 'yes', label: 'Yes' },
  { value: 'no', label: 'No' },
] as const

interface EXTOBViewProps extends FormRuntimeProps {
  persona: PersonaCode
}

/**
 * External onboarding flow. Manager submits a request → HR creates the AD account.
 * Per docs/all-flows-real-plan.md PR-L2: promoted from a static read-only view to
 * a dual-mode form that can start a real ProcessRuntime instance (manager) and
 * be opened by HR in task mode to fill in AD login + ticket details.
 */
export function EXTOBView({ persona, ...rt }: EXTOBViewProps) {
  const runtime = useFormRuntime('EXTOB', rt)
  const isTaskMode = runtime.mode === 'task'

  // Submit-step fields (manager-initiated)
  const [firstName, setFirstName] = useState('Raven')
  const [lastName, setLastName] = useState('Wang')
  const [businessTitle, setBusinessTitle] = useState('Cloud Platform Engineer')
  const [company, setCompany] = useState('廉誠資訊有限公司')
  const [onboardDate, setOnboardDate] = useState('')
  const [companyEmail, setCompanyEmail] = useState('ext_ravenw@acme')
  const [department, setDepartment] = useState('engineering')
  const [contractNo, setContractNo] = useState('')
  const [contractExpiration, setContractExpiration] = useState('')
  const [needsMailbox, setNeedsMailbox] = useState<'yes' | 'no'>('yes')

  // HR-step fields (account creation)
  const [adLogin, setAdLogin] = useState('')
  const [accountCreated, setAccountCreated] = useState<'yes' | 'no'>('yes')
  const [ticketNo, setTicketNo] = useState('')
  const [hrNote, setHrNote] = useState('')

  useEffect(() => {
    if (!isTaskMode || !runtime.task) return
    const fd = (runtime.task.mergedFormData ?? {}) as Record<string, unknown>
    if (typeof fd.first_name === 'string') setFirstName(fd.first_name)
    if (typeof fd.last_name === 'string') setLastName(fd.last_name)
    if (typeof fd.business_title === 'string') setBusinessTitle(fd.business_title)
    if (typeof fd.company === 'string') setCompany(fd.company)
    if (typeof fd.onboard_date === 'string') setOnboardDate(fd.onboard_date)
    if (typeof fd.company_email === 'string') setCompanyEmail(fd.company_email)
    if (typeof fd.department === 'string') setDepartment(fd.department)
    if (typeof fd.contract_no === 'string') setContractNo(fd.contract_no)
    if (typeof fd.contract_expiration === 'string') setContractExpiration(fd.contract_expiration)
    if (fd.needs_mailbox === 'yes' || fd.needs_mailbox === 'no') setNeedsMailbox(fd.needs_mailbox)
    if (typeof fd.ad_login === 'string') setAdLogin(fd.ad_login)
    if (fd.account_created === 'yes' || fd.account_created === 'no') setAccountCreated(fd.account_created)
    if (typeof fd.ticket_no === 'string') setTicketNo(fd.ticket_no)
    if (typeof fd.hr_note === 'string') setHrNote(fd.hr_note)
  }, [isTaskMode, runtime.task])

  // Are we on the HR account-creation task? (UserTask kind in task mode)
  const isHrTask = isTaskMode && runtime.task?.task.nodeId === 'task_hr_account'
  const ro = isTaskMode && !isHrTask

  function toSubmitPayload() {
    return {
      first_name: firstName,
      last_name: lastName,
      business_title: businessTitle,
      company,
      onboard_date: onboardDate,
      company_email: companyEmail || undefined,
      department,
      contract_no: contractNo || undefined,
      contract_expiration: contractExpiration || undefined,
      needs_mailbox: needsMailbox,
    }
  }

  function toHrAccountPayload() {
    return {
      ad_login: adLogin,
      account_created: accountCreated,
      ticket_no: ticketNo || undefined,
      hr_note: hrNote || undefined,
    }
  }

  return (
    <FormShell code="EXTOB" activeStep={0} setActiveStep={() => {}} persona={persona} copySelector={false} mode={runtime.mode}>
      <FlowToast message={runtime.toast} />

      <SectionCard>
        <SectionTitle>New Hire Info</SectionTitle>
        <div className="grid grid-cols-2 gap-x-8 gap-y-3 p-4">
          <Field label="First Name" required>
            <Input value={firstName} readOnly={ro} onChange={e => setFirstName(e.target.value)} />
          </Field>
          <Field label="Last Name" required>
            <Input value={lastName} readOnly={ro} onChange={e => setLastName(e.target.value)} />
          </Field>
          <Field label="Business Title" required>
            <Input value={businessTitle} readOnly={ro} onChange={e => setBusinessTitle(e.target.value)} />
          </Field>
          <Field label="Company / 派遣公司" required>
            <Input value={company} readOnly={ro} onChange={e => setCompany(e.target.value)} />
          </Field>
          <Field label="Onboard Date" required>
            <Input type="date" value={onboardDate} readOnly={ro} onChange={e => setOnboardDate(e.target.value)} />
          </Field>
          <Field label="Department" required>
            <Select value={department} disabled={ro} onChange={e => setDepartment(e.target.value)}>
              {DEPT_OPTIONS.map(o => <option key={o.value} value={o.value}>{o.label}</option>)}
            </Select>
          </Field>
          <Field label="Corp Email">
            <Input value={companyEmail} readOnly={ro} onChange={e => setCompanyEmail(e.target.value)} />
          </Field>
          <Field label="Mailbox Required">
            <Select value={needsMailbox} disabled={ro} onChange={e => setNeedsMailbox(e.target.value as 'yes' | 'no')}>
              {MAILBOX_OPTIONS.map(o => <option key={o.value} value={o.value}>{o.label}</option>)}
            </Select>
          </Field>
        </div>
      </SectionCard>

      <SectionCard>
        <SectionTitle>Contract Info</SectionTitle>
        <div className="grid grid-cols-2 gap-x-8 gap-y-3 p-4">
          <Field label="Contract Number">
            <Input value={contractNo} readOnly={ro} onChange={e => setContractNo(e.target.value)} />
          </Field>
          <Field label="Contract Expiration">
            <Input type="date" value={contractExpiration} readOnly={ro} onChange={e => setContractExpiration(e.target.value)} />
          </Field>
        </div>
      </SectionCard>

      {isHrTask && (
        <SectionCard>
          <SectionTitle>HR — Account Creation</SectionTitle>
          <div className="grid grid-cols-2 gap-x-8 gap-y-3 p-4">
            <Field label="AD Login" required>
              <Input value={adLogin} onChange={e => setAdLogin(e.target.value)} placeholder="e.g. ext_ravenw" />
            </Field>
            <Field label="Account Created">
              <Select value={accountCreated} onChange={e => setAccountCreated(e.target.value as 'yes' | 'no')}>
                <option value="yes">Yes</option>
                <option value="no">No</option>
              </Select>
            </Field>
            <Field label="GSA Ticket #">
              <Input value={ticketNo} onChange={e => setTicketNo(e.target.value)} />
            </Field>
            <div className="col-span-2">
              <Field label="HR Note">
                <Textarea rows={2} value={hrNote} onChange={e => setHrNote(e.target.value)} />
              </Field>
            </div>
          </div>
        </SectionCard>
      )}

      <ActionBar
        code="EXTOB"
        activeStep={0}
        persona={persona}
        mode={runtime.mode}
        nodeKind={runtime.task?.task.nodeKind}
        pending={runtime.pending}
        onSubmit={() => {
          if (isHrTask) {
            if (!adLogin.trim()) { runtime.fireToast('AD Login is required.'); return }
            void runtime.submitUserTask(toHrAccountPayload())
            return
          }
          if (isTaskMode) { void runtime.submitUserTask(); return }
          if (!firstName.trim() || !lastName.trim() || !businessTitle.trim() || !company.trim() || !onboardDate) {
            runtime.fireToast('Name, title, company and onboard date are required.')
            return
          }
          void runtime.submitCreate(toSubmitPayload())
        }}
        onApprove={c => { void runtime.approve(c) }}
        onReject={c => { void runtime.reject(c) }}
        onReturnTask={c => { void runtime.returnTask(c) }}
      />
    </FormShell>
  )
}
