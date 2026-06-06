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
import { AuthedFileLink } from '@/components/ui/FilePicker'
import { apiFetch, getJwt } from '@/lib/apiFetch'
import { decodeJwt } from '@/lib/jwt'
import { useDelegatedFor } from '@/lib/useDelegatedFor'
import { FORMS } from '@/lib/workflow'
import type { CaseDetailProps } from '@/features/registry'
import LEAVE_V1_BpmnXml from './LEAVE_V1.bpmn.xml?raw'
import type { LEAVE_V1_CaseResponse, LEAVE_V1_Status } from './LEAVE_V1_types'

/**
 * Read-mostly detail page for a LEAVE V1 case. Approve / Reject /
 * HR-archive controls render conditionally when the viewer is the
 * current assignee. View BPMN modal lights up the spec node ids the
 * case has walked through.
 */
export function LEAVE_V1_CaseDetail({ caseId }: CaseDetailProps) {
  const navigate = useNavigate()
  const [data, setData] = useState<LEAVE_V1_CaseResponse | null>(null)
  const [viewerUserId, setViewerUserId] = useState<string | null>(null)
  const [loading, setLoading] = useState(true)
  const [error, setError] = useState<string | null>(null)
  const [actionPending, setActionPending] = useState(false)
  const [actionError, setActionError] = useState<string | null>(null)
  const [bpmnOpen, setBpmnOpen] = useState(false)

  const load = useCallback(async () => {
    try {
      const res = await apiFetch(`/api/leave/v1/${caseId}`)
      if (!res.ok) throw new Error(`HTTP ${res.status}`)
      const body = (await res.json()) as LEAVE_V1_CaseResponse
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
    // Viewer user id = JWT `sub` claim (same value the backend reads).
    const tok = getJwt()
    if (!tok) return
    const decoded = decodeJwt(tok)
    if (decoded?.sub) setViewerUserId(decoded.sub)
  }, [])

  const delegatedFor = useDelegatedFor()
  // The viewer may act on the case if they are the current assignee OR an active
  // delegate of the current assignee (delegation-aware — see useDelegatedFor).
  const isCurrentAssignee = !!data && !!viewerUserId && !!data.currentAssigneeUserId &&
    (data.currentAssigneeUserId === viewerUserId || delegatedFor.includes(data.currentAssigneeUserId))
  const isSubmitter       = !!data && !!viewerUserId && data.submitterUserId === viewerUserId
  const trail = useMemo(() => (data ? deriveTrail(data.status) : null), [data])

  // Decision form holds the comment / note text; ActionFooter at the
  // bottom posts it. Lifted out of the inline cards so the buttons can
  // live in the sticky bar.
  const [approvalComment, setApprovalComment] = useState('')
  const [archiveNote, setArchiveNote] = useState('')

  const postApproval = useCallback(async (approve: boolean) => {
    if (!data) return
    setActionPending(true); setActionError(null)
    try {
      const path = data.status === 'PendingManager' ? 'manager-decision' : 'vp-decision'
      const res = await apiFetch(`/api/leave/v1/${caseId}/${path}`, {
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
  }, [data, caseId, approvalComment, load])

  const postArchive = useCallback(async () => {
    setActionPending(true); setActionError(null)
    try {
      const res = await apiFetch(`/api/leave/v1/${caseId}/hr-archive`, {
        method: 'POST',
        headers: { 'Content-Type': 'application/json' },
        body: JSON.stringify({ archiveNote }),
      })
      if (!res.ok) throw new Error(await res.text() || `HTTP ${res.status}`)
      setArchiveNote('')
      await load()
    } catch (e) {
      setActionError(e instanceof Error ? e.message : String(e))
    } finally {
      setActionPending(false)
    }
  }, [caseId, archiveNote, load])

  const postCancel = useCallback(async () => {
    if (!window.confirm('確定要撤回此申請？撤回後無法復原。')) return
    setActionPending(true); setActionError(null)
    try {
      const res = await apiFetch(`/api/leave/v1/${caseId}/cancel`, { method: 'POST' })
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
    if (isCurrentAssignee && (data.status === 'PendingManager' || data.status === 'PendingVp')) {
      actions.push(
        { id: 'reject',  label: '退件 / Reject',  variant: 'destructive', pending: actionPending, onClick: () => postApproval(false) },
        { id: 'approve', label: '核准 / Approve', variant: 'primary',     pending: actionPending, onClick: () => postApproval(true)  },
      )
    }
    if (isCurrentAssignee && data.status === 'PendingHr') {
      actions.push({
        id: 'archive',
        label: '送出備案 / Archive',
        variant: 'primary',
        disabled: !archiveNote.trim(),
        pending: actionPending,
        title: !archiveNote.trim() ? '請先填寫備案備註' : undefined,
        onClick: postArchive,
      })
    }
    // Submitter may withdraw their own case while it is still in flight.
    if (isSubmitter && data.status !== 'Completed' && data.status !== 'Rejected' && data.status !== 'Cancelled') {
      actions.push({ id: 'withdraw', label: '撤回申請', variant: 'destructive', pending: actionPending, onClick: () => postCancel() })
    }
    return actions
  }, [data, isCurrentAssignee, isSubmitter, actionPending, archiveNote, postApproval, postArchive, postCancel])

  const footerHint = (() => {
    if (!data) return null
    if (actionError) return <span className="text-danger">{actionError}</span>
    if (!isCurrentAssignee) return null
    if (data.status === 'PendingManager') return '請審閱簽核意見後決定'
    if (data.status === 'PendingVp')      return '請審閱簽核意見後決定（VP 階段）'
    if (data.status === 'PendingHr')      return '備註不可為空'
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
            請假案件 <span className="ml-2 text-base font-medium text-ink-muted">· LEAVE V1</span>
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
          <FlowStateBanner flowCode="LEAVE" flowVersion={1} />

          <SectionCard className="!p-0">
            <div className="bg-slate-50 px-4 py-2">
              <Stepper steps={FORMS.LEAVE.steps} activeStep={activeStepFor(data.status)} withZh />
            </div>
          </SectionCard>

          <SectionCard>
            <SectionTitle>狀態 / Status</SectionTitle>
            <div className="grid grid-cols-2 gap-4 px-5 py-4 text-sm md:grid-cols-4">
              <Stat label="目前狀態" value={<StatusBadge kind={statusKind(data.status)} />} />
              <Stat label="申請人" value={<span>{data.submitterDisplayName ?? '—'}</span>} />
              <Stat label="目前指派給" value={<span>{data.currentAssigneeDisplayName ?? '—'}</span>} />
              <Stat label="送出時間" value={<span className="font-mono">{formatDate(data.submittedAt)}</span>} />
            </div>
          </SectionCard>

          <SectionCard>
            <SectionTitle>申請內容</SectionTitle>
            <div className="grid grid-cols-2 gap-4 px-5 py-4 text-sm">
              <Row label="假別 / Leave Type" value={data.leaveType} />
              <Row label="天數 / Days" value={String(data.days)} />
              <Row label="期間 / Date Range" value={`${data.startDate} → ${data.endDate}`} />
              <Row label="證明文件 / Cert" value={data.certFileId ? <FileLink id={data.certFileId} /> : '—'} />
              <Row label="事由 / Reason" value={data.reason} fullWidth />
              {data.archiveNote && <Row label="備案備註 / Archive note" value={data.archiveNote} fullWidth />}
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
                label="主管核准 / Manager"
                actor={data.managerDecision?.displayName ?? '—'}
                state={decisionState(data.managerDecision?.approved)}
                at={data.managerDecision?.decidedAt}
                comment={data.managerDecision?.comment}
              />
              {(data.vpDecision || data.status === 'PendingVp') && (
                <TimelineRow
                  label="VP 核准 / VP"
                  actor={data.vpDecision?.displayName ?? '—'}
                  state={decisionState(data.vpDecision?.approved)}
                  at={data.vpDecision?.decidedAt}
                  comment={data.vpDecision?.comment}
                />
              )}
              <TimelineRow
                label="HR 備案 / HR Archive"
                actor={data.hrArchive?.displayName ?? '—'}
                state={data.hrArchive?.decidedAt ? 'done' : (data.status === 'PendingHr' ? 'current' : 'idle')}
                at={data.hrArchive?.decidedAt}
                comment={data.hrArchive?.comment}
              />
            </ol>
          </SectionCard>

          {isCurrentAssignee && (data.status === 'PendingManager' || data.status === 'PendingVp') && (
            <ApprovalForm
              stage={data.status}
              comment={approvalComment}
              onCommentChange={setApprovalComment}
              pending={actionPending}
            />
          )}

          {isCurrentAssignee && data.status === 'PendingHr' && (
            <HrArchiveForm
              note={archiveNote}
              onNoteChange={setArchiveNote}
              pending={actionPending}
            />
          )}

          {!isCurrentAssignee && data.status !== 'Completed' && data.status !== 'Rejected' && data.status !== 'Cancelled' && (
            <SectionCard>
              <InfoBanner>
                目前由 <strong>{data.currentAssigneeDisplayName ?? '—'}</strong> 處理；若您是此階段的處理人，請從首頁「Pending My Approval」進入。
              </InfoBanner>
            </SectionCard>
          )}
        </>
      )}

      <BpmnView
        open={bpmnOpen}
        onClose={() => setBpmnOpen(false)}
        formLabel={`${FORMS.LEAVE.code} — ${FORMS.LEAVE.label}`}
        steps={FORMS.LEAVE.steps}
        activeStep={0}
        ownerByStep={FORMS.LEAVE.ownerByStep}
        bpmnXml={LEAVE_V1_BpmnXml}
        completedNodes={trail?.completed}
        currentNode={trail?.current}
      />

      <ActionFooter hint={footerHint} actions={footerActions} />
    </div>
  )
}

function ApprovalForm({
  stage, comment, onCommentChange, pending,
}: {
  stage: 'PendingManager' | 'PendingVp'
  comment: string
  onCommentChange: (v: string) => void
  pending: boolean
}) {
  const label = stage === 'PendingManager' ? '主管核准' : 'VP 核准'
  return (
    <SectionCard>
      <SectionTitle>您的決定 / Your decision ({label})</SectionTitle>
      <div className="space-y-3 px-5 py-4">
        <Field label="簽核意見 / Comment" hint="退件時建議填寫原因">
          <Textarea rows={3} value={comment} onChange={e => onCommentChange(e.target.value)} disabled={pending} />
        </Field>
      </div>
    </SectionCard>
  )
}

function HrArchiveForm({
  note, onNoteChange, pending,
}: {
  note: string
  onNoteChange: (v: string) => void
  pending: boolean
}) {
  return (
    <SectionCard>
      <SectionTitle>HR 備案 / Archive</SectionTitle>
      <div className="space-y-3 px-5 py-4">
        <Field label="備案備註 / Archive note" required hint="HR 留下處理紀錄供日後追溯">
          <Textarea rows={3} value={note} onChange={e => onNoteChange(e.target.value)} disabled={pending} />
        </Field>
      </div>
    </SectionCard>
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

function Row({ label, value, fullWidth }: { label: string; value: React.ReactNode; fullWidth?: boolean }) {
  return (
    <div className={fullWidth ? 'col-span-2' : ''}>
      <p className="text-[11px] font-semibold uppercase tracking-wider text-ink-muted">{label}</p>
      <div className="mt-1 text-ink">{value}</div>
    </div>
  )
}

function FileLink({ id }: { id: string }) {
  // GET /api/files/{id} is auth'd — fetch with the JWT and open the blob,
  // otherwise a plain new-tab link 401s.
  return <AuthedFileLink id={id} className="text-primary hover:underline">下載證明</AuthedFileLink>
}

type TimelineState = 'idle' | 'current' | 'done' | 'rejected'

function decisionState(approved: boolean | null | undefined): TimelineState {
  if (approved === true) return 'done'
  if (approved === false) return 'rejected'
  return 'idle'
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

function statusKind(s: LEAVE_V1_Status): StatusKind {
  switch (s) {
    case 'Completed':       return 'closed'
    case 'Rejected':        return 'rejected'
    case 'Cancelled':       return 'cancelled'
    case 'PendingManager':  return 'pending'
    case 'PendingVp':       return 'pending'
    case 'PendingHr':       return 'hr_record'
  }
}

/** Map the case status → index into FORMS.LEAVE.steps for the header stepper
 *  (apply 0 · manager_approve 1 · hr_record 2 · closed 3). LEAVE.steps has no
 *  separate VP step, so PendingManager and PendingVp both map to the approve
 *  step. */
function activeStepFor(status: LEAVE_V1_Status): number {
  switch (status) {
    case 'PendingManager': return 1
    case 'PendingVp':      return 1
    case 'PendingHr':      return 2
    case 'Completed':      return 3
    case 'Rejected':       return 1
    case 'Cancelled':      return 1
  }
}

/**
 * Map per-flow status → BPMN node ids. Skipped branches (e.g. approval_vp
 * when days < 7) stay out of `completed` so the diagram shows them as
 * un-walked.
 */
function deriveTrail(status: LEAVE_V1_Status): { completed: string[]; current: string | null } {
  switch (status) {
    case 'PendingManager':
      return { completed: ['start_1', 'task_apply'], current: 'approval_manager' }
    case 'PendingVp':
      return { completed: ['start_1', 'task_apply', 'approval_manager', 'gateway_days'], current: 'approval_vp' }
    case 'PendingHr':
      // Could have walked through either the VP branch or the bypass.
      // We can't reliably tell from status alone, so include gateway + leave
      // VP/short-edge uncoloured — the diagram still reads correctly.
      return { completed: ['start_1', 'task_apply', 'approval_manager', 'gateway_days'], current: 'task_hr_archive' }
    case 'Completed':
      return { completed: ['start_1', 'task_apply', 'approval_manager', 'gateway_days', 'task_hr_archive', 'end_1'], current: null }
    case 'Rejected':
    case 'Cancelled':
      return { completed: ['start_1', 'task_apply'], current: null }
  }
}

function formatDate(iso: string): string {
  const d = new Date(iso)
  if (Number.isNaN(d.getTime())) return iso
  const pad = (n: number) => String(n).padStart(2, '0')
  return `${d.getFullYear()}/${pad(d.getMonth() + 1)}/${pad(d.getDate())} ${pad(d.getHours())}:${pad(d.getMinutes())}`
}
