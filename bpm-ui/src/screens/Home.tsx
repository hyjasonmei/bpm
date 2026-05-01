import { useMemo } from "react"
import {
  Plus, FileText, Plane, Send, Laptop, DollarSign, Building2,
  Check, AlertCircle, Inbox, Pencil, Calendar, ExternalLink,
  Clock, RefreshCw, Briefcase,
} from 'lucide-react'
import { cn } from '@/lib/cn'
import { Button } from '@/components/ui/button'
import { SectionCard, SectionTitle } from '@/components/ui/card'
import { StatusBadge, TypeChip } from '@/components/ui/badge'
import {
  MOCK_CASES, MOCK_ACTIVITY, MOCK_REMINDERS, casesCreatedBy, casesPendingForPersona,
} from '@/lib/mocks'
import { PERSONAS, type PersonaCode } from '@/lib/role'
import type { Screen } from '@/components/AppLayout'
import type { FormCode } from '@/lib/workflow'

const QUICK_ACTIONS: Array<{ id: FormCode; label: string; Icon: React.ComponentType<{ className?: string }> }> = [
  { id: 'LEAVE', label: 'Leave Request',     Icon: Calendar },
  { id: 'GEE',   label: 'Employee Expense',  Icon: DollarSign },
  { id: 'GEV',   label: 'Vendor Expense',    Icon: FileText },
  { id: 'TRQ',   label: 'Travel Request',    Icon: Plane },
  { id: 'TEO',   label: 'Travel Expense',    Icon: Send },
  { id: 'APE',   label: 'Advance Payment',   Icon: DollarSign },
  { id: 'HWP',   label: 'Hardware Purchase', Icon: Laptop },
  { id: 'EXTOB', label: 'External Onboarding', Icon: Briefcase },
]

const ICON_FOR_ACTIVITY = {
  approved:  { Icon: Check,       color: 'text-good',    bg: 'bg-green-50' },
  submitted: { Icon: FileText,    color: 'text-slate-500', bg: 'bg-slate-100' },
  returned:  { Icon: AlertCircle, color: 'text-amber-600', bg: 'bg-amber-50' },
  closed:    { Icon: Check,       color: 'text-good',    bg: 'bg-green-50' },
  created:   { Icon: FileText,    color: 'text-slate-500', bg: 'bg-slate-100' },
  rejected:  { Icon: AlertCircle, color: 'text-red-600',  bg: 'bg-red-50'   },
} as const

const ICON_FOR_REMINDER = {
  draft:    { Icon: Pencil,      color: 'text-slate-500', bg: 'bg-slate-100' },
  return:   { Icon: Calendar,    color: 'text-amber-600', bg: 'bg-amber-50' },
  travel:   { Icon: Plane,       color: 'text-blue-600',  bg: 'bg-blue-50' },
  contract: { Icon: AlertCircle, color: 'text-amber-600', bg: 'bg-amber-50' },
} as const

interface HomeProps {
  persona: PersonaCode
  setScreen: (s: Screen) => void
}

export function Home({ persona, setScreen }: HomeProps) {
  const personaInfo = PERSONAS[persona]
  const myPending = useMemo(() => casesPendingForPersona(persona), [persona])
  const myCases = useMemo(() => casesCreatedBy('wilson'), []) // demo: requester is always Wilson
  const today = '2026/04/24'

  return (
    <div className="space-y-4">
      {/* Greeting */}
      <div className="flex items-end justify-between">
        <div>
          <h1 className="text-lg font-bold text-ink">
            {greetingFor(persona)}
          </h1>
          <p className="mt-0.5 text-sm text-ink-muted">
            {persona === 'admin'
              ? `System overview. ${MOCK_CASES.length} total cases tracked across all personas.`
              : `You have ${myPending.length} cases pending your action today.`}
          </p>
        </div>
        <div className="font-mono text-sm text-ink-faint">{today} · {personaInfo.displayName}</div>
      </div>

      {/* Stat cards */}
      <StatCards persona={persona} />

      {/* Two-column grid */}
      <div className="grid grid-cols-[1fr_320px] gap-4">
        <div className="min-w-0 space-y-4">
          <PendingTable persona={persona} cases={myPending} setScreen={setScreen} />
          <MyCasesTable cases={myCases} setScreen={setScreen} />
        </div>
        <div className="min-w-0 space-y-4">
          <QuickActionsPanel setScreen={setScreen} />
          <ActivityFeedPanel />
          <RemindersPanel />
        </div>
      </div>
    </div>
  )
}

function greetingFor(persona: PersonaCode) {
  const u = PERSONAS[persona].user.name.split(' ')[0]
  switch (persona) {
    case 'employee': return `👋 Good morning, ${u}`
    case 'manager':  return `👋 Good morning, ${u} — Manager view`
    case 'finance':  return `👋 Good morning, ${u} — Finance review queue`
    case 'it':       return `👋 Good morning, ${u} — IT spec & quoting queue`
    case 'hr':       return `👋 Good morning, ${u} — HR records queue`
    case 'admin':    return `👋 Good morning, Admin — All cases`
  }
}

/* ── Stat cards ─────────────────────────────────────────── */
function StatCards({ persona }: { persona: PersonaCode }) {
  // Generic counts derived from the mock dataset, then re-labeled per persona.
  const drafts = MOCK_CASES.filter(c => c.status === 'draft').length
  const myApproved30d = MOCK_CASES.filter(c => c.status === 'approved' || c.status === 'closed').length
  const myTotal = MOCK_CASES.length
  const pendingCount = casesPendingForPersona(persona).length

  const cards: Array<{ title: string; value: number; tone: string; Icon: React.ComponentType<{ className?: string }>; sub?: string }> = persona === 'employee'
    ? [
      { title: 'My Pending Actions',  value: pendingCount,    tone: 'amber', Icon: Inbox,     sub: 'Cases awaiting your action' },
      { title: 'My Drafts',           value: drafts,          tone: 'blue',  Icon: Pencil,    sub: 'Saved but not yet submitted' },
      { title: 'Approved (30d)',      value: myApproved30d,   tone: 'green', Icon: Check,     sub: 'Last 30 days' },
      { title: 'My Total Cases',      value: myTotal,         tone: 'slate', Icon: FileText,  sub: 'All-time submissions' },
    ]
    : persona === 'manager'
    ? [
      { title: 'Pending My Approval', value: pendingCount,    tone: 'amber', Icon: Inbox,     sub: 'Awaiting your approval' },
      { title: 'Approved Today',      value: 4,               tone: 'green', Icon: Check },
      { title: 'Returned',            value: 1,               tone: 'orange',Icon: RefreshCw },
      { title: 'Team Cases (30d)',    value: 27,              tone: 'slate', Icon: FileText },
    ]
    : persona === 'finance'
    ? [
      { title: 'FIN Review Queue',    value: pendingCount,    tone: 'violet', Icon: Inbox },
      { title: 'PO Processing',       value: 2,               tone: 'blue',   Icon: DollarSign },
      { title: 'Closed Today',        value: 6,               tone: 'green',  Icon: Check },
      { title: 'Total This Month',    value: 42,              tone: 'slate',  Icon: FileText },
    ]
    : persona === 'it'
    ? [
      { title: 'IT Spec Queue',       value: pendingCount,    tone: 'cyan',   Icon: Laptop },
      { title: 'Awaiting Quotation',  value: 1,               tone: 'amber',  Icon: Clock },
      { title: 'Closed (30d)',        value: 12,              tone: 'green',  Icon: Check },
      { title: 'Total Hardware Cases',value: 26,              tone: 'slate',  Icon: FileText },
    ]
    : persona === 'hr'
    ? [
      { title: 'HR Queue',            value: pendingCount,    tone: 'amber',  Icon: Inbox },
      { title: 'Onboardings (30d)',   value: 4,               tone: 'blue',   Icon: Building2 },
      { title: 'Leave Cases (30d)',   value: 18,              tone: 'green',  Icon: Calendar },
      { title: 'Terminations (30d)',  value: 1,               tone: 'red',    Icon: AlertCircle },
    ]
    : [ // admin
      { title: 'Open Cases',          value: MOCK_CASES.filter(c => c.status !== 'closed' && c.status !== 'rejected').length, tone: 'amber',  Icon: Inbox },
      { title: 'Drafts',              value: drafts,                                                                          tone: 'blue',   Icon: Pencil },
      { title: 'Closed (this month)', value: MOCK_CASES.filter(c => c.status === 'closed').length,                            tone: 'green',  Icon: Check },
      { title: 'Total Tracked',       value: myTotal,                                                                         tone: 'slate',  Icon: FileText },
    ]

  return (
    <div className="grid grid-cols-4 gap-3">
      {cards.map(card => <StatCard key={card.title} {...card} />)}
    </div>
  )
}

function StatCard({ title, value, tone, Icon, sub }: { title: string; value: number; tone: string; Icon: React.ComponentType<{ className?: string }>; sub?: string }) {
  const ring = {
    amber:  'border-amber-200 bg-amber-50/30',
    blue:   'border-blue-200 bg-blue-50/30',
    green:  'border-green-200 bg-green-50/30',
    slate:  'border-slate-200',
    violet: 'border-violet-200 bg-violet-50/30',
    cyan:   'border-cyan-200 bg-cyan-50/30',
    orange: 'border-orange-200 bg-orange-50/30',
    red:    'border-red-200 bg-red-50/30',
  }[tone] ?? 'border-slate-200'

  const text = {
    amber:  'text-amber-700',
    blue:   'text-blue-700',
    green:  'text-green-700',
    slate:  'text-slate-700',
    violet: 'text-violet-700',
    cyan:   'text-cyan-700',
    orange: 'text-orange-700',
    red:    'text-red-700',
  }[tone] ?? 'text-slate-700'

  return (
    <div className={cn('flex items-center gap-3 rounded-lg border bg-card px-4 py-3', ring)}>
      <div className={cn('flex h-9 w-9 shrink-0 items-center justify-center rounded-md', text, 'bg-white')}>
        <Icon className="h-4 w-4" />
      </div>
      <div className="min-w-0">
        <div className="text-[11px] font-semibold uppercase tracking-wider text-ink-muted truncate">{title}</div>
        <div className="flex items-baseline gap-1.5">
          <span className={cn('text-2xl font-bold tabular', text)}>{value}</span>
          {sub && <span className="text-[10.5px] text-ink-faint truncate">{sub}</span>}
        </div>
      </div>
    </div>
  )
}

/* ── Pending action table ───────────────────────────────── */

function PendingTable({ persona, cases, setScreen }: { persona: PersonaCode; cases: ReturnType<typeof casesPendingForPersona>; setScreen: (s: Screen) => void }) {
  const titlePerPersona: Record<PersonaCode, string> = {
    employee: 'Pending My Action',
    manager:  'Pending My Approval',
    finance:  'FIN Review Queue',
    it:       'IT Spec Queue',
    hr:       'HR Queue',
    admin:    'All Open Cases',
  }
  return (
    <SectionCard>
      <SectionTitle right={<span className="text-xs text-ink-muted">{cases.length} cases</span>}>
        {titlePerPersona[persona]}
      </SectionTitle>
      <div className="overflow-x-auto">
        <table className="w-full text-sm">
          <thead className="bg-slate-50">
            <tr className="border-b border-rule">
              <Th>Request No.</Th>
              <Th>Type</Th>
              <Th>Requestor</Th>
              <Th>Submitted</Th>
              <Th right>Days</Th>
              <Th>Action</Th>
              <Th right></Th>
            </tr>
          </thead>
          <tbody>
            {cases.length === 0 ? (
              <tr><td colSpan={7} className="px-4 py-12 text-center text-sm text-ink-faint">✨ Inbox zero. No pending action right now.</td></tr>
            ) : cases.map(c => {
              const days = Math.max(1, daysBetween(c.submitted, '2026/04/24'))
              return (
                <tr key={c.no} className="border-b border-slate-100 hover:bg-slate-50/60 transition-colors">
                  <Td><span className="font-mono text-[12px] font-semibold text-ink">{c.no}</span></Td>
                  <Td><div className="flex items-center gap-2"><TypeChip type={c.type} /><span className="text-xs text-ink-muted">{c.typeLabel}</span></div></Td>
                  <Td><div><div className="text-sm">{c.requestor}</div><div className="text-[11px] text-ink-faint">{c.dept}</div></div></Td>
                  <Td className="font-mono text-xs text-ink-muted">{c.submitted}</Td>
                  <Td right>
                    <span className={cn(
                      'inline-flex items-center justify-center rounded px-1.5 py-0.5 font-mono text-xs font-medium',
                      days > 7 ? 'bg-amber-50 text-amber-700' : days > 14 ? 'bg-red-50 text-red-700' : 'text-ink-muted',
                    )}>{days}d</span>
                  </Td>
                  <Td><span className="text-xs text-ink-muted">Awaiting {labelForOwner(c.currentOwner)}</span></Td>
                  <Td right><Button variant="primary" size="xs" onClick={() => setScreen({ kind: 'form', code: c.type })}>Open</Button></Td>
                </tr>
              )
            })}
          </tbody>
        </table>
      </div>
    </SectionCard>
  )
}

function MyCasesTable({ cases, setScreen }: { cases: ReturnType<typeof casesCreatedBy>; setScreen: (s: Screen) => void }) {
  return (
    <SectionCard>
      <SectionTitle right={<span className="text-xs text-ink-muted">{cases.length} cases</span>}>
        My Recent Cases
      </SectionTitle>
      <div className="overflow-x-auto">
        <table className="w-full text-sm">
          <thead className="bg-slate-50">
            <tr className="border-b border-rule">
              <Th>Request No.</Th>
              <Th>Type</Th>
              <Th>Status</Th>
              <Th>Submitted</Th>
              <Th>Updated</Th>
              <Th right>Amount</Th>
            </tr>
          </thead>
          <tbody>
            {cases.slice(0, 8).map(c => (
              <tr key={c.no} className="cursor-pointer border-b border-slate-100 hover:bg-slate-50/60 transition-colors" onClick={() => setScreen({ kind: 'form', code: c.type })}>
                <Td><span className="font-mono text-[12px] font-semibold text-ink">{c.no}</span></Td>
                <Td><TypeChip type={c.type} /></Td>
                <Td><StatusBadge kind={c.status} /></Td>
                <Td className="font-mono text-xs text-ink-muted">{c.submitted}</Td>
                <Td className="font-mono text-xs text-ink-muted">{c.updated}</Td>
                <Td right className="font-mono">{c.amount}</Td>
              </tr>
            ))}
          </tbody>
        </table>
      </div>
    </SectionCard>
  )
}

/* ── Right rail ─────────────────────────────────────────── */

function QuickActionsPanel({ setScreen }: { setScreen: (s: Screen) => void }) {
  return (
    <SectionCard>
      <SectionTitle>Quick Actions</SectionTitle>
      <div className="grid grid-cols-2 gap-2 p-3">
        {QUICK_ACTIONS.map(a => (
          <button
            key={a.id}
            onClick={() => setScreen({ kind: 'form', code: a.id })}
            className="flex items-center gap-2 rounded-md border border-rule bg-white px-2.5 py-2 text-left text-xs font-medium text-ink-muted transition-colors hover:bg-slate-50 hover:text-ink"
          >
            <a.Icon className="h-4 w-4 shrink-0 text-primary" />
            <span className="truncate">{a.label}</span>
          </button>
        ))}
      </div>
      <div className="border-t border-rule px-3 py-2 text-center">
        <button className="inline-flex items-center gap-1 text-[11px] text-primary hover:underline">
          <Plus className="h-3 w-3" /> Browse all forms
        </button>
      </div>
    </SectionCard>
  )
}

function ActivityFeedPanel() {
  return (
    <SectionCard>
      <SectionTitle>Activity Feed</SectionTitle>
      <div className="divide-y divide-slate-100">
        {MOCK_ACTIVITY.map((a, i) => {
          const meta = ICON_FOR_ACTIVITY[a.type]
          return (
            <div key={i} className="flex items-start gap-2.5 px-3 py-2.5 hover:bg-slate-50/60">
              <span className={cn('mt-0.5 inline-flex h-6 w-6 shrink-0 items-center justify-center rounded-full', meta.bg, meta.color)}>
                <meta.Icon className="h-3 w-3" strokeWidth={2.5} />
              </span>
              <div className="min-w-0 flex-1 text-xs">
                <p className="text-ink"><span className="font-medium">{a.msg}</span> <span className="font-mono text-[10.5px] text-ink-muted">{a.doc}</span></p>
                <p className="mt-0.5 text-[10.5px] text-ink-faint">{a.time}</p>
              </div>
            </div>
          )
        })}
      </div>
    </SectionCard>
  )
}

function RemindersPanel() {
  return (
    <SectionCard>
      <SectionTitle>Reminders</SectionTitle>
      <div className="divide-y divide-slate-100">
        {MOCK_REMINDERS.map((r, i) => {
          const meta = ICON_FOR_REMINDER[r.type]
          return (
            <div key={i} className="flex items-start gap-2.5 px-3 py-2.5 hover:bg-slate-50/60">
              <span className={cn('mt-0.5 inline-flex h-6 w-6 shrink-0 items-center justify-center rounded-full', meta.bg, meta.color)}>
                <meta.Icon className="h-3 w-3" strokeWidth={2.5} />
              </span>
              <div className="min-w-0 flex-1 text-xs">
                <div className="flex items-center gap-1.5">
                  <span className="font-medium text-ink">{r.label}</span>
                  <span className="font-mono text-[10.5px] text-ink-muted">{r.doc}</span>
                  <ExternalLink className="ml-auto h-3 w-3 text-ink-faint" />
                </div>
                <p className="mt-0.5 text-[10.5px] text-ink-faint">{r.detail}</p>
              </div>
            </div>
          )
        })}
      </div>
    </SectionCard>
  )
}

/* ── helpers ────────────────────────────────────────────── */

function Th({ children, right }: { children?: React.ReactNode; right?: boolean }) {
  return (
    <th className={cn(
      'px-4 py-2.5 text-[11px] font-semibold uppercase tracking-wider text-ink-muted whitespace-nowrap',
      right ? 'text-right' : 'text-left',
    )}>{children}</th>
  )
}

function Td({ children, right, className }: { children: React.ReactNode; right?: boolean; className?: string }) {
  return (
    <td className={cn(
      'px-4 py-3 align-middle text-ink',
      right ? 'text-right' : 'text-left',
      className,
    )}>{children}</td>
  )
}

function labelForOwner(owner: PersonaCode | null) {
  if (!owner) return '—'
  return PERSONAS[owner].displayName
}

function daysBetween(a: string, b: string) {
  const [y1, m1, d1] = a.split('/').map(Number)
  const [y2, m2, d2] = b.split('/').map(Number)
  const A = new Date(y1, m1 - 1, d1).getTime()
  const B = new Date(y2, m2 - 1, d2).getTime()
  return Math.round((B - A) / (1000 * 60 * 60 * 24))
}
