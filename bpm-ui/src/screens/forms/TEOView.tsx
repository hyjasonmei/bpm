import { useEffect, useMemo, useState } from 'react'
import { SectionCard, SectionTitle } from '@/components/ui/card'
import { Input, Textarea, Field } from '@/components/ui/form'
import { FormShell, ActionBar } from './FormShell'
import type { PersonaCode } from '@/lib/role'
import { FlowToast, useFormRuntime, type FormRuntimeProps } from '@/hooks/useFormRuntime'

interface TEOViewProps extends FormRuntimeProps {
  persona: PersonaCode
}

/**
 * Travel expense settlement. Inherits the threshold gateway: if total >= 50000,
 * the runtime routes through `approval_extra` (any 2/3 of dept-head + Finance + CEO).
 * Promoted from the static demo view to a dual-mode form.
 */
export function TEOView({ persona, ...rt }: TEOViewProps) {
  const runtime = useFormRuntime('TEO', rt)
  const isTaskMode = runtime.mode === 'task'

  const [trqNo, setTrqNo] = useState('')
  const [destination, setDestination] = useState('Dubai, UAE')
  const [purpose, setPurpose] = useState('DMCC 2026 RSM auditing')
  const [travelStart, setTravelStart] = useState('')
  const [travelEnd, setTravelEnd] = useState('')
  const [actualAmount, setActualAmount] = useState('')
  const [breakdown, setBreakdown] = useState('Airfare 50400\nHotel 44652\nTaxi 1948')

  // Per-step task fields
  const [printNo, setPrintNo] = useState('')
  const [confirmNote, setConfirmNote] = useState('')
  const [reviewNote, setReviewNote] = useState('')
  const [paidAt, setPaidAt] = useState('')

  useEffect(() => {
    if (!isTaskMode || !runtime.task) return
    const fd = (runtime.task.mergedFormData ?? {}) as Record<string, unknown>
    if (typeof fd.trq_no === 'string') setTrqNo(fd.trq_no)
    if (typeof fd.destination === 'string') setDestination(fd.destination)
    if (typeof fd.purpose === 'string') setPurpose(fd.purpose)
    const dr = fd.travel_dates as { start?: string; end?: string } | undefined
    if (dr?.start) setTravelStart(dr.start)
    if (dr?.end) setTravelEnd(dr.end)
    if (fd.actual_amount !== undefined) setActualAmount(String(fd.actual_amount))
    if (typeof fd.expense_breakdown === 'string') setBreakdown(fd.expense_breakdown)
    if (typeof fd.print_no === 'string') setPrintNo(fd.print_no)
    if (typeof fd.confirm_note === 'string') setConfirmNote(fd.confirm_note)
    if (typeof fd.review_note === 'string') setReviewNote(fd.review_note)
    if (typeof fd.paid_at === 'string') setPaidAt(fd.paid_at)
  }, [isTaskMode, runtime.task])

  const total = useMemo(() => Number(actualAmount || 0), [actualAmount])
  const overThreshold = total >= 50000
  const ro = isTaskMode
  const nodeId = runtime.task?.task.nodeId

  function toApplyPayload() {
    return {
      trq_no: trqNo || undefined,
      destination,
      purpose,
      travel_dates: { start: travelStart, end: travelEnd },
      actual_amount: total,
      total_amount: total,
      expense_breakdown: breakdown,
      receipts: 'receipts.pdf',
    }
  }

  function patchForCurrentTask(): unknown {
    switch (nodeId) {
      case 'task_finance_confirm':
        return { print_no: printNo, confirm_note: confirmNote || undefined }
      case 'task_finance_review':
        return { review_note: reviewNote || undefined, paid_at: paidAt }
      default:
        return undefined
    }
  }

  return (
    <FormShell code="TEO" activeStep={0} setActiveStep={() => {}} persona={persona} copySelector={false} mode={runtime.mode}>
      <FlowToast message={runtime.toast} />

      <SectionCard>
        <SectionTitle>Travel Expense</SectionTitle>
        <div className="grid grid-cols-2 gap-x-8 gap-y-3 p-4">
          <Field label="Travel Request No.">
            <Input value={trqNo} readOnly={ro} onChange={e => setTrqNo(e.target.value)} />
          </Field>
          <Field label="Destination" required>
            <Input value={destination} readOnly={ro} onChange={e => setDestination(e.target.value)} />
          </Field>
          <div className="col-span-2">
            <Field label="Purpose" required>
              <Textarea rows={2} value={purpose} readOnly={ro} onChange={e => setPurpose(e.target.value)} />
            </Field>
          </div>
          <Field label="Travel start" required>
            <Input type="date" value={travelStart} readOnly={ro} onChange={e => setTravelStart(e.target.value)} />
          </Field>
          <Field label="Travel end" required>
            <Input type="date" value={travelEnd} readOnly={ro} onChange={e => setTravelEnd(e.target.value)} />
          </Field>
          <Field label="Actual Amount" required>
            <Input type="number" min={0} value={actualAmount} readOnly={ro} onChange={e => setActualAmount(e.target.value)} className="text-right font-mono" />
          </Field>
          <div className="flex flex-col gap-0.5">
            <span className="text-xs text-ink-muted">Total</span>
            <span className="font-mono text-base font-bold text-danger">{total.toLocaleString()}</span>
            {overThreshold && (
              <span className="text-[11px] text-amber-700">≥ 50,000 — runtime will request 2-of-3 high-amount approval (dept head, Finance, CEO).</span>
            )}
          </div>
          <div className="col-span-2">
            <Field label="Expense Breakdown" required hint="One line per item: 品項 金額 幣別">
              <Textarea rows={4} value={breakdown} readOnly={ro} onChange={e => setBreakdown(e.target.value)} />
            </Field>
          </div>
        </div>
      </SectionCard>

      {nodeId === 'task_finance_confirm' && (
        <SectionCard>
          <SectionTitle>Finance Confirm</SectionTitle>
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

      {nodeId === 'task_finance_review' && (
        <SectionCard>
          <SectionTitle>Finance Review</SectionTitle>
          <div className="grid grid-cols-2 gap-x-8 gap-y-3 p-4">
            <Field label="Paid At" required>
              <Input type="date" value={paidAt} onChange={e => setPaidAt(e.target.value)} />
            </Field>
            <div className="col-span-2">
              <Field label="Review Note"><Textarea rows={2} value={reviewNote} onChange={e => setReviewNote(e.target.value)} /></Field>
            </div>
          </div>
        </SectionCard>
      )}

      <ActionBar
        code="TEO"
        activeStep={0}
        persona={persona}
        mode={runtime.mode}
        nodeKind={runtime.task?.task.nodeKind}
        pending={runtime.pending}
        onSubmit={() => {
          if (isTaskMode) { void runtime.submitUserTask(patchForCurrentTask()); return }
          if (!destination.trim() || !purpose.trim() || !travelStart || !travelEnd || !total) {
            runtime.fireToast('Destination, purpose, travel dates and amount are required.')
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
