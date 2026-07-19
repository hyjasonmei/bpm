import { useCallback, useEffect, useMemo, useState } from 'react'
import { ArrowLeft, CheckCircle2, RotateCcw, Workflow as WorkflowIcon, XCircle } from 'lucide-react'
import { useNavigate } from 'react-router-dom'

import { Button } from '@/components/ui/button'
import { SectionCard, SectionTitle } from '@/components/ui/card'
import { StatusBadge, type StatusKind } from '@/components/ui/badge'
import { Field, Select, Textarea, InfoBanner } from '@/components/ui/form'
import { ActionFooter, type ActionFooterItem } from '@/components/ui/action-footer/ActionFooter'
import { FlowStateBanner } from '@/components/ui/flow-state-banner/FlowStateBanner'
import { Stepper } from '@/components/Stepper'
import { BpmnView } from '@/components/BpmnView'
import { apiFetch, getJwt } from '@/lib/apiFetch'
import { decodeJwt } from '@/lib/jwt'
import { useDelegatedFor } from '@/lib/useDelegatedFor'
import { useCaseTransfer } from '@/components/CaseTransfer'
import { FORMS } from '@/lib/workflow'
import type { CaseDetailProps } from '@/features/registry'
import FAP_V1_BpmnXml from './FAP_V1.bpmn.xml?raw'
import { RECEIVED_OPTIONS, zhStatus } from './FAP_V1_shared'
import type { FAP_V1_CaseResponse, FAP_V1_PurchaseItemDto, FAP_V1_Status } from './FAP_V1_types'

/** Read-mostly detail page for a FAP V1 case (manager approve → PO → verification). */
export function FAP_V1_CaseDetail({ caseId }: CaseDetailProps) {
  const navigate = useNavigate()
  const [data, setData] = useState<FAP_V1_CaseResponse | null>(null)
  const [viewerUserId, setViewerUserId] = useState<string | null>(null)
  const [loading, setLoading] = useState(true)
  const [error, setError] = useState<string | null>(null)
  const [actionPending, setActionPending] = useState(false)
  const [actionError, setActionError] = useState<string | null>(null)
  const [bpmnOpen, setBpmnOpen] = useState(false)
  const [approvalComment, setApprovalComment] = useState('')
  const [received, setReceived] = useState('Received')
  const [verifyRemark, setVerifyRemark] = useState('')

  const load = useCallback(async () => {
    try {
      const res = await apiFetch(`/api/fap/v1/${caseId}`)
      if (!res.ok) throw new Error(`HTTP ${res.status}`)
      setData((await res.json()) as FAP_V1_CaseResponse)
      setError(null)
    } catch (e) {
      setError(e instanceof Error ? e.message : String(e))
    } finally {
      setLoading(false)
    }
  }, [caseId])

  useEffect(() => { void load() }, [load])
  useEffect(() => {
    const tok = getJwt(); if (!tok) return
    const decoded = decodeJwt(tok); if (decoded?.sub) setViewerUserId(decoded.sub)
  }, [])

  const delegatedFor = useDelegatedFor()
  const transfer = useCaseTransfer({
    flowCode: 'FAP',
    caseId,
    isOpen: !!data,
    currentAssigneeUserId: data?.currentAssigneeUserId ?? null,
    currentAssigneeRoleCode: null,
    viewerUserId,
    delegatedFor,
    onTransferred: load,
  })
  // The viewer may act on the case if they are the current assignee OR an active
  // delegate of the current assignee (delegation-aware — see useDelegatedFor).
  const isCurrentAssignee = !!data && !!viewerUserId && !!data.currentAssigneeUserId &&
    (data.currentAssigneeUserId === viewerUserId || delegatedFor.includes(data.currentAssigneeUserId))
  const isSubmitter       = !!data && !!viewerUserId && data.submitterUserId === viewerUserId
  const trail = useMemo(() => (data ? deriveTrail(data.status) : null), [data])

  const postManagerDecision = useCallback(async (approve: boolean) => {
    setActionPending(true); setActionError(null)
    try {
      const res = await apiFetch(`/api/fap/v1/${caseId}/manager-decision`, {
        method: 'POST', headers: { 'Content-Type': 'application/json' },
        body: JSON.stringify({ approve, comment: approvalComment.trim() ? approvalComment : null }),
      })
      if (!res.ok) throw new Error(await res.text() || `HTTP ${res.status}`)
      setApprovalComment(''); await load()
    } catch (e) { setActionError(e instanceof Error ? e.message : String(e)) } finally { setActionPending(false) }
  }, [caseId, approvalComment, load])

  const postVerify = useCallback(async () => {
    setActionPending(true); setActionError(null)
    try {
      const res = await apiFetch(`/api/fap/v1/${caseId}/verify`, {
        method: 'POST', headers: { 'Content-Type': 'application/json' },
        body: JSON.stringify({ received, remark: verifyRemark.trim() ? verifyRemark : null }),
      })
      if (!res.ok) throw new Error(await res.text() || `HTTP ${res.status}`)
      setVerifyRemark(''); await load()
    } catch (e) { setActionError(e instanceof Error ? e.message : String(e)) } finally { setActionPending(false) }
  }, [caseId, received, verifyRemark, load])

  const postCancel = useCallback(async () => {
    setActionPending(true); setActionError(null)
    try {
      const res = await apiFetch(`/api/fap/v1/${caseId}/cancel`, { method: 'POST' })
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
    if (isCurrentAssignee && data.status === 'PendingVerification') {
      actions.push({ id: 'verify', label: '完成驗收 / Complete verification', variant: 'primary', pending: actionPending, onClick: () => postVerify() })
    }
    if (isSubmitter && data.status === 'ResubmitRequired') {
      actions.push({
        id: 'resubmit', confirm: false,
        label: <span className="inline-flex items-center gap-1"><RotateCcw className="h-3.5 w-3.5" />修正後重新送出</span>,
        variant: 'primary', pending: actionPending,
        onClick: () => navigate(`/apply/FAP?resubmit=${data.id}`),
      })
    }
    // Submitter may withdraw their own case while it is still in flight.
    if (isSubmitter && data.status !== 'Completed' && data.status !== 'Cancelled') {
      actions.push({ id: 'withdraw', label: '撤回申請', variant: 'destructive', pending: actionPending, confirm: { titleZh: '撤回申請？', description: '撤回後無法復原。', confirmText: '確認撤回' }, onClick: () => postCancel() })
    }
    return actions
  }, [data, isCurrentAssignee, isSubmitter, actionPending, postManagerDecision, postVerify, postCancel, navigate])

  const footerHint = (() => {
    if (!data) return null
    if (actionError) return <span className="text-danger">{actionError}</span>
    if (isCurrentAssignee && data.status === 'PendingManager') return '請審閱簽核意見後決定'
    if (isCurrentAssignee && data.status === 'PendingVerification') return '貨品到位後請完成驗收'
    if (isSubmitter && data.status === 'ResubmitRequired') return '請依照退件意見修正後重新送出'
    return null
  })()

  return (
    <div className="mx-auto max-w-screen-lg space-y-4 pb-24 md:px-6 md:pt-6">
      <div className="flex flex-wrap items-center gap-3">
        <Button variant="outline" size="sm" onClick={() => navigate('/')}><ArrowLeft className="h-3.5 w-3.5" /> 返回</Button>
        <div className="order-last w-full min-w-0 md:order-none md:w-auto md:flex-1">
          <h1 className="truncate text-lg font-bold text-ink">資產採購案件 <span className="ml-2 text-base font-medium text-ink-muted">· FAP V1</span></h1>
          <p className="font-mono text-[11px] text-ink-faint">{caseId}</p>
        </div>
        <div className="ml-auto"><Button variant="outline" size="sm" onClick={() => setBpmnOpen(true)}><WorkflowIcon className="h-3.5 w-3.5" /> View BPMN</Button></div>
      </div>

      {loading && <SectionCard><div className="px-5 py-10 text-center text-sm text-ink-muted">載入中…</div></SectionCard>}
      {error && !loading && <SectionCard><div className="px-5 py-6 text-sm text-danger">載入失敗：{error}</div></SectionCard>}

      {data && (
        <>
          <FlowStateBanner flowCode="FAP" flowVersion={1} />

          <SectionCard className="!p-0">
            <div className="bg-slate-50 px-4 py-2">
              <Stepper steps={FORMS.FAP.steps} activeStep={activeStepFor(data.status)} withZh />
            </div>
          </SectionCard>

          <SectionCard>
            <SectionTitle>狀態 / Status</SectionTitle>
            <div className="grid grid-cols-2 gap-4 px-5 py-4 text-sm md:grid-cols-4">
              <Stat label="目前狀態" value={<span className="inline-flex items-center gap-2"><StatusBadge kind={statusKind(data.status)} /><span className="text-xs text-ink-muted">{zhStatus(data.status)}</span></span>} />
              <Stat label="申請人" value={<span>{data.submitterDisplayName ?? '—'}</span>} />
              <Stat label="目前指派給" value={<span>{data.currentAssigneeDisplayName ?? '—'}</span>} />
              <Stat label="採購單號" value={<span className="font-mono text-xs">{data.purchaseOrderNo ?? '—'}</span>} />
            </div>
            {data.roundCount > 1 && <div className="border-t border-rule px-5 py-3"><InfoBanner>本案件為第 <strong>{data.roundCount}</strong> 次送出。</InfoBanner></div>}
          </SectionCard>

          <SectionCard>
            <SectionTitle>採購明細 · {data.purchaseItems.length}</SectionTitle>
            <div className="space-y-2 px-5 py-4">
              {data.purchaseItems.map((it, i) => <ItemRow key={i} index={i} value={it} />)}
            </div>
            <div className="grid grid-cols-12 gap-3 border-t border-rule px-5 py-4 text-sm">
              <ReadField label="收貨地點" value={data.shippingLocation} className="col-span-4" />
              <ReadField label="費用歸屬" value={data.chargeTo} className="col-span-4" />
              <ReadField label="用途" value={data.purpose} className="col-span-4" />
              <ReadField label="期望日" value={data.expectedDate?.slice(0, 10) ?? null} className="col-span-4" />
              <ReadField label="備註" value={data.note} className="col-span-8" />
            </div>
          </SectionCard>

          <SectionCard>
            <SectionTitle>簽核時序 / Approval Timeline</SectionTitle>
            <ol className="divide-y divide-slate-100">
              <TimelineRow label="請購 / Submit" actor={data.submitterDisplayName ?? '—'} state="done" at={data.submittedAt} />
              <TimelineRow label="主管核准 / Manager" actor={data.managerDecision?.displayName ?? '—'}
                state={data.managerDecision?.approved === true ? 'done' : data.managerDecision?.approved === false ? 'rejected' : data.status === 'PendingManager' ? 'current' : 'idle'}
                at={data.managerDecision?.decidedAt} comment={data.managerDecision?.comment} />
              <TimelineRow label="採購單成立 / PO" actor={data.purchaseOrderNo ?? '—'}
                state={data.purchaseOrderNo ? 'done' : 'idle'} at={null} />
              <TimelineRow label="驗收 / Verification" actor={data.verification?.displayName ?? '—'}
                state={data.verification ? 'done' : data.status === 'PendingVerification' ? 'current' : 'idle'}
                at={data.verification?.verifiedAt} comment={data.verification?.received ? `${data.verification.received}${data.verification.remark ? ' — ' + data.verification.remark : ''}` : null} />
              <TimelineRow label="入帳 / Closed" actor={data.status === 'Completed' ? '系統' : data.status === 'Cancelled' ? '申請人撤回' : '—'} state={data.status === 'Completed' ? 'done' : data.status === 'Cancelled' ? 'rejected' : 'idle'} at={data.completedAt} />
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

          {isCurrentAssignee && data.status === 'PendingVerification' && (
            <SectionCard>
              <SectionTitle>驗收 / Verification</SectionTitle>
              <div className="grid grid-cols-12 gap-3 px-5 py-4">
                <Field label="驗收結果 / Received" required className="col-span-4">
                  <Select value={received} onChange={e => setReceived(e.target.value)} disabled={actionPending}>
                    {RECEIVED_OPTIONS.map(o => <option key={o} value={o}>{o}</option>)}
                  </Select>
                </Field>
                <Field label="備註 / Remark" className="col-span-8">
                  <Textarea rows={2} value={verifyRemark} onChange={e => setVerifyRemark(e.target.value)} disabled={actionPending} />
                </Field>
              </div>
            </SectionCard>
          )}
        </>
      )}

      <BpmnView
        open={bpmnOpen}
        onClose={() => setBpmnOpen(false)}
        formLabel={`${FORMS.FAP.code} — ${FORMS.FAP.label}`}
        steps={FORMS.FAP.steps}
        activeStep={0}
        ownerByStep={FORMS.FAP.ownerByStep}
        bpmnXml={FAP_V1_BpmnXml}
        completedNodes={trail?.completed}
        currentNode={trail?.current}
      />

      <ActionFooter hint={footerHint} actions={transfer.action ? [...footerActions, transfer.action] : footerActions} />
      {transfer.modal}
    </div>
  )
}

function ItemRow({ index, value }: { index: number; value: FAP_V1_PurchaseItemDto }) {
  return (
    <div className="flex items-center justify-between rounded-md border border-rule bg-card px-4 py-2 text-sm">
      <span className="font-medium text-ink">#{index + 1} · {value.itemSpec || '—'}</span>
      <span className="text-ink-muted">{value.category} · ×{value.qty}</span>
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

function statusKind(s: FAP_V1_Status): StatusKind {
  switch (s) {
    case 'PendingManager':      return 'pending'
    case 'PendingVerification': return 'verification'
    case 'ResubmitRequired':    return 'returned'
    case 'Completed':           return 'closed'
    case 'Cancelled':           return 'cancelled'
  }
}

/** Map the case status → index into FORMS.FAP.steps for the header stepper
 *  (apply 0 · approve 1 · po 2 · verify 3 · closed 4). */
function activeStepFor(status: FAP_V1_Status): number {
  switch (status) {
    case 'PendingManager':      return 1
    case 'PendingVerification': return 3
    case 'ResubmitRequired':    return 1
    case 'Completed':           return 4
    case 'Cancelled':           return 1
  }
}

function deriveTrail(status: FAP_V1_Status): { completed: string[]; current: string | null } {
  switch (status) {
    case 'PendingManager':      return { completed: ['s', 'pr'], current: 'ap' }
    case 'PendingVerification': return { completed: ['s', 'pr', 'ap', 'po'], current: 'vf' }
    case 'ResubmitRequired':    return { completed: ['s'], current: 'pr' }
    case 'Completed':           return { completed: ['s', 'pr', 'ap', 'po', 'vf', 'e'], current: null }
    case 'Cancelled':           return { completed: ['s'], current: null }
  }
}

function formatDate(iso: string | null | undefined): string {
  if (!iso) return '—'
  const d = new Date(iso)
  if (Number.isNaN(d.getTime())) return iso
  const pad = (n: number) => String(n).padStart(2, '0')
  return `${d.getFullYear()}/${pad(d.getMonth() + 1)}/${pad(d.getDate())} ${pad(d.getHours())}:${pad(d.getMinutes())}`
}
