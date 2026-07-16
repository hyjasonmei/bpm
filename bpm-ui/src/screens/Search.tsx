import { useCallback, useEffect, useMemo, useRef, useState } from 'react'
import { Search as SearchIcon, X, Filter, ChevronDown, ChevronRight } from 'lucide-react'
import { useNavigate } from 'react-router-dom'

import { cn } from '@/lib/cn'
import { Button } from '@/components/ui/button'
import { Input, Select, FieldLabel } from '@/components/ui/form'
import { SectionCard, SectionTitle } from '@/components/ui/card'
import { CaseCard, CaseCardList } from '@/components/ui/case-card'
import { TypeChip } from '@/components/ui/badge'
import { inboxRowUrl, useInboxMine, useInboxPending, type InboxRow } from '@/hooks/useUnifiedInbox'
import { useFlowLabel } from '@/hooks/useFlowRegistry'

const FILTERS_OPEN_KEY = 'bpm.search.filtersOpen'

/**
 * Search over the cases the viewer is involved in — both the ones they
 * submitted ("mine") and the ones waiting on them ("pending"), merged and
 * de-duped by caseId. This reads the same live unified-inbox feed the Home
 * page uses (`/api/inbox/{mine,pending}`), so it sees every chef-cooked
 * Model-B flow. Filters apply client-side over the merged list (fast for an
 * inbox-sized set); the row's detailUrl deep-links to the real case page.
 */
function useMyCases() {
  const mine = useInboxMine()
  const pending = useInboxPending()

  const rows = useMemo(() => {
    const byId = new Map<string, InboxRow>()
    for (const r of mine.data ?? []) byId.set(r.caseId, r)
    for (const r of pending.data ?? []) if (!byId.has(r.caseId)) byId.set(r.caseId, r)
    return [...byId.values()].sort((a, b) => b.lastActivityAt.localeCompare(a.lastActivityAt))
  }, [mine.data, pending.data])

  const refresh = useCallback(async () => {
    await Promise.all([mine.refresh(), pending.refresh()])
  }, [mine.refresh, pending.refresh])

  return {
    rows,
    loading: mine.loading || pending.loading,
    error: mine.error ?? pending.error,
    refresh,
  }
}

function matchRows(rows: InboxRow[], opts: {
  keyword?: string; caseId?: string; formTypes?: string[]; statuses?: string[]; dateFrom?: string; dateTo?: string
}, flowLabel: (code: string, version?: number) => string): InboxRow[] {
  let r = rows
  if (opts.caseId) {
    const id = opts.caseId.toLowerCase()
    r = r.filter(c => c.caseId.toLowerCase().includes(id))
  }
  if (opts.keyword) {
    const k = opts.keyword.toLowerCase()
    r = r.filter(c =>
      c.title.toLowerCase().includes(k)
      || c.flowCode.toLowerCase().includes(k)
      || c.caseId.toLowerCase().includes(k)
      || c.status.toLowerCase().includes(k)
      || flowLabel(c.flowCode, c.flowVersion).toLowerCase().includes(k))
  }
  if (opts.formTypes?.length) r = r.filter(c => opts.formTypes!.includes(c.flowCode))
  if (opts.statuses?.length) r = r.filter(c => opts.statuses!.includes(c.status))
  if (opts.dateFrom) r = r.filter(c => c.submittedAt.slice(0, 10) >= opts.dateFrom!)
  if (opts.dateTo) r = r.filter(c => c.submittedAt.slice(0, 10) <= opts.dateTo!)
  return r
}

export function Search() {
  const navigate = useNavigate()
  const { rows, loading, error, refresh } = useMyCases()
  const flowLabel = useFlowLabel()

  const [keyword, setKeyword] = useState('')
  const [caseId, setCaseId] = useState('')
  const [formTypes, setFormTypes] = useState<string[]>([])
  const [statuses, setStatuses] = useState<string[]>([])
  const [dateFrom, setDateFrom] = useState('')
  const [dateTo, setDateTo] = useState('')
  const [page, setPage] = useState(0)
  const [pageSize, setPageSize] = useState(10)

  // Filter panel collapse state — persisted so it opens the way the user
  // left it last time.
  const [filtersOpen, setFiltersOpen] = useState<boolean>(() => {
    const saved = localStorage.getItem(FILTERS_OPEN_KEY)
    return saved === null ? true : saved === 'true'
  })
  useEffect(() => {
    localStorage.setItem(FILTERS_OPEN_KEY, String(filtersOpen))
  }, [filtersOpen])

  const activeFilterCount =
    (keyword ? 1 : 0) + (caseId ? 1 : 0) + formTypes.length + statuses.length +
    (dateFrom ? 1 : 0) + (dateTo ? 1 : 0)

  // Filter chips are derived from the data so they only ever offer flow
  // types / statuses that actually appear in the viewer's cases.
  const availableTypes = useMemo(() => [...new Set(rows.map(c => c.flowCode))].sort(), [rows])
  const availableStatuses = useMemo(() => [...new Set(rows.map(c => c.status))].sort(), [rows])

  const results = useMemo(
    () => matchRows(rows, { keyword, caseId, formTypes, statuses, dateFrom, dateTo }, flowLabel),
    [rows, keyword, caseId, formTypes, statuses, dateFrom, dateTo, flowLabel],
  )

  const totalPages = Math.max(1, Math.ceil(results.length / pageSize))
  const pageRows = results.slice(page * pageSize, page * pageSize + pageSize)

  const toggleType = (t: string) => setFormTypes(p => p.includes(t) ? p.filter(x => x !== t) : [...p, t])
  const toggleStatus = (s: string) => setStatuses(p => p.includes(s) ? p.filter(x => x !== s) : [...p, s])
  const clearAll = () => {
    setKeyword(''); setCaseId(''); setFormTypes([]); setStatuses([])
    setDateFrom(''); setDateTo(''); setPage(0)
  }

  return (
    <div className="space-y-4">
      <div className="flex items-end justify-between">
        <div>
          <h1 className="text-lg font-bold text-ink">Search</h1>
          <p className="text-sm text-ink-muted">搜尋我相關的案件（我送出的 + 待我處理的）</p>
        </div>
        <p className="font-mono text-sm text-ink-faint">
          {loading && rows.length === 0 ? 'loading…' : `${rows.length} case${rows.length === 1 ? '' : 's'}`}
        </p>
      </div>

      {/* Filters (collapsible — state persisted across visits) */}
      <SectionCard>
        <div className="flex items-center justify-between border-b border-rule bg-slate-50 px-4 py-2.5">
          <button
            onClick={() => setFiltersOpen(o => !o)}
            className="inline-flex items-center gap-1.5 text-sm font-semibold text-ink"
            aria-expanded={filtersOpen}
          >
            {filtersOpen ? <ChevronDown className="h-4 w-4 text-ink-muted" /> : <ChevronRight className="h-4 w-4 text-ink-muted" />}
            <Filter className="h-4 w-4" /> Filters
            {!filtersOpen && activeFilterCount > 0 && (
              <span className="ml-1 rounded-full bg-blue-100 px-1.5 py-0.5 text-[10px] font-bold leading-none text-blue-700">{activeFilterCount}</span>
            )}
          </button>
          <button onClick={clearAll} className="text-xs text-blue-600 hover:underline">Clear all</button>
        </div>
        {filtersOpen && (<>
        <div className="grid grid-cols-1 gap-4 p-5 md:grid-cols-3">
          <div className="md:col-span-3">
            <FieldLabel>Keyword</FieldLabel>
            <div className="relative">
              <SearchIcon className="pointer-events-none absolute left-3 top-1/2 h-4 w-4 -translate-y-1/2 text-ink-faint" />
              <Input className="pl-9" placeholder="Search by title, flow, case id, status…" value={keyword} onChange={e => setKeyword(e.target.value)} />
            </div>
          </div>
          <div>
            <FieldLabel>Case ID prefix</FieldLabel>
            <Input placeholder="e.g. 5b3a8e2c" value={caseId} onChange={e => setCaseId(e.target.value)} />
          </div>
          <div className="grid grid-cols-2 gap-2 md:col-span-2">
            <div>
              <FieldLabel>Submitted from</FieldLabel>
              <Input type="date" value={dateFrom} onChange={e => setDateFrom(e.target.value)} />
            </div>
            <div>
              <FieldLabel>To</FieldLabel>
              <Input type="date" value={dateTo} onChange={e => setDateTo(e.target.value)} />
            </div>
          </div>
          {availableTypes.length > 0 && (
            <div className="md:col-span-3">
              <FieldLabel>Form Types</FieldLabel>
              <div className="flex flex-wrap gap-1.5">
                {availableTypes.map(t => (
                  <button key={t} onClick={() => toggleType(t)}
                    className={cn(
                      'rounded-md border px-2 py-1 text-xs font-mono font-medium transition-colors',
                      formTypes.includes(t) ? 'border-blue-500 bg-blue-50 text-blue-700' : 'border-rule text-ink-muted hover:bg-slate-50',
                    )}>
                    {t}
                  </button>
                ))}
              </div>
            </div>
          )}
          {availableStatuses.length > 0 && (
            <div className="md:col-span-3">
              <FieldLabel>Statuses</FieldLabel>
              <div className="flex flex-wrap gap-1.5">
                {availableStatuses.map(s => (
                  <button key={s} onClick={() => toggleStatus(s)}
                    className={cn(
                      'rounded-md border px-2 py-1 text-xs font-medium transition-colors',
                      statuses.includes(s) ? 'border-blue-500 bg-blue-50 text-blue-700' : 'border-rule text-ink-muted hover:bg-slate-50',
                    )}>
                    {s}
                  </button>
                ))}
              </div>
            </div>
          )}
        </div>
        <div className="flex items-center justify-end gap-2 border-t border-rule px-4 py-3">
          <Button variant="outline" size="md" onClick={clearAll}>Reset</Button>
          <Button variant="primary" size="md" onClick={() => { void refresh(); setPage(0) }}>
            <SearchIcon className="h-3.5 w-3.5" /> Refresh
          </Button>
        </div>
        </>)}
      </SectionCard>

      {/* Results */}
      <SectionCard>
        <SectionTitle right={
          <span className="text-xs text-ink-muted">{results.length} result{results.length === 1 ? '' : 's'}</span>
        }>
          Results
        </SectionTitle>

        {/* Mobile cards — same pageRows + navigate handler as the table below */}
        <CaseCardList
          className="p-3 md:hidden"
          state={
            loading && results.length === 0 ? 'Loading cases…'
            : error && results.length === 0 ? `Search failed: ${error.message}`
            : pageRows.length === 0 ? 'No matches.'
            : null
          }
        >
          {pageRows.map(c => (
            <CaseCard
              key={c.caseId}
              onClick={() => navigate(inboxRowUrl(c))}
              top={<>
                <TypeChip type={flowLabel(c.flowCode, c.flowVersion)} />
                <span className="text-xs text-ink-muted">{c.status}</span>
              </>}
              title={c.title}
              meta={<>
                <span>{c.caseId.slice(0, 8)}</span>
                <span>{formatDate(c.submittedAt)}</span>
                <span>活動 {formatDate(c.lastActivityAt)}</span>
              </>}
            />
          ))}
        </CaseCardList>

        <div className="hidden overflow-x-auto md:block">
          <table className="w-full text-sm">
            <thead className="bg-slate-50">
              <tr className="border-b border-rule">
                <Th>Case ID</Th>
                <Th>Type</Th>
                <Th>Title</Th>
                <Th>Submitted</Th>
                <Th>Last activity</Th>
                <Th>Status</Th>
              </tr>
            </thead>
            <tbody>
              {loading && results.length === 0 ? (
                <tr><td colSpan={6} className="px-4 py-12 text-center text-sm text-ink-faint">Loading cases…</td></tr>
              ) : error && results.length === 0 ? (
                <tr><td colSpan={6} className="px-4 py-12 text-center text-sm text-red-600">Search failed: {error.message}</td></tr>
              ) : pageRows.length === 0 ? (
                <tr><td colSpan={6} className="px-4 py-12 text-center text-sm text-ink-faint">No matches.</td></tr>
              ) : pageRows.map(c => (
                <tr key={c.caseId}
                  onClick={() => navigate(inboxRowUrl(c))}
                  className="cursor-pointer border-b border-slate-100 transition-colors hover:bg-slate-50/60">
                  <Td><span className="font-mono text-[12px] font-semibold text-ink">{c.caseId.slice(0, 8)}</span></Td>
                  <Td><TypeChip type={flowLabel(c.flowCode, c.flowVersion)} /></Td>
                  <Td className="text-xs text-ink-muted truncate max-w-xs">{c.title}</Td>
                  <Td className="font-mono text-xs">{formatDate(c.submittedAt)}</Td>
                  <Td className="font-mono text-xs">{formatDate(c.lastActivityAt)}</Td>
                  <Td><span className="text-xs text-ink-muted">{c.status}</span></Td>
                </tr>
              ))}
            </tbody>
          </table>
        </div>

        {/* Paginator */}
        <div className="flex flex-wrap items-center justify-between gap-3 border-t border-rule px-4 py-3 text-xs text-ink-muted">
          <div className="inline-flex items-center gap-2">
            <span>Rows per page</span>
            <Select className="h-7 w-20" value={pageSize.toString()} onChange={e => { setPageSize(Number(e.target.value)); setPage(0) }}>
              {[10, 25, 50].map(n => <option key={n} value={n}>{n}</option>)}
            </Select>
          </div>
          <div className="inline-flex items-center gap-2 font-mono">
            <span>Page {page + 1} of {totalPages}</span>
            <span className="text-ink-faint">·</span>
            <span>Showing {results.length === 0 ? 0 : page * pageSize + 1}-{Math.min(results.length, (page + 1) * pageSize)} of {results.length}</span>
          </div>
          <div className="inline-flex items-center gap-1">
            <Button variant="outline" size="xs" disabled={page === 0} onClick={() => setPage(0)}>« First</Button>
            <Button variant="outline" size="xs" disabled={page === 0} onClick={() => setPage(p => Math.max(0, p - 1))}>‹ Prev</Button>
            <Button variant="outline" size="xs" disabled={page + 1 >= totalPages} onClick={() => setPage(p => Math.min(totalPages - 1, p + 1))}>Next ›</Button>
            <Button variant="outline" size="xs" disabled={page + 1 >= totalPages} onClick={() => setPage(totalPages - 1)}>Last »</Button>
          </div>
        </div>
      </SectionCard>
    </div>
  )
}

function Th({ children, right }: { children?: React.ReactNode; right?: boolean }) {
  return <th className={cn('px-4 py-2.5 text-[11px] font-semibold uppercase tracking-wider text-ink-muted whitespace-nowrap', right ? 'text-right' : 'text-left')}>{children}</th>
}
function Td({ children, right, className }: { children: React.ReactNode; right?: boolean; className?: string }) {
  return <td className={cn('px-4 py-3 align-middle text-ink', right ? 'text-right' : 'text-left', className)}>{children}</td>
}

function formatDate(iso: string): string {
  const d = new Date(iso)
  if (Number.isNaN(d.getTime())) return iso
  return d.toISOString().slice(0, 10).replace(/-/g, '/')
}

/* ───────── Search Modal (Cmd+K quick search) ───────── */

export function SearchModal({ open, onClose }: { open: boolean; onClose: () => void }) {
  const navigate = useNavigate()
  const { rows } = useMyCases()
  const flowLabel = useFlowLabel()
  const inputRef = useRef<HTMLInputElement>(null)
  const [q, setQ] = useState('')

  useEffect(() => {
    if (!open) return
    const onEsc = (e: KeyboardEvent) => { if (e.key === 'Escape') onClose() }
    document.addEventListener('keydown', onEsc)
    inputRef.current?.focus()
    return () => document.removeEventListener('keydown', onEsc)
  }, [open, onClose])

  const matches = useMemo(() => (q ? matchRows(rows, { keyword: q }, flowLabel).slice(0, 8) : []), [q, rows, flowLabel])

  if (!open) return null

  const openCase = (r: InboxRow) => { onClose(); navigate(inboxRowUrl(r)) }

  return (
    <div className="fixed inset-0 z-50 flex items-start justify-center bg-black/40 p-6 pt-24" onClick={onClose}>
      <div className="w-full max-w-2xl overflow-hidden rounded-xl bg-white shadow-2xl" onClick={e => e.stopPropagation()} role="dialog" aria-modal="true">
        <div className="flex items-center gap-3 border-b border-rule px-4 py-3">
          <SearchIcon className="h-4 w-4 text-ink-faint" />
          <input ref={inputRef} value={q} onChange={e => setQ(e.target.value)} placeholder="Quick search… title, case id, or LEAVE / APE" className="flex-1 bg-transparent text-sm text-ink placeholder:text-ink-faint focus:outline-none" />
          <button onClick={onClose} className="rounded p-1 text-ink-faint hover:bg-slate-100"><X className="h-4 w-4" /></button>
        </div>
        <div className="max-h-[420px] overflow-y-auto">
          {q && matches.length === 0 ? (
            <p className="px-4 py-12 text-center text-sm text-ink-faint">No matches for "{q}".</p>
          ) : !q ? (
            <p className="px-4 py-12 text-center text-sm text-ink-faint">Type to search across your cases. <span className="font-mono">Esc</span> to close.</p>
          ) : matches.map(c => (
            <button key={c.caseId} onClick={() => openCase(c)}
              className="flex w-full items-center justify-between gap-3 border-b border-slate-50 px-4 py-2.5 text-left hover:bg-blue-50/40">
              <div className="flex min-w-0 items-center gap-3">
                <TypeChip type={flowLabel(c.flowCode, c.flowVersion)} />
                <div className="min-w-0">
                  <p className="truncate text-[13px] text-ink">{c.title}</p>
                  <p className="font-mono text-[11px] text-ink-muted">{c.caseId.slice(0, 8)} · {flowLabel(c.flowCode, c.flowVersion)}</p>
                </div>
              </div>
              <span className="shrink-0 text-xs text-ink-muted">{c.status}</span>
            </button>
          ))}
        </div>
      </div>
    </div>
  )
}
