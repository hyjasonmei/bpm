import { useMemo, useRef, useState, useEffect } from 'react'
import { Search as SearchIcon, X, Filter } from 'lucide-react'
import { cn } from '@/lib/cn'
import { Button } from '@/components/ui/button'
import { Input, Select, FieldLabel } from '@/components/ui/form'
import { SectionCard, SectionTitle } from '@/components/ui/card'
import { StatusBadge, TypeChip, type StatusKind } from '@/components/ui/badge'
import { MOCK_CASES } from '@/lib/mocks'
import type { FormCode } from '@/lib/workflow'

const FORM_TYPES: FormCode[] = ['LEAVE', 'GEE', 'GEV', 'APE', 'HWP', 'ITPR', 'TRQ', 'TEO', 'EXTOB']
const ALL_STATUSES: StatusKind[] = ['draft', 'pending', 'approved', 'fin_review', 'it_spec_review', 'returned', 'closed', 'rejected']

export function Search() {
  const [keyword, setKeyword] = useState('')
  const [reqNo, setReqNo] = useState('')
  const [formTypes, setFormTypes] = useState<FormCode[]>([])
  const [statuses, setStatuses] = useState<StatusKind[]>([])
  const [requestor, setRequestor] = useState('')
  const [dateFrom, setDateFrom] = useState('')
  const [dateTo, setDateTo] = useState('')
  const [page, setPage] = useState(0)
  const [pageSize, setPageSize] = useState(10)
  const [searched, setSearched] = useState(true) // start showing all rows for demo

  const allRequestors = useMemo(() => Array.from(new Set(MOCK_CASES.map(c => c.requestor))).sort(), [])

  const results = useMemo(() => {
    let r = MOCK_CASES
    if (!searched) return []
    if (keyword) r = r.filter(c =>
      c.no.toLowerCase().includes(keyword.toLowerCase()) ||
      c.typeLabel.toLowerCase().includes(keyword.toLowerCase()) ||
      c.requestor.toLowerCase().includes(keyword.toLowerCase()) ||
      c.dept.toLowerCase().includes(keyword.toLowerCase()))
    if (reqNo) r = r.filter(c => c.no.toLowerCase().includes(reqNo.toLowerCase()))
    if (formTypes.length) r = r.filter(c => formTypes.includes(c.type))
    if (statuses.length) r = r.filter(c => statuses.includes(c.status))
    if (requestor) r = r.filter(c => c.requestor === requestor)
    if (dateFrom) r = r.filter(c => c.submitted.replace(/\//g, '-') >= dateFrom)
    if (dateTo)   r = r.filter(c => c.submitted.replace(/\//g, '-') <= dateTo)
    return r
  }, [keyword, reqNo, formTypes, statuses, requestor, dateFrom, dateTo, searched])

  const totalPages = Math.max(1, Math.ceil(results.length / pageSize))
  const pageRows = results.slice(page * pageSize, page * pageSize + pageSize)

  const toggleType = (t: FormCode) => setFormTypes(p => p.includes(t) ? p.filter(x => x !== t) : [...p, t])
  const toggleStatus = (s: StatusKind) => setStatuses(p => p.includes(s) ? p.filter(x => x !== s) : [...p, s])
  const clearAll = () => {
    setKeyword(''); setReqNo(''); setFormTypes([]); setStatuses([])
    setRequestor(''); setDateFrom(''); setDateTo(''); setPage(0); setSearched(true)
  }

  return (
    <div className="space-y-4">
      <div className="flex items-end justify-between">
        <div>
          <h1 className="text-lg font-bold text-ink">Search</h1>
          <p className="text-sm text-ink-muted">搜尋公司全域所有表單</p>
        </div>
        <p className="font-mono text-sm text-ink-faint">{MOCK_CASES.length} cases indexed</p>
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
              <Input className="pl-9" placeholder="Search by request no., type label, requestor, department…" value={keyword} onChange={e => setKeyword(e.target.value)} />
            </div>
          </div>
          <div>
            <FieldLabel>Request No.</FieldLabel>
            <Input placeholder="e.g. TW-GEE-26-001342" value={reqNo} onChange={e => setReqNo(e.target.value)} />
          </div>
          <div>
            <FieldLabel>Requestor</FieldLabel>
            <Select value={requestor} onChange={e => setRequestor(e.target.value)}>
              <option value="">All</option>
              {allRequestors.map(r => <option key={r}>{r}</option>)}
            </Select>
          </div>
          <div className="grid grid-cols-2 gap-2">
            <div>
              <FieldLabel>Submitted from</FieldLabel>
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
          <Button variant="primary" size="md" onClick={() => { setSearched(true); setPage(0) }}>
            <SearchIcon className="h-3.5 w-3.5" /> Search
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
                <Th>Request No.</Th>
                <Th>Type</Th>
                <Th>Requestor</Th>
                <Th>Department</Th>
                <Th>Submitted</Th>
                <Th>Updated</Th>
                <Th right>Amount</Th>
                <Th>Status</Th>
              </tr>
            </thead>
            <tbody>
              {pageRows.length === 0 ? (
                <tr><td colSpan={8} className="px-4 py-12 text-center text-sm text-ink-faint">No matches.</td></tr>
              ) : pageRows.map(c => (
                <tr key={c.no} className="border-b border-slate-100 hover:bg-slate-50/60">
                  <Td><span className="font-mono text-[12px] font-semibold text-ink">{c.no}</span></Td>
                  <Td><div className="flex items-center gap-2"><TypeChip type={c.type} /><span className="text-xs text-ink-muted">{c.typeLabel}</span></div></Td>
                  <Td>{c.requestor}</Td>
                  <Td className="text-xs text-ink-muted">{c.dept}</Td>
                  <Td className="font-mono text-xs">{c.submitted}</Td>
                  <Td className="font-mono text-xs">{c.updated}</Td>
                  <Td right className="font-mono">{c.amount}</Td>
                  <Td><StatusBadge kind={c.status} /></Td>
                </tr>
              ))}
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

/* ───────── Search Modal ───────── */

export function SearchModal({ open, onClose }: { open: boolean; onClose: () => void }) {
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
    if (!q) return []
    return MOCK_CASES.filter(c =>
      c.no.toLowerCase().includes(q.toLowerCase()) ||
      c.typeLabel.toLowerCase().includes(q.toLowerCase()) ||
      c.requestor.toLowerCase().includes(q.toLowerCase()),
    ).slice(0, 8)
  }, [q])

  if (!open) return null

  return (
    <div className="fixed inset-0 z-50 flex items-start justify-center bg-black/40 p-6 pt-24" onClick={onClose}>
      <div className="w-full max-w-2xl overflow-hidden rounded-xl bg-white shadow-2xl" onClick={e => e.stopPropagation()} role="dialog" aria-modal="true">
        <div className="flex items-center gap-3 border-b border-rule px-4 py-3">
          <SearchIcon className="h-4 w-4 text-ink-faint" />
          <input ref={inputRef} value={q} onChange={e => setQ(e.target.value)} placeholder="Quick search… try TW-GEE / Wilson / Travel" className="flex-1 bg-transparent text-sm text-ink placeholder:text-ink-faint focus:outline-none" />
          <button onClick={onClose} className="rounded p-1 text-ink-faint hover:bg-slate-100"><X className="h-4 w-4" /></button>
        </div>
        <div className="max-h-[420px] overflow-y-auto">
          {q && matches.length === 0 ? (
            <p className="px-4 py-12 text-center text-sm text-ink-faint">No matches for "{q}".</p>
          ) : !q ? (
            <p className="px-4 py-12 text-center text-sm text-ink-faint">Type to search across all cases. <span className="font-mono">Esc</span> to close.</p>
          ) : matches.map(c => (
            <button key={c.no} className="flex w-full items-center justify-between gap-3 border-b border-slate-50 px-4 py-2.5 text-left hover:bg-blue-50/40">
              <div className="flex items-center gap-3">
                <TypeChip type={c.type} />
                <div>
                  <p className="font-mono text-[12px] font-semibold text-ink">{c.no}</p>
                  <p className="text-xs text-ink-muted">{c.typeLabel} · {c.requestor}</p>
                </div>
              </div>
              <StatusBadge kind={c.status} />
            </button>
          ))}
        </div>
      </div>
    </div>
  )
}
