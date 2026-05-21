import { useEffect, useMemo, useState } from 'react'
import { Calendar as CalendarIcon, Loader2 } from 'lucide-react'

import { SectionCard, SectionTitle } from '@/components/ui/card'
import { Input, Textarea, Field, InfoBanner } from '@/components/ui/form'
import { Button } from '@/components/ui/button'
import { ConfirmDialog } from '@/components/ui/confirm-dialog'
import { FormShell, ActionBar } from './FormShell'
import type { PersonaCode } from '@/lib/role'
import {
  startHrFlow, getHrFlow, approveHrFlow, returnHrFlow, resubmitHrFlow, cancelHrFlow, getMyHrFlowTodos,
} from '@/lib/api/hrFlows'
import {
  HrFlowStatus, HrFlowStep, HrFlowActionType,
  type HrFlowInstanceDto, type HrFlowSummaryDto,
} from '@/types/hrFlows'
import { FlowToast, useFormRuntime, type FormRuntimeProps } from '@/hooks/useFormRuntime'

interface ResignFormFields {
  expectedLastDay: string
  reason: string
  handover: string
  note: string
}

const EMPTY: ResignFormFields = { expectedLastDay: '', reason: '', handover: '', note: '' }

interface ResignFormProps extends FormRuntimeProps { persona: PersonaCode }

/**
 * Resignation flow. Phase 1 has TWO backing implementations:
 *   - Legacy: HrFlowsController (already wired below; default for create mode).
 *   - New:    ProcessRuntime via sample_specs/resign_v1.json (used when the
 *             form is opened in task mode from the inbox in PR-L3).
 *
 * The two coexist per docs/all-flows-real-plan.md §HrFlows decision deferred.
 */
export function ResignForm({ persona, ...rt }: ResignFormProps) {
  // ── PR-L2: spec-runtime path (only active when mode === 'task') ──────
  const runtime = useFormRuntime('RESIGN', rt)
  if (runtime.mode === 'task') {
    return <ResignTaskMode persona={persona} runtime={runtime} />
  }
  // ── Existing HrFlowsController path preserved verbatim below ─────────
  const [instanceId, setInstanceId] = useState<string | null>(null)
  const [instance, setInstance] = useState<HrFlowInstanceDto | null>(null)
  const [fields, setFields] = useState<ResignFormFields>(EMPTY)
  const [comment, setComment] = useState('')
  const [busy, setBusy] = useState(false)
  const [toast, setToast] = useState<string | null>(null)
  const [confirm, setConfirm] = useState<null | { title: string; description?: string; tone?: 'danger' | 'default'; onConfirm: () => void }>(null)
  const [todos, setTodos] = useState<HrFlowSummaryDto[]>([])

  const fireToast = (m: string) => { setToast(m); setTimeout(() => setToast(null), 2800) }

  useEffect(() => {
    let cancelled = false
    if (instanceId) {
      setBusy(true)
      getHrFlow(instanceId).then(d => {
        if (cancelled) return
        setInstance(d)
        const fd = d.formData as Partial<ResignFormFields>
        setFields({
          expectedLastDay: fd.expectedLastDay ?? '',
          reason: fd.reason ?? '',
          handover: fd.handover ?? '',
          note: fd.note ?? '',
        })
      }).catch(e => fireToast(`Load failed: ${e.message}`))
        .finally(() => { if (!cancelled) setBusy(false) })
    } else {
      setInstance(null)
    }
    return () => { cancelled = true }
  }, [instanceId])

  useEffect(() => {
    let cancelled = false
    if (!instanceId && (persona === 'manager' || persona === 'hr')) {
      getMyHrFlowTodos()
        .then(rows => { if (!cancelled) setTodos(rows.filter(r => r.specCode === 1)) })
        .catch(() => { /* swallow; just empty list */ })
    } else {
      setTodos([])
    }
    return () => { cancelled = true }
  }, [instanceId, persona])

  const activeStep = useMemo(() => {
    if (!instance) return 0
    switch (instance.currentStep) {
      case HrFlowStep.Apply: return 0
      case HrFlowStep.ManagerApprove: return 1
      case HrFlowStep.HrApprove: return 2
      case HrFlowStep.Closed: return 3
    }
    return 0
  }, [instance])

  const isInitiator = instance && persona === 'employee'
  const isManager = instance && persona === 'manager' && instance.status === HrFlowStatus.PendingManager
  const isHrTurn = instance && persona === 'hr' && instance.status === HrFlowStatus.PendingHr
  const isReturned = instance?.status === HrFlowStatus.Returned
  const isCompleted = instance?.status === HrFlowStatus.Completed
  const isCancelled = instance?.status === HrFlowStatus.Cancelled
  const readonlyFields = !!instance && !(isInitiator && isReturned) && !(!instance)

  async function handleSubmit() {
    if (!fields.expectedLastDay || !fields.reason.trim()) {
      fireToast('Expected last day and reason are required.')
      return
    }
    setBusy(true)
    try {
      const dto = await startHrFlow('RESIGN', fields)
      setInstanceId(dto.id)
      fireToast('Submitted. Awaiting manager approval.')
    } catch (e) {
      fireToast(`Submit failed: ${(e as Error).message}`)
    } finally { setBusy(false) }
  }

  async function handleApprove() {
    if (!instance) return
    setBusy(true)
    try {
      const dto = await approveHrFlow(instance.id, comment || undefined)
      setInstance(dto)
      setComment('')
      fireToast(dto.status === HrFlowStatus.Completed ? 'HR approved. Case closed.' : 'Approved. Sent to HR.')
    } catch (e) {
      fireToast(`Approve failed: ${(e as Error).message}`)
    } finally { setBusy(false) }
  }

  async function handleReturn() {
    if (!instance) return
    if (!comment.trim()) { fireToast('Comment is required to return.'); return }
    setBusy(true)
    try {
      const dto = await returnHrFlow(instance.id, comment)
      setInstance(dto)
      setComment('')
      fireToast('Returned to applicant.')
    } catch (e) {
      fireToast(`Return failed: ${(e as Error).message}`)
    } finally { setBusy(false) }
  }

  async function handleResubmit() {
    if (!instance) return
    if (!fields.expectedLastDay || !fields.reason.trim()) {
      fireToast('Expected last day and reason are required.')
      return
    }
    setBusy(true)
    try {
      const dto = await resubmitHrFlow(instance.id, fields)
      setInstance(dto)
      fireToast('Resubmitted. Awaiting manager approval.')
    } catch (e) {
      fireToast(`Resubmit failed: ${(e as Error).message}`)
    } finally { setBusy(false) }
  }

  async function handleCancel() {
    if (!instance) return
    setBusy(true)
    try {
      const dto = await cancelHrFlow(instance.id)
      setInstance(dto)
      fireToast('Cancelled.')
    } catch (e) {
      fireToast(`Cancel failed: ${(e as Error).message}`)
    } finally { setBusy(false) }
  }

  const stepperStep = instance ? activeStep : 0

  return (
    <FormShell code="RESIGN" activeStep={stepperStep} setActiveStep={() => {}} persona={persona} copySelector={false}
      rightActions={instance ? (
        <Button variant="outline" size="sm" onClick={() => { setInstanceId(null); setFields(EMPTY); setComment('') }}>
          New / 開新單
        </Button>
      ) : null}>
      {toast && (
        <div className="fixed bottom-6 right-6 z-50 rounded-lg bg-slate-800 px-4 py-2.5 text-sm text-white shadow-2xl">
          {toast}
        </div>
      )}

      {!instance && todos.length > 0 && (
        <SectionCard>
          <SectionTitle>Pending {persona === 'hr' ? 'HR' : 'Manager'} Review / 待簽案件</SectionTitle>
          <div className="divide-y divide-rule">
            {todos.map(t => (
              <button key={t.id} onClick={() => setInstanceId(t.id)} className="flex w-full items-center justify-between px-4 py-2.5 text-left text-sm hover:bg-slate-50">
                <span>
                  <span className="font-mono text-xs text-ink-muted">{t.id.slice(0, 8)}</span>
                  <span className="ml-3 font-medium text-ink">{t.initiatorName}</span>
                  <span className="ml-2 text-ink-muted">started {new Date(t.startedAt).toLocaleDateString()}</span>
                </span>
                <span className="text-xs text-ink-muted">{statusLabel(t.status)}</span>
              </button>
            ))}
          </div>
        </SectionCard>
      )}

      <SectionCard>
        <SectionTitle>Resignation Detail / 離職資訊</SectionTitle>
        <div className="grid grid-cols-2 gap-4 p-5">
          <Field label="Expected last day / 預計離職日" required>
            <div className="relative">
              <Input type="date" value={fields.expectedLastDay}
                onChange={e => setFields({ ...fields, expectedLastDay: e.target.value })}
                readOnly={readonlyFields} />
              <CalendarIcon className="pointer-events-none absolute right-2 top-2 h-4 w-4 text-ink-faint" />
            </div>
          </Field>
          <Field label="Handover to / 工作交接對象">
            <Input value={fields.handover}
              onChange={e => setFields({ ...fields, handover: e.target.value })}
              placeholder="同事姓名 / colleague name"
              readOnly={readonlyFields} />
          </Field>
        </div>
        <div className="border-t border-rule px-5 py-4">
          <Field label="Reason / 離職原因" required>
            <Textarea rows={3} value={fields.reason}
              onChange={e => setFields({ ...fields, reason: e.target.value })}
              placeholder="e.g. 生涯規劃 / career change"
              readOnly={readonlyFields} />
          </Field>
        </div>
        <div className="border-t border-rule px-5 py-4">
          <Field label="Note / 備註">
            <Textarea rows={2} value={fields.note}
              onChange={e => setFields({ ...fields, note: e.target.value })}
              readOnly={readonlyFields} />
          </Field>
        </div>
      </SectionCard>

      {instance && (
        <SectionCard>
          <SectionTitle>History / 流程紀錄</SectionTitle>
          <div className="divide-y divide-rule">
            {instance.actions.map(a => (
              <div key={a.id} className="flex items-start justify-between px-4 py-2.5 text-sm">
                <div>
                  <span className="font-medium text-ink">{a.actorName}</span>
                  <span className="ml-2 text-ink-muted">{actionLabel(a.action)} ({stepLabel(a.fromStep)} → {stepLabel(a.toStep)})</span>
                  {a.comment && <p className="mt-0.5 text-xs text-ink-muted">"{a.comment}"</p>}
                </div>
                <span className="shrink-0 text-xs text-ink-faint">{new Date(a.createdAt).toLocaleString()}</span>
              </div>
            ))}
          </div>
        </SectionCard>
      )}

      {!instance && persona === 'employee' && (
        <SectionCard>
          <div className="flex items-center justify-end gap-2 px-4 py-3">
            <Button variant="primary" size="md" onClick={handleSubmit} disabled={busy}>
              {busy && <Loader2 className="h-3.5 w-3.5 animate-spin" />}
              Submit
            </Button>
          </div>
        </SectionCard>
      )}

      {instance && isInitiator && isReturned && (
        <SectionCard>
          <InfoBanner>
            Your request was returned by {instance.managerName}. Please revise and resubmit.
          </InfoBanner>
          <div className="flex items-center justify-end gap-2 px-4 py-3">
            <Button variant="outline" size="md" onClick={() => setConfirm({
              title: 'Cancel this request?', tone: 'danger',
              onConfirm: handleCancel,
            })} disabled={busy}>Cancel Request</Button>
            <Button variant="primary" size="md" onClick={handleResubmit} disabled={busy}>
              {busy && <Loader2 className="h-3.5 w-3.5 animate-spin" />}
              Resubmit
            </Button>
          </div>
        </SectionCard>
      )}

      {instance && isManager && (
        <SectionCard>
          <SectionTitle>Manager Decision / 主管簽核</SectionTitle>
          <div className="space-y-3 p-5">
            <Field label="Comment / 簽核意見">
              <Textarea rows={3} value={comment} onChange={e => setComment(e.target.value)}
                placeholder="Optional for approve. Required for return." />
            </Field>
            <div className="flex justify-end gap-2">
              <Button variant="destructive" size="md" onClick={() => setConfirm({
                title: 'Return this request?', description: 'Applicant will be notified to revise and resubmit.',
                tone: 'danger', onConfirm: handleReturn,
              })} disabled={busy}>Return</Button>
              <Button variant="primary" size="md" onClick={handleApprove} disabled={busy}>
                {busy && <Loader2 className="h-3.5 w-3.5 animate-spin" />}
                Approve
              </Button>
            </div>
          </div>
        </SectionCard>
      )}

      {instance && isHrTurn && (
        <SectionCard>
          <SectionTitle>HR Decision / 人資簽核</SectionTitle>
          <div className="space-y-3 p-5">
            <Field label="Comment / 簽核意見">
              <Textarea rows={2} value={comment} onChange={e => setComment(e.target.value)} placeholder="Optional." />
            </Field>
            <div className="flex justify-end gap-2">
              <Button variant="primary" size="md" onClick={handleApprove} disabled={busy}>
                {busy && <Loader2 className="h-3.5 w-3.5 animate-spin" />}
                Approve & Close
              </Button>
            </div>
          </div>
        </SectionCard>
      )}

      {instance && (isCompleted || isCancelled) && (
        <SectionCard>
          <div className="flex items-center justify-between gap-3 px-4 py-3">
            <p className="text-sm text-ink-muted">
              {isCompleted ? '✓ This case is completed.' : '✕ This case was cancelled.'}
            </p>
          </div>
        </SectionCard>
      )}

      {instance && !isCompleted && !isCancelled && persona === 'employee' && !isReturned && (
        <SectionCard>
          <div className="flex items-center justify-end gap-2 px-4 py-3">
            <Button variant="outline" size="sm" onClick={() => setConfirm({
              title: 'Cancel this request?', tone: 'danger',
              onConfirm: handleCancel,
            })} disabled={busy}>Cancel Request</Button>
          </div>
        </SectionCard>
      )}

      <ConfirmDialog
        open={!!confirm}
        title={confirm?.title ?? ''}
        description={confirm?.description}
        tone={confirm?.tone}
        confirmText={confirm?.tone === 'danger' ? 'Confirm' : 'Confirm'}
        onCancel={() => setConfirm(null)}
        onConfirm={() => { confirm?.onConfirm(); setConfirm(null) }}
      />
    </FormShell>
  )
}

function statusLabel(s: number): string {
  switch (s) {
    case HrFlowStatus.PendingManager: return '待主管簽核'
    case HrFlowStatus.PendingHr: return '待 HR 簽核'
    case HrFlowStatus.Returned: return '已退回'
    case HrFlowStatus.Completed: return '已完成'
    case HrFlowStatus.Cancelled: return '已取消'
  }
  return ''
}

function actionLabel(a: number): string {
  switch (a) {
    case HrFlowActionType.Submit: return 'submitted'
    case HrFlowActionType.Approve: return 'approved'
    case HrFlowActionType.Return: return 'returned'
    case HrFlowActionType.Cancel: return 'cancelled'
  }
  return ''
}

function stepLabel(s: number): string {
  switch (s) {
    case HrFlowStep.Apply: return 'Apply'
    case HrFlowStep.ManagerApprove: return 'Manager'
    case HrFlowStep.HrApprove: return 'HR'
    case HrFlowStep.Closed: return 'Closed'
  }
  return ''
}

/**
 * Task-mode renderer for the ProcessRuntime path. Reads the merged form
 * snapshot and exposes Approve / Reject / Return via the standard ActionBar.
 * No fields are editable: per the spec the apply step's data is read-only by
 * approvers.
 */
function ResignTaskMode({ persona, runtime }: {
  persona: PersonaCode
  runtime: ReturnType<typeof useFormRuntime>
}) {
  const fd = (runtime.task?.mergedFormData ?? {}) as {
    expected_last_day?: string
    reason_category?: string
    reason_detail?: string
    handover_to?: string
    handover_items?: string
    note?: string
  }

  return (
    <FormShell code="RESIGN" activeStep={0} setActiveStep={() => {}} persona={persona} copySelector={false} mode="task">
      <FlowToast message={runtime.toast} />
      {runtime.taskLoading && <SectionCard><div className="px-4 py-3 text-sm text-ink-muted">Loading task…</div></SectionCard>}
      {runtime.taskError && <SectionCard><div className="px-4 py-3 text-sm text-danger">Load failed: {runtime.taskError}</div></SectionCard>}

      <SectionCard>
        <SectionTitle>Resignation Detail / 離職資訊</SectionTitle>
        <div className="grid grid-cols-2 gap-x-8 gap-y-3 p-4">
          <Field label="Expected last day"><Input value={fd.expected_last_day ?? ''} readOnly /></Field>
          <Field label="Reason"><Input value={fd.reason_category ?? ''} readOnly /></Field>
          <Field label="Handover to"><Input value={fd.handover_to ?? ''} readOnly /></Field>
          <div className="col-span-2"><Field label="Reason Detail"><Textarea rows={2} value={fd.reason_detail ?? ''} readOnly /></Field></div>
          <div className="col-span-2"><Field label="Handover Items"><Textarea rows={2} value={fd.handover_items ?? ''} readOnly /></Field></div>
          <div className="col-span-2"><Field label="Note"><Textarea rows={2} value={fd.note ?? ''} readOnly /></Field></div>
        </div>
      </SectionCard>

      <ActionBar
        code="RESIGN"
        activeStep={0}
        persona={persona}
        mode="task"
        nodeKind={runtime.task?.task.nodeKind}
        pending={runtime.pending}
        onSubmit={() => { void runtime.submitUserTask() }}
        onApprove={c => { void runtime.approve(c) }}
        onReject={c => { void runtime.reject(c) }}
        onReturnTask={c => { void runtime.returnTask(c) }}
      />
    </FormShell>
  )
}
