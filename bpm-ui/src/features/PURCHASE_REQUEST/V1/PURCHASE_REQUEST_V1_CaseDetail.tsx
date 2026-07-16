import { useCallback, useEffect, useMemo, useState } from 'react'
import { ArrowLeft, CheckCircle2, ExternalLink, RotateCcw, Workflow as WorkflowIcon, XCircle } from 'lucide-react'
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
import { roleLabel } from '@/lib/roleLabels'
import type { CaseDetailProps } from '@/features/registry'
import PURCHASE_REQUEST_V1_BpmnXml from './PURCHASE_REQUEST_V1.bpmn.xml?raw'
import { zhStatus } from './PURCHASE_REQUEST_V1_shared'
import type {
  PURCHASE_REQUEST_V1_CaseResponse,
  PURCHASE_REQUEST_V1_InvoiceDto,
  PURCHASE_REQUEST_V1_Status,
} from './PURCHASE_REQUEST_V1_types'

/**
 * Read-mostly detail page for a PURCHASE_REQUEST V1 case. Renders
 * status / submitter comment / invoices grid / approval timeline,
 * plus a sticky ActionFooter scoped to the current viewer's role:
 *
 *  - PendingDeptHead + isDeptHeadAssignee → Reject / Approve
 *  - PendingFinance  + isFinanceAssignee  → Reject / Approve
 *  - ResubmitRequired + isSubmitter       → Edit &amp; resubmit
 *                                             (navigates to /apply/PURCHASE_REQUEST?resubmit=)
 */
export function PURCHASE_REQUEST_V1_CaseDetail({ caseId }: CaseDetailProps) {
  const navigate = useNavigate()
  const [data, setData] = useState<PURCHASE_REQUEST_V1_CaseResponse | null>(null)
  const [viewerUserId, setViewerUserId] = useState<string | null>(null)
  const [loading, setLoading] = useState(true)
  const [error, setError] = useState<string | null>(null)
  const [actionPending, setActionPending] = useState(false)
  const [actionError, setActionError] = useState<string | null>(null)
  const [bpmnOpen, setBpmnOpen] = useState(false)
  const [approvalComment, setApprovalComment] = useState('')

  // Whether this case is in the viewer's server-computed "pending on me" set.
  // The finance step is a shared role queue (role code, no assignee user id),
  // so the userId equality check below can never match there — the /pending
  // set is the server truth for role-queue steps (inheritance-aware).
  const [canActOnCase, setCanActOnCase] = useState(false)

  const load = useCallback(async () => {
    try {
      const [res, pendingRes] = await Promise.all([
        apiFetch(`/api/purchase-request/v1/${caseId}`),
        apiFetch(`/api/purchase-request/v1/pending`),
      ])
      if (!res.ok) throw new Error(`HTTP ${res.status}`)
      const body = (await res.json()) as PURCHASE_REQUEST_V1_CaseResponse
      setData(body)
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
  // The viewer may act on the case if they are the current assignee, an active
  // delegate of the current assignee (the /pending set is not delegate-expanded,
  // so the explicit check stays), OR the case is in their server-computed
  // pending set — which is what grants the role-queue finance step.
  const isCurrentAssignee = (!!data && !!viewerUserId && !!data.currentAssigneeUserId &&
    (data.currentAssigneeUserId === viewerUserId || delegatedFor.includes(data.currentAssigneeUserId)))
    || canActOnCase
  const isSubmitter       = !!data && !!viewerUserId && data.submitterUserId === viewerUserId
  const trail = useMemo(() => (data ? deriveTrail(data.status) : null), [data])

  const postDecision = useCallback(async (path: 'dept-head-decision' | 'finance-decision', approve: boolean) => {
    setActionPending(true); setActionError(null)
    try {
      const res = await apiFetch(`/api/purchase-request/v1/${caseId}/${path}`, {
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
      const res = await apiFetch(`/api/purchase-request/v1/${caseId}/cancel`, { method: 'POST' })
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
    if (isCurrentAssignee && data.status === 'PendingDeptHead') {
      actions.push(
        { id: 'reject',  label: '退件 / Reject',  variant: 'destructive', pending: actionPending, onClick: () => postDecision('dept-head-decision', false) },
        { id: 'approve', label: '核准 / Approve', variant: 'primary',     pending: actionPending, onClick: () => postDecision('dept-head-decision', true)  },
      )
    }
    if (isCurrentAssignee && data.status === 'PendingFinance') {
      actions.push(
        { id: 'reject',  label: '退件 / Reject',  variant: 'destructive', pending: actionPending, onClick: () => postDecision('finance-decision', false) },
        { id: 'approve', label: '核准 / Approve', variant: 'primary',     pending: actionPending, onClick: () => postDecision('finance-decision', true)  },
      )
    }
    if (isSubmitter && data.status === 'ResubmitRequired') {
      actions.push({
        id: 'resubmit', confirm: false,
        label: <span className="inline-flex items-center gap-1"><RotateCcw className="h-3.5 w-3.5" />修正後重新送出</span>,
        variant: 'primary',
        pending: actionPending,
        onClick: () => navigate(`/apply/PURCHASE_REQUEST?resubmit=${data.id}`),
      })
    }
    // Submitter may withdraw their own case while it is still in flight.
    if (isSubmitter && data.status !== 'Completed' && data.status !== 'Cancelled') {
      actions.push({ id: 'withdraw', label: '撤回申請', variant: 'destructive', pending: actionPending, confirm: { titleZh: '撤回申請？', description: '撤回後無法復原。', confirmText: '確認撤回' }, onClick: () => postCancel() })
    }
    return actions
  }, [data, isCurrentAssignee, isSubmitter, actionPending, postDecision, postCancel, navigate])

  const footerHint = (() => {
    if (!data) return null
    if (actionError) return <span className="text-danger">{actionError}</span>
    if (isCurrentAssignee && (data.status === 'PendingDeptHead' || data.status === 'PendingFinance')) {
      return '請審閱簽核意見後決定'
    }
    if (isSubmitter && data.status === 'ResubmitRequired') {
      return '請依照退件意見修正後重新送出'
    }
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
            採購申請案件 <span className="ml-2 text-base font-medium text-ink-muted">· PURCHASE_REQUEST V1</span>
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
          <FlowStateBanner flowCode="PURCHASE_REQUEST" flowVersion={1} />

          <SectionCard className="!p-0">
            <div className="bg-slate-50 px-4 py-2">
              <Stepper steps={FORMS.PURCHASE_REQUEST.steps} activeStep={activeStepFor(data.status)} withZh />
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
              <Stat label="目前指派給" value={<span>{data.currentAssigneeDisplayName ?? (data.currentAssigneeRoleCode ? `待 ${roleLabel(data.currentAssigneeRoleCode)}` : '—')}</span>} />
              <Stat label="送出時間" value={<span className="font-mono">{formatDate(data.submittedAt)}</span>} />
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
            <SectionTitle>採購明細 / Invoices · {data.invoices.length}</SectionTitle>
            <div className="space-y-4 px-5 py-4">
              {data.invoices.map((inv, i) => (
                <InvoiceReadCard key={i} index={i} value={inv} />
              ))}
              {(() => {
                const totals = data.invoices.reduce<Record<string, number>>((acc, inv) => {
                  const amt = parseFloat((inv.amount ?? '').toString())
                  if (!Number.isNaN(amt) && amt !== 0) {
                    const c = (inv.currency ?? '').trim() || '—'
                    acc[c] = (acc[c] ?? 0) + amt
                  }
                  return acc
                }, {})
                const entries = Object.entries(totals)
                if (entries.length === 0) return null
                return (
                  <div className="flex flex-wrap items-center justify-end gap-x-6 gap-y-1 rounded-md border border-rule bg-slate-50 px-4 py-3">
                    <span className="text-sm font-semibold text-ink">合計 / Total</span>
                    {entries.map(([ccy, sum]) => (
                      <span key={ccy} className="font-mono text-base font-bold text-ink">
                        {ccy === '—' ? '' : `${ccy} `}{sum.toLocaleString('en-US', { maximumFractionDigits: 2 })}
                      </span>
                    ))}
                  </div>
                )
              })()}
            </div>
          </SectionCard>

          <SectionCard>
            <SectionTitle>簽核時序 / Approval Timeline</SectionTitle>
            <ol className="divide-y divide-slate-100">
              <TimelineRow
                label="員工申請 / Submit"
                actor={data.submitterDisplayName ?? '—'}
                state="done"
                at={data.submittedAt}
              />
              <TimelineRow
                label="部門主管核准 / Dept Head"
                actor={data.deptHeadDecision?.displayName ?? '—'}
                state={data.deptHeadDecision?.approved === true ? 'done'
                  : data.deptHeadDecision?.approved === false ? 'rejected'
                  : data.status === 'PendingDeptHead' ? 'current'
                  : 'idle'}
                at={data.deptHeadDecision?.decidedAt}
                comment={data.deptHeadDecision?.comment}
              />
              <TimelineRow
                label="財務核准 / Finance"
                actor={data.financeDecision?.displayName ?? '—'}
                state={data.financeDecision?.approved === true ? 'done'
                  : data.financeDecision?.approved === false ? 'rejected'
                  : data.status === 'PendingFinance' ? 'current'
                  : 'idle'}
                at={data.financeDecision?.decidedAt}
                comment={data.financeDecision?.comment}
              />
              <TimelineRow
                label="結案 / Closed"
                actor={data.status === 'Completed' ? '系統' : data.status === 'Cancelled' ? '申請人撤回' : '—'}
                state={data.status === 'Completed' ? 'done' : data.status === 'Cancelled' ? 'rejected' : 'idle'}
                at={data.completedAt}
              />
            </ol>
          </SectionCard>

          {isCurrentAssignee && (data.status === 'PendingDeptHead' || data.status === 'PendingFinance') && (
            <SectionCard>
              <SectionTitle>您的決定 / Your decision</SectionTitle>
              <div className="space-y-3 px-5 py-4">
                <Field label="簽核意見 / Comment" hint="退件時建議填寫原因">
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
        formLabel={`${FORMS.PURCHASE_REQUEST.code} — ${FORMS.PURCHASE_REQUEST.label}`}
        steps={FORMS.PURCHASE_REQUEST.steps}
        activeStep={0}
        ownerByStep={FORMS.PURCHASE_REQUEST.ownerByStep}
        bpmnXml={PURCHASE_REQUEST_V1_BpmnXml}
        completedNodes={trail?.completed}
        currentNode={trail?.current}
      />

      <ActionFooter hint={footerHint} actions={footerActions} />
    </div>
  )
}

function InvoiceReadCard({ index, value }: { index: number; value: PURCHASE_REQUEST_V1_InvoiceDto }) {
  const amt = parseFloat((value.amount ?? '').toString())
  const amountDisplay = !Number.isNaN(amt)
    ? `${value.currency ? `${value.currency} ` : ''}${amt.toLocaleString('en-US', { maximumFractionDigits: 2 })}`
    : value.amount
  return (
    <div className="overflow-hidden rounded-md border border-rule">
      <div className="flex flex-wrap items-center gap-x-3 gap-y-1 bg-slate-700 px-4 py-2 text-sm text-white">
        <span className="font-semibold">Invoice #{index + 1}</span>
        {value.invoiceNo && <span className="text-slate-300">· {value.invoiceNo}</span>}
        <span className="ml-auto font-mono text-xs text-slate-300">{value.invoiceDate || '—'}</span>
      </div>
      <div className="grid grid-cols-12 gap-3 bg-card p-4 text-sm">
        <ReadField label="Charge To" value={value.chargeTo} className="col-span-6" />
        <ReadField label="Project" value={value.project} className="col-span-6" />
        <ReadField label="Category" value={value.category} className="col-span-6" />
        <ReadField label="Currency" value={value.currency} className="col-span-3" />
        <ReadField label="Amount" value={amountDisplay} className="col-span-3" mono />
        <ReadField label="Description" value={value.description} className="col-span-12" />
        <div className="col-span-6">
          <p className="mb-1 text-[11px] font-semibold uppercase tracking-wider text-ink-muted">Attachment</p>
          {value.attachmentFileId
            ? (
                <AuthedFileLink
                  id={value.attachmentFileId}
                  className="inline-flex items-center gap-1 text-primary hover:underline"
                >
                  下載 <ExternalLink className="h-3.5 w-3.5" />
                </AuthedFileLink>
              )
            : <span className="text-ink-faint">—</span>}
        </div>
      </div>
    </div>
  )
}

function ReadField({ label, value, className, mono }: { label: string; value: string | null; className?: string; mono?: boolean }) {
  return (
    <div className={className}>
      <p className="text-[11px] font-semibold uppercase tracking-wider text-ink-muted">{label}</p>
      <p className={`mt-1 text-ink${mono ? ' font-mono' : ''}`}>{value || <span className="font-sans text-ink-faint">—</span>}</p>
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

function statusKind(s: PURCHASE_REQUEST_V1_Status): StatusKind {
  switch (s) {
    case 'PendingDeptHead':   return 'pending'
    case 'PendingFinance':    return 'fin_review'
    case 'ResubmitRequired':  return 'returned'
    case 'Completed':         return 'closed'
    case 'Cancelled':         return 'cancelled'
  }
}

/** Map the case status → index into FORMS.PURCHASE_REQUEST.steps for the
 *  header stepper (apply 0 · dept_head 1 · finance 2 · closed 3). */
function activeStepFor(status: PURCHASE_REQUEST_V1_Status): number {
  switch (status) {
    case 'PendingDeptHead':   return 1
    case 'PendingFinance':    return 2
    case 'ResubmitRequired':  return 1
    case 'Completed':         return 3
    case 'Cancelled':         return 1
  }
}

/**
 * Map per-flow status → BPMN node ids. The reject branches loop back
 * to <c>task_apply</c>, so <c>ResubmitRequired</c> highlights
 * <c>task_apply</c> as current with only the start completed.
 */
function deriveTrail(status: PURCHASE_REQUEST_V1_Status): { completed: string[]; current: string | null } {
  switch (status) {
    case 'PendingDeptHead':
      return { completed: ['start_1', 'task_apply'], current: 'approval_dept_head' }
    case 'PendingFinance':
      return { completed: ['start_1', 'task_apply', 'approval_dept_head', 'gateway_dept'], current: 'approval_finance' }
    case 'ResubmitRequired':
      return { completed: ['start_1'], current: 'task_apply' }
    case 'Completed':
      return {
        completed: ['start_1', 'task_apply', 'approval_dept_head', 'gateway_dept',
                    'approval_finance', 'gateway_finance', 'end_1'],
        current: null,
      }
    case 'Cancelled':
      return { completed: ['start_1'], current: null }
  }
}

function formatDate(iso: string | null | undefined): string {
  if (!iso) return '—'
  const d = new Date(iso)
  if (Number.isNaN(d.getTime())) return iso
  const pad = (n: number) => String(n).padStart(2, '0')
  return `${d.getFullYear()}/${pad(d.getMonth() + 1)}/${pad(d.getDate())} ${pad(d.getHours())}:${pad(d.getMinutes())}`
}
