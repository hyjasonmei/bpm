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
import { FORMS } from '@/lib/workflow'
import type { CaseDetailProps } from '@/features/registry'
import ETM_V1_BpmnXml from './ETM_V1.bpmn.xml?raw'
import { ACTION_OPTIONS, ITEM_STATUS_OPTIONS, YESNO_OPTIONS, defaultReturnItems, emptyItem, zhStatus } from './ETM_V1_shared'
import type { ETM_V1_CaseResponse, ETM_V1_ReturnItemDto, ETM_V1_Status } from './ETM_V1_types'

/** Read-mostly detail page for an ETM V1 case (manager approval → handover). */
export function ETM_V1_CaseDetail({ caseId }: CaseDetailProps) {
  const navigate = useNavigate()
  const [data, setData] = useState<ETM_V1_CaseResponse | null>(null)
  const [viewerUserId, setViewerUserId] = useState<string | null>(null)
  const [loading, setLoading] = useState(true)
  const [error, setError] = useState<string | null>(null)
  const [actionPending, setActionPending] = useState(false)
  const [actionError, setActionError] = useState<string | null>(null)
  const [bpmnOpen, setBpmnOpen] = useState(false)
  const [approvalComment, setApprovalComment] = useState('')
  const [outstandingPayment, setOutstandingPayment] = useState('No')
  const [items, setItems] = useState<ETM_V1_ReturnItemDto[]>(defaultReturnItems())

  const load = useCallback(async () => {
    try {
      const res = await apiFetch(`/api/etm/v1/${caseId}`)
      if (!res.ok) throw new Error(`HTTP ${res.status}`)
      setData((await res.json()) as ETM_V1_CaseResponse)
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
      const res = await apiFetch(`/api/etm/v1/${caseId}/manager-decision`, {
        method: 'POST', headers: { 'Content-Type': 'application/json' },
        body: JSON.stringify({ approve, comment: approvalComment.trim() ? approvalComment : null }),
      })
      if (!res.ok) throw new Error(await res.text() || `HTTP ${res.status}`)
      setApprovalComment(''); await load()
    } catch (e) { setActionError(e instanceof Error ? e.message : String(e)) } finally { setActionPending(false) }
  }, [caseId, approvalComment, load])

  const postHandover = useCallback(async () => {
    setActionPending(true); setActionError(null)
    try {
      const res = await apiFetch(`/api/etm/v1/${caseId}/handover`, {
        method: 'POST', headers: { 'Content-Type': 'application/json' },
        body: JSON.stringify({ outstandingPayment, returnItems: items.filter(i => i.assetType?.trim()) }),
      })
      if (!res.ok) throw new Error(await res.text() || `HTTP ${res.status}`)
      await load()
    } catch (e) { setActionError(e instanceof Error ? e.message : String(e)) } finally { setActionPending(false) }
  }, [caseId, outstandingPayment, items, load])

  const postCancel = useCallback(async () => {
    if (!window.confirm('確定要撤回此申請？撤回後無法復原。')) return
    setActionPending(true); setActionError(null)
    try {
      const res = await apiFetch(`/api/etm/v1/${caseId}/cancel`, { method: 'POST' })
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
    if (isCurrentAssignee && data.status === 'PendingHandover') {
      actions.push({ id: 'handover', label: '完成交接 / Complete handover', variant: 'primary', pending: actionPending, onClick: () => postHandover() })
    }
    if (isSubmitter && data.status === 'ResubmitRequired') {
      actions.push({
        id: 'resubmit',
        label: <span className="inline-flex items-center gap-1"><RotateCcw className="h-3.5 w-3.5" />修正後重新送出</span>,
        variant: 'primary', pending: actionPending,
        onClick: () => navigate(`/apply/ETM?resubmit=${data.id}`),
      })
    }
    // Submitter may withdraw their own case while it is still in flight.
    if (isSubmitter && data.status !== 'Completed' && data.status !== 'Cancelled') {
      actions.push({ id: 'withdraw', label: '撤回申請', variant: 'destructive', pending: actionPending, onClick: () => postCancel() })
    }
    return actions
  }, [data, isCurrentAssignee, isSubmitter, actionPending, postManagerDecision, postHandover, postCancel, navigate])

  const footerHint = (() => {
    if (!data) return null
    if (actionError) return <span className="text-danger">{actionError}</span>
    if (isCurrentAssignee && data.status === 'PendingManager') return '請審閱後決定'
    if (isCurrentAssignee && data.status === 'PendingHandover') return '請完成交接 / 物品返還'
    if (isSubmitter && data.status === 'ResubmitRequired') return '請依照退件意見修正後重新送出'
    return null
  })()

  return (
    <div className="mx-auto max-w-screen-lg space-y-4 p-6 pb-24">
      <div className="flex items-center gap-3">
        <Button variant="outline" size="sm" onClick={() => navigate('/')}><ArrowLeft className="h-3.5 w-3.5" /> 返回</Button>
        <div className="min-w-0">
          <h1 className="truncate text-lg font-bold text-ink">員工離職案件 <span className="ml-2 text-base font-medium text-ink-muted">· ETM V1</span></h1>
          <p className="font-mono text-[11px] text-ink-faint">{caseId}</p>
        </div>
        <div className="ml-auto"><Button variant="outline" size="sm" onClick={() => setBpmnOpen(true)}><WorkflowIcon className="h-3.5 w-3.5" /> View BPMN</Button></div>
      </div>

      {loading && <SectionCard><div className="px-5 py-10 text-center text-sm text-ink-muted">載入中…</div></SectionCard>}
      {error && !loading && <SectionCard><div className="px-5 py-6 text-sm text-danger">載入失敗：{error}</div></SectionCard>}

      {data && (
        <>
          <FlowStateBanner flowCode="ETM" flowVersion={1} />

          <SectionCard className="!p-0">
            <div className="bg-slate-50 px-4 py-2">
              <Stepper steps={FORMS.ETM.steps} activeStep={activeStepFor(data.status)} withZh />
            </div>
          </SectionCard>

          <SectionCard>
            <SectionTitle>狀態 / Status</SectionTitle>
            <div className="grid grid-cols-2 gap-4 px-5 py-4 text-sm md:grid-cols-4">
              <Stat label="目前狀態" value={<span className="inline-flex items-center gap-2"><StatusBadge kind={statusKind(data.status)} /><span className="text-xs text-ink-muted">{zhStatus(data.status)}</span></span>} />
              <Stat label="申請人" value={<span>{data.submitterDisplayName ?? '—'}</span>} />
              <Stat label="目前指派給" value={<span>{data.currentAssigneeDisplayName ?? '—'}</span>} />
              <Stat label="最後工作日" value={<span className="font-mono text-xs">{data.lastWorkingDate?.slice(0, 10)}</span>} />
            </div>
            {data.roundCount > 1 && <div className="border-t border-rule px-5 py-3"><InfoBanner>本案件為第 <strong>{data.roundCount}</strong> 次送出。</InfoBanner></div>}
          </SectionCard>

          <SectionCard>
            <SectionTitle>離職資訊 / Termination Info</SectionTitle>
            <div className="grid grid-cols-12 gap-3 px-5 py-4 text-sm">
              <ReadField label="員工姓名" value={data.employeeName} className="col-span-6" />
              <ReadField label="員工編號" value={data.employeeId} className="col-span-6" />
              <ReadField label="離職原因" value={data.reason} className="col-span-4" />
              <ReadField label="提供離職證明" value={data.provideCertificate} className="col-span-4" />
              <ReadField label="欠款" value={data.outstandingPayment} className="col-span-4" />
            </div>
          </SectionCard>

          {data.returnItems.length > 0 && (
            <SectionCard>
              <SectionTitle>交接 / 物品返還 · {data.returnItems.length}</SectionTitle>
              <div className="space-y-2 px-5 py-4">
                {data.returnItems.map((it, i) => (
                  <div key={i} className="flex items-center justify-between rounded-md border border-rule bg-card px-4 py-2 text-sm">
                    <span className="text-ink">{it.assetType} <span className="text-ink-faint">· {it.action}</span></span>
                    <StatusBadge kind={it.status === 'Complete' ? 'closed' : 'pending'} />
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
              <TimelineRow label="交接 / Handover" actor={data.handoverByDisplayName ?? '—'}
                state={data.handoverAt ? 'done' : data.status === 'PendingHandover' ? 'current' : 'idle'} at={data.handoverAt} />
              <TimelineRow label="結案 / Closed"
                actor={data.status === 'Completed' ? '系統' : data.status === 'Cancelled' ? '申請人撤回' : '—'}
                state={data.status === 'Completed' ? 'done' : data.status === 'Cancelled' ? 'rejected' : 'idle'} at={data.completedAt} />
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

          {isCurrentAssignee && data.status === 'PendingHandover' && (
            <SectionCard>
              <SectionTitle>交接 / Handover</SectionTitle>
              <div className="space-y-3 px-5 py-4">
                <Field label="欠款 / Outstanding Payment" className="w-48">
                  <Select value={outstandingPayment} onChange={e => setOutstandingPayment(e.target.value)} disabled={actionPending}>
                    {YESNO_OPTIONS.map(o => <option key={o} value={o}>{o}</option>)}
                  </Select>
                </Field>
                {items.map((t, i) => (
                  <div key={i} className="grid grid-cols-12 items-center gap-3">
                    <div className="col-span-6"><Input value={t.assetType ?? ''} onChange={e => setItems(prev => prev.map((x, idx) => idx === i ? { ...x, assetType: e.target.value } : x))} disabled={actionPending} placeholder="資產 / 帳號" /></div>
                    <div className="col-span-3">
                      <Select value={t.action ?? 'Return'} onChange={e => setItems(prev => prev.map((x, idx) => idx === i ? { ...x, action: e.target.value } : x))} disabled={actionPending}>
                        {ACTION_OPTIONS.map(o => <option key={o} value={o}>{o}</option>)}
                      </Select>
                    </div>
                    <div className="col-span-2">
                      <Select value={t.status ?? 'Pending'} onChange={e => setItems(prev => prev.map((x, idx) => idx === i ? { ...x, status: e.target.value } : x))} disabled={actionPending}>
                        {ITEM_STATUS_OPTIONS.map(o => <option key={o} value={o}>{o}</option>)}
                      </Select>
                    </div>
                    <div className="col-span-1">
                      {items.length > 1 && <Button variant="ghost" size="xs" onClick={() => setItems(prev => prev.filter((_, idx) => idx !== i))} disabled={actionPending}><Trash2 className="h-3.5 w-3.5" /></Button>}
                    </div>
                  </div>
                ))}
                <div className="flex justify-end">
                  <Button variant="outline" size="sm" onClick={() => setItems(prev => [...prev, emptyItem()])} disabled={actionPending}><Plus className="h-3.5 w-3.5" /> 新增項目</Button>
                </div>
              </div>
            </SectionCard>
          )}
        </>
      )}

      <BpmnView
        open={bpmnOpen}
        onClose={() => setBpmnOpen(false)}
        formLabel={`${FORMS.ETM.code} — ${FORMS.ETM.label}`}
        steps={FORMS.ETM.steps}
        activeStep={0}
        ownerByStep={FORMS.ETM.ownerByStep}
        bpmnXml={ETM_V1_BpmnXml}
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

function statusKind(s: ETM_V1_Status): StatusKind {
  switch (s) {
    case 'PendingManager':   return 'pending'
    case 'PendingHandover':  return 'fin_review'
    case 'ResubmitRequired': return 'returned'
    case 'Completed':        return 'closed'
    case 'Cancelled':        return 'cancelled'
  }
}

/** Map the case status → index into FORMS.ETM.steps for the header stepper
 *  (apply 0 · approve 1 · handover 2 · closed 3). */
function activeStepFor(status: ETM_V1_Status): number {
  switch (status) {
    case 'PendingManager':   return 1
    case 'PendingHandover':  return 2
    case 'ResubmitRequired': return 1
    case 'Completed':        return 3
    case 'Cancelled':        return 1
  }
}

function deriveTrail(status: ETM_V1_Status): { completed: string[]; current: string | null } {
  switch (status) {
    case 'PendingManager':   return { completed: ['s', 'req'], current: 'ap' }
    case 'PendingHandover':  return { completed: ['s', 'req', 'ap'], current: 'ho' }
    case 'ResubmitRequired': return { completed: ['s'], current: 'req' }
    case 'Completed':        return { completed: ['s', 'req', 'ap', 'ho', 'e'], current: null }
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
