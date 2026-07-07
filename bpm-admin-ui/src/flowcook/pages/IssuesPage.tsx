import { useCallback, useEffect, useState } from 'react'
import { Bug, CheckCircle2, Inbox, Lightbulb, Loader2, MessageCircleQuestion, RefreshCw } from 'lucide-react'
import { cn } from '@/lib/cn'
import { SectionCard, SectionTitle } from '@/components/ui/card'
import { Button } from '@/components/ui/button'
import { Badge } from '@/components/ui/badge'
import { IssueStatus, listIssues, setIssueStatus, type IssueDto, type IssueStatusValue } from '@/flowcook/api/issues'

const KIND_META: Record<string, { label: string; icon: React.ComponentType<{ className?: string }>; cls: string }> = {
  bug:      { label: 'Bug',      icon: Bug,                    cls: 'text-danger' },
  feature:  { label: 'Feature',  icon: Lightbulb,              cls: 'text-accent' },
  question: { label: 'Question', icon: MessageCircleQuestion,  cls: 'text-primary' },
}

const STATUS_META: Record<number, { label: string; tone: 'warn' | 'info' | 'default' }> = {
  [IssueStatus.New]:          { label: '未處理',   tone: 'warn' },
  [IssueStatus.Acknowledged]: { label: '處理中',   tone: 'info' },
  [IssueStatus.Closed]:       { label: '已結案',   tone: 'default' },
}

type Filter = IssueStatusValue | 0

export function IssuesPage() {
  const [rows, setRows] = useState<IssueDto[]>([])
  const [loading, setLoading] = useState(true)
  const [err, setErr] = useState<string | null>(null)
  const [filter, setFilter] = useState<Filter>(0)
  const [expanded, setExpanded] = useState<string | null>(null)
  const [working, setWorking] = useState<string | null>(null)

  const load = useCallback(async () => {
    setLoading(true); setErr(null)
    try { setRows(await listIssues(filter === 0 ? undefined : filter)) }
    catch (e) { setErr(e instanceof Error ? e.message : 'load failed') }
    finally { setLoading(false) }
  }, [filter])
  useEffect(() => { void load() }, [load])

  async function transition(row: IssueDto, status: IssueStatusValue) {
    setWorking(row.id)
    try {
      await setIssueStatus(row.id, status)
      await load()
    } catch (e) {
      window.alert(e instanceof Error ? e.message : 'failed')
    } finally {
      setWorking(null)
    }
  }

  const counts = {
    all: rows.length,
  }

  return (
    <div className="space-y-4">
      <SectionCard>
        <SectionTitle
          right={
            <div className="flex items-center gap-2">
              <FilterPill active={filter === 0} onClick={() => setFilter(0)}>全部</FilterPill>
              <FilterPill active={filter === IssueStatus.New} onClick={() => setFilter(IssueStatus.New)}>未處理</FilterPill>
              <FilterPill active={filter === IssueStatus.Acknowledged} onClick={() => setFilter(IssueStatus.Acknowledged)}>處理中</FilterPill>
              <FilterPill active={filter === IssueStatus.Closed} onClick={() => setFilter(IssueStatus.Closed)}>已結案</FilterPill>
              <Button variant="outline" size="sm" onClick={() => void load()} disabled={loading}>
                {loading ? <Loader2 className="h-3.5 w-3.5 animate-spin" /> : <RefreshCw className="h-3.5 w-3.5" />}
                Refresh
              </Button>
            </div>
          }
        >
          <span className="inline-flex items-center gap-2"><Inbox className="h-4 w-4 text-primary" /> 問題回報 / Issues <span className="text-xs font-normal text-ink-muted">{counts.all}</span></span>
        </SectionTitle>

        {err && <p className="px-5 py-3 text-sm text-danger">載入失敗:{err}</p>}
        {!err && !loading && rows.length === 0 && (
          <p className="px-5 py-8 text-center text-sm text-ink-muted">目前沒有回報。員工端 Help → Report an issue 送出的內容會出現在這裡。</p>
        )}

        <ul className="divide-y divide-rule">
          {rows.map(row => {
            const kind = KIND_META[row.kind] ?? KIND_META.bug
            const status = STATUS_META[row.status]
            const KindIcon = kind.icon
            const open = expanded === row.id
            return (
              <li key={row.id} className="px-5 py-3">
                <button
                  onClick={() => setExpanded(open ? null : row.id)}
                  className="flex w-full items-center gap-3 text-left"
                >
                  <KindIcon className={cn('h-4 w-4 shrink-0', kind.cls)} />
                  <span className="min-w-0 flex-1 truncate text-sm font-medium text-ink">{row.title}</span>
                  <span className="hidden text-xs text-ink-muted sm:inline">{row.userName}</span>
                  <span className="font-mono text-[11px] text-ink-faint">{formatDate(row.submittedAt)}</span>
                  <Badge tone={status?.tone ?? 'default'}>{status?.label ?? row.status}</Badge>
                </button>
                {open && (
                  <div className="mt-3 space-y-3 rounded border border-rule bg-bg px-4 py-3">
                    <p className="whitespace-pre-wrap text-sm text-ink">{row.description}</p>
                    <dl className="grid grid-cols-2 gap-x-6 gap-y-1 text-xs text-ink-muted sm:grid-cols-4">
                      <Meta k="回報人">{row.userName}</Meta>
                      <Meta k="聯絡方式">{row.contact ?? '—'}</Meta>
                      <Meta k="頁面">{row.page ?? '—'}</Meta>
                      <Meta k="瀏覽器">{row.userAgent ? shortUa(row.userAgent) : '—'}</Meta>
                    </dl>
                    <div className="flex justify-end gap-2 border-t border-rule pt-2">
                      {row.status === IssueStatus.New && (
                        <Button variant="outline" size="sm" disabled={working === row.id} onClick={() => void transition(row, IssueStatus.Acknowledged)}>
                          開始處理
                        </Button>
                      )}
                      {row.status !== IssueStatus.Closed && (
                        <Button variant="primary" size="sm" disabled={working === row.id} onClick={() => void transition(row, IssueStatus.Closed)}>
                          {working === row.id ? <Loader2 className="h-3.5 w-3.5 animate-spin" /> : <CheckCircle2 className="h-3.5 w-3.5" />}
                          結案
                        </Button>
                      )}
                      {row.status === IssueStatus.Closed && (
                        <Button variant="outline" size="sm" disabled={working === row.id} onClick={() => void transition(row, IssueStatus.Acknowledged)}>
                          重新開啟
                        </Button>
                      )}
                    </div>
                  </div>
                )}
              </li>
            )
          })}
        </ul>
      </SectionCard>
    </div>
  )
}

function FilterPill({ active, onClick, children }: { active: boolean; onClick: () => void; children: React.ReactNode }) {
  return (
    <button
      onClick={onClick}
      className={cn(
        'rounded-full px-2.5 py-1 text-xs transition-colors',
        active ? 'bg-primary text-white' : 'bg-bg text-ink-muted hover:text-ink',
      )}
    >
      {children}
    </button>
  )
}

function Meta({ k, children }: { k: string; children: React.ReactNode }) {
  return (
    <div>
      <dt className="font-mono text-[10px] uppercase tracking-wider text-ink-faint">{k}</dt>
      <dd className="mt-0.5 truncate text-ink">{children}</dd>
    </div>
  )
}

function formatDate(iso: string): string {
  return new Date(iso).toLocaleString('zh-TW', { month: '2-digit', day: '2-digit', hour: '2-digit', minute: '2-digit' })
}

function shortUa(ua: string): string {
  const m = ua.match(/(Chrome|Firefox|Safari|Edg)\/[\d.]+/)
  return m ? m[0] : ua.slice(0, 40)
}
