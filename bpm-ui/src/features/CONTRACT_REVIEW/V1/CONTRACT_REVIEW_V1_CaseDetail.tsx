import { useCallback, useEffect, useMemo, useState } from 'react'
import { ArrowLeft, CheckCircle2, RotateCcw, Workflow as WorkflowIcon, XCircle } from 'lucide-react'
import { useNavigate } from 'react-router-dom'

import { Button } from '@/components/ui/button'
import { SectionCard, SectionTitle } from '@/components/ui/card'
import { StatusBadge, type StatusKind } from '@/components/ui/badge'
import { Field, InfoBanner, Textarea } from '@/components/ui/form'
import { AuthedFileLink } from '@/components/ui/FilePicker'
import { ActionFooter, type ActionFooterItem } from '@/components/ui/action-footer/ActionFooter'
import { FlowStateBanner } from '@/components/ui/flow-state-banner/FlowStateBanner'
import { CaseToolbar } from '@/components/CaseToolbar'
import { Stepper } from '@/components/Stepper'
import { BpmnView } from '@/components/BpmnView'
import { ParallelApprovalPanel, type ParallelSlotView } from '@/components/ParallelApprovalPanel'
import { apiFetch, getJwt } from '@/lib/apiFetch'
import { decodeJwt, jwtRoles } from '@/lib/jwt'
import { FORMS } from '@/lib/workflow'
import type { CaseDetailProps } from '@/features/registry'
import CrBpmnXml from './CONTRACT_REVIEW_V1.bpmn.xml?raw'
import type { CrCaseResponse, CrSlotView, CrStatus } from './CONTRACT_REVIEW_V1_types'

const { LEGAL, FINANCE, LEGAL_MANAGER } = { LEGAL: 'LEGAL', FINANCE: 'FINANCE', LEGAL_MANAGER: 'LEGAL_MANAGER' }

/**
 * Read-mostly detail page for a CONTRACT_REVIEW V1 case (合約審查). Renders the
 * 並簽 checklist (ParallelApprovalPanel) + a 簽核時序 timeline + multi-node BPMN
 * highlight. Decision footer is role-scoped:
 *  - PendingParallelReview + viewer holds a pending slot's role → 核准 / 退件
 *  - PendingLegalManager   + viewer holds LEGAL_MANAGER          → 核准 / 退回
 *  - ResubmitRequired      + isSubmitter                         → 修正後重新送審
 *  - any non-terminal       + isSubmitter                         → 撤回申請
 */
export function CONTRACT_REVIEW_V1_CaseDetail({ caseId, persona }: CaseDetailProps) {
  const navigate = useNavigate()
  const [data, setData] = useState<CrCaseResponse | null>(null)
  const [viewerUserId, setViewerUserId] = useState<string | null>(null)
  const [viewerRoles, setViewerRoles] = useState<string[]>([])
  const [loading, setLoading] = useState(true)
  const [error, setError] = useState<string | null>(null)
  const [actionPending, setActionPending] = useState(false)
  const [actionError, setActionError] = useState<string | null>(null)
  const [bpmnOpen, setBpmnOpen] = useState(false)
  const [comment, setComment] = useState('')

  const load = useCallback(async () => {
    try {
      const res = await apiFetch(`/api/contract-review/v1/${caseId}`)
      if (!res.ok) throw new Error(`HTTP ${res.status}`)
      setData((await res.json()) as CrCaseResponse)
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
    setViewerRoles(jwtRoles(decoded).map(r => r.toUpperCase()))
  }, [])

  const isSubmitter = !!data && !!viewerUserId && data.submitterUserId === viewerUserId
  const isAdmin = persona === 'admin'
  const holdsRole = useCallback((code?: string | null) =>
    !!code && (isAdmin || viewerRoles.includes(code.toUpperCase())), [isAdmin, viewerRoles])

  const myPendingSlot = useMemo<CrSlotView | undefined>(
    () => data?.review?.slots.find(s => s.state === 'pending' && holdsRole(s.roleCode)),
    [data, holdsRole])

  const postSlotDecision = useCallback(async (slotId: string, approve: boolean) => {
    if (!approve && !comment.trim()) { setActionError('退件時請填寫退回意見。'); return }
    setActionPending(true); setActionError(null)
    try {
      const res = await apiFetch(`/api/contract-review/v1/${caseId}/slots/${slotId}/decision`, {
        method: 'POST', headers: { 'Content-Type': 'application/json' },
        body: JSON.stringify({ approve, comment: comment.trim() || null }),
      })
      if (!res.ok) throw new Error(res.status === 403 ? '你不是這個並簽關卡的指定審查人' : (await res.text() || `HTTP ${res.status}`))
      setComment(''); await load()
    } catch (e) { setActionError(e instanceof Error ? e.message : String(e)) }
    finally { setActionPending(false) }
  }, [caseId, comment, load])

  const postLegalManager = useCallback(async (approve: boolean) => {
    setActionPending(true); setActionError(null)
    try {
      const res = await apiFetch(`/api/contract-review/v1/${caseId}/legal-manager-decision`, {
        method: 'POST', headers: { 'Content-Type': 'application/json' },
        body: JSON.stringify({ approve, comment: comment.trim() || null }),
      })
      if (!res.ok) throw new Error(res.status === 403 ? '只有法務主管可以定案歸檔' : (await res.text() || `HTTP ${res.status}`))
      setComment(''); await load()
    } catch (e) { setActionError(e instanceof Error ? e.message : String(e)) }
    finally { setActionPending(false) }
  }, [caseId, comment, load])

  const postCancel = useCallback(async () => {
    setActionPending(true); setActionError(null)
    try {
      const res = await apiFetch(`/api/contract-review/v1/${caseId}/cancel`, { method: 'POST' })
      if (!res.ok) throw new Error(await res.text() || `HTTP ${res.status}`)
      await load()
    } catch (e) { setActionError(e instanceof Error ? e.message : String(e)) }
    finally { setActionPending(false) }
  }, [caseId, load])

  const footerActions: ActionFooterItem[] = useMemo(() => {
    if (!data) return []
    const actions: ActionFooterItem[] = []
    if (data.status === 'PendingParallelReview' && myPendingSlot) {
      actions.push(
        { id: 'reject', label: '退件 / Reject', variant: 'destructive', pending: actionPending, onClick: () => postSlotDecision(myPendingSlot.slotId, false) },
        { id: 'approve', label: '核准 / Approve', variant: 'primary', pending: actionPending, onClick: () => postSlotDecision(myPendingSlot.slotId, true) },
      )
    }
    if (data.status === 'PendingLegalManager' && holdsRole(LEGAL_MANAGER)) {
      actions.push(
        { id: 'mgr-reject', label: '退回 / Send back', variant: 'destructive', pending: actionPending, onClick: () => postLegalManager(false) },
        { id: 'mgr-approve', label: '定案歸檔 / Archive', variant: 'primary', pending: actionPending, onClick: () => postLegalManager(true) },
      )
    }
    if (isSubmitter && data.status === 'ResubmitRequired') {
      actions.push({
        id: 'resubmit', confirm: false,
        label: <span className="inline-flex items-center gap-1"><RotateCcw className="h-3.5 w-3.5" />修正後重新送審</span>,
        variant: 'primary', pending: actionPending,
        onClick: () => navigate(`/apply/CONTRACT_REVIEW?resubmit=${data.id}`),
      })
    }
    if (isSubmitter && data.status !== 'Completed' && data.status !== 'Cancelled') {
      actions.push({
        id: 'withdraw', label: '撤回申請', variant: 'destructive', pending: actionPending,
        confirm: { titleZh: '撤回此合約審查？', description: '撤回後無法復原。', confirmText: '確認撤回' },
        onClick: () => postCancel(),
      })
    }
    return actions
  }, [data, myPendingSlot, holdsRole, isSubmitter, actionPending, postSlotDecision, postLegalManager, postCancel, navigate])

  const footerHint = (() => {
    if (!data) return null
    if (actionError) return <span className="text-danger">{actionError}</span>
    if (data.status === 'PendingParallelReview' && myPendingSlot) return '請審閱合約內容後核准或退件（退件請填意見）'
    if (data.status === 'PendingLegalManager' && holdsRole(LEGAL_MANAGER)) return '雙邊審查已通過，請確認後定案歸檔'
    if (isSubmitter && data.status === 'ResubmitRequired') return '請依退回意見修正後重新送審'
    return null
  })()

  const showDecisionBox =
    !!data && ((data.status === 'PendingParallelReview' && !!myPendingSlot) ||
      (data.status === 'PendingLegalManager' && holdsRole(LEGAL_MANAGER)))

  const trail = useMemo(() => (data ? trailOf(data) : null), [data])
  const legalSlot = data?.review?.slots.find(s => s.roleCode?.toUpperCase() === LEGAL)
  const financeSlot = data?.review?.slots.find(s => s.roleCode?.toUpperCase() === FINANCE)

  const panelSlots: ParallelSlotView[] = (data?.review?.slots ?? []).map(s => ({
    role: s.roleCode ?? undefined,
    name: s.deciderName ?? roleZh(s.roleCode) ?? s.nodeId,
    state: s.state,
    comment: s.comment ?? undefined,
    at: s.at ? formatDate(s.at) : undefined,
  }))

  return (
    <div className="mx-auto max-w-screen-lg space-y-4 p-6 pb-24">
      <div className="flex flex-wrap items-center gap-3">
        <Button variant="outline" size="sm" onClick={() => navigate('/')}>
          <ArrowLeft className="h-3.5 w-3.5" /> 返回
        </Button>
        <div className="order-last w-full min-w-0 md:order-none md:w-auto md:flex-1">
          <h1 className="truncate text-lg font-bold text-ink">
            合約審查案件 <span className="ml-2 text-base font-medium text-ink-muted">· CONTRACT_REVIEW V1</span>
          </h1>
          <p className="font-mono text-[11px] text-ink-faint">{caseId}</p>
        </div>
        <div className="ml-auto flex items-center gap-2">
          <CaseToolbar />
          <Button variant="outline" size="sm" onClick={() => setBpmnOpen(true)}>
            <WorkflowIcon className="h-3.5 w-3.5" /> 檢視流程圖
          </Button>
        </div>
      </div>

      {loading && <SectionCard><div className="px-5 py-10 text-center text-sm text-ink-muted">載入中…</div></SectionCard>}
      {error && !loading && <SectionCard><div className="px-5 py-6 text-sm text-danger">載入失敗：{error}</div></SectionCard>}

      {data && (
        <>
          <FlowStateBanner flowCode="CONTRACT_REVIEW" flowVersion={1} />

          <SectionCard className="!p-0">
            <div className="bg-slate-50 px-4 py-2">
              <Stepper steps={FORMS.CONTRACT_REVIEW.steps} activeStep={activeStepFor(data.status)} withZh />
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
              <Stat label="申請人" value={<span>{data.submitterName ?? '—'}</span>} />
              <Stat label="目前階段" value={<span>{stageLabel(data.status)}</span>} />
              <Stat label="合約金額" value={<span className="font-mono">NT$ {data.amount.toLocaleString('en-US')}</span>} />
            </div>
            {data.currentRound > 1 && (
              <div className="border-t border-rule px-5 py-3">
                <InfoBanner>本案件為第 <strong>{data.currentRound}</strong> 次送審（先前曾被退回並修正）。</InfoBanner>
              </div>
            )}
          </SectionCard>

          <SectionCard>
            <SectionTitle>合約資訊 / Contract</SectionTitle>
            <div className="grid grid-cols-12 gap-3 px-5 py-4 text-sm">
              <ReadField label="對方公司" value={data.counterpartyName} className="col-span-6" />
              <ReadField label="合約金額" value={`NT$ ${data.amount.toLocaleString('en-US')}`} className="col-span-6" />
              <ReadField label="合約標的" value={data.contractSubject} className="col-span-12" />
              <ReadField label="合約起日" value={data.periodStart?.slice(0, 10)} className="col-span-6" />
              <ReadField label="合約迄日" value={data.periodEnd?.slice(0, 10)} className="col-span-6" />
              <div className="col-span-6">
                <p className="text-[11px] font-semibold uppercase tracking-wider text-ink-muted">合約草稿</p>
                <p className="mt-1">
                  {data.draftFileId
                    ? <AuthedFileLink id={data.draftFileId} className="text-blue-600 hover:underline">下載草稿</AuthedFileLink>
                    : <span className="text-ink-faint">—</span>}
                </p>
              </div>
              <ReadField label="備註" value={data.remarks} className="col-span-6" />
              {data.revisionNote && <ReadField label="最近修改說明" value={data.revisionNote} className="col-span-12" />}
            </div>
          </SectionCard>

          <SectionCard>
            <SectionTitle>簽核時序 / Approval Timeline</SectionTitle>
            <ol className="divide-y divide-slate-100">
              <TimelineRow label="申請送審 / Submit" actor={data.submitterName ?? '—'} state="done" at={data.submittedAt} />
              <TimelineRow label="法務審查 / Legal" actor={legalSlot?.deciderName ?? '法務'} state={slotState(legalSlot, data.status)} at={legalSlot?.at} comment={legalSlot?.comment} />
              <TimelineRow label="財務審查 / Finance" actor={financeSlot?.deciderName ?? '財務'} state={slotState(financeSlot, data.status)} at={financeSlot?.at} comment={financeSlot?.comment} />
              <TimelineRow
                label="法務主管定案 / Archive"
                actor={data.legalManager?.name ?? '法務主管'}
                state={data.legalManager?.approved === true ? 'done'
                  : data.legalManager?.approved === false ? 'rejected'
                  : data.status === 'PendingLegalManager' ? 'current' : 'idle'}
                at={data.legalManager?.at}
                comment={data.legalManager?.comment}
              />
              <TimelineRow
                label="結案 / Closed"
                actor={data.status === 'Completed' ? '系統' : data.status === 'Cancelled' ? '申請人放棄 / 撤回' : '—'}
                state={data.status === 'Completed' ? 'done' : data.status === 'Cancelled' ? 'rejected' : 'idle'}
                at={data.completedAt}
              />
            </ol>
          </SectionCard>

          {data.review && (
            <SectionCard className="!p-1">
              <ParallelApprovalPanel
                policyLabel={data.review.policyLabel}
                approvedCount={data.review.approvedCount}
                threshold={data.review.threshold}
                slots={panelSlots}
              />
            </SectionCard>
          )}

          {showDecisionBox && (
            <SectionCard>
              <SectionTitle>您的決定 / Your decision</SectionTitle>
              <div className="space-y-3 px-5 py-4">
                <Field label="意見 / Comment" hint="退件時請填寫原因；核准可留空">
                  <Textarea rows={3} value={comment} onChange={e => setComment(e.target.value)} disabled={actionPending} />
                </Field>
              </div>
            </SectionCard>
          )}

          {!showDecisionBox && !isSubmitter && data.status !== 'Completed' && data.status !== 'Cancelled' && (
            <SectionCard>
              <InfoBanner>目前尚待相關審查人處理。若您是此階段的處理人，請從首頁「Pending My Approval」進入。</InfoBanner>
            </SectionCard>
          )}
        </>
      )}

      <BpmnView
        open={bpmnOpen}
        onClose={() => setBpmnOpen(false)}
        formLabel={`${FORMS.CONTRACT_REVIEW.code} — ${FORMS.CONTRACT_REVIEW.label}`}
        steps={FORMS.CONTRACT_REVIEW.steps}
        activeStep={0}
        ownerByStep={FORMS.CONTRACT_REVIEW.ownerByStep}
        bpmnXml={CrBpmnXml}
        completedNodes={trail?.completed}
        currentNodes={trail?.current}
        rejectedNodes={trail?.rejected}
        skippedNodes={trail?.skipped}
      />

      <ActionFooter hint={footerHint} actions={footerActions} />
    </div>
  )
}

// ── small presentational helpers ────────────────────────────────────────────
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

function slotState(slot: CrSlotView | undefined, status: CrStatus): TimelineState {
  if (slot?.state === 'approved') return 'done'
  if (slot?.state === 'rejected') return 'rejected'
  if (slot?.state === 'pending' && status === 'PendingParallelReview') return 'current'
  return 'idle'
}

function statusKind(s: CrStatus): StatusKind {
  switch (s) {
    case 'PendingParallelReview': return 'pending'
    case 'PendingLegalManager':   return 'pending'
    case 'ResubmitRequired':      return 'returned'
    case 'Completed':             return 'closed'
    case 'Cancelled':             return 'cancelled'
  }
}

export function zhStatus(s: CrStatus): string {
  switch (s) {
    case 'PendingParallelReview': return '並簽審查中'
    case 'PendingLegalManager':   return '待法務主管定案'
    case 'ResubmitRequired':      return '待修改重送'
    case 'Completed':             return '已完成'
    case 'Cancelled':             return '已撤回'
  }
}

function stageLabel(s: CrStatus): string {
  switch (s) {
    case 'PendingParallelReview': return '法務 + 財務並簽'
    case 'PendingLegalManager':   return '法務主管定案歸檔'
    case 'ResubmitRequired':      return '申請人修改中'
    case 'Completed':             return '已結案'
    case 'Cancelled':             return '已撤回'
  }
}

function roleZh(code?: string | null): string | undefined {
  switch (code?.toUpperCase()) {
    case 'LEGAL': return '法務'
    case 'FINANCE': return '財務'
    case 'LEGAL_MANAGER': return '法務主管'
    default: return undefined
  }
}

/** Map case status → index into FORMS.CONTRACT_REVIEW.steps (apply 0 · review 1 · archive 2 · close 3). */
function activeStepFor(status: CrStatus): number {
  switch (status) {
    case 'PendingParallelReview': return 1
    case 'ResubmitRequired':      return 1
    case 'PendingLegalManager':   return 2
    case 'Completed':             return 3
    case 'Cancelled':             return 1
  }
}

/** Per-flow status → BPMN node highlight (ids match CONTRACT_REVIEW_V1.bpmn.xml). */
function trailOf(data: CrCaseResponse): { completed: string[]; current: string[]; rejected: string[]; skipped: string[] } {
  const slots = data.review?.slots ?? []
  const nodesInState = (st: string) => slots.filter(s => s.state === st).map(s => s.nodeId)
  const completed = new Set<string>(['start_1', 'task_apply', 'gateway_fork'])
  nodesInState('approved').forEach(n => completed.add(n))
  const rejected = nodesInState('rejected')
  const skipped = nodesInState('skipped')
  let current: string[] = []

  switch (data.status) {
    case 'PendingParallelReview':
      current = nodesInState('pending')
      break
    case 'ResubmitRequired':
      completed.add('gateway_join'); completed.add('gateway_decision')
      current = ['task_revise']
      break
    case 'PendingLegalManager':
      completed.add('gateway_join'); completed.add('gateway_decision')
      current = ['approval_legal_mgr']
      break
    case 'Completed':
      ['gateway_join', 'gateway_decision', 'approval_legal_mgr', 'end_approved'].forEach(n => completed.add(n))
      break
    case 'Cancelled':
      break
  }
  return { completed: [...completed], current, rejected, skipped }
}

function formatDate(iso: string | null | undefined): string {
  if (!iso) return '—'
  const d = new Date(iso)
  if (Number.isNaN(d.getTime())) return iso
  const pad = (n: number) => String(n).padStart(2, '0')
  return `${d.getFullYear()}/${pad(d.getMonth() + 1)}/${pad(d.getDate())} ${pad(d.getHours())}:${pad(d.getMinutes())}`
}
