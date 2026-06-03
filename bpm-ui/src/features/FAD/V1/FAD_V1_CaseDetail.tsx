import { useCallback, useEffect, useMemo, useState } from 'react'
import { ArrowLeft, CheckCircle2, ExternalLink, RotateCcw, Workflow as WorkflowIcon, XCircle } from 'lucide-react'
import { useNavigate } from 'react-router-dom'

import { Button } from '@/components/ui/button'
import { SectionCard, SectionTitle } from '@/components/ui/card'
import { StatusBadge, type StatusKind } from '@/components/ui/badge'
import { Field, Select, Textarea, InfoBanner } from '@/components/ui/form'
import { ActionFooter, type ActionFooterItem } from '@/components/ui/action-footer/ActionFooter'
import { FlowStateBanner } from '@/components/ui/flow-state-banner/FlowStateBanner'
import { Stepper } from '@/components/Stepper'
import { BpmnView } from '@/components/BpmnView'
import { apiFetch, BPM_SVC_URL, getJwt } from '@/lib/apiFetch'
import { decodeJwt } from '@/lib/jwt'
import { FORMS } from '@/lib/workflow'
import type { CaseDetailProps } from '@/features/registry'
import FAD_V1_BpmnXml from './FAD_V1.bpmn.xml?raw'
import { HANDLING_OPTIONS, zhStatus } from './FAD_V1_shared'
import type { FAD_V1_CaseResponse, FAD_V1_Status } from './FAD_V1_types'

/** Read-mostly detail page for a FAD V1 case (IT judgment → confirm). */
export function FAD_V1_CaseDetail({ caseId }: CaseDetailProps) {
  const navigate = useNavigate()
  const [data, setData] = useState<FAD_V1_CaseResponse | null>(null)
  const [viewerUserId, setViewerUserId] = useState<string | null>(null)
  const [loading, setLoading] = useState(true)
  const [error, setError] = useState<string | null>(null)
  const [actionPending, setActionPending] = useState(false)
  const [actionError, setActionError] = useState<string | null>(null)
  const [bpmnOpen, setBpmnOpen] = useState(false)
  const [approvalComment, setApprovalComment] = useState('')
  const [handlingResult, setHandlingResult] = useState(HANDLING_OPTIONS[0])
  const [confirmRemark, setConfirmRemark] = useState('')

  const load = useCallback(async () => {
    try {
      const res = await apiFetch(`/api/fad/v1/${caseId}`)
      if (!res.ok) throw new Error(`HTTP ${res.status}`)
      setData((await res.json()) as FAD_V1_CaseResponse)
      setError(null)
    } catch (e) { setError(e instanceof Error ? e.message : String(e)) } finally { setLoading(false) }
  }, [caseId])

  useEffect(() => { void load() }, [load])
  useEffect(() => {
    const tok = getJwt(); if (!tok) return
    const decoded = decodeJwt(tok); if (decoded?.sub) setViewerUserId(decoded.sub)
  }, [])

  const isCurrentAssignee = !!data && !!viewerUserId && data.currentAssigneeUserId === viewerUserId
  const isSubmitter       = !!data && !!viewerUserId && data.submitterUserId === viewerUserId
  const trail = useMemo(() => (data ? deriveTrail(data.status) : null), [data])

  const postManagerDecision = useCallback(async (approve: boolean) => {
    setActionPending(true); setActionError(null)
    try {
      const res = await apiFetch(`/api/fad/v1/${caseId}/manager-decision`, {
        method: 'POST', headers: { 'Content-Type': 'application/json' },
        body: JSON.stringify({ approve, comment: approvalComment.trim() ? approvalComment : null }),
      })
      if (!res.ok) throw new Error(await res.text() || `HTTP ${res.status}`)
      setApprovalComment(''); await load()
    } catch (e) { setActionError(e instanceof Error ? e.message : String(e)) } finally { setActionPending(false) }
  }, [caseId, approvalComment, load])

  const postConfirm = useCallback(async () => {
    setActionPending(true); setActionError(null)
    try {
      const res = await apiFetch(`/api/fad/v1/${caseId}/confirm`, {
        method: 'POST', headers: { 'Content-Type': 'application/json' },
        body: JSON.stringify({ handlingResult, remark: confirmRemark.trim() ? confirmRemark : null }),
      })
      if (!res.ok) throw new Error(await res.text() || `HTTP ${res.status}`)
      setConfirmRemark(''); await load()
    } catch (e) { setActionError(e instanceof Error ? e.message : String(e)) } finally { setActionPending(false) }
  }, [caseId, handlingResult, confirmRemark, load])

  const postCancel = useCallback(async () => {
    if (!window.confirm('確定要撤回此申請？撤回後無法復原。')) return
    setActionPending(true); setActionError(null)
    try {
      const res = await apiFetch(`/api/fad/v1/${caseId}/cancel`, { method: 'POST' })
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
    if (isCurrentAssignee && data.status === 'PendingConfirm') {
      actions.push({ id: 'confirm', label: '完成領收確認 / Confirm', variant: 'primary', pending: actionPending, onClick: () => postConfirm() })
    }
    if (isSubmitter && data.status === 'ResubmitRequired') {
      actions.push({
        id: 'resubmit',
        label: <span className="inline-flex items-center gap-1"><RotateCcw className="h-3.5 w-3.5" />修正後重新送出</span>,
        variant: 'primary', pending: actionPending,
        onClick: () => navigate(`/apply/FAD?resubmit=${data.id}`),
      })
    }
    // Submitter may withdraw their own case while it is still in flight.
    if (isSubmitter && data.status !== 'Completed' && data.status !== 'Cancelled') {
      actions.push({ id: 'withdraw', label: '撤回申請', variant: 'destructive', pending: actionPending, onClick: () => postCancel() })
    }
    return actions
  }, [data, isCurrentAssignee, isSubmitter, actionPending, postManagerDecision, postConfirm, postCancel, navigate])

  const footerHint = (() => {
    if (!data) return null
    if (actionError) return <span className="text-danger">{actionError}</span>
    if (isCurrentAssignee && data.status === 'PendingManager') return '請判別後決定'
    if (isCurrentAssignee && data.status === 'PendingConfirm') return '請完成領收確認'
    if (isSubmitter && data.status === 'ResubmitRequired') return '請依照退件意見修正後重新送出'
    return null
  })()

  return (
    <div className="mx-auto max-w-screen-lg space-y-4 p-6 pb-24">
      <div className="flex items-center gap-3">
        <Button variant="outline" size="sm" onClick={() => navigate('/')}><ArrowLeft className="h-3.5 w-3.5" /> 返回</Button>
        <div className="min-w-0">
          <h1 className="truncate text-lg font-bold text-ink">資產處份案件 <span className="ml-2 text-base font-medium text-ink-muted">· FAD V1</span></h1>
          <p className="font-mono text-[11px] text-ink-faint">{caseId}</p>
        </div>
        <div className="ml-auto"><Button variant="outline" size="sm" onClick={() => setBpmnOpen(true)}><WorkflowIcon className="h-3.5 w-3.5" /> View BPMN</Button></div>
      </div>

      {loading && <SectionCard><div className="px-5 py-10 text-center text-sm text-ink-muted">載入中…</div></SectionCard>}
      {error && !loading && <SectionCard><div className="px-5 py-6 text-sm text-danger">載入失敗：{error}</div></SectionCard>}

      {data && (
        <>
          <FlowStateBanner flowCode="FAD" flowVersion={1} />

          <SectionCard className="!p-0">
            <div className="bg-slate-50 px-4 py-2">
              <Stepper steps={FORMS.FAD.steps} activeStep={activeStepFor(data.status)} withZh />
            </div>
          </SectionCard>

          <SectionCard>
            <SectionTitle>狀態 / Status</SectionTitle>
            <div className="grid grid-cols-2 gap-4 px-5 py-4 text-sm md:grid-cols-4">
              <Stat label="目前狀態" value={<span className="inline-flex items-center gap-2"><StatusBadge kind={statusKind(data.status)} /><span className="text-xs text-ink-muted">{zhStatus(data.status)}</span></span>} />
              <Stat label="申請人" value={<span>{data.submitterDisplayName ?? '—'}</span>} />
              <Stat label="目前指派給" value={<span>{data.currentAssigneeDisplayName ?? '—'}</span>} />
              <Stat label="資產編號" value={<span className="font-mono text-xs">{data.assetId}</span>} />
            </div>
            {data.roundCount > 1 && <div className="border-t border-rule px-5 py-3"><InfoBanner>本案件為第 <strong>{data.roundCount}</strong> 次送出。</InfoBanner></div>}
          </SectionCard>

          <SectionCard>
            <SectionTitle>處份資訊 / Disposal Info</SectionTitle>
            <div className="grid grid-cols-12 gap-3 px-5 py-4 text-sm">
              <ReadField label="報廢原因" value={data.disposalReason} className="col-span-6" />
              <ReadField label="資產名稱" value={data.assetName} className="col-span-6" />
              <ReadField label="說明" value={data.description} className="col-span-12" />
              <div className="col-span-6">
                <p className="text-[11px] font-semibold uppercase tracking-wider text-ink-muted">照片</p>
                {data.photoFileId
                  ? <a className="mt-1 inline-flex items-center gap-1 text-primary hover:underline" target="_blank" rel="noreferrer" href={`${BPM_SVC_URL}/api/files/${data.photoFileId}`}>下載 <ExternalLink className="h-3.5 w-3.5" /></a>
                  : <p className="mt-1 text-ink-faint">—</p>}
              </div>
            </div>
          </SectionCard>

          <SectionCard>
            <SectionTitle>簽核時序 / Approval Timeline</SectionTitle>
            <ol className="divide-y divide-slate-100">
              <TimelineRow label="處份申請 / Submit" actor={data.submitterDisplayName ?? '—'} state="done" at={data.submittedAt} />
              <TimelineRow label="固定資產判別 / IT" actor={data.managerDecision?.displayName ?? '—'}
                state={data.managerDecision?.approved === true ? 'done' : data.managerDecision?.approved === false ? 'rejected' : data.status === 'PendingManager' ? 'current' : 'idle'}
                at={data.managerDecision?.decidedAt} comment={data.managerDecision?.comment} />
              <TimelineRow label="領收確認 / Confirmed" actor={data.confirmation?.displayName ?? '—'}
                state={data.confirmation ? 'done' : data.status === 'PendingConfirm' ? 'current' : 'idle'}
                at={data.confirmation?.confirmedAt} comment={data.confirmation?.handlingResult ? `${data.confirmation.handlingResult}${data.confirmation.remark ? ' — ' + data.confirmation.remark : ''}` : null} />
              <TimelineRow label="處份 / Disposed" actor={data.status === 'Completed' ? '系統' : data.status === 'Cancelled' ? '申請人撤回' : '—'} state={data.status === 'Completed' ? 'done' : data.status === 'Cancelled' ? 'rejected' : 'idle'} at={data.completedAt} />
            </ol>
          </SectionCard>

          {isCurrentAssignee && data.status === 'PendingManager' && (
            <SectionCard>
              <SectionTitle>判別決定 / Your decision</SectionTitle>
              <div className="px-5 py-4">
                <Field label="判別意見 / Comment" hint="退件時建議填寫原因">
                  <Textarea rows={3} value={approvalComment} onChange={e => setApprovalComment(e.target.value)} disabled={actionPending} />
                </Field>
              </div>
            </SectionCard>
          )}

          {isCurrentAssignee && data.status === 'PendingConfirm' && (
            <SectionCard>
              <SectionTitle>領收確認 / Confirmation</SectionTitle>
              <div className="grid grid-cols-12 gap-3 px-5 py-4">
                <Field label="處置結果 / Handling" required className="col-span-5">
                  <Select value={handlingResult} onChange={e => setHandlingResult(e.target.value)} disabled={actionPending}>
                    {HANDLING_OPTIONS.map(o => <option key={o} value={o}>{o}</option>)}
                  </Select>
                </Field>
                <Field label="備註 / Remark" className="col-span-7">
                  <Textarea rows={2} value={confirmRemark} onChange={e => setConfirmRemark(e.target.value)} disabled={actionPending} />
                </Field>
              </div>
            </SectionCard>
          )}
        </>
      )}

      <BpmnView
        open={bpmnOpen}
        onClose={() => setBpmnOpen(false)}
        formLabel={`${FORMS.FAD.code} — ${FORMS.FAD.label}`}
        steps={FORMS.FAD.steps}
        activeStep={0}
        ownerByStep={FORMS.FAD.ownerByStep}
        bpmnXml={FAD_V1_BpmnXml}
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

function statusKind(s: FAD_V1_Status): StatusKind {
  switch (s) {
    case 'PendingManager':   return 'pending'
    case 'PendingConfirm':   return 'fin_review'
    case 'ResubmitRequired': return 'returned'
    case 'Completed':        return 'closed'
    case 'Cancelled':        return 'cancelled'
  }
}

/** Map the case status → index into FORMS.FAD.steps for the header stepper
 *  (apply 0 · judge 1 · confirm 2 · closed 3). */
function activeStepFor(status: FAD_V1_Status): number {
  switch (status) {
    case 'PendingManager':   return 1
    case 'PendingConfirm':   return 2
    case 'ResubmitRequired': return 1
    case 'Completed':        return 3
    case 'Cancelled':        return 1
  }
}

function deriveTrail(status: FAD_V1_Status): { completed: string[]; current: string | null } {
  switch (status) {
    case 'PendingManager':   return { completed: ['s', 'dr'], current: 'ap' }
    case 'PendingConfirm':   return { completed: ['s', 'dr', 'ap'], current: 'cf' }
    case 'ResubmitRequired': return { completed: ['s'], current: 'dr' }
    case 'Completed':        return { completed: ['s', 'dr', 'ap', 'cf', 'e'], current: null }
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
