import { useEffect, useMemo, useState } from 'react'
import { Calendar as CalendarIcon, Loader2 } from 'lucide-react'

import { SectionCard, SectionTitle } from '@/components/ui/card'
import { Input, Textarea, Select, Field, InfoBanner } from '@/components/ui/form'
import { Button } from '@/components/ui/button'
import { ConfirmDialog } from '@/components/ui/confirm-dialog'
import { FormShell } from './FormShell'
import { PERSONAS, type PersonaCode } from '@/lib/role'
import { DEPARTMENTS } from '@/lib/mocks'
import {
  startHrFlow, getHrFlow, approveHrFlow, returnHrFlow, resubmitHrFlow, cancelHrFlow, getMyHrFlowTodos,
} from '@/lib/api/hrFlows'
import {
  HrFlowStatus, HrFlowStep, HrFlowActionType,
  type HrFlowInstanceDto, type HrFlowSummaryDto,
} from '@/types/hrFlows'

interface DeptxFormFields {
  currentDepartment: string
  targetDepartment: string
  effectiveDate: string
  reason: string
}

interface DeptxFormProps { persona: PersonaCode }

export function DeptxForm({ persona }: DeptxFormProps) {
  const myDept = PERSONAS[persona].user.dept
  const empty: DeptxFormFields = useMemo(() => ({
    currentDepartment: myDept,
    targetDepartment: '',
    effectiveDate: '',
    reason: '',
  }), [myDept])

  const [instanceId, setInstanceId] = useState<string | null>(null)
  const [instance, setInstance] = useState<HrFlowInstanceDto | null>(null)
  const [fields, setFields] = useState<DeptxFormFields>(empty)
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
        const fd = d.formData as Partial<DeptxFormFields>
        setFields({
          currentDepartment: fd.currentDepartment ?? myDept,
          targetDepartment: fd.targetDepartment ?? '',
          effectiveDate: fd.effectiveDate ?? '',
          reason: fd.reason ?? '',
        })
      }).catch(e => fireToast(`Load failed: ${e.message}`))
        .finally(() => { if (!cancelled) setBusy(false) })
    } else {
      setInstance(null)
      setFields(empty)
    }
    return () => { cancelled = true }
  }, [instanceId, empty, myDept])

  useEffect(() => {
    let cancelled = false
    if (!instanceId && (persona === 'manager' || persona === 'hr')) {
      getMyHrFlowTodos()
        .then(rows => { if (!cancelled) setTodos(rows.filter(r => r.specCode === 2)) })
        .catch(() => { /* swallow */ })
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
  const readonlyFields = !!instance && !(isInitiator && isReturned)

  async function handleSubmit() {
    if (!fields.targetDepartment || !fields.effectiveDate || !fields.reason.trim()) {
      fireToast('Target department, effective date and reason are required.')
      return
    }
    setBusy(true)
    try {
      const dto = await startHrFlow('DEPTX', fields)
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
    if (!fields.targetDepartment || !fields.effectiveDate || !fields.reason.trim()) {
      fireToast('All fields are required.')
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

  return (
    <FormShell code="DEPTX" activeStep={instance ? activeStep : 0} setActiveStep={() => {}} persona={persona} copySelector={false}
      rightActions={instance ? (
        <Button variant="outline" size="sm" onClick={() => { setInstanceId(null); setComment('') }}>
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
        <SectionTitle>Department Transfer Detail / 異動資訊</SectionTitle>
        <div className="grid grid-cols-2 gap-4 p-5">
          <Field label="Current department / 目前部門">
            <Input value={fields.currentDepartment} readOnly />
          </Field>
          <Field label="Target department / 異動目標部門" required>
            <Select value={fields.targetDepartment}
              onChange={e => setFields({ ...fields, targetDepartment: e.target.value })}
              disabled={readonlyFields}>
              <option value="">— Select target department —</option>
              {DEPARTMENTS.filter(d => d !== fields.currentDepartment).map(d => (
                <option key={d} value={d}>{d}</option>
              ))}
            </Select>
          </Field>
          <Field label="Effective date / 生效日" required>
            <div className="relative">
              <Input type="date" value={fields.effectiveDate}
                onChange={e => setFields({ ...fields, effectiveDate: e.target.value })}
                readOnly={readonlyFields} />
              <CalendarIcon className="pointer-events-none absolute right-2 top-2 h-4 w-4 text-ink-faint" />
            </div>
          </Field>
        </div>
        <div className="border-t border-rule px-5 py-4">
          <Field label="Reason / 異動原因" required>
            <Textarea rows={3} value={fields.reason}
              onChange={e => setFields({ ...fields, reason: e.target.value })}
              placeholder="e.g. 業務重組 / org restructure"
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
              title: 'Cancel this request?', tone: 'danger', onConfirm: handleCancel,
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
              title: 'Cancel this request?', tone: 'danger', onConfirm: handleCancel,
            })} disabled={busy}>Cancel Request</Button>
          </div>
        </SectionCard>
      )}

      <ConfirmDialog
        open={!!confirm}
        title={confirm?.title ?? ''}
        description={confirm?.description}
        tone={confirm?.tone}
        confirmText="Confirm"
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
