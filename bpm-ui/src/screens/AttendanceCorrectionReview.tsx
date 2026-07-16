import { useCallback, useEffect, useState } from 'react'
import { useNavigate, useParams } from 'react-router-dom'
import { ArrowLeft, FileEdit } from 'lucide-react'

import { SectionCard, SectionTitle } from '@/components/ui/card'
import { Button } from '@/components/ui/button'
import { Textarea, Field } from '@/components/ui/form'
import { ActionFooter, type ActionFooterItem } from '@/components/ui/action-footer/ActionFooter'
import { decideCorrection, getCorrection } from '@/lib/api/attendance'
import { getJwt } from '@/lib/apiFetch'
import { decodeJwt } from '@/lib/jwt'
import { CorrectionStatus, PunchType, type CorrectionDto } from '@/types/attendance'

/** Manager-side review of a 補打卡 request (linked from the unified inbox).
 *  Requesters land here too and get a read-only status view. */
export function AttendanceCorrectionReview() {
  const { id } = useParams<{ id: string }>()
  const navigate = useNavigate()
  const [data, setData] = useState<CorrectionDto | null>(null)
  const [loading, setLoading] = useState(true)
  const [error, setError] = useState<string | null>(null)
  const [actionError, setActionError] = useState<string | null>(null)
  const [pending, setPending] = useState(false)
  const [note, setNote] = useState('')

  const viewerUserId = (() => {
    const jwt = getJwt()
    return jwt ? decodeJwt(jwt)?.sub ?? null : null
  })()

  const load = useCallback(async () => {
    if (!id) return
    try {
      setData(await getCorrection(id))
      setError(null)
    } catch (e) {
      setError(e instanceof Error ? e.message : String(e))
    } finally {
      setLoading(false)
    }
  }, [id])

  useEffect(() => { void load() }, [load])

  async function decide(approve: boolean) {
    if (!id) return
    setPending(true)
    setActionError(null)
    try {
      await decideCorrection(id, approve, note.trim() || null)
      await load()
    } catch (e) {
      setActionError(e instanceof Error ? e.message : String(e))
    } finally {
      setPending(false)
    }
  }

  if (loading) return <div className="mx-auto max-w-screen-md py-10 md:px-4 text-sm text-ink-muted">Loading…</div>
  if (error || !data) {
    return (
      <div className="mx-auto max-w-screen-md py-10 md:px-4">
        <SectionCard>
          <div className="space-y-3 px-5 py-6">
            <p className="text-sm text-danger">載入失敗：{error ?? 'not found'}</p>
            <Button variant="outline" size="sm" onClick={() => navigate(-1)}>
              <ArrowLeft className="h-3.5 w-3.5" /> 返回
            </Button>
          </div>
        </SectionCard>
      </div>
    )
  }

  const isRequester = viewerUserId != null && viewerUserId.toLowerCase() === data.userId.toLowerCase()
  const isPending = data.status === CorrectionStatus.Pending
  const canDecide = isPending && !isRequester

  const actions: ActionFooterItem[] = canDecide ? [
    {
      id: 'reject',
      label: '駁回',
      variant: 'outline',
      pending,
      confirm: {
        titleZh: '駁回補卡申請',
        description: `駁回 ${data.userName} 的補卡申請（${data.date} ${data.punchType === PunchType.In ? '上班卡' : '下班卡'}）。申請人會收到通知。`,
        confirmText: '駁回',
        tone: 'danger',
      },
      onClick: () => decide(false),
    },
    {
      id: 'approve',
      label: '核准補卡',
      variant: 'primary',
      pending,
      confirm: {
        titleZh: '核准補卡申請',
        description: `核准後系統會自動補上 ${data.userName} 在 ${data.date} ${formatTime(data.requestedPunchAt)} 的${data.punchType === PunchType.In ? '上班' : '下班'}卡。`,
        confirmText: '核准',
      },
      onClick: () => decide(true),
    },
  ] : []

  return (
    <div className="mx-auto max-w-screen-md space-y-4 pb-24 md:px-4 md:pt-6">
      <div className="flex items-center justify-between">
        <div>
          <h1 className="flex items-center gap-2 text-xl font-bold text-ink">
            <FileEdit className="h-5 w-5 text-primary" /> 補打卡申請
          </h1>
          <p className="text-[11px] uppercase tracking-wider text-ink-muted">Attendance correction · {data.id.slice(0, 8)}</p>
        </div>
        <StatusChip status={data.status} />
      </div>

      <SectionCard>
        <SectionTitle>申請內容</SectionTitle>
        <dl className="grid grid-cols-2 gap-x-6 gap-y-4 p-5 text-sm">
          <ReadField label="申請人 / Requester" value={data.userName} />
          <ReadField label="日期 / Date" value={data.date} />
          <ReadField label="卡別 / Type" value={data.punchType === PunchType.In ? '上班卡 / Check-in' : '下班卡 / Check-out'} />
          <ReadField label="補卡時間 / Time" value={formatTime(data.requestedPunchAt)} />
          <ReadField label="送出時間 / Submitted" value={formatDateTime(data.submittedAt)} />
          <div className="col-span-2">
            <ReadField label="事由 / Reason" value={data.reason} />
          </div>
        </dl>
      </SectionCard>

      {data.status !== CorrectionStatus.Pending && (
        <SectionCard>
          <SectionTitle>審核結果</SectionTitle>
          <dl className="grid grid-cols-2 gap-x-6 gap-y-4 p-5 text-sm">
            <ReadField label="審核人 / Reviewer" value={data.deciderName ?? '—'} />
            <ReadField label="審核時間 / Decided" value={data.decidedAt ? formatDateTime(data.decidedAt) : '—'} />
            <div className="col-span-2">
              <ReadField label="備註 / Note" value={data.decisionNote ?? '—'} />
            </div>
          </dl>
        </SectionCard>
      )}

      {canDecide && (
        <SectionCard>
          <SectionTitle>審核 / Review</SectionTitle>
          <div className="space-y-3 p-5">
            <Field label="備註 / Note（選填）">
              <Textarea rows={2} value={note} onChange={e => setNote(e.target.value)} placeholder="核准或駁回的補充說明，申請人看得到" />
            </Field>
            {actionError && <p className="rounded border border-red-200 bg-red-50 px-3 py-2 text-xs text-red-700">{actionError}</p>}
          </div>
        </SectionCard>
      )}

      {isPending && isRequester && (
        <p className="text-xs text-ink-muted">等待主管審核中——核准後打卡紀錄會自動補上。</p>
      )}

      {actions.length > 0 && <ActionFooter actions={actions} />}
    </div>
  )
}

function StatusChip({ status }: { status: number }) {
  const map: Record<number, { label: string; cls: string }> = {
    [CorrectionStatus.Pending]:  { label: '待主管核准', cls: 'bg-amber-50 text-amber-700 border-amber-200' },
    [CorrectionStatus.Approved]: { label: '已核准補卡', cls: 'bg-green-50 text-green-700 border-green-200' },
    [CorrectionStatus.Rejected]: { label: '已駁回', cls: 'bg-red-50 text-red-700 border-red-200' },
  }
  const m = map[status] ?? { label: String(status), cls: 'bg-slate-50 text-ink border-slate-200' }
  return <span className={`inline-block rounded-full border px-3 py-1 text-xs font-medium ${m.cls}`}>{m.label}</span>
}

function ReadField({ label, value }: { label: string; value: string }) {
  return (
    <div>
      <dt className="text-[11px] font-semibold uppercase tracking-wider text-ink-muted">{label}</dt>
      <dd className="mt-1 text-ink">{value || <span className="text-ink-faint">—</span>}</dd>
    </div>
  )
}

function formatTime(iso: string): string {
  return new Date(iso).toLocaleTimeString('zh-TW', { hour: '2-digit', minute: '2-digit' })
}

function formatDateTime(iso: string): string {
  return new Date(iso).toLocaleString('zh-TW', { year: 'numeric', month: '2-digit', day: '2-digit', hour: '2-digit', minute: '2-digit' })
}
