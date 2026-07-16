import { useCallback, useEffect, useMemo, useState } from 'react'
import { ArrowLeft, CheckCircle2, Workflow as WorkflowIcon, XCircle } from 'lucide-react'
import { useNavigate } from 'react-router-dom'

import { Button } from '@/components/ui/button'
import { SectionCard, SectionTitle } from '@/components/ui/card'
import { StatusBadge, type StatusKind } from '@/components/ui/badge'
import { Field, Textarea, InfoBanner } from '@/components/ui/form'
import { ActionFooter, type ActionFooterItem } from '@/components/ui/action-footer/ActionFooter'
import { FlowStateBanner } from '@/components/ui/flow-state-banner/FlowStateBanner'
import { Stepper } from '@/components/Stepper'
import { BpmnView } from '@/components/BpmnView'
import { apiFetch, getJwt } from '@/lib/apiFetch'
import { decodeJwt } from '@/lib/jwt'
import { useDelegatedFor } from '@/lib/useDelegatedFor'
import { roleLabel } from '@/lib/roleLabels'
import { FORMS } from '@/lib/workflow'
import type { CaseDetailProps } from '@/features/registry'
import OVERTIME_V1_BpmnXml from './OVERTIME_V1.bpmn.xml?raw'
import type { OVERTIME_V1_CaseResponse, OVERTIME_V1_Status } from './OVERTIME_V1_types'

/**
 * Read-mostly detail page for an OVERTIME V1 case. Role-scoped ActionFooter:
 *  - PendingManager + manager assignee (or delegate) → Reject / Approve
 *  - PendingHr      + HR role holder                 → Reject / Approve
 *  - submitter, non-terminal                         → 撤回 (withdraw)
 */
export function OVERTIME_V1_CaseDetail({ caseId }: CaseDetailProps) {
  const navigate = useNavigate()
  const [data, setData] = useState<OVERTIME_V1_CaseResponse | null>(null)
  const [viewerUserId, setViewerUserId] = useState<string | null>(null)
  // Whether this case is in the viewer's server-computed "pending on me" set.
  // Sourced from /pending (not the JWT roles claim) so shared-role-queue steps
  // resolved via dept inheritance — where the token carries no role — still let
  // the eligible actor decide.
  const [canActOnCase, setCanActOnCase] = useState(false)
  const [loading, setLoading] = useState(true)
  const [error, setError] = useState<string | null>(null)
  const [actionPending, setActionPending] = useState(false)
  const [actionError, setActionError] = useState<string | null>(null)
  const [bpmnOpen, setBpmnOpen] = useState(false)
  const [approvalComment, setApprovalComment] = useState('')

  const load = useCallback(async () => {
    try {
      const [caseRes, pendingRes] = await Promise.all([
        apiFetch(`/api/overtime/v1/${caseId}`),
        apiFetch(`/api/overtime/v1/pending`),
      ])
      if (!caseRes.ok) throw new Error(`HTTP ${caseRes.status}`)
      setData((await caseRes.json()) as OVERTIME_V1_CaseResponse)
      if (pendingRes.ok) {
        const rows = (await pendingRes.json()) as { id: string }[]
        setCanActOnCase(rows.some(r => r.id === caseId))
      }
      setError(null)
    } catch (e) {
      setError(e instanceof Error ? e.message : String(e))
    } finally {
      setLoading(false)
    }
  }, [caseId])

  useEffect(() => { void load() }, [load])
  useEffect(() => {
    const tok = getJwt()
    if (!tok) return
    const decoded = decodeJwt(tok)
    if (decoded?.sub) setViewerUserId(decoded.sub)
  }, [])

  const delegatedFor = useDelegatedFor()
  // Manager step is assigned to a specific user; the viewer may act if they ARE
  // that user or an active delegate of them (delegation-aware — the /pending set
  // is not delegate-expanded, so keep this explicit check for delegates).
  const isManagerAssignee = !!data && data.status === 'PendingManager' && (
    (!!viewerUserId && !!data.currentAssigneeUserId &&
      (data.currentAssigneeUserId === viewerUserId || delegatedFor.includes(data.currentAssigneeUserId)))
    || canActOnCase)
  // HR step is a shared role queue; any holder of the pending role may act —
  // determined server-side via the viewer's /pending set (inheritance-aware).
  const isHrAssignee = !!data && data.status === 'PendingHr' && canActOnCase
  const isSubmitter = !!data && !!viewerUserId && data.submitterUserId === viewerUserId
  const trail = useMemo(() => (data ? deriveTrail(data) : null), [data])

  const postDecision = useCallback(async (path: 'manager-decision' | 'hr-decision', approve: boolean) => {
    setActionPending(true); setActionError(null)
    try {
      const res = await apiFetch(`/api/overtime/v1/${caseId}/${path}`, {
        method: 'POST',
        headers: { 'Content-Type': 'application/json' },
        body: JSON.stringify({ approve, comment: approvalComment.trim() ? approvalComment : null }),
      })
      if (!res.ok) throw new Error(await res.text() || `HTTP ${res.status}`)
      setApprovalComment('')
      await load()
    } catch (e) {
      setActionError(e instanceof Error ? e.message : String(e))
    } finally {
      setActionPending(false)
    }
  }, [caseId, approvalComment, load])

  const postCancel = useCallback(async () => {
    setActionPending(true); setActionError(null)
    try {
      const res = await apiFetch(`/api/overtime/v1/${caseId}/cancel`, { method: 'POST' })
      if (!res.ok) throw new Error(await res.text() || `HTTP ${res.status}`)
      await load()
    } catch (e) {
      setActionError(e instanceof Error ? e.message : String(e))
    } finally {
      setActionPending(false)
    }
  }, [caseId, load])

  const footerActions: ActionFooterItem[] = useMemo(() => {
    if (!data) return []
    const actions: ActionFooterItem[] = []
    if (isManagerAssignee && data.status === 'PendingManager') {
      actions.push(
        { id: 'reject',  label: '退回 / Reject',  variant: 'destructive', pending: actionPending, onClick: () => postDecision('manager-decision', false) },
        { id: 'approve', label: '核准 / Approve', variant: 'primary',     pending: actionPending, onClick: () => postDecision('manager-decision', true)  },
      )
    }
    if (isHrAssignee && data.status === 'PendingHr') {
      actions.push(
        { id: 'reject',  label: '退回 / Reject',  variant: 'destructive', pending: actionPending, onClick: () => postDecision('hr-decision', false) },
        { id: 'approve', label: '核准 / Approve', variant: 'primary',     pending: actionPending, onClick: () => postDecision('hr-decision', true)  },
      )
    }
    // Submitter may withdraw their own case while it is still in flight.
    if (isSubmitter && (data.status === 'PendingManager' || data.status === 'PendingHr')) {
      actions.push({ id: 'withdraw', label: '撤回申請', variant: 'destructive', pending: actionPending, confirm: { titleZh: '撤回申請？', description: '撤回後無法復原。', confirmText: '確認撤回' }, onClick: () => postCancel() })
    }
    return actions
  }, [data, isManagerAssignee, isHrAssignee, isSubmitter, actionPending, postDecision, postCancel])

  const footerHint = (() => {
    if (!data) return null
    if (actionError) return <span className="text-danger">{actionError}</span>
    if (isManagerAssignee && data.status === 'PendingManager') return '請審閱後決定是否核准'
    if (isHrAssignee && data.status === 'PendingHr') return '請複核當月累計加班時數後決定'
    return null
  })()

  const canDecide = (isManagerAssignee && data?.status === 'PendingManager')
    || (isHrAssignee && data?.status === 'PendingHr')

  const fmtHours = (n: number | null) => `${n ?? 0} 小時`

  return (
    <div className="mx-auto max-w-screen-lg space-y-4 p-6 pb-24">
      <div className="flex flex-wrap items-center gap-3">
        <Button variant="outline" size="sm" onClick={() => navigate('/')}>
          <ArrowLeft className="h-3.5 w-3.5" /> 返回
        </Button>
        <div className="order-last w-full min-w-0 md:order-none md:w-auto md:flex-1">
          <h1 className="truncate text-lg font-bold text-ink">
            加班申請案件 <span className="ml-2 text-base font-medium text-ink-muted">· OVERTIME V1</span>
          </h1>
          <p className="font-mono text-[11px] text-ink-faint">{caseId}</p>
        </div>
        <div className="ml-auto">
          <Button variant="outline" size="sm" onClick={() => setBpmnOpen(true)}>
            <WorkflowIcon className="h-3.5 w-3.5" /> View BPMN
          </Button>
        </div>
      </div>

      {loading && <SectionCard><div className="px-5 py-10 text-center text-sm text-ink-muted">載入中…</div></SectionCard>}
      {error && !loading && <SectionCard><div className="px-5 py-6 text-sm text-danger">載入失敗：{error}</div></SectionCard>}

      {data && (
        <>
          <FlowStateBanner flowCode="OVERTIME" flowVersion={1} />

          <SectionCard className="!p-0">
            <div className="bg-slate-50 px-4 py-2">
              <Stepper steps={FORMS.OVERTIME.steps} activeStep={activeStepFor(data.status)} withZh />
            </div>
          </SectionCard>

          <SectionCard>
            <SectionTitle>狀態 / Status</SectionTitle>
            <div className="grid grid-cols-2 gap-4 px-5 py-4 text-sm md:grid-cols-4">
              <Stat label="目前狀態" value={
                <span className="inline-flex items-center gap-2">
                  <StatusBadge kind={statusKind(data.status)} />
                  <span className="text-xs text-ink-muted">{zhStatus(data.status)}</span>
                </span>
              } />
              <Stat label="申請人" value={<span>{data.submitterDisplayName ?? '—'}</span>} />
              <Stat label="目前指派給" value={
                <span>{data.currentAssigneeDisplayName ?? (data.currentAssigneeRoleCode ? `待 ${roleLabel(data.currentAssigneeRoleCode)}` : '—')}</span>
              } />
              <Stat label="預估時數" value={<span className="font-mono">{fmtHours(data.estimatedHours)}</span>} />
            </div>
            {data.monthlyHours != null && (
              <div className="border-t border-rule px-5 py-3">
                <InfoBanner>
                  當月累計加班時數 <strong>{data.monthlyHours} 小時</strong>
                  {data.monthlyHours > 20 ? '，超過 20 小時 → 需人資複核。' : '，未超過 20 小時 → 主管核准後即結案。'}
                </InfoBanner>
              </div>
            )}
          </SectionCard>

          <SectionCard>
            <SectionTitle>加班資訊 / Overtime</SectionTitle>
            <div className="grid grid-cols-12 gap-3 px-5 py-4 text-sm">
              <ReadField label="加班日期" value={data.overtimeDate?.slice(0, 10)} className="col-span-6" />
              <ReadField label="預估時數" value={fmtHours(data.estimatedHours)} className="col-span-6" />
              <ReadField label="開始時間" value={data.startTime} className="col-span-6" />
              <ReadField label="結束時間" value={data.endTime} className="col-span-6" />
              <ReadField label="加班事由" value={data.overtimeReason} className="col-span-12" />
            </div>
          </SectionCard>

          <SectionCard>
            <SectionTitle>簽核時序 / Approval Timeline</SectionTitle>
            <ol className="divide-y divide-slate-100">
              <TimelineRow label="員工申請 / Submit" actor={data.submitterDisplayName ?? '—'} state="done" at={data.submittedAt} />
              <TimelineRow
                label="主管審核 / Manager"
                actor={data.managerDecision?.displayName ?? '—'}
                state={data.managerDecision?.approved === true ? 'done'
                  : data.managerDecision?.approved === false ? 'rejected'
                  : data.status === 'PendingManager' ? 'current' : 'idle'}
                at={data.managerDecision?.decidedAt}
                comment={data.managerDecision?.comment}
              />
              <TimelineRow
                label="人資複核 / HR"
                actor={data.hrDecision?.displayName
                  ?? (data.status === 'PendingHr' ? `待 ${roleLabel(data.currentAssigneeRoleCode)}` : '—')}
                state={data.hrDecision?.approved === true ? 'done'
                  : data.hrDecision?.approved === false ? 'rejected'
                  : data.status === 'PendingHr' ? 'current' : 'idle'}
                at={data.hrDecision?.decidedAt}
                comment={data.hrDecision?.comment}
              />
              <TimelineRow
                label="結案 / Closed"
                actor={data.status === 'Completed' ? '系統'
                  : data.status === 'Cancelled' ? '申請人撤回'
                  : data.status === 'RejectedByManager' ? '主管退回'
                  : data.status === 'RejectedByHr' ? '人資退回' : '—'}
                state={data.status === 'Completed' ? 'done'
                  : data.status === 'Cancelled' || data.status === 'RejectedByManager' || data.status === 'RejectedByHr' ? 'rejected'
                  : 'idle'}
                at={data.completedAt}
              />
            </ol>
          </SectionCard>

          {canDecide && (
            <SectionCard>
              <SectionTitle>您的決定 / Your decision</SectionTitle>
              <div className="space-y-3 px-5 py-4">
                <Field label="簽核意見 / Comment" hint="退回時建議填寫原因">
                  <Textarea rows={3} value={approvalComment} onChange={e => setApprovalComment(e.target.value)} disabled={actionPending} />
                </Field>
              </div>
            </SectionCard>
          )}

          {!canDecide && !isSubmitter && data.status !== 'Completed' && data.status !== 'Cancelled'
            && data.status !== 'RejectedByManager' && data.status !== 'RejectedByHr' && (
            <SectionCard>
              <InfoBanner>
                目前由 <strong>{data.currentAssigneeDisplayName ?? (data.currentAssigneeRoleCode ? roleLabel(data.currentAssigneeRoleCode) : '—')}</strong> 處理。若您是此階段的處理人，請從首頁「Pending My Approval」進入。
              </InfoBanner>
            </SectionCard>
          )}
        </>
      )}

      <BpmnView
        open={bpmnOpen}
        onClose={() => setBpmnOpen(false)}
        formLabel={`${FORMS.OVERTIME.code} — ${FORMS.OVERTIME.label}`}
        steps={FORMS.OVERTIME.steps}
        activeStep={0}
        ownerByStep={FORMS.OVERTIME.ownerByStep}
        bpmnXml={OVERTIME_V1_BpmnXml}
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
      <p className="mt-1 whitespace-pre-wrap text-ink">{value || <span className="text-ink-faint">—</span>}</p>
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

export function zhStatus(s: OVERTIME_V1_Status): string {
  switch (s) {
    case 'PendingManager':    return '待主管審核'
    case 'PendingHr':         return '待人資複核'
    case 'Completed':         return '已核准'
    case 'RejectedByManager': return '主管退回'
    case 'RejectedByHr':      return '人資退回'
    case 'Cancelled':         return '已撤回'
  }
}

function statusKind(s: OVERTIME_V1_Status): StatusKind {
  switch (s) {
    case 'PendingManager':    return 'pending'
    case 'PendingHr':         return 'hr_review'
    case 'Completed':         return 'closed'
    case 'RejectedByManager': return 'rejected'
    case 'RejectedByHr':      return 'rejected'
    case 'Cancelled':         return 'cancelled'
  }
}

/** Map the case status → index into FORMS.OVERTIME.steps for the header stepper
 *  (apply 0 · manager 1 · hr 2 · close 3). */
function activeStepFor(status: OVERTIME_V1_Status): number {
  switch (status) {
    case 'PendingManager':    return 1
    case 'RejectedByManager': return 1
    case 'PendingHr':         return 2
    case 'RejectedByHr':      return 2
    case 'Completed':         return 3
    case 'Cancelled':         return 0
  }
}

/** Status → spec.flow.nodes[].id trail for the BPMN highlight. Completion has two
 *  shapes: via HR review (hrDecision present) or auto-approved under threshold. */
function deriveTrail(data: OVERTIME_V1_CaseResponse): { completed: string[]; current: string | null } {
  const base = ['start_1', 'task_apply']
  switch (data.status) {
    case 'PendingManager':
      return { completed: base, current: 'approval_manager' }
    case 'RejectedByManager':
      return { completed: [...base, 'approval_manager', 'gateway_approved', 'notify_reject_manager', 'end_rejected_manager'], current: null }
    case 'PendingHr':
      return { completed: [...base, 'approval_manager', 'gateway_approved', 'gateway_hours'], current: 'approval_hr' }
    case 'RejectedByHr':
      return { completed: [...base, 'approval_manager', 'gateway_approved', 'gateway_hours', 'approval_hr', 'gateway_hr_result', 'notify_reject_hr', 'end_rejected_hr'], current: null }
    case 'Completed': {
      const viaHr = data.hrDecision?.approved === true
      const mid = viaHr
        ? ['approval_manager', 'gateway_approved', 'gateway_hours', 'approval_hr', 'gateway_hr_result', 'notify_approve', 'end_approved']
        : ['approval_manager', 'gateway_approved', 'gateway_hours', 'notify_approve', 'end_approved']
      return { completed: [...base, ...mid], current: null }
    }
    case 'Cancelled':
      return { completed: base, current: null }
  }
}

function formatDate(iso: string | null | undefined): string {
  if (!iso) return '—'
  const d = new Date(iso)
  if (Number.isNaN(d.getTime())) return iso
  const pad = (n: number) => String(n).padStart(2, '0')
  return `${d.getFullYear()}/${pad(d.getMonth() + 1)}/${pad(d.getDate())} ${pad(d.getHours())}:${pad(d.getMinutes())}`
}
