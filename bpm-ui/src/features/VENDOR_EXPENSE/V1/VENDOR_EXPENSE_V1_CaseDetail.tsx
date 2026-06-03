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
import { apiFetch, getJwt } from '@/lib/apiFetch'
import { decodeJwt } from '@/lib/jwt'
import { FORMS } from '@/lib/workflow'
import type { CaseDetailProps } from '@/features/registry'
import VENDOR_EXPENSE_V1_BpmnXml from './VENDOR_EXPENSE_V1.bpmn.xml?raw'
import { zhStatus } from './VENDOR_EXPENSE_V1_shared'
import type {
  VENDOR_EXPENSE_V1_CaseResponse,
  VENDOR_EXPENSE_V1_InvoiceDto,
  VENDOR_EXPENSE_V1_Status,
} from './VENDOR_EXPENSE_V1_types'

type DecisionPath = 'supervisor-decision' | 'procurement-decision' | 'sign-decision'

/**
 * Read-mostly detail page for a VENDOR_EXPENSE V1 case. Renders status /
 * vendor / submit note / invoices grid / 3-stage approval timeline, plus
 * a sticky ActionFooter scoped to the current viewer's role:
 *
 *  - PendingSupervisor  + assignee → Reject / Approve (supervisor-decision)
 *  - PendingProcurement + assignee → Reject / Approve (procurement-decision)
 *  - PendingSign        + assignee → Reject / Approve (sign-decision)
 *  - ResubmitRequired   + submitter → Edit & resubmit
 */
export function VENDOR_EXPENSE_V1_CaseDetail({ caseId }: CaseDetailProps) {
  const navigate = useNavigate()
  const [data, setData] = useState<VENDOR_EXPENSE_V1_CaseResponse | null>(null)
  const [viewerUserId, setViewerUserId] = useState<string | null>(null)
  const [loading, setLoading] = useState(true)
  const [error, setError] = useState<string | null>(null)
  const [actionPending, setActionPending] = useState(false)
  const [actionError, setActionError] = useState<string | null>(null)
  const [bpmnOpen, setBpmnOpen] = useState(false)
  const [approvalComment, setApprovalComment] = useState('')

  const load = useCallback(async () => {
    try {
      const res = await apiFetch(`/api/vendor-expense/v1/${caseId}`)
      if (!res.ok) throw new Error(`HTTP ${res.status}`)
      const body = (await res.json()) as VENDOR_EXPENSE_V1_CaseResponse
      setData(body)
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

  const isCurrentAssignee = !!data && !!viewerUserId && data.currentAssigneeUserId === viewerUserId
  const isSubmitter       = !!data && !!viewerUserId && data.submitterUserId === viewerUserId
  const trail = useMemo(() => (data ? deriveTrail(data.status) : null), [data])

  const postDecision = useCallback(async (path: DecisionPath, approve: boolean) => {
    setActionPending(true); setActionError(null)
    try {
      const res = await apiFetch(`/api/vendor-expense/v1/${caseId}/${path}`, {
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
    if (!window.confirm('確定要撤回此申請？撤回後無法復原。')) return
    setActionPending(true); setActionError(null)
    try {
      const res = await apiFetch(`/api/vendor-expense/v1/${caseId}/cancel`, { method: 'POST' })
      if (!res.ok) throw new Error(await res.text() || `HTTP ${res.status}`)
      await load()
    } catch (e) {
      setActionError(e instanceof Error ? e.message : String(e))
    } finally {
      setActionPending(false)
    }
  }, [caseId, load])

  const decisionPath = data ? PATH_FOR_STATUS[data.status] : undefined

  const footerActions: ActionFooterItem[] = useMemo(() => {
    if (!data) return []
    const actions: ActionFooterItem[] = []
    if (isCurrentAssignee && decisionPath) {
      actions.push(
        { id: 'reject',  label: '退回 / Reject',  variant: 'destructive', pending: actionPending, onClick: () => postDecision(decisionPath, false) },
        { id: 'approve', label: '核准 / Approve', variant: 'primary',     pending: actionPending, onClick: () => postDecision(decisionPath, true)  },
      )
    }
    if (isSubmitter && data.status === 'ResubmitRequired') {
      actions.push({
        id: 'resubmit',
        label: <span className="inline-flex items-center gap-1"><RotateCcw className="h-3.5 w-3.5" />修正後重新送出</span>,
        variant: 'primary',
        pending: actionPending,
        onClick: () => navigate(`/apply/VENDOR_EXPENSE?resubmit=${data.id}`),
      })
    }
    // Submitter may withdraw their own case while it is still in flight.
    if (isSubmitter && data.status !== 'Completed' && data.status !== 'Cancelled') {
      actions.push({ id: 'withdraw', label: '撤回申請', variant: 'destructive', pending: actionPending, onClick: () => postCancel() })
    }
    return actions
  }, [data, isCurrentAssignee, isSubmitter, decisionPath, actionPending, postDecision, postCancel, navigate])

  const footerHint = (() => {
    if (!data) return null
    if (actionError) return <span className="text-danger">{actionError}</span>
    if (isCurrentAssignee && decisionPath) return '請審閱後決定'
    if (isSubmitter && data.status === 'ResubmitRequired') return '請依照退回意見修正後重新送出'
    return null
  })()

  return (
    <div className="mx-auto max-w-screen-lg space-y-4 p-6 pb-24">
      <div className="flex items-center gap-3">
        <Button variant="outline" size="sm" onClick={() => navigate('/')}>
          <ArrowLeft className="h-3.5 w-3.5" /> 返回
        </Button>
        <div className="min-w-0">
          <h1 className="truncate text-lg font-bold text-ink">
            採購申請案件 <span className="ml-2 text-base font-medium text-ink-muted">· VENDOR_EXPENSE V1</span>
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
          <FlowStateBanner flowCode="VENDOR_EXPENSE" flowVersion={1} />

          <SectionCard className="!p-0">
            <div className="bg-slate-50 px-4 py-2">
              <Stepper steps={FORMS.VENDOR_EXPENSE.steps} activeStep={activeStepFor(data.status)} withZh />
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
              <Stat label="廠商" value={<span>{data.vendor || '—'}</span>} />
            </div>
            {data.roundCount > 1 && (
              <div className="border-t border-rule px-5 py-3">
                <InfoBanner>本案件為第 <strong>{data.roundCount}</strong> 次送出（先前曾被退回 / 補件）。</InfoBanner>
              </div>
            )}
          </SectionCard>

          <SectionCard>
            <SectionTitle>送出說明 / Submission Note</SectionTitle>
            <div className="px-5 py-4 text-sm text-ink">
              {data.submitterComment || <span className="text-ink-faint">—</span>}
            </div>
          </SectionCard>

          <SectionCard>
            <SectionTitle>Invoices · {data.invoices.length}</SectionTitle>
            <div className="space-y-3 px-5 py-4">
              {data.invoices.map((inv, i) => (
                <InvoiceReadCard key={i} index={i} value={inv} />
              ))}
            </div>
          </SectionCard>

          <SectionCard>
            <SectionTitle>簽核時序 / Approval Timeline</SectionTitle>
            <ol className="divide-y divide-slate-100">
              <TimelineRow
                label="填寫採購申請 / Submit"
                actor={data.submitterDisplayName ?? '—'}
                state="done"
                at={data.submittedAt}
              />
              <TimelineRow
                label="主管審核 / Supervisor"
                actor={data.supervisorDecision?.displayName ?? '—'}
                state={decisionState(data.supervisorDecision?.approved, data.status === 'PendingSupervisor')}
                at={data.supervisorDecision?.decidedAt}
                comment={data.supervisorDecision?.comment}
              />
              <TimelineRow
                label="採購審定 / Procurement"
                actor={data.procurementDecision?.displayName ?? '—'}
                state={decisionState(data.procurementDecision?.approved, data.status === 'PendingProcurement')}
                at={data.procurementDecision?.decidedAt}
                comment={data.procurementDecision?.comment}
              />
              <TimelineRow
                label="簽核 / Sign"
                actor={data.signDecision?.displayName ?? '—'}
                state={decisionState(data.signDecision?.approved, data.status === 'PendingSign')}
                at={data.signDecision?.decidedAt}
                comment={data.signDecision?.comment}
              />
              <TimelineRow
                label="結案 / Closed"
                actor={data.status === 'Completed' ? '系統' : data.status === 'Cancelled' ? '申請人撤回' : '—'}
                state={data.status === 'Completed' ? 'done' : data.status === 'Cancelled' ? 'rejected' : 'idle'}
                at={data.completedAt}
              />
            </ol>
          </SectionCard>

          {isCurrentAssignee && decisionPath && (
            <SectionCard>
              <SectionTitle>您的決定 / Your decision</SectionTitle>
              <div className="space-y-3 px-5 py-4">
                <Field label="簽核意見 / Comment" hint="退回時建議填寫原因">
                  <Textarea
                    rows={3}
                    value={approvalComment}
                    onChange={e => setApprovalComment(e.target.value)}
                    disabled={actionPending}
                  />
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
        formLabel={`${FORMS.VENDOR_EXPENSE.code} — ${FORMS.VENDOR_EXPENSE.label}`}
        steps={FORMS.VENDOR_EXPENSE.steps}
        activeStep={0}
        ownerByStep={FORMS.VENDOR_EXPENSE.ownerByStep}
        bpmnXml={VENDOR_EXPENSE_V1_BpmnXml}
        completedNodes={trail?.completed}
        currentNode={trail?.current}
      />

      <ActionFooter hint={footerHint} actions={footerActions} />
    </div>
  )
}

function InvoiceReadCard({ index, value }: { index: number; value: VENDOR_EXPENSE_V1_InvoiceDto }) {
  return (
    <div className="rounded-md border border-rule bg-card p-4">
      <div className="mb-3 flex items-center justify-between">
        <p className="text-sm font-semibold text-ink">#{index + 1} · {value.invoiceNo || '—'}</p>
        <p className="font-mono text-xs text-ink-faint">{value.invoiceDate || '—'}</p>
      </div>
      <div className="grid grid-cols-12 gap-3 text-sm">
        <ReadField label="Charge To" value={value.chargeTo} className="col-span-6" />
        <ReadField label="Project" value={value.project} className="col-span-6" />
        <ReadField label="Category" value={value.category} className="col-span-6" />
        <ReadField label="Currency" value={value.currency} className="col-span-3" />
        <ReadField label="Amount" value={value.amount} className="col-span-3" />
        <ReadField label="Description" value={value.description} className="col-span-12" />
      </div>
    </div>
  )
}

function ReadField({ label, value, className }: { label: string; value: string | null; className?: string }) {
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
  label: string
  actor: string
  state: TimelineState
  at?: string | null
  comment?: string | null
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

const PATH_FOR_STATUS: Partial<Record<VENDOR_EXPENSE_V1_Status, DecisionPath>> = {
  PendingSupervisor:  'supervisor-decision',
  PendingProcurement: 'procurement-decision',
  PendingSign:        'sign-decision',
}

function statusKind(s: VENDOR_EXPENSE_V1_Status): StatusKind {
  switch (s) {
    case 'PendingSupervisor':  return 'pending'
    case 'PendingProcurement': return 'fin_review'
    case 'PendingSign':        return 'it_spec_review'
    case 'ResubmitRequired':   return 'returned'
    case 'Completed':          return 'closed'
    case 'Cancelled':          return 'cancelled'
  }
}

/** Map the case status → index into FORMS.VENDOR_EXPENSE.steps for the header
 *  stepper (apply 0 · supervisor 1 · procurement 2 · sign 3 · closed 4). */
function activeStepFor(status: VENDOR_EXPENSE_V1_Status): number {
  switch (status) {
    case 'PendingSupervisor':  return 1
    case 'PendingProcurement': return 2
    case 'PendingSign':        return 3
    case 'ResubmitRequired':   return 1
    case 'Completed':          return 4
    case 'Cancelled':          return 1
  }
}

/**
 * Map per-flow status → BPMN node ids (spec.flow.nodes[].id). The reject
 * branches loop back to <c>task_fill</c>, so <c>ResubmitRequired</c>
 * highlights <c>task_fill</c> as current with only the start completed.
 */
function deriveTrail(status: VENDOR_EXPENSE_V1_Status): { completed: string[]; current: string | null } {
  switch (status) {
    case 'PendingSupervisor':
      return { completed: ['start_1', 'task_fill'], current: 'approval_supervisor' }
    case 'PendingProcurement':
      return { completed: ['start_1', 'task_fill', 'approval_supervisor', 'gateway_supervisor'], current: 'approval_procurement' }
    case 'PendingSign':
      return {
        completed: ['start_1', 'task_fill', 'approval_supervisor', 'gateway_supervisor',
                    'approval_procurement', 'gateway_procurement'],
        current: 'approval_sign',
      }
    case 'ResubmitRequired':
      return { completed: ['start_1'], current: 'task_fill' }
    case 'Cancelled':
      return { completed: ['start_1'], current: null }
    case 'Completed':
      return {
        completed: ['start_1', 'task_fill', 'approval_supervisor', 'gateway_supervisor',
                    'approval_procurement', 'gateway_procurement', 'approval_sign', 'end_1'],
        current: null,
      }
  }
}

function formatDate(iso: string | null | undefined): string {
  if (!iso) return '—'
  const d = new Date(iso)
  if (Number.isNaN(d.getTime())) return iso
  const pad = (n: number) => String(n).padStart(2, '0')
  return `${d.getFullYear()}/${pad(d.getMonth() + 1)}/${pad(d.getDate())} ${pad(d.getHours())}:${pad(d.getMinutes())}`
}
