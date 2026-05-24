import { useEffect, useState } from 'react'
import { ArrowLeft, Clock, CheckCircle2, XCircle, AlertTriangle, FileText } from 'lucide-react'
import { useNavigate } from 'react-router-dom'

import { Button } from '@/components/ui/button'
import { SectionCard, SectionTitle } from '@/components/ui/card'
import { StatusBadge } from '@/components/ui/badge'
import { cn } from '@/lib/cn'
import { getInstance, getInstanceHistory } from '@/lib/api/process'
import type { ProcessInstanceDto, TaskHistoryDto, InstanceStatus } from '@/types/process'
import { FORMS, type FormCode } from '@/lib/workflow'

interface CaseDetailProps {
  instanceId: string
}

export function CaseDetail({ instanceId }: CaseDetailProps) {
  const navigate = useNavigate()
  const onBack = () => navigate('/')
  const [instance, setInstance] = useState<ProcessInstanceDto | null>(null)
  const [history, setHistory] = useState<TaskHistoryDto[]>([])
  const [loading, setLoading] = useState(true)
  const [error, setError] = useState<string | null>(null)

  useEffect(() => {
    let cancelled = false
    setLoading(true)
    setError(null)
    Promise.all([
      getInstance(instanceId),
      getInstanceHistory(instanceId, undefined, 200),
    ])
      .then(([inst, hist]) => {
        if (cancelled) return
        setInstance(inst)
        setHistory(hist.items)
      })
      .catch(e => { if (!cancelled) setError(e instanceof Error ? e.message : String(e)) })
      .finally(() => { if (!cancelled) setLoading(false) })
    return () => { cancelled = true }
  }, [instanceId])

  const code = isFormCode(instance?.specCode) ? instance!.specCode : null
  const def = code ? FORMS[code] : null

  return (
    <div className="mx-auto max-w-screen-lg space-y-4 p-6">
      <div className="flex items-center gap-3">
        <Button variant="outline" size="sm" onClick={onBack}>
          <ArrowLeft className="h-3.5 w-3.5" /> 返回
        </Button>
        <div className="min-w-0">
          <h1 className="truncate text-lg font-bold text-ink">
            案件詳情 {def && <span className="ml-2 text-base font-medium text-ink-muted">· {def.zhLabel}</span>}
          </h1>
          <p className="font-mono text-[11px] text-ink-faint">{instanceId}</p>
        </div>
      </div>

      {loading && (
        <SectionCard><div className="px-5 py-10 text-center text-sm text-ink-muted">載入中…</div></SectionCard>
      )}
      {error && !loading && (
        <SectionCard><div className="px-5 py-6 text-sm text-danger">載入失敗：{error}</div></SectionCard>
      )}

      {instance && !loading && (
        <>
          <SectionCard>
            <SectionTitle>狀態</SectionTitle>
            <div className="grid grid-cols-2 gap-4 px-5 py-4 text-sm md:grid-cols-4">
              <Stat label="目前狀態" value={<StatusBadge kind={statusBadge(instance.status)} />} />
              <Stat label="開始時間" value={<span className="font-mono">{formatDate(instance.startedAt)}</span>} />
              <Stat label="完成時間" value={<span className="font-mono">{instance.completedAt ? formatDate(instance.completedAt) : '—'}</span>} />
              <Stat label="未結任務" value={<span className="font-bold tabular">{instance.openTasks.length}</span>} />
            </div>
            {instance.cancelReason && (
              <div className="border-t border-rule bg-rose-50/50 px-5 py-2 text-xs text-rose-700">
                取消原因：{instance.cancelReason}
              </div>
            )}
          </SectionCard>

          <SectionCard>
            <SectionTitle>申請內容 / Submitted form data</SectionTitle>
            {instance.currentFormData && typeof instance.currentFormData === 'object' && Object.keys(instance.currentFormData as object).length > 0 ? (
              <FormDataView data={instance.currentFormData} code={code} />
            ) : (
              <div className="px-5 py-6 text-center text-sm text-ink-faint">尚無申請內容快照。</div>
            )}
          </SectionCard>

          {instance.openTasks.length > 0 && (
            <SectionCard>
              <SectionTitle>進行中任務</SectionTitle>
              <div className="divide-y divide-slate-100">
                {instance.openTasks.map(t => (
                  <div key={t.id} className="flex items-center justify-between gap-3 px-5 py-3 text-sm">
                    <div className="min-w-0">
                      <p className="font-medium text-ink">{t.nodeId}</p>
                      <p className="text-[11px] text-ink-muted">{t.nodeKind} · 指派給 <span className="font-mono">{t.actualAssigneeUserId?.slice(0, 8) ?? '—'}</span></p>
                    </div>
                    <StatusBadge kind="pending" />
                  </div>
                ))}
              </div>
            </SectionCard>
          )}

          <SectionCard>
            <SectionTitle right={<span className="text-xs text-ink-muted">{history.length} 筆</span>}>
              歷程
            </SectionTitle>
            {history.length === 0 ? (
              <div className="px-5 py-6 text-center text-sm text-ink-faint">尚無歷程紀錄。</div>
            ) : (
              <ol className="divide-y divide-slate-100">
                {history.map(h => (
                  <li key={h.id} className="flex items-start gap-3 px-5 py-3">
                    <span className={cn('mt-0.5 inline-flex h-6 w-6 shrink-0 items-center justify-center rounded-full', eventIconBg(h.eventType))}>
                      {eventIcon(h.eventType)}
                    </span>
                    <div className="min-w-0 flex-1 text-sm">
                      <p className="text-ink"><span className="font-medium">{labelForEvent(h.eventType)}</span></p>
                      <p className="mt-0.5 font-mono text-[11px] text-ink-faint">
                        {formatDate(h.createdAt)}
                        {h.actorUserId && <span> · actor {h.actorUserId.slice(0, 8)}</span>}
                        {h.taskId && <span> · task {h.taskId.slice(0, 8)}</span>}
                      </p>
                    </div>
                  </li>
                ))}
              </ol>
            )}
          </SectionCard>
        </>
      )}
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

// Human-readable field labels per flow code. Falls back to the raw
// field key when no entry is found.
const FIELD_LABELS: Partial<Record<FormCode, Record<string, string>>> = {
  LEAVE: {
    leave_type: '假別 / Leave Type',
    date_range: '期間 / Date range',
    days: '天數 / Days',
    reason: '事由 / Reason',
    cert: '證明文件 / Cert',
    archive_note: '備案備註 / Archive note',
  },
}

function FormDataView({ data, code }: { data: unknown; code: FormCode | null }) {
  if (data == null || typeof data !== 'object') {
    return <div className="px-5 py-4 text-sm text-ink-muted"><code className="font-mono text-xs">{String(data)}</code></div>
  }
  const labels = (code && FIELD_LABELS[code]) ?? {}
  const entries = Object.entries(data as Record<string, unknown>)
  return (
    <dl className="divide-y divide-slate-100">
      {entries.map(([key, value]) => (
        <div key={key} className="grid grid-cols-[180px_1fr] gap-3 px-5 py-3 text-sm">
          <dt className="text-ink-muted">{labels[key] ?? <code className="font-mono text-xs">{key}</code>}</dt>
          <dd className="text-ink">{renderValue(value)}</dd>
        </div>
      ))}
    </dl>
  )
}

function renderValue(v: unknown): React.ReactNode {
  if (v == null) return <span className="text-ink-faint">—</span>
  if (typeof v === 'string') return v === '' ? <span className="text-ink-faint">（空）</span> : v
  if (typeof v === 'number' || typeof v === 'boolean') return <span className="font-mono tabular">{String(v)}</span>
  if (Array.isArray(v)) {
    if (v.length === 0) return <span className="text-ink-faint">[]</span>
    return (
      <ul className="space-y-1">
        {v.map((item, i) => <li key={i}>{renderValue(item)}</li>)}
      </ul>
    )
  }
  if (typeof v === 'object') {
    const o = v as Record<string, unknown>
    if ('start' in o && 'end' in o) {
      return <span className="font-mono">{String(o.start ?? '?')} → {String(o.end ?? '?')}</span>
    }
    return (
      <div className="space-y-1 rounded border border-rule bg-slate-50/50 p-2 text-xs">
        {Object.entries(o).map(([k, val]) => (
          <div key={k} className="grid grid-cols-[120px_1fr] gap-2">
            <span className="text-ink-muted">{k}</span>
            <span>{renderValue(val)}</span>
          </div>
        ))}
      </div>
    )
  }
  return <code className="font-mono text-xs">{String(v)}</code>
}

const FORM_CODES: ReadonlyArray<FormCode> = ['LEAVE', 'GEE', 'GEV', 'APE', 'TRQ', 'TEO', 'HWP', 'ITPR', 'EXTOB', 'RESIGN', 'DEPTX']
function isFormCode(s: string | undefined | null): s is FormCode {
  return s != null && (FORM_CODES as readonly string[]).includes(s)
}

function statusBadge(s: InstanceStatus): 'pending' | 'closed' | 'rejected' | 'returned' {
  switch (s) {
    case 'Completed': return 'closed'
    case 'Cancelled': return 'rejected'
    case 'Errored':   return 'returned'
    case 'Running':   return 'pending'
  }
}

function formatDate(iso: string): string {
  const d = new Date(iso)
  if (Number.isNaN(d.getTime())) return iso
  const pad = (n: number) => String(n).padStart(2, '0')
  return `${d.getFullYear()}/${pad(d.getMonth() + 1)}/${pad(d.getDate())} ${pad(d.getHours())}:${pad(d.getMinutes())}`
}

function labelForEvent(t: string): string {
  switch (t) {
    case 'TaskSpawned': return '任務指派'
    case 'TaskClaimed': return '任務認領'
    case 'TaskSubmitted': return '任務送出'
    case 'TaskApproved': return '核准'
    case 'TaskRejected': return '退件'
    case 'TaskReturned': return '退回'
    case 'InstanceStarted': return '案件啟動'
    case 'InstanceCompleted': return '案件完成'
    case 'InstanceCancelled': return '案件取消'
    case 'NotificationFired': return '通知發送'
    case 'DelegationApplied': return '套用代理'
    default: return t
  }
}

function eventIcon(t: string): React.ReactNode {
  if (t === 'TaskApproved' || t === 'InstanceCompleted') return <CheckCircle2 className="h-3 w-3 text-good" />
  if (t === 'TaskRejected' || t === 'InstanceCancelled') return <XCircle className="h-3 w-3 text-danger" />
  if (t === 'TaskReturned') return <AlertTriangle className="h-3 w-3 text-amber-600" />
  if (t === 'NotificationFired') return <FileText className="h-3 w-3 text-blue-600" />
  return <Clock className="h-3 w-3 text-ink-muted" />
}

function eventIconBg(t: string): string {
  if (t === 'TaskApproved' || t === 'InstanceCompleted') return 'bg-green-50'
  if (t === 'TaskRejected' || t === 'InstanceCancelled') return 'bg-red-50'
  if (t === 'TaskReturned') return 'bg-amber-50'
  if (t === 'NotificationFired') return 'bg-blue-50'
  return 'bg-slate-100'
}
