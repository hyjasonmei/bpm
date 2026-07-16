import { useCallback, useEffect, useMemo, useState } from 'react'
import { ArrowLeft, CheckCircle2, RotateCcw, Workflow as WorkflowIcon, XCircle } from 'lucide-react'
import { useNavigate } from 'react-router-dom'

import { Button } from '@/components/ui/button'
import { SectionCard, SectionTitle } from '@/components/ui/card'
import { StatusBadge, type StatusKind } from '@/components/ui/badge'
import { Field, Textarea, InfoBanner } from '@/components/ui/form'
import { ActionFooter, type ActionFooterItem } from '@/components/ui/action-footer/ActionFooter'
import { FlowStateBanner } from '@/components/ui/flow-state-banner/FlowStateBanner'
import { Stepper } from '@/components/Stepper'
import { BpmnView } from '@/components/BpmnView'
import { AuthedFileLink } from '@/components/ui/FilePicker'
import { apiFetch, getJwt } from '@/lib/apiFetch'
import { decodeJwt } from '@/lib/jwt'
import { useDelegatedFor } from '@/lib/useDelegatedFor'
import { FORMS } from '@/lib/workflow'
import type { CaseDetailProps } from '@/features/registry'
import WFH_V6_BpmnXml from './WFH_V6.bpmn.xml?raw'
import { zhStatus } from './WFH_V6_shared'
import type { WFH_V6_CaseResponse, WFH_V6_Status } from './WFH_V6_types'

/**
 * Read-mostly detail page for a WFH V6 case. Role-scoped ActionFooter:
 *  - PendingManager + assignee → Reject / Approve (manager-decision)
 *  - PendingSenior  + assignee → Reject / Approve (senior-decision)
 *  - ResubmitRequired + submitter → Edit & resubmit
 *  - any in-flight + submitter → Withdraw
 */
export function WFH_V6_CaseDetail({ caseId }: CaseDetailProps) {
  const navigate = useNavigate()
  const [data, setData] = useState<WFH_V6_CaseResponse | null>(null)
  const [viewerUserId, setViewerUserId] = useState<string | null>(null)
  const [loading, setLoading] = useState(true)
  const [error, setError] = useState<string | null>(null)
  const [actionPending, setActionPending] = useState(false)
  const [actionError, setActionError] = useState<string | null>(null)
  const [bpmnOpen, setBpmnOpen] = useState(false)
  const [approvalComment, setApprovalComment] = useState('')

  const load = useCallback(async () => {
    try {
      const res = await apiFetch(`/api/wfh/v6/${caseId}`)
      if (!res.ok) throw new Error(`HTTP ${res.status}`)
      setData((await res.json()) as WFH_V6_CaseResponse)
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
  const isCurrentAssignee = !!data && !!viewerUserId && !!data.currentAssigneeUserId &&
    (data.currentAssigneeUserId === viewerUserId || delegatedFor.includes(data.currentAssigneeUserId))
  const isSubmitter = !!data && !!viewerUserId && data.submitterUserId === viewerUserId
  const trail = useMemo(() => (data ? deriveTrail(data) : null), [data])

  const isPendingApproval = data?.status === 'PendingManager' || data?.status === 'PendingSenior'

  const postDecision = useCallback(async (approve: boolean) => {
    if (!data) return
    setActionPending(true); setActionError(null)
    try {
      const path = data.status === 'PendingManager' ? 'manager-decision' : 'senior-decision'
      const res = await apiFetch(`/api/wfh/v6/${caseId}/${path}`, {
        method: 'POST',
        headers: { 'Content-Type': 'application/json' },
        body: JSON.stringify({ approve, comment: approvalComment.trim() ? approvalComment : null }),
      })
      if (!res.ok) throw new Error((await res.text()) || `HTTP ${res.status}`)
      setApprovalComment('')
      await load()
    } catch (e) {
      setActionError(e instanceof Error ? e.message : String(e))
    } finally {
      setActionPending(false)
    }
  }, [data, caseId, approvalComment, load])

  const postCancel = useCallback(async () => {
    setActionPending(true); setActionError(null)
    try {
      const res = await apiFetch(`/api/wfh/v6/${caseId}/cancel`, { method: 'POST' })
      if (!res.ok) throw new Error((await res.text()) || `HTTP ${res.status}`)
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
    if (isCurrentAssignee && isPendingApproval) {
      actions.push(
        { id: 'reject',  label: '退件 / Reject',  variant: 'destructive', pending: actionPending, onClick: () => postDecision(false) },
        { id: 'approve', label: '核准 / Approve', variant: 'primary',     pending: actionPending, onClick: () => postDecision(true)  },
      )
    }
    if (isSubmitter && data.status === 'ResubmitRequired') {
      actions.push({
        id: 'resubmit', confirm: false,
        label: <span className="inline-flex items-center gap-1"><RotateCcw className="h-3.5 w-3.5" />修正後重新送出</span>,
        variant: 'primary', pending: actionPending,
        onClick: () => navigate(`/apply/WFH?resubmit=${data.id}`),
      })
    }
    if (isSubmitter && data.status !== 'Completed' && data.status !== 'Cancelled') {
      actions.push({ id: 'withdraw', label: '撤回申請', variant: 'destructive', pending: actionPending, confirm: { titleZh: '撤回申請？', description: '撤回後無法復原。', confirmText: '確認撤回' }, onClick: () => postCancel() })
    }
    return actions
  }, [data, isCurrentAssignee, isPendingApproval, isSubmitter, actionPending, postDecision, postCancel, navigate])

  const footerHint = (() => {
    if (!data) return null
    if (actionError) return <span className="text-danger">{actionError}</span>
    if (isCurrentAssignee && data.status === 'PendingManager') return '請審閱後決定（主管核准）'
    if (isCurrentAssignee && data.status === 'PendingSenior') return '請審閱後決定（上級主管核准）'
    if (isSubmitter && data.status === 'ResubmitRequired') return '請依照退件意見修正後重新送出'
    return null
  })()

  return (
    <div className="mx-auto max-w-screen-lg space-y-4 p-6 pb-24">
      <div className="flex flex-wrap items-center gap-3">
        <Button variant="outline" size="sm" onClick={() => navigate('/')}>
          <ArrowLeft className="h-3.5 w-3.5" /> 返回
        </Button>
        <div className="order-last w-full min-w-0 md:order-none md:w-auto md:flex-1">
          <h1 className="truncate text-lg font-bold text-ink">
            居家辦公案件 <span className="ml-2 text-base font-medium text-ink-muted">· WFH V6</span>
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
          <FlowStateBanner flowCode="WFH" flowVersion={6} />

          <SectionCard className="!p-0">
            <div className="bg-slate-50 px-4 py-2">
              <Stepper steps={FORMS.WFH.steps} activeStep={activeStepFor(data.status)} withZh />
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
              <Stat label="目前指派給" value={<span>{data.currentAssigneeDisplayName ?? '—'}</span>} />
              <Stat label="連續天數" value={<span className="font-mono">{data.days} 天</span>} />
            </div>
            {data.roundCount > 1 && (
              <div className="border-t border-rule px-5 py-3">
                <InfoBanner>本案件為第 <strong>{data.roundCount}</strong> 次送出（先前曾被退回補件）。</InfoBanner>
              </div>
            )}
          </SectionCard>

          <SectionCard>
            <SectionTitle>申請內容 / WFH Detail</SectionTitle>
            <div className="grid grid-cols-12 gap-3 px-5 py-4 text-sm">
              <ReadField label="申請日期" value={data.applyDate?.slice(0, 10)} className="col-span-6" />
              <ReadField label="連續天數" value={`${data.days} 天`} className="col-span-6" />
              <ReadField label="居家辦公期間" value={`${data.startDate?.slice(0, 10)} → ${data.endDate?.slice(0, 10)}`} className="col-span-6" />
              <div className="col-span-6">
                <p className="text-[11px] font-semibold uppercase tracking-wider text-ink-muted">附件 / Attachment</p>
                <p className="mt-1 text-ink">{data.attachmentFileId ? <AuthedFileLink id={data.attachmentFileId} className="text-primary hover:underline">下載附件</AuthedFileLink> : <span className="text-ink-faint">—</span>}</p>
              </div>
              <ReadField label="申請原因 / Reason" value={data.reason} className="col-span-12" />
            </div>
          </SectionCard>

          <SectionCard>
            <SectionTitle>簽核時序 / Approval Timeline</SectionTitle>
            <ol className="divide-y divide-slate-100">
              <TimelineRow label="員工申請 / Submit" actor={data.submitterDisplayName ?? '—'} state="done" at={data.submittedAt} />
              <TimelineRow
                label="主管核准 / Manager"
                actor={data.managerDecision?.displayName ?? '—'}
                state={decisionState(data.managerDecision?.approved, data.status === 'PendingManager')}
                at={data.managerDecision?.decidedAt}
                comment={data.managerDecision?.comment}
              />
              {(data.seniorDecision || data.status === 'PendingSenior') && (
                <TimelineRow
                  label="上級主管核准 / Senior"
                  actor={data.seniorDecision?.displayName ?? '—'}
                  state={decisionState(data.seniorDecision?.approved, data.status === 'PendingSenior')}
                  at={data.seniorDecision?.decidedAt}
                  comment={data.seniorDecision?.comment}
                />
              )}
              <TimelineRow
                label="結案 / Closed"
                actor={data.status === 'Completed' ? '系統' : data.status === 'Cancelled' ? '申請人撤回' : '—'}
                state={data.status === 'Completed' ? 'done' : data.status === 'Cancelled' ? 'rejected' : 'idle'}
                at={data.completedAt}
              />
            </ol>
          </SectionCard>

          {isCurrentAssignee && isPendingApproval && (
            <SectionCard>
              <SectionTitle>您的決定 / Your decision（{data.status === 'PendingManager' ? '主管核准' : '上級主管核准'}）</SectionTitle>
              <div className="space-y-3 px-5 py-4">
                <Field label="簽核意見 / Comment" hint="退件時建議填寫原因">
                  <Textarea rows={3} value={approvalComment} onChange={e => setApprovalComment(e.target.value)} disabled={actionPending} />
                </Field>
              </div>
            </SectionCard>
          )}

          {!isCurrentAssignee && !isSubmitter && data.status !== 'Completed' && data.status !== 'Cancelled' && (
            <SectionCard>
              <InfoBanner>
                目前由 <strong>{data.currentAssigneeDisplayName ?? '—'}</strong> 處理。若您是此階段的處理人，請從首頁「Pending My Approval」進入。
              </InfoBanner>
            </SectionCard>
          )}
        </>
      )}

      <BpmnView
        open={bpmnOpen}
        onClose={() => setBpmnOpen(false)}
        formLabel={`${FORMS.WFH.code} — ${FORMS.WFH.label}`}
        steps={FORMS.WFH.steps}
        activeStep={0}
        ownerByStep={FORMS.WFH.ownerByStep}
        bpmnXml={WFH_V6_BpmnXml}
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

function decisionState(approved: boolean | null | undefined, isCurrent: boolean): TimelineState {
  if (approved === true) return 'done'
  if (approved === false) return 'rejected'
  return isCurrent ? 'current' : 'idle'
}

function TimelineRow({ label, actor, state, at, comment }: {
  label: string; actor: string; state: TimelineState; at?: string | null; comment?: string | null
}) {
  const dot = state === 'done' ? <CheckCircle2 className="h-4 w-4 text-good" />
    : state === 'rejected' ? <XCircle className="h-4 w-4 text-danger" />
    : state === 'current' ? <span className="block h-3 w-3 rounded-full bg-amber-400" />
    : <span className="block h-3 w-3 rounded-full bg-slate-200" />
  return (
    <li className="grid grid-cols-[24px_180px_1fr_180px] items-start gap-3 px-5 py-3">
      <span className="mt-0.5 inline-flex h-5 w-5 items-center justify-center">{dot}</span>
      <span className="text-sm font-medium text-ink">{label}</span>
      <div className="text-sm text-ink-muted">
        <div>{actor}</div>
        {comment && <div className="mt-1 italic text-ink-faint">「{comment}」</div>}
      </div>
      <span className="text-right font-mono text-[11px] text-ink-faint">{at ? formatDate(at) : '—'}</span>
    </li>
  )
}

function statusKind(s: WFH_V6_Status): StatusKind {
  switch (s) {
    case 'PendingManager':   return 'pending'
    case 'PendingSenior':    return 'pending'
    case 'ResubmitRequired': return 'returned'
    case 'Completed':        return 'closed'
    case 'Cancelled':        return 'cancelled'
  }
}

/** Map the case status → index into FORMS.WFH.steps
 *  (apply 0 · manager 1 · senior 2 · close 3). */
function activeStepFor(status: WFH_V6_Status): number {
  switch (status) {
    case 'PendingManager':   return 1
    case 'PendingSenior':    return 2
    case 'ResubmitRequired': return 1
    case 'Completed':        return 3
    case 'Cancelled':        return 1
  }
}

/**
 * Map per-flow status → BPMN node ids (spec.flow.nodes ids). The senior
 * branch (approval_senior / end_senior_approved) only lights up when the
 * case actually walked it — inferred from the presence of a senior
 * decision. Skipped branches stay uncoloured.
 */
function deriveTrail(c: WFH_V6_CaseResponse): { completed: string[]; current: string | null } {
  const tookSenior = !!c.seniorDecision || c.status === 'PendingSenior'
  switch (c.status) {
    case 'PendingManager':
      return { completed: ['start_1', 'task_apply'], current: 'approval_manager' }
    case 'PendingSenior':
      return { completed: ['start_1', 'task_apply', 'approval_manager', 'gateway_days'], current: 'approval_senior' }
    case 'ResubmitRequired':
      return { completed: ['start_1', 'task_apply'], current: 'task_apply' }
    case 'Completed':
      return tookSenior
        ? { completed: ['start_1', 'task_apply', 'approval_manager', 'gateway_days', 'approval_senior', 'end_senior_approved'], current: null }
        : { completed: ['start_1', 'task_apply', 'approval_manager', 'gateway_days', 'end_approved'], current: null }
    case 'Cancelled':
      return { completed: ['start_1', 'task_apply'], current: null }
  }
}

function formatDate(iso: string | null | undefined): string {
  if (!iso) return '—'
  const d = new Date(iso)
  if (Number.isNaN(d.getTime())) return iso
  const pad = (n: number) => String(n).padStart(2, '0')
  return `${d.getFullYear()}/${pad(d.getMonth() + 1)}/${pad(d.getDate())} ${pad(d.getHours())}:${pad(d.getMinutes())}`
}
