import { Fragment, useCallback, useEffect, useState } from 'react'
import { RefreshCw, ChevronDown, ChevronRight, Search, Filter, Loader2 } from 'lucide-react'
import { cn } from '@/lib/cn'
import { SectionCard } from '@/components/ui/card'
import { Input, Select } from '@/components/ui/form'
import { Badge } from '@/components/ui/badge'
import {
  listAuditEvents,
  getAuditFacets,
  type AuditEvent,
  type AuditFacets,
} from '@/flowcook/api/audit'

const PAGE_SIZE = 50

export function AuditPage() {
  const [items, setItems] = useState<AuditEvent[]>([])
  const [total, setTotal] = useState(0)
  const [facets, setFacets] = useState<AuditFacets>({ actionTypes: [], targetTypes: [], sources: [] })
  const [loading, setLoading] = useState(true)
  const [error, setError] = useState<string | null>(null)
  const [expanded, setExpanded] = useState<string | null>(null)

  const [search, setSearch] = useState('')
  const [actionType, setActionType] = useState('')
  const [targetType, setTargetType] = useState('')
  const [source, setSource] = useState('')
  const [from, setFrom] = useState('')
  const [to, setTo] = useState('')
  const [page, setPage] = useState(0)

  const load = useCallback(async () => {
    setLoading(true)
    setError(null)
    try {
      const res = await listAuditEvents({
        search, actionType, targetType, source, from, to,
        skip: page * PAGE_SIZE, take: PAGE_SIZE,
      })
      setItems(res.items)
      setTotal(res.total)
    } catch (e) {
      setError(e instanceof Error ? e.message : 'Failed to load audit log')
    } finally {
      setLoading(false)
    }
  }, [search, actionType, targetType, source, from, to, page])

  useEffect(() => { void load() }, [load])
  useEffect(() => { void getAuditFacets().then(setFacets).catch(() => undefined) }, [])

  // Any filter change resets to the first page.
  const onFilter = <T,>(set: (v: T) => void) => (v: T) => { set(v); setPage(0) }

  const totalPages = Math.max(1, Math.ceil(total / PAGE_SIZE))
  const reset = () => {
    setSearch(''); setActionType(''); setTargetType(''); setSource(''); setFrom(''); setTo(''); setPage(0)
  }

  return (
    <div className="space-y-4">
      <div className="flex items-end justify-between">
        <div>
          <h1 className="text-lg font-bold text-ink">Audit</h1>
          <p className="text-sm text-ink-muted">
            全系統操作的 append-only 事件帳 — {total} 筆
          </p>
        </div>
        <button
          onClick={() => void load()}
          disabled={loading}
          className="inline-flex items-center gap-1.5 rounded border border-rule bg-card px-3 py-1.5 text-xs font-semibold text-ink-muted transition-colors hover:border-primary hover:text-primary disabled:opacity-50"
        >
          {loading ? <Loader2 className="h-3.5 w-3.5 animate-spin" /> : <RefreshCw className="h-3.5 w-3.5" />} Refresh
        </button>
      </div>

      {/* Filters */}
      <SectionCard>
        <div className="flex items-center justify-between border-b border-rule bg-slate-50 px-4 py-2.5 text-sm font-semibold text-ink">
          <span className="inline-flex items-center gap-1.5"><Filter className="h-4 w-4" /> Filters</span>
          <button onClick={reset} className="text-xs font-normal text-blue-600 hover:underline">Clear all</button>
        </div>
        <div className="grid grid-cols-1 gap-3 p-4 sm:grid-cols-2 lg:grid-cols-4">
          <div className="lg:col-span-2">
            <FieldLabel>Search</FieldLabel>
            <div className="relative">
              <Search className="pointer-events-none absolute left-3 top-1/2 h-4 w-4 -translate-y-1/2 text-ink-faint" />
              <Input className="pl-9" placeholder="action / target / id / reason…" value={search} onChange={e => onFilter(setSearch)(e.target.value)} />
            </div>
          </div>
          <div>
            <FieldLabel>Action</FieldLabel>
            <Select value={actionType} onChange={e => onFilter(setActionType)(e.target.value)}>
              <option value="">All actions</option>
              {facets.actionTypes.map(a => <option key={a} value={a}>{a}</option>)}
            </Select>
          </div>
          <div>
            <FieldLabel>Source</FieldLabel>
            <Select value={source} onChange={e => onFilter(setSource)(e.target.value)}>
              <option value="">All sources</option>
              {facets.sources.map(s => <option key={s} value={s}>{s}</option>)}
            </Select>
          </div>
          <div>
            <FieldLabel>Target type</FieldLabel>
            <Select value={targetType} onChange={e => onFilter(setTargetType)(e.target.value)}>
              <option value="">All targets</option>
              {facets.targetTypes.map(t => <option key={t} value={t}>{t}</option>)}
            </Select>
          </div>
          <div>
            <FieldLabel>From</FieldLabel>
            <Input type="date" value={from} onChange={e => onFilter(setFrom)(e.target.value)} />
          </div>
          <div>
            <FieldLabel>To</FieldLabel>
            <Input type="date" value={to} onChange={e => onFilter(setTo)(e.target.value)} />
          </div>
        </div>
      </SectionCard>

      {/* Ledger */}
      <SectionCard>
        <div className="overflow-x-auto">
          <table className="w-full text-sm">
            <thead className="bg-slate-50">
              <tr className="border-b border-rule">
                <Th className="w-6"></Th>
                <Th>Time</Th>
                <Th>Actor</Th>
                <Th>Action</Th>
                <Th>Target</Th>
                <Th>Source</Th>
              </tr>
            </thead>
            <tbody>
              {loading && items.length === 0 ? (
                <tr><td colSpan={6} className="px-4 py-12 text-center text-sm text-ink-faint">載入中…</td></tr>
              ) : error ? (
                <tr><td colSpan={6} className="px-4 py-12 text-center text-sm text-danger">{error}</td></tr>
              ) : items.length === 0 ? (
                <tr><td colSpan={6} className="px-4 py-12 text-center text-sm text-ink-faint">沒有符合條件的事件。</td></tr>
              ) : items.map(e => {
                const open = expanded === e.eventId
                const hasDetail = !!(e.beforeJson || e.afterJson || e.reason)
                return (
                  <Fragment key={e.eventId}>
                    <tr
                      onClick={() => hasDetail && setExpanded(open ? null : e.eventId)}
                      className={cn('border-b border-slate-100 transition-colors', hasDetail && 'cursor-pointer hover:bg-slate-50/60', open && 'bg-slate-50/60')}
                    >
                      <Td className="text-ink-faint">{hasDetail ? (open ? <ChevronDown className="h-3.5 w-3.5" /> : <ChevronRight className="h-3.5 w-3.5" />) : null}</Td>
                      <Td className="whitespace-nowrap font-mono text-[11.5px] text-ink-muted">{fmtTime(e.timestamp)}</Td>
                      <Td className="whitespace-nowrap">{e.actorDisplayName ?? <span className="text-ink-faint">{e.actorUserId ? short(e.actorUserId) : 'system'}</span>}</Td>
                      <Td><Badge tone={actionTone(e.actionType)}>{e.actionType}</Badge></Td>
                      <Td className="text-xs text-ink-muted">
                        <span className="font-medium text-ink">{e.targetType}</span>
                        {e.targetId && <span className="ml-1 font-mono text-ink-faint">{short(e.targetId)}</span>}
                      </Td>
                      <Td className="text-xs text-ink-muted">{e.sourceSystem}</Td>
                    </tr>
                    {open && (
                      <tr className="border-b border-slate-100 bg-slate-50/40">
                        <td colSpan={6} className="px-6 py-3">
                          {e.reason && <p className="mb-2 text-xs text-ink"><span className="font-semibold">Reason:</span> {e.reason}</p>}
                          <div className="grid grid-cols-1 gap-3 lg:grid-cols-2">
                            <JsonBlock label="Before" json={e.beforeJson} />
                            <JsonBlock label="After" json={e.afterJson} />
                          </div>
                          <p className="mt-2 font-mono text-[10px] text-ink-faint">event {e.eventId}</p>
                        </td>
                      </tr>
                    )}
                  </Fragment>
                )
              })}
            </tbody>
          </table>
        </div>

        {/* Paginator */}
        <div className="flex items-center justify-between gap-3 border-t border-rule px-4 py-3 text-xs text-ink-muted">
          <span className="font-mono">
            {total === 0 ? 0 : page * PAGE_SIZE + 1}–{Math.min(total, (page + 1) * PAGE_SIZE)} of {total}
          </span>
          <div className="inline-flex items-center gap-1">
            <PageBtn disabled={page === 0} onClick={() => setPage(0)}>« First</PageBtn>
            <PageBtn disabled={page === 0} onClick={() => setPage(p => Math.max(0, p - 1))}>‹ Prev</PageBtn>
            <span className="px-2 font-mono">Page {page + 1} / {totalPages}</span>
            <PageBtn disabled={page + 1 >= totalPages} onClick={() => setPage(p => Math.min(totalPages - 1, p + 1))}>Next ›</PageBtn>
            <PageBtn disabled={page + 1 >= totalPages} onClick={() => setPage(totalPages - 1)}>Last »</PageBtn>
          </div>
        </div>
      </SectionCard>
    </div>
  )
}

function FieldLabel({ children }: { children: React.ReactNode }) {
  return <label className="mb-1 block text-[11px] font-semibold uppercase tracking-wider text-ink-muted">{children}</label>
}
function Th({ children, className }: { children?: React.ReactNode; className?: string }) {
  return <th className={cn('px-4 py-2.5 text-left text-[11px] font-semibold uppercase tracking-wider text-ink-muted whitespace-nowrap', className)}>{children}</th>
}
function Td({ children, className }: { children: React.ReactNode; className?: string }) {
  return <td className={cn('px-4 py-2.5 align-middle text-ink', className)}>{children}</td>
}
function PageBtn({ children, disabled, onClick }: { children: React.ReactNode; disabled?: boolean; onClick: () => void }) {
  return (
    <button onClick={onClick} disabled={disabled}
      className="rounded border border-rule bg-card px-2 py-1 text-[11px] text-ink-muted transition-colors hover:bg-slate-50 disabled:opacity-40">
      {children}
    </button>
  )
}

function JsonBlock({ label, json }: { label: string; json: string | null }) {
  if (!json) return (
    <div>
      <p className="mb-1 text-[10px] font-semibold uppercase tracking-wider text-ink-faint">{label}</p>
      <p className="rounded border border-rule bg-card px-3 py-2 text-xs text-ink-faint">—</p>
    </div>
  )
  let pretty = json
  try { pretty = JSON.stringify(JSON.parse(json), null, 2) } catch { /* keep raw */ }
  return (
    <div className="min-w-0">
      <p className="mb-1 text-[10px] font-semibold uppercase tracking-wider text-ink-faint">{label}</p>
      <pre className="max-h-64 overflow-auto rounded border border-rule bg-card px-3 py-2 text-[11px] leading-relaxed text-ink">{pretty}</pre>
    </div>
  )
}

function fmtTime(iso: string): string {
  const d = new Date(iso)
  if (Number.isNaN(d.getTime())) return iso
  const p = (n: number) => String(n).padStart(2, '0')
  return `${d.getFullYear()}/${p(d.getMonth() + 1)}/${p(d.getDate())} ${p(d.getHours())}:${p(d.getMinutes())}:${p(d.getSeconds())}`
}
function short(id: string): string {
  return id.length > 10 ? id.slice(0, 8) : id
}
function actionTone(action: string): 'default' | 'good' | 'warn' | 'danger' | 'info' {
  const a = action.toLowerCase()
  if (/(fail|delete|remove|revoke|reject|retire|cancel|undeploy|archive)/.test(a)) return 'danger'
  if (/(create|add|assign|approve|register|deploy|restore|login(?!_fail))/.test(a)) return 'good'
  if (/(update|set|reorder|clone|spec|password|on_hold)/.test(a)) return 'info'
  return 'default'
}
