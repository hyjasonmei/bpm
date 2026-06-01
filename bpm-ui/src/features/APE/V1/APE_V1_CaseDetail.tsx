import { useCallback, useEffect, useMemo, useState } from 'react'
import { ArrowLeft, CheckCircle2, RotateCcw, Workflow as WorkflowIcon, XCircle } from 'lucide-react'
import { useNavigate } from 'react-router-dom'

import { Button } from '@/components/ui/button'
import { SectionCard, SectionTitle } from '@/components/ui/card'
import { StatusBadge, type StatusKind } from '@/components/ui/badge'
import { Field, Textarea, InfoBanner } from '@/components/ui/form'
import { ActionFooter, type ActionFooterItem } from '@/components/ui/action-footer/ActionFooter'
import { FlowStateBanner } from '@/components/ui/flow-state-banner/FlowStateBanner'
import { BpmnView } from '@/components/BpmnView'
import { apiFetch, getJwt } from '@/lib/apiFetch'
import { decodeJwt } from '@/lib/jwt'
import { FORMS } from '@/lib/workflow'
import type { CaseDetailProps } from '@/features/registry'
import APE_V1_BpmnXml from './APE_V1.bpmn.xml?raw'
import { zhStatus } from './APE_V1_shared'
import type { APE_V1_CaseResponse, APE_V1_Status } from './APE_V1_types'

/**
 * Read-mostly detail page for an APE V1 case. Role-scoped ActionFooter:
 *  - PendingManager   + isManagerAssignee → Reject / Approve
 *  - ResubmitRequired + isSubmitter       → Edit & resubmit
 */
export function APE_V1_CaseDetail({ caseId }: CaseDetailProps) {
  const navigate = useNavigate()
  const [data, setData] = useState<APE_V1_CaseResponse | null>(null)
  const [viewerUserId, setViewerUserId] = useState<string | null>(null)
  const [loading, setLoading] = useState(true)
  const [error, setError] = useState<string | null>(null)
  const [actionPending, setActionPending] = useState(false)
  const [actionError, setActionError] = useState<string | null>(null)
  const [bpmnOpen, setBpmnOpen] = useState(false)
  const [approvalComment, setApprovalComment] = useState('')

  const load = useCallback(async () => {
    try {
      const res = await apiFetch(`/api/ape/v1/${caseId}`)
      if (!res.ok) throw new Error(`HTTP ${res.status}`)
      setData((await res.json()) as APE_V1_CaseResponse)
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

  const postDecision = useCallback(async (approve: boolean) => {
    setActionPending(true); setActionError(null)
    try {
      const res = await apiFetch(`/api/ape/v1/${caseId}/manager-decision`, {
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

  const footerActions: ActionFooterItem[] = useMemo(() => {
    if (!data) return []
    if (isCurrentAssignee && data.status === 'PendingManager') {
      return [
        { id: 'reject',  label: '退件 / Reject',  variant: 'destructive', pending: actionPending, onClick: () => postDecision(false) },
        { id: 'approve', label: '核准 / Approve', variant: 'primary',     pending: actionPending, onClick: () => postDecision(true)  },
      ]
    }
    if (isSubmitter && data.status === 'ResubmitRequired') {
      return [{
        id: 'resubmit',
        label: <span className="inline-flex items-center gap-1"><RotateCcw className="h-3.5 w-3.5" />修正後重新送出</span>,
        variant: 'primary', pending: actionPending,
        onClick: () => navigate(`/apply/APE?resubmit=${data.id}`),
      }]
    }
    return []
  }, [data, isCurrentAssignee, isSubmitter, actionPending, postDecision, navigate])

  const footerHint = (() => {
    if (!data) return null
    if (actionError) return <span className="text-danger">{actionError}</span>
    if (isCurrentAssignee && data.status === 'PendingManager') return '請審閱簽核意見後決定'
    if (isSubmitter && data.status === 'ResubmitRequired') return '請依照退件意見修正後重新送出'
    return null
  })()

  const fmtAmount = (n: number, ccy: string) =>
    `${ccy} ${new Intl.NumberFormat('en-US').format(n)}`

  return (
    <div className="mx-auto max-w-screen-lg space-y-4 p-6 pb-24">
      <div className="flex items-center gap-3">
        <Button variant="outline" size="sm" onClick={() => navigate('/')}>
          <ArrowLeft className="h-3.5 w-3.5" /> 返回
        </Button>
        <div className="min-w-0">
          <h1 className="truncate text-lg font-bold text-ink">
            預支現金案件 <span className="ml-2 text-base font-medium text-ink-muted">· APE V1</span>
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
          <FlowStateBanner flowCode="APE" flowVersion={1} />

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
              <Stat label="金額" value={<span className="font-mono">{fmtAmount(data.amount, data.currency)}</span>} />
            </div>
            {data.roundCount > 1 && (
              <div className="border-t border-rule px-5 py-3">
                <InfoBanner>本案件為第 <strong>{data.roundCount}</strong> 次送出（先前曾被退回 / 補件）。</InfoBanner>
              </div>
            )}
          </SectionCard>

          <SectionCard>
            <SectionTitle>預支資訊 / Advance Payment</SectionTitle>
            <div className="grid grid-cols-12 gap-3 px-5 py-4 text-sm">
              <ReadField label="預計領款日" value={data.expectReceiveDate?.slice(0, 10)} className="col-span-6" />
              <ReadField label="沖還日" value={data.deductReturnDate?.slice(0, 10)} className="col-span-6" />
              <ReadField label="費用部門" value={data.chargeDepartment} className="col-span-6" />
              <ReadField label="轉嫁海外" value={data.rechargeOutside} className="col-span-6" />
              <ReadField label="金額" value={fmtAmount(data.amount, data.currency)} className="col-span-6" />
              <ReadField label="說明" value={data.description} className="col-span-12" />
            </div>
          </SectionCard>

          <SectionCard>
            <SectionTitle>簽核時序 / Approval Timeline</SectionTitle>
            <ol className="divide-y divide-slate-100">
              <TimelineRow label="員工申請 / Submit" actor={data.submitterDisplayName ?? '—'} state="done" at={data.submittedAt} />
              <TimelineRow
                label="主管核准 / Manager"
                actor={data.managerDecision?.displayName ?? '—'}
                state={data.managerDecision?.approved === true ? 'done'
                  : data.managerDecision?.approved === false ? 'rejected'
                  : data.status === 'PendingManager' ? 'current' : 'idle'}
                at={data.managerDecision?.decidedAt}
                comment={data.managerDecision?.comment}
              />
              <TimelineRow label="結案 / Closed" actor={data.status === 'Completed' ? '系統' : '—'} state={data.status === 'Completed' ? 'done' : 'idle'} at={data.completedAt} />
            </ol>
          </SectionCard>

          {isCurrentAssignee && data.status === 'PendingManager' && (
            <SectionCard>
              <SectionTitle>您的決定 / Your decision</SectionTitle>
              <div className="space-y-3 px-5 py-4">
                <Field label="簽核意見 / Comment" hint="退件時建議填寫原因">
                  <Textarea rows={3} value={approvalComment} onChange={e => setApprovalComment(e.target.value)} disabled={actionPending} />
                </Field>
              </div>
            </SectionCard>
          )}

          {!isCurrentAssignee && !isSubmitter && data.status !== 'Completed' && (
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
        formLabel={`${FORMS.APE.code} — ${FORMS.APE.label}`}
        steps={FORMS.APE.steps}
        activeStep={0}
        ownerByStep={FORMS.APE.ownerByStep}
        bpmnXml={APE_V1_BpmnXml}
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

function statusKind(s: APE_V1_Status): StatusKind {
  switch (s) {
    case 'PendingManager':   return 'pending'
    case 'ResubmitRequired': return 'returned'
    case 'Completed':        return 'closed'
  }
}

function deriveTrail(status: APE_V1_Status): { completed: string[]; current: string | null } {
  switch (status) {
    case 'PendingManager':   return { completed: ['s', 'ape'], current: 'ap' }
    case 'ResubmitRequired': return { completed: ['s'], current: 'ape' }
    case 'Completed':        return { completed: ['s', 'ape', 'ap', 'e'], current: null }
  }
}

function formatDate(iso: string | null | undefined): string {
  if (!iso) return '—'
  const d = new Date(iso)
  if (Number.isNaN(d.getTime())) return iso
  const pad = (n: number) => String(n).padStart(2, '0')
  return `${d.getFullYear()}/${pad(d.getMonth() + 1)}/${pad(d.getDate())} ${pad(d.getHours())}:${pad(d.getMinutes())}`
}
