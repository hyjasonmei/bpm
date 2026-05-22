import { useEffect, useMemo, useRef, useState } from 'react'
import { Search as SearchIcon, X, Filter } from 'lucide-react'
import { cn } from '@/lib/cn'
import { Button } from '@/components/ui/button'
import { Input, Select, FieldLabel } from '@/components/ui/form'
import { SectionCard, SectionTitle } from '@/components/ui/card'
import { StatusBadge, TypeChip, type StatusKind } from '@/components/ui/badge'
import { FORMS, type FormCode } from '@/lib/workflow'
import { useMyInstances } from '@/hooks/useMyInstances'
import type { InstanceStatus, MyInstanceSummaryDto } from '@/types/process'

const FORM_TYPES: FormCode[] = ['LEAVE', 'GEE', 'GEV', 'APE', 'HWP', 'ITPR', 'TRQ', 'TEO', 'EXTOB', 'RESIGN', 'DEPTX']
const ALL_STATUSES: InstanceStatus[] = ['Running', 'Completed', 'Cancelled', 'Errored']

export function Search() {
  // PR-L3: Search is currently scoped to "my own initiated cases" because
  // the backend hasn't shipped a cross-user instance index yet (that's the
  // add-real-search proposal). Filters apply client-side over the polled
  // myInstances list — fast for an inbox-sized result set, and the parameter
  // shape stays compatible when the proper search endpoint lands.
  const myCases = useMyInstances('all')

  const [keyword, setKeyword] = useState('')
  const [caseId, setCaseId] = useState('')
  const [formTypes, setFormTypes] = useState<FormCode[]>([])
  const [statuses, setStatuses] = useState<InstanceStatus[]>([])
  const [dateFrom, setDateFrom] = useState('')
  const [dateTo, setDateTo] = useState('')
  const [page, setPage] = useState(0)
  const [pageSize, setPageSize] = useState(10)

  const all = myCases.data ?? []

  const results = useMemo(() => {
    let r = all
    if (caseId) r = r.filter(c => c.id.toLowerCase().includes(caseId.toLowerCase()))
    if (keyword) {
      const k = keyword.toLowerCase()
      r = r.filter(c =>
        c.id.toLowerCase().includes(k)
        || c.specCode.toLowerCase().includes(k)
        || (c.currentNodeLabel ?? '').toLowerCase().includes(k))
    }
    if (formTypes.length) r = r.filter(c => formTypes.includes(c.specCode as FormCode))
    if (statuses.length) r = r.filter(c => statuses.includes(c.status))
    if (dateFrom) r = r.filter(c => c.startedAt.slice(0, 10) >= dateFrom)
    if (dateTo)   r = r.filter(c => c.startedAt.slice(0, 10) <= dateTo)
    return r
  }, [all, keyword, caseId, formTypes, statuses, dateFrom, dateTo])

  const totalPages = Math.max(1, Math.ceil(results.length / pageSize))
  const pageRows = results.slice(page * pageSize, page * pageSize + pageSize)

  const toggleType = (t: FormCode) => setFormTypes(p => p.includes(t) ? p.filter(x => x !== t) : [...p, t])
  const toggleStatus = (s: InstanceStatus) => setStatuses(p => p.includes(s) ? p.filter(x => x !== s) : [...p, s])
  const clearAll = () => {
    setKeyword(''); setCaseId(''); setFormTypes([]); setStatuses([])
    setDateFrom(''); setDateTo(''); setPage(0)
  }

  return (
    <div className="space-y-4">
      <div className="flex items-end justify-between">
        <div>
          <h1 className="text-lg font-bold text-ink">Search</h1>
          <p className="text-sm text-ink-muted">
            搜尋我發起過的所有案件
            <span className="ml-2 text-[11px] text-ink-faint">(cross-user search 預定 add-real-search 提案)</span>
          </p>
        </div>
        <p className="font-mono text-sm text-ink-faint">
          {myCases.loading && all.length === 0 ? 'loading…' : `${all.length} cases indexed`}
        </p>
      </div>

      {/* Filters */}
      <SectionCard>
        <SectionTitle right={
          <button onClick={clearAll} className="text-xs text-blue-600 hover:underline">Clear all</button>
        }>
          <span className="inline-flex items-center gap-1.5"><Filter className="h-4 w-4" /> Filters</span>
        </SectionTitle>
        <div className="grid grid-cols-3 gap-4 p-5">
          <div className="col-span-3">
            <FieldLabel>Keyword</FieldLabel>
            <div className="relative">
              <SearchIcon className="pointer-events-none absolute left-3 top-1/2 h-4 w-4 -translate-y-1/2 text-ink-faint" />
              <Input className="pl-9" placeholder="Search by case id, spec code, current step…" value={keyword} onChange={e => setKeyword(e.target.value)} />
            </div>
          </div>
          <div>
            <FieldLabel>Case ID prefix</FieldLabel>
            <Input placeholder="e.g. 5b3a8e2c" value={caseId} onChange={e => setCaseId(e.target.value)} />
          </div>
          <div className="grid grid-cols-2 gap-2 col-span-2">
            <div>
              <FieldLabel>Started from</FieldLabel>
              <Input type="date" value={dateFrom} onChange={e => setDateFrom(e.target.value)} />
            </div>
            <div>
              <FieldLabel>To</FieldLabel>
              <Input type="date" value={dateTo} onChange={e => setDateTo(e.target.value)} />
            </div>
          </div>
          <div className="col-span-3">
            <FieldLabel>Form Types</FieldLabel>
            <div className="flex flex-wrap gap-1.5">
              {FORM_TYPES.map(t => (
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
          <div className="col-span-3">
            <FieldLabel>Statuses</FieldLabel>
            <div className="flex flex-wrap gap-1.5">
              {ALL_STATUSES.map(s => (
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
        </div>
        <div className="flex items-center justify-end gap-2 border-t border-rule px-4 py-3">
          <Button variant="outline" size="md" onClick={clearAll}>Reset</Button>
          <Button variant="primary" size="md" onClick={() => { void myCases.refresh(); setPage(0) }}>
            <SearchIcon className="h-3.5 w-3.5" /> Refresh
          </Button>
        </div>
      </SectionCard>

      {/* Results */}
      <SectionCard>
        <SectionTitle right={
          <span className="text-xs text-ink-muted">{results.length} result{results.length === 1 ? '' : 's'}</span>
        }>
          Results
        </SectionTitle>
        <div className="overflow-x-auto">
          <table className="w-full text-sm">
            <thead className="bg-slate-50">
              <tr className="border-b border-rule">
                <Th>Case ID</Th>
                <Th>Type</Th>
                <Th>Current step</Th>
                <Th>Started</Th>
                <Th>Last activity</Th>
                <Th right>Open tasks</Th>
                <Th>Status</Th>
              </tr>
            </thead>
            <tbody>
              {myCases.loading && results.length === 0 ? (
                <tr><td colSpan={7} className="px-4 py-12 text-center text-sm text-ink-faint">Loading cases…</td></tr>
              ) : myCases.error && results.length === 0 ? (
                <tr><td colSpan={7} className="px-4 py-12 text-center text-sm text-red-600">Search failed: {myCases.error.message}</td></tr>
              ) : pageRows.length === 0 ? (
                <tr><td colSpan={7} className="px-4 py-12 text-center text-sm text-ink-faint">No matches.</td></tr>
              ) : pageRows.map(c => {
                const formCode = isFormCode(c.specCode) ? c.specCode : null
                return (
                  <tr key={c.id} className="border-b border-slate-100 hover:bg-slate-50/60">
                    <Td><span className="font-mono text-[12px] font-semibold text-ink">{c.id.slice(0, 8)}</span></Td>
                    <Td>
                      <div className="flex items-center gap-2">
                        {formCode ? <TypeChip type={formCode} /> : <span className="text-xs">{c.specCode}</span>}
                        {formCode && <span className="text-xs text-ink-muted truncate">{FORMS[formCode].label}</span>}
                      </div>
                    </Td>
                    <Td className="text-xs text-ink-muted">{c.currentNodeLabel ?? (c.openTaskCount === 0 ? '—' : '?')}</Td>
                    <Td className="font-mono text-xs">{formatDate(c.startedAt)}</Td>
                    <Td className="font-mono text-xs">{formatDate(c.lastActivityAt)}</Td>
                    <Td right className="font-mono text-xs">{c.openTaskCount}</Td>
                    <Td><StatusBadge kind={instanceStatusToBadge(c.status)} /></Td>
                  </tr>
                )
              })}
            </tbody>
          </table>
        </div>

        {/* Paginator */}
        <div className="flex items-center justify-between gap-3 border-t border-rule px-4 py-3 text-xs text-ink-muted">
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

const FORM_CODES: ReadonlyArray<FormCode> = ['LEAVE', 'GEE', 'GEV', 'APE', 'TRQ', 'TEO', 'HWP', 'ITPR', 'EXTOB', 'RESIGN', 'DEPTX']
function isFormCode(s: string): s is FormCode {
  return (FORM_CODES as readonly string[]).includes(s)
}

function instanceStatusToBadge(s: InstanceStatus): StatusKind {
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
  return d.toISOString().slice(0, 10).replace(/-/g, '/')
}

/* ───────── Search Modal ───────── */

export function SearchModal({ open, onClose }: { open: boolean; onClose: () => void }) {
  const myCases = useMyInstances('all')
  const inputRef = useRef<HTMLInputElement>(null)
  const [q, setQ] = useState('')

  useEffect(() => {
    if (!open) return
    const onEsc = (e: KeyboardEvent) => { if (e.key === 'Escape') onClose() }
    document.addEventListener('keydown', onEsc)
    inputRef.current?.focus()
    return () => document.removeEventListener('keydown', onEsc)
  }, [open, onClose])

  const matches = useMemo(() => {
    if (!q) return [] as MyInstanceSummaryDto[]
    const k = q.toLowerCase()
    return (myCases.data ?? []).filter(c =>
      c.id.toLowerCase().includes(k)
      || c.specCode.toLowerCase().includes(k)
      || (c.currentNodeLabel ?? '').toLowerCase().includes(k),
    ).slice(0, 8)
  }, [q, myCases.data])

  if (!open) return null

  return (
    <div className="fixed inset-0 z-50 flex items-start justify-center bg-black/40 p-6 pt-24" onClick={onClose}>
      <div className="w-full max-w-2xl overflow-hidden rounded-xl bg-white shadow-2xl" onClick={e => e.stopPropagation()} role="dialog" aria-modal="true">
        <div className="flex items-center gap-3 border-b border-rule px-4 py-3">
          <SearchIcon className="h-4 w-4 text-ink-faint" />
          <input ref={inputRef} value={q} onChange={e => setQ(e.target.value)} placeholder="Quick search… try case id prefix or LEAVE / GEE" className="flex-1 bg-transparent text-sm text-ink placeholder:text-ink-faint focus:outline-none" />
          <button onClick={onClose} className="rounded p-1 text-ink-faint hover:bg-slate-100"><X className="h-4 w-4" /></button>
        </div>
        <div className="max-h-[420px] overflow-y-auto">
          {q && matches.length === 0 ? (
            <p className="px-4 py-12 text-center text-sm text-ink-faint">No matches for "{q}".</p>
          ) : !q ? (
            <p className="px-4 py-12 text-center text-sm text-ink-faint">Type to search across your cases. <span className="font-mono">Esc</span> to close.</p>
          ) : matches.map(c => {
            const formCode = isFormCode(c.specCode) ? c.specCode : null
            return (
              <button key={c.id} className="flex w-full items-center justify-between gap-3 border-b border-slate-50 px-4 py-2.5 text-left hover:bg-blue-50/40">
                <div className="flex items-center gap-3">
                  {formCode ? <TypeChip type={formCode} /> : <span className="text-xs">{c.specCode}</span>}
                  <div>
                    <p className="font-mono text-[12px] font-semibold text-ink">{c.id.slice(0, 8)}</p>
                    <p className="text-xs text-ink-muted">{formCode ? FORMS[formCode].label : c.specCode} · {c.currentNodeLabel ?? '—'}</p>
                  </div>
                </div>
                <StatusBadge kind={instanceStatusToBadge(c.status)} />
              </button>
            )
          })}
        </div>
      </div>
    </div>
  )
}
