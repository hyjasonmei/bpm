import { useCallback, useEffect, useMemo, useState } from 'react'
import { ArrowLeft, CheckCircle2, Plus, RotateCcw, Trash2, Workflow as WorkflowIcon, XCircle } from 'lucide-react'
import { useNavigate } from 'react-router-dom'

import { Button } from '@/components/ui/button'
import { SectionCard, SectionTitle } from '@/components/ui/card'
import { StatusBadge, type StatusKind } from '@/components/ui/badge'
import { Field, Input, Select, Textarea, InfoBanner } from '@/components/ui/form'
import { ActionFooter, type ActionFooterItem } from '@/components/ui/action-footer/ActionFooter'
import { FlowStateBanner } from '@/components/ui/flow-state-banner/FlowStateBanner'
import { Stepper } from '@/components/Stepper'
import { BpmnView } from '@/components/BpmnView'
import { apiFetch, getJwt } from '@/lib/apiFetch'
import { decodeJwt } from '@/lib/jwt'
import { useDelegatedFor } from '@/lib/useDelegatedFor'
import { FORMS } from '@/lib/workflow'
import type { CaseDetailProps } from '@/features/registry'
import EOB_V1_BpmnXml from './EOB_V1.bpmn.xml?raw'
import { TASK_STATUS_OPTIONS, defaultSetupTasks, emptyTask, zhStatus } from './EOB_V1_shared'
import type { EOB_V1_CaseResponse, EOB_V1_SetupTaskDto, EOB_V1_Status } from './EOB_V1_types'

/** Read-mostly detail page for an EOB V1 case (manager approval → setup checklist). */
export function EOB_V1_CaseDetail({ caseId }: CaseDetailProps) {
  const navigate = useNavigate()
  const [data, setData] = useState<EOB_V1_CaseResponse | null>(null)
  const [viewerUserId, setViewerUserId] = useState<string | null>(null)
  const [loading, setLoading] = useState(true)
  const [error, setError] = useState<string | null>(null)
  const [actionPending, setActionPending] = useState(false)
  const [actionError, setActionError] = useState<string | null>(null)
  const [bpmnOpen, setBpmnOpen] = useState(false)
  const [approvalComment, setApprovalComment] = useState('')
  const [tasks, setTasks] = useState<EOB_V1_SetupTaskDto[]>(defaultSetupTasks())

  const load = useCallback(async () => {
    try {
      const res = await apiFetch(`/api/eob/v1/${caseId}`)
      if (!res.ok) throw new Error(`HTTP ${res.status}`)
      setData((await res.json()) as EOB_V1_CaseResponse)
      setError(null)
    } catch (e) { setError(e instanceof Error ? e.message : String(e)) } finally { setLoading(false) }
  }, [caseId])

  useEffect(() => { void load() }, [load])
  useEffect(() => {
    const tok = getJwt(); if (!tok) return
    const decoded = decodeJwt(tok); if (decoded?.sub) setViewerUserId(decoded.sub)
  }, [])

  const delegatedFor = useDelegatedFor()
  // The viewer may act on the case if they are the current assignee OR an active
  // delegate of the current assignee (delegation-aware — see useDelegatedFor).
  const isCurrentAssignee = !!data && !!viewerUserId && !!data.currentAssigneeUserId &&
    (data.currentAssigneeUserId === viewerUserId || delegatedFor.includes(data.currentAssigneeUserId))
  const isSubmitter       = !!data && !!viewerUserId && data.submitterUserId === viewerUserId
  const trail = useMemo(() => (data ? deriveTrail(data.status) : null), [data])

  const postManagerDecision = useCallback(async (approve: boolean) => {
    setActionPending(true); setActionError(null)
    try {
      const res = await apiFetch(`/api/eob/v1/${caseId}/manager-decision`, {
        method: 'POST', headers: { 'Content-Type': 'application/json' },
        body: JSON.stringify({ approve, comment: approvalComment.trim() ? approvalComment : null }),
      })
      if (!res.ok) throw new Error(await res.text() || `HTTP ${res.status}`)
      setApprovalComment(''); await load()
    } catch (e) { setActionError(e instanceof Error ? e.message : String(e)) } finally { setActionPending(false) }
  }, [caseId, approvalComment, load])

  const postSetup = useCallback(async () => {
    setActionPending(true); setActionError(null)
    try {
      const res = await apiFetch(`/api/eob/v1/${caseId}/setup`, {
        method: 'POST', headers: { 'Content-Type': 'application/json' },
        body: JSON.stringify({ setupTasks: tasks.filter(t => t.task?.trim()) }),
      })
      if (!res.ok) throw new Error(await res.text() || `HTTP ${res.status}`)
      await load()
    } catch (e) { setActionError(e instanceof Error ? e.message : String(e)) } finally { setActionPending(false) }
  }, [caseId, tasks, load])

  const postCancel = useCallback(async () => {
    setActionPending(true); setActionError(null)
    try {
      const res = await apiFetch(`/api/eob/v1/${caseId}/cancel`, { method: 'POST' })
      if (!res.ok) throw new Error(await res.text() || `HTTP ${res.status}`)
      await load()
    } catch (e) { setActionError(e instanceof Error ? e.message : String(e)) } finally { setActionPending(false) }
  }, [caseId, load])

  const footerActions: ActionFooterItem[] = useMemo(() => {
    if (!data) return []
    const actions: ActionFooterItem[] = []
    if (isCurrentAssignee && data.status === 'PendingManager') {
      actions.push(
        { id: 'reject',  label: '退件 / Reject',  variant: 'destructive', pending: actionPending, onClick: () => postManagerDecision(false) },
        { id: 'approve', label: '核准 / Approve', variant: 'primary',     pending: actionPending, onClick: () => postManagerDecision(true)  },
      )
    }
    if (isCurrentAssignee && data.status === 'PendingSetup') {
      actions.push({ id: 'setup', label: '完成基本設定 / Complete setup', variant: 'primary', pending: actionPending, onClick: () => postSetup() })
    }
    if (isSubmitter && data.status === 'ResubmitRequired') {
      actions.push({
        id: 'resubmit', confirm: false,
        label: <span className="inline-flex items-center gap-1"><RotateCcw className="h-3.5 w-3.5" />修正後重新送出</span>,
        variant: 'primary', pending: actionPending,
        onClick: () => navigate(`/apply/EOB?resubmit=${data.id}`),
      })
    }
    // Submitter may withdraw their own case while it is still in flight.
    if (isSubmitter && data.status !== 'Completed' && data.status !== 'Cancelled') {
      actions.push({ id: 'withdraw', label: '撤回申請', variant: 'destructive', pending: actionPending, confirm: { titleZh: '撤回申請？', description: '撤回後無法復原。', confirmText: '確認撤回' }, onClick: () => postCancel() })
    }
    return actions
  }, [data, isCurrentAssignee, isSubmitter, actionPending, postManagerDecision, postSetup, postCancel, navigate])

  const footerHint = (() => {
    if (!data) return null
    if (actionError) return <span className="text-danger">{actionError}</span>
    if (isCurrentAssignee && data.status === 'PendingManager') return '請審閱後決定'
    if (isCurrentAssignee && data.status === 'PendingSetup') return '請完成新人基本設定'
    if (isSubmitter && data.status === 'ResubmitRequired') return '請依照退件意見修正後重新送出'
    return null
  })()

  return (
    <div className="mx-auto max-w-screen-lg space-y-4 pb-24 md:px-6 md:pt-6">
      <div className="flex flex-wrap items-center gap-3">
        <Button variant="outline" size="sm" onClick={() => navigate('/')}><ArrowLeft className="h-3.5 w-3.5" /> 返回</Button>
        <div className="order-last w-full min-w-0 md:order-none md:w-auto md:flex-1">
          <h1 className="truncate text-lg font-bold text-ink">新進員工報到案件 <span className="ml-2 text-base font-medium text-ink-muted">· EOB V1</span></h1>
          <p className="font-mono text-[11px] text-ink-faint">{caseId}</p>
        </div>
        <div className="ml-auto"><Button variant="outline" size="sm" onClick={() => setBpmnOpen(true)}><WorkflowIcon className="h-3.5 w-3.5" /> View BPMN</Button></div>
      </div>

      {loading && <SectionCard><div className="px-5 py-10 text-center text-sm text-ink-muted">載入中…</div></SectionCard>}
      {error && !loading && <SectionCard><div className="px-5 py-6 text-sm text-danger">載入失敗：{error}</div></SectionCard>}

      {data && (
        <>
          <FlowStateBanner flowCode="EOB" flowVersion={1} />

          <SectionCard className="!p-0">
            <div className="bg-slate-50 px-4 py-2">
              <Stepper steps={FORMS.EOB.steps} activeStep={activeStepFor(data.status)} withZh />
            </div>
          </SectionCard>

          <SectionCard>
            <SectionTitle>狀態 / Status</SectionTitle>
            <div className="grid grid-cols-2 gap-4 px-5 py-4 text-sm md:grid-cols-4">
              <Stat label="目前狀態" value={<span className="inline-flex items-center gap-2"><StatusBadge kind={statusKind(data.status)} /><span className="text-xs text-ink-muted">{zhStatus(data.status)}</span></span>} />
              <Stat label="申請人" value={<span>{data.submitterDisplayName ?? '—'}</span>} />
              <Stat label="目前指派給" value={<span>{data.currentAssigneeDisplayName ?? '—'}</span>} />
              <Stat label="到職日" value={<span className="font-mono text-xs">{data.onboardDate?.slice(0, 10)}</span>} />
            </div>
            {data.roundCount > 1 && <div className="border-t border-rule px-5 py-3"><InfoBanner>本案件為第 <strong>{data.roundCount}</strong> 次送出。</InfoBanner></div>}
          </SectionCard>

          <SectionCard>
            <SectionTitle>新進員工資料 / New Hire</SectionTitle>
            <div className="grid grid-cols-12 gap-3 px-5 py-4 text-sm">
              <ReadField label="姓名" value={`${data.firstName} ${data.lastName}`} className="col-span-6" />
              <ReadField label="職稱" value={data.businessTitle} className="col-span-6" />
              <ReadField label="地點" value={data.employeeLocation} className="col-span-4" />
              <ReadField label="需信箱" value={data.requireMailbox} className="col-span-4" />
              <ReadField label="成本中心" value={data.costCenter} className="col-span-4" />
              <ReadField label="合約編號" value={data.contractNumber} className="col-span-4" />
              <ReadField label="合約生效" value={data.contractEffectiveDate?.slice(0, 10) ?? null} className="col-span-4" />
              <ReadField label="合約到期" value={data.contractExpirationDate?.slice(0, 10) ?? null} className="col-span-4" />
            </div>
          </SectionCard>

          {data.setupTasks.length > 0 && (
            <SectionCard>
              <SectionTitle>登入設定清單 · {data.setupTasks.length}</SectionTitle>
              <div className="space-y-2 px-5 py-4">
                {data.setupTasks.map((t, i) => (
                  <div key={i} className="flex items-center justify-between rounded-md border border-rule bg-card px-4 py-2 text-sm">
                    <span className="text-ink">{t.task}</span>
                    <StatusBadge kind={t.status === 'Complete' ? 'closed' : 'pending'} />
                  </div>
                ))}
              </div>
            </SectionCard>
          )}

          <SectionCard>
            <SectionTitle>簽核時序 / Approval Timeline</SectionTitle>
            <ol className="divide-y divide-slate-100">
              <TimelineRow label="申請 / Submit" actor={data.submitterDisplayName ?? '—'} state="done" at={data.submittedAt} />
              <TimelineRow label="主管核准 / Manager" actor={data.managerDecision?.displayName ?? '—'}
                state={data.managerDecision?.approved === true ? 'done' : data.managerDecision?.approved === false ? 'rejected' : data.status === 'PendingManager' ? 'current' : 'idle'}
                at={data.managerDecision?.decidedAt} comment={data.managerDecision?.comment} />
              <TimelineRow label="基本設定 / Setup" actor={data.setupByDisplayName ?? '—'}
                state={data.setupAt ? 'done' : data.status === 'PendingSetup' ? 'current' : 'idle'} at={data.setupAt} />
              <TimelineRow label="結案 / Closed" actor={data.status === 'Completed' ? '系統' : data.status === 'Cancelled' ? '申請人撤回' : '—'} state={data.status === 'Completed' ? 'done' : data.status === 'Cancelled' ? 'rejected' : 'idle'} at={data.completedAt} />
            </ol>
          </SectionCard>

          {isCurrentAssignee && data.status === 'PendingManager' && (
            <SectionCard>
              <SectionTitle>您的決定 / Your decision</SectionTitle>
              <div className="px-5 py-4">
                <Field label="簽核意見 / Comment" hint="退件時建議填寫原因">
                  <Textarea rows={3} value={approvalComment} onChange={e => setApprovalComment(e.target.value)} disabled={actionPending} />
                </Field>
              </div>
            </SectionCard>
          )}

          {isCurrentAssignee && data.status === 'PendingSetup' && (
            <SectionCard>
              <SectionTitle>基本設定 / Employee Setup</SectionTitle>
              <div className="space-y-2 px-5 py-4">
                {tasks.map((t, i) => (
                  <div key={i} className="grid grid-cols-12 items-center gap-3">
                    <div className="col-span-7"><Input value={t.task ?? ''} onChange={e => setTasks(prev => prev.map((x, idx) => idx === i ? { ...x, task: e.target.value } : x))} disabled={actionPending} placeholder="設定項目" /></div>
                    <div className="col-span-4">
                      <Select value={t.status ?? 'Pending'} onChange={e => setTasks(prev => prev.map((x, idx) => idx === i ? { ...x, status: e.target.value } : x))} disabled={actionPending}>
                        {TASK_STATUS_OPTIONS.map(o => <option key={o} value={o}>{o}</option>)}
                      </Select>
                    </div>
                    <div className="col-span-1">
                      {tasks.length > 1 && <Button variant="ghost" size="xs" onClick={() => setTasks(prev => prev.filter((_, idx) => idx !== i))} disabled={actionPending}><Trash2 className="h-3.5 w-3.5" /></Button>}
                    </div>
                  </div>
                ))}
                <div className="flex justify-end">
                  <Button variant="outline" size="sm" onClick={() => setTasks(prev => [...prev, emptyTask()])} disabled={actionPending}><Plus className="h-3.5 w-3.5" /> 新增項目</Button>
                </div>
              </div>
            </SectionCard>
          )}
        </>
      )}

      <BpmnView
        open={bpmnOpen}
        onClose={() => setBpmnOpen(false)}
        formLabel={`${FORMS.EOB.code} — ${FORMS.EOB.label}`}
        steps={FORMS.EOB.steps}
        activeStep={0}
        ownerByStep={FORMS.EOB.ownerByStep}
        bpmnXml={EOB_V1_BpmnXml}
        completedNodes={trail?.completed}
        currentNode={trail?.current}
      />

      <ActionFooter hint={footerHint} actions={footerActions} />
    </div>
  )
}

function ReadField({ label, value, className }: { label: string; value: string | null | undefined; className?: string }) {
  return (
    <div className={className}>
      <p className="text-[11px] font-semibold uppercase tracking-wider text-ink-muted">{label}</p>
      <p className="mt-1 text-ink">{value || <span className="text-ink-faint">—</span>}</p>
    </div>
  )
}

function Stat({ label, value }: { label: string; value: React.ReactNode }) {
  return (
    <div>
      <p className="text-[11px] font-semibold uppercase tracking-wider text-ink-muted">{label}</p>
      <div className="mt-1">{value}</div>
    </div>
  )
}

type TimelineState = 'idle' | 'current' | 'done' | 'rejected'

function TimelineRow({ label, actor, state, at, comment }: {
  label: string; actor: string; state: TimelineState; at?: string | null; comment?: string | null
}) {
  const dot = state === 'done' ? <CheckCircle2 className="h-4 w-4 text-good" />
    : state === 'rejected' ? <XCircle className="h-4 w-4 text-danger" />
    : state === 'current' ? <span className="block h-3 w-3 rounded-full bg-amber-400" />
    : <span className="block h-3 w-3 rounded-full bg-slate-200" />
  return (
    <li className="grid grid-cols-[24px_1fr_auto] items-start gap-x-3 gap-y-1 px-5 py-3 md:grid-cols-[24px_180px_1fr_180px] md:gap-3">
      <span className="mt-0.5 inline-flex h-5 w-5 items-center justify-center">{dot}</span>
      <span className="text-sm font-medium text-ink">{label}</span>
      <div className="col-span-2 col-start-2 text-sm text-ink-muted md:col-span-1 md:col-start-auto">
        <div>{actor}</div>
        {comment && <div className="mt-1 italic text-ink-faint">「{comment}」</div>}
      </div>
      <span className="col-start-3 row-start-1 text-right font-mono text-[11px] text-ink-faint md:col-start-auto md:row-start-auto">{at ? formatDate(at) : '—'}</span>
    </li>
  )
}

function statusKind(s: EOB_V1_Status): StatusKind {
  switch (s) {
    case 'PendingManager':   return 'pending'
    case 'PendingSetup':     return 'setup'
    case 'ResubmitRequired': return 'returned'
    case 'Completed':        return 'closed'
    case 'Cancelled':        return 'cancelled'
  }
}

/** Map the case status → index into FORMS.EOB.steps for the header stepper
 *  (apply 0 · approve 1 · setup 2 · closed 3). */
function activeStepFor(status: EOB_V1_Status): number {
  switch (status) {
    case 'PendingManager':   return 1
    case 'PendingSetup':     return 2
    case 'ResubmitRequired': return 1
    case 'Completed':        return 3
    case 'Cancelled':        return 1
  }
}

function deriveTrail(status: EOB_V1_Status): { completed: string[]; current: string | null } {
  switch (status) {
    case 'PendingManager':   return { completed: ['s', 'req'], current: 'ap' }
    case 'PendingSetup':     return { completed: ['s', 'req', 'ap'], current: 'su' }
    case 'ResubmitRequired': return { completed: ['s'], current: 'req' }
    case 'Completed':        return { completed: ['s', 'req', 'ap', 'su', 'e'], current: null }
    case 'Cancelled':        return { completed: ['s'], current: null }
  }
}

function formatDate(iso: string | null | undefined): string {
  if (!iso) return '—'
  const d = new Date(iso)
  if (Number.isNaN(d.getTime())) return iso
  const pad = (n: number) => String(n).padStart(2, '0')
  return `${d.getFullYear()}/${pad(d.getMonth() + 1)}/${pad(d.getDate())} ${pad(d.getHours())}:${pad(d.getMinutes())}`
}
