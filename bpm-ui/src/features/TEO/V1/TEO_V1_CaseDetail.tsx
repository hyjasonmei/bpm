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
import { useDelegatedFor } from '@/lib/useDelegatedFor'
import { FORMS } from '@/lib/workflow'
import { roleLabel } from '@/lib/roleLabels'
import type { CaseDetailProps } from '@/features/registry'
import TEO_V1_BpmnXml from './TEO_V1.bpmn.xml?raw'
import { zhStatus } from './TEO_V1_shared'
import type { TEO_V1_CaseResponse, TEO_V1_ExpenseItemDto, TEO_V1_Status } from './TEO_V1_types'

/** Read-mostly detail page for a TEO V1 case (two-stage: manager + finance). */
export function TEO_V1_CaseDetail({ caseId }: CaseDetailProps) {
  const navigate = useNavigate()
  const [data, setData] = useState<TEO_V1_CaseResponse | null>(null)
  const [viewerUserId, setViewerUserId] = useState<string | null>(null)
  const [loading, setLoading] = useState(true)
  const [error, setError] = useState<string | null>(null)
  const [actionPending, setActionPending] = useState(false)
  const [actionError, setActionError] = useState<string | null>(null)
  const [bpmnOpen, setBpmnOpen] = useState(false)
  const [approvalComment, setApprovalComment] = useState('')

  const load = useCallback(async () => {
    try {
      const res = await apiFetch(`/api/teo/v1/${caseId}`)
      if (!res.ok) throw new Error(`HTTP ${res.status}`)
      setData((await res.json()) as TEO_V1_CaseResponse)
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
  // The viewer may act on the case if they are the current assignee OR an active
  // delegate of the current assignee (delegation-aware — see useDelegatedFor).
  const isCurrentAssignee = !!data && !!viewerUserId && !!data.currentAssigneeUserId &&
    (data.currentAssigneeUserId === viewerUserId || delegatedFor.includes(data.currentAssigneeUserId))
  const isSubmitter       = !!data && !!viewerUserId && data.submitterUserId === viewerUserId
  const trail = useMemo(() => (data ? deriveTrail(data.status) : null), [data])

  const postDecision = useCallback(async (path: 'manager-decision' | 'finance-decision', approve: boolean) => {
    setActionPending(true); setActionError(null)
    try {
      const res = await apiFetch(`/api/teo/v1/${caseId}/${path}`, {
        method: 'POST', headers: { 'Content-Type': 'application/json' },
        body: JSON.stringify({ approve, comment: approvalComment.trim() ? approvalComment : null }),
      })
      if (!res.ok) throw new Error(await res.text() || `HTTP ${res.status}`)
      setApprovalComment(''); await load()
    } catch (e) {
      setActionError(e instanceof Error ? e.message : String(e))
    } finally {
      setActionPending(false)
    }
  }, [caseId, approvalComment, load])

  const postCancel = useCallback(async () => {
    setActionPending(true); setActionError(null)
    try {
      const res = await apiFetch(`/api/teo/v1/${caseId}/cancel`, { method: 'POST' })
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
    if (isCurrentAssignee && data.status === 'PendingManager') {
      actions.push(
        { id: 'reject',  label: '退件 / Reject',  variant: 'destructive', pending: actionPending, onClick: () => postDecision('manager-decision', false) },
        { id: 'approve', label: '核准 / Approve', variant: 'primary',     pending: actionPending, onClick: () => postDecision('manager-decision', true)  },
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
        variant: 'primary', pending: actionPending,
        onClick: () => navigate(`/apply/TEO?resubmit=${data.id}`),
      })
    }
    // Submitter may withdraw their own case while it is still in flight
    // (any non-terminal stage: PendingManager / PendingFinance / ResubmitRequired).
    if (isSubmitter && data.status !== 'Completed' && data.status !== 'Cancelled') {
      actions.push({ id: 'withdraw', label: '撤回申請', variant: 'destructive', pending: actionPending, confirm: { titleZh: '撤回申請？', description: '撤回後無法復原。', confirmText: '確認撤回' }, onClick: () => postCancel() })
    }
    return actions
  }, [data, isCurrentAssignee, isSubmitter, actionPending, postDecision, postCancel, navigate])

  const footerHint = (() => {
    if (!data) return null
    if (actionError) return <span className="text-danger">{actionError}</span>
    if (isCurrentAssignee && (data.status === 'PendingManager' || data.status === 'PendingFinance')) return '請審閱簽核意見後決定'
    if (isSubmitter && data.status === 'ResubmitRequired') return '請依照退件意見修正後重新送出'
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
            差旅費用核銷案件 <span className="ml-2 text-base font-medium text-ink-muted">· TEO V1</span>
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
          <FlowStateBanner flowCode="TEO" flowVersion={1} />

          <SectionCard className="!p-0">
            <div className="bg-slate-50 px-4 py-2">
              <Stepper steps={FORMS.TEO.steps} activeStep={activeStepFor(data.status)} withZh />
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
              <Stat label="關聯差旅單" value={<span className="font-mono text-xs">{data.travelRequestNo}</span>} />
            </div>
            {data.roundCount > 1 && (
              <div className="border-t border-rule px-5 py-3">
                <InfoBanner>本案件為第 <strong>{data.roundCount}</strong> 次送出（先前曾被退回 / 補件）。</InfoBanner>
              </div>
            )}
          </SectionCard>

          <SectionCard>
            <SectionTitle>差旅費用明細 · {data.expenseItems.length}</SectionTitle>
            <div className="space-y-3 px-5 py-4">
              {data.expenseItems.map((it, i) => <ItemReadCard key={i} index={i} value={it} />)}
              {(() => {
                const totalLcy = data.expenseItems.reduce((acc, it) => {
                  const n = parseFloat((it.amountLcy ?? '').toString())
                  return Number.isNaN(n) ? acc : acc + n
                }, 0)
                return totalLcy > 0 ? (
                  <div className="flex flex-wrap items-center justify-end gap-x-6 gap-y-1 rounded-md border border-rule bg-slate-50 px-4 py-3">
                    <span className="text-sm font-semibold text-ink">本幣合計 / Total (LCY)</span>
                    <span className="font-mono text-base font-bold text-danger">
                      NTD {totalLcy.toLocaleString('en-US', { maximumFractionDigits: 2 })}
                    </span>
                  </div>
                ) : null
              })()}
            </div>
          </SectionCard>

          <SectionCard>
            <SectionTitle>簽核時序 / Approval Timeline</SectionTitle>
            <ol className="divide-y divide-slate-100">
              <TimelineRow label="員工申請 / Submit" actor={data.submitterDisplayName ?? '—'} state="done" at={data.submittedAt} />
              <TimelineRow label="主管核准 / Manager" actor={data.managerDecision?.displayName ?? '—'}
                state={data.managerDecision?.approved === true ? 'done' : data.managerDecision?.approved === false ? 'rejected' : data.status === 'PendingManager' ? 'current' : 'idle'}
                at={data.managerDecision?.decidedAt} comment={data.managerDecision?.comment} />
              <TimelineRow label="財務審定 / Finance" actor={data.financeDecision?.displayName ?? '—'}
                state={data.financeDecision?.approved === true ? 'done' : data.financeDecision?.approved === false ? 'rejected' : data.status === 'PendingFinance' ? 'current' : 'idle'}
                at={data.financeDecision?.decidedAt} comment={data.financeDecision?.comment} />
              <TimelineRow label="入帳 / Closed" actor={data.status === 'Completed' ? '系統' : data.status === 'Cancelled' ? '申請人撤回' : '—'} state={data.status === 'Completed' ? 'done' : data.status === 'Cancelled' ? 'rejected' : 'idle'} at={data.completedAt} />
            </ol>
          </SectionCard>

          {isCurrentAssignee && (data.status === 'PendingManager' || data.status === 'PendingFinance') && (
            <SectionCard>
              <SectionTitle>您的決定 / Your decision</SectionTitle>
              <div className="space-y-3 px-5 py-4">
                <Field label="簽核意見 / Comment" hint="退件時建議填寫原因">
                  <Textarea rows={3} value={approvalComment} onChange={e => setApprovalComment(e.target.value)} disabled={actionPending} />
                </Field>
              </div>
            </SectionCard>
          )}
        </>
      )}

      <BpmnView
        open={bpmnOpen}
        onClose={() => setBpmnOpen(false)}
        formLabel={`${FORMS.TEO.code} — ${FORMS.TEO.label}`}
        steps={FORMS.TEO.steps}
        activeStep={0}
        ownerByStep={FORMS.TEO.ownerByStep}
        bpmnXml={TEO_V1_BpmnXml}
        completedNodes={trail?.completed}
        currentNode={trail?.current}
      />

      <ActionFooter hint={footerHint} actions={footerActions} />
    </div>
  )
}

function ItemReadCard({ index, value }: { index: number; value: TEO_V1_ExpenseItemDto }) {
  return (
    <div className="rounded-md border border-rule bg-card p-4">
      <div className="mb-3 flex items-center justify-between">
        <p className="text-sm font-semibold text-ink">#{index + 1} · {value.category || '—'}</p>
        <p className="font-mono text-xs text-ink-faint">{value.date?.slice(0, 10)}</p>
      </div>
      <div className="grid grid-cols-12 gap-3 text-sm">
        <ReadField label="國家" value={value.country} className="col-span-6" />
        <ReadMoney label="金額 (原幣)" value={value.amount} className="col-span-3" />
        <ReadMoney label="本幣金額" prefix="NTD" value={value.amountLcy} className="col-span-3" />
        <ReadField label="說明" value={value.description} className="col-span-12" />
      </div>
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

function ReadMoney({ label, value, prefix, className }: {
  label: string; value: string | null | undefined; prefix?: string; className?: string
}) {
  const n = parseFloat((value ?? '').toString())
  const formatted = Number.isNaN(n)
    ? null
    : `${prefix ? `${prefix} ` : ''}${n.toLocaleString('en-US', { maximumFractionDigits: 2 })}`
  return (
    <div className={className}>
      <p className="text-[11px] font-semibold uppercase tracking-wider text-ink-muted">{label}</p>
      <p className="mt-1 text-right font-mono text-ink">
        {formatted ?? <span className="text-ink-faint">—</span>}
      </p>
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

function statusKind(s: TEO_V1_Status): StatusKind {
  switch (s) {
    case 'PendingManager':   return 'pending'
    case 'PendingFinance':   return 'fin_review'
    case 'ResubmitRequired': return 'returned'
    case 'Completed':        return 'closed'
    case 'Cancelled':        return 'cancelled'
  }
}

/** Map the case status → index into FORMS.TEO.steps for the header stepper
 *  (apply 0 · approve 1 · fin_review 2 · close 3). */
function activeStepFor(status: TEO_V1_Status): number {
  switch (status) {
    case 'PendingManager':   return 1
    case 'PendingFinance':   return 2
    case 'ResubmitRequired': return 1
    case 'Completed':        return 3
    case 'Cancelled':        return 1
  }
}

function deriveTrail(status: TEO_V1_Status): { completed: string[]; current: string | null } {
  switch (status) {
    case 'PendingManager':   return { completed: ['s', 'exp'], current: 'ap' }
    case 'PendingFinance':   return { completed: ['s', 'exp', 'ap'], current: 'fin' }
    case 'ResubmitRequired': return { completed: ['s'], current: 'exp' }
    case 'Completed':        return { completed: ['s', 'exp', 'ap', 'fin', 'e'], current: null }
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
