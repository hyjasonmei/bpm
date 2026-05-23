import {
  Plus, FileText, Laptop, DollarSign, Building2,
  Check, AlertCircle, Inbox, Pencil, Calendar,
  ChefHat,
} from 'lucide-react'
import { cn } from '@/lib/cn'
import { Button } from '@/components/ui/button'
import { SectionCard, SectionTitle } from '@/components/ui/card'
import { StatusBadge, TypeChip, type StatusKind } from '@/components/ui/badge'
import { PERSONAS, type PersonaCode } from '@/lib/role'
import type { Screen } from '@/components/AppLayout'
import { FORMS, type FormCode } from '@/lib/workflow'
import { formRegistry } from '@/features/registry'
import { useMyTasks } from '@/hooks/useMyTasks'
import { useMyInstances } from '@/hooks/useMyInstances'
import type { InstanceStatus, MyInstanceSummaryDto, ProcessTaskDto } from '@/types/process'

const ICON_FOR_ACTIVITY = {
  approved:  { Icon: Check,       color: 'text-good',    bg: 'bg-green-50' },
  submitted: { Icon: FileText,    color: 'text-slate-500', bg: 'bg-slate-100' },
  returned:  { Icon: AlertCircle, color: 'text-amber-600', bg: 'bg-amber-50' },
  closed:    { Icon: Check,       color: 'text-good',    bg: 'bg-green-50' },
  created:   { Icon: FileText,    color: 'text-slate-500', bg: 'bg-slate-100' },
  rejected:  { Icon: AlertCircle, color: 'text-red-600',  bg: 'bg-red-50'   },
} as const

interface HomeProps {
  persona: PersonaCode
  setScreen: (s: Screen) => void
}

export function Home({ persona, setScreen }: HomeProps) {
  const personaInfo = PERSONAS[persona]
  // PR-L3: real backend data — open tasks the caller owns + instances they
  // initiated. Polling cadence is 30s (see hook); manual refresh on action
  // is left to PR-L5 once we wire actionable rows.
  const inbox = useMyTasks('open')
  const myCases = useMyInstances('all')

  const today = new Date().toISOString().slice(0, 10).replace(/-/g, '/')

  return (
    <div className="space-y-4">
      {/* Greeting */}
      <div className="flex items-end justify-between">
        <div>
          <h1 className="text-lg font-bold text-ink">
            {greetingFor(persona)}
          </h1>
          <p className="mt-0.5 text-sm text-ink-muted">
            {inbox.loading
              ? 'Loading inbox…'
              : inbox.error
              ? `Inbox unavailable (${inbox.error.message})`
              : `You have ${inbox.data?.length ?? 0} cases pending your action.`}
          </p>
        </div>
        <div className="font-mono text-sm text-ink-faint">{today} · {personaInfo.displayName}</div>
      </div>

      {/* Stat cards */}
      <StatCards persona={persona} inboxCount={inbox.data?.length ?? 0} myCases={myCases.data ?? []} />

      {/* Two-column grid */}
      <div className="grid grid-cols-[1fr_320px] gap-4">
        <div className="min-w-0 space-y-4">
          <PendingTable persona={persona} tasks={inbox.data ?? []} loading={inbox.loading} error={inbox.error} setScreen={setScreen} />
          <MyCasesTable cases={myCases.data ?? []} loading={myCases.loading} error={myCases.error} />
        </div>
        <div className="min-w-0 space-y-4">
          <QuickActionsPanel setScreen={setScreen} />
          <ActivityFeedPanel cases={myCases.data ?? []} loading={myCases.loading} />
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
function StatCards({ persona, inboxCount, myCases }: { persona: PersonaCode; inboxCount: number; myCases: MyInstanceSummaryDto[] }) {
  // PR-L3: derived from the live inbox + my-instances. The downstream
  // counters (Approved Today / Closed Today / Onboardings 30d / etc.) are
  // not exposed by today's API surface — they're scoreboard widgets that
  // belong to the add-real-reporting proposal. Keep the visual but hard-
  // wire to 0 until that proposal lands.
  const myActive = myCases.filter(c => c.status === 'Running' || c.status === 'Errored').length
  const myCompleted = myCases.filter(c => c.status === 'Completed').length
  const myCancelled = myCases.filter(c => c.status === 'Cancelled').length
  const myTotal = myCases.length

  const cards: Array<{ title: string; value: number; tone: string; Icon: React.ComponentType<{ className?: string }>; sub?: string }> = persona === 'employee'
    ? [
      { title: 'My Pending Actions',  value: inboxCount,    tone: 'amber', Icon: Inbox,     sub: 'Cases awaiting your action' },
      { title: 'My Active Cases',     value: myActive,      tone: 'blue',  Icon: Pencil,    sub: 'Open cases you started' },
      { title: 'Completed (all-time)',value: myCompleted,   tone: 'green', Icon: Check,     sub: 'Cases that closed cleanly' },
      { title: 'My Total Cases',      value: myTotal,       tone: 'slate', Icon: FileText,  sub: 'All-time submissions' },
    ]
    : persona === 'manager'
    ? [
      { title: 'Pending My Approval', value: inboxCount,    tone: 'amber', Icon: Inbox,     sub: 'Awaiting your approval' },
      { title: 'My Active Cases',     value: myActive,      tone: 'blue',  Icon: Pencil },
      { title: 'My Completed Cases',  value: myCompleted,   tone: 'green', Icon: Check },
      { title: 'My Total Cases',      value: myTotal,       tone: 'slate', Icon: FileText },
    ]
    : persona === 'finance'
    ? [
      { title: 'FIN Review Queue',    value: inboxCount,    tone: 'violet', Icon: Inbox },
      { title: 'My Active Cases',     value: myActive,      tone: 'blue',   Icon: DollarSign },
      { title: 'My Completed Cases',  value: myCompleted,   tone: 'green',  Icon: Check },
      { title: 'My Total Cases',      value: myTotal,       tone: 'slate',  Icon: FileText },
    ]
    : persona === 'it'
    ? [
      { title: 'IT Spec Queue',       value: inboxCount,    tone: 'cyan',   Icon: Laptop },
      { title: 'My Active Cases',     value: myActive,      tone: 'blue',   Icon: Pencil },
      { title: 'My Completed Cases',  value: myCompleted,   tone: 'green',  Icon: Check },
      { title: 'My Total Cases',      value: myTotal,       tone: 'slate',  Icon: FileText },
    ]
    : persona === 'hr'
    ? [
      { title: 'HR Queue',            value: inboxCount,    tone: 'amber',  Icon: Inbox },
      { title: 'My Active Cases',     value: myActive,      tone: 'blue',   Icon: Building2 },
      { title: 'My Completed Cases',  value: myCompleted,   tone: 'green',  Icon: Calendar },
      { title: 'My Cancelled Cases',  value: myCancelled,   tone: 'red',    Icon: AlertCircle },
    ]
    : [ // admin — for now derives from the same caller-scoped data; cross-user roll-up belongs to add-real-reporting.
      { title: 'Pending My Approval', value: inboxCount,    tone: 'amber',  Icon: Inbox },
      { title: 'My Active Cases',     value: myActive,      tone: 'blue',   Icon: Pencil },
      { title: 'My Completed Cases',  value: myCompleted,   tone: 'green',  Icon: Check },
      { title: 'My Total Cases',      value: myTotal,       tone: 'slate',  Icon: FileText },
    ]

  return (
    <div className="grid grid-cols-4 gap-3">
      {cards.map(card => <StatCard key={card.title} {...card} />)}
    </div>
  )
}

function StatCard({ title, value, tone, Icon, sub }: { title: string; value: number; tone: string; Icon: React.ComponentType<{ className?: string }>; sub?: string }) {
  const iconColor = {
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
    <div className="flex items-center gap-3 rounded-lg border border-slate-200 bg-card px-4 py-3">
      <div className={cn('flex h-9 w-9 shrink-0 items-center justify-center rounded-md bg-slate-50', iconColor)}>
        <Icon className="h-4 w-4" />
      </div>
      <div className="min-w-0">
        <div className="text-[11px] font-semibold uppercase tracking-wider text-ink-muted truncate">{title}</div>
        <div className="flex items-baseline gap-1.5">
          <span className="text-2xl font-bold tabular text-ink">{value}</span>
          {sub && <span className="text-[10.5px] text-ink-faint truncate">{sub}</span>}
        </div>
      </div>
    </div>
  )
}

/* ── Pending action table ───────────────────────────────── */

function PendingTable({ persona, tasks, loading, error, setScreen }: {
  persona: PersonaCode
  tasks: ProcessTaskDto[]
  loading: boolean
  error: Error | null
  setScreen: (s: Screen) => void
}) {
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
      <SectionTitle right={<span className="text-xs text-ink-muted">{tasks.length} task{tasks.length === 1 ? '' : 's'}</span>}>
        {titlePerPersona[persona]}
      </SectionTitle>
      <div className="overflow-x-auto">
        <table className="w-full text-sm">
          <thead className="bg-slate-50">
            <tr className="border-b border-rule">
              <Th>Task ID</Th>
              <Th>Type</Th>
              <Th>Step</Th>
              <Th>Assigned</Th>
              <Th>Status</Th>
              <Th right></Th>
            </tr>
          </thead>
          <tbody>
            {loading && tasks.length === 0 ? (
              <tr><td colSpan={6} className="px-4 py-12 text-center text-sm text-ink-faint">Loading inbox…</td></tr>
            ) : error && tasks.length === 0 ? (
              <tr><td colSpan={6} className="px-4 py-12 text-center text-sm text-red-600">Inbox load failed: {error.message}</td></tr>
            ) : tasks.length === 0 ? (
              <tr><td colSpan={6} className="px-4 py-12 text-center text-sm text-ink-faint">✨ Inbox zero. No pending action right now.</td></tr>
            ) : tasks.map(t => {
              const formCode = isFormCode(t.specCode) ? t.specCode : null
              const typeLabel = formCode ? FORMS[formCode].label : t.specCode
              const stepLabel = nodeIdToStepLabel(formCode, t.nodeId)
              const assignedAgo = t.claimedAt ? humanAgo(t.claimedAt) : '—'
              return (
                <tr key={t.id} className="border-b border-slate-100 hover:bg-slate-50/60 transition-colors">
                  <Td><span className="font-mono text-[11px] text-ink-muted">{t.id.slice(0, 8)}</span></Td>
                  <Td><div className="flex items-center gap-2">{formCode && <TypeChip type={formCode} />}<span className="text-xs text-ink-muted truncate">{typeLabel}</span></div></Td>
                  <Td className="text-xs text-ink-muted">{stepLabel}</Td>
                  <Td className="font-mono text-[11px] text-ink-muted">{assignedAgo}</Td>
                  <Td><StatusBadge kind={taskStatusToBadge(t)} /></Td>
                  <Td right>
                    <Button
                      variant="primary"
                      size="xs"
                      disabled={!formCode}
                      onClick={() => formCode && setScreen({ kind: 'form', code: formCode, taskId: t.id })}
                    >
                      Open
                    </Button>
                  </Td>
                </tr>
              )
            })}
          </tbody>
        </table>
      </div>
    </SectionCard>
  )
}

function MyCasesTable({ cases, loading, error }: { cases: MyInstanceSummaryDto[]; loading: boolean; error: Error | null }) {
  return (
    <SectionCard>
      <SectionTitle right={<span className="text-xs text-ink-muted">{cases.length} case{cases.length === 1 ? '' : 's'}</span>}>
        My Recent Cases
      </SectionTitle>
      <div className="overflow-x-auto">
        <table className="w-full text-sm">
          <thead className="bg-slate-50">
            <tr className="border-b border-rule">
              <Th>Case ID</Th>
              <Th>Type</Th>
              <Th>Status</Th>
              <Th>Started</Th>
              <Th>Last activity</Th>
              <Th right>Open tasks</Th>
            </tr>
          </thead>
          <tbody>
            {loading && cases.length === 0 ? (
              <tr><td colSpan={6} className="px-4 py-12 text-center text-sm text-ink-faint">Loading cases…</td></tr>
            ) : error && cases.length === 0 ? (
              <tr><td colSpan={6} className="px-4 py-12 text-center text-sm text-red-600">Cases load failed: {error.message}</td></tr>
            ) : cases.length === 0 ? (
              <tr><td colSpan={6} className="px-4 py-12 text-center text-sm text-ink-faint">No cases yet — start one from Quick Actions.</td></tr>
            ) : cases.slice(0, 8).map(c => {
              const formCode = isFormCode(c.specCode) ? c.specCode : null
              return (
                <tr key={c.id} className="border-b border-slate-100 hover:bg-slate-50/60 transition-colors">
                  <Td><span className="font-mono text-[11px] text-ink">{c.id.slice(0, 8)}</span></Td>
                  <Td>{formCode ? <TypeChip type={formCode} /> : <span className="text-xs">{c.specCode}</span>}</Td>
                  <Td><StatusBadge kind={instanceStatusToBadge(c.status)} /></Td>
                  <Td className="font-mono text-xs text-ink-muted">{formatDate(c.startedAt)}</Td>
                  <Td className="font-mono text-xs text-ink-muted">{humanAgo(c.lastActivityAt)}</Td>
                  <Td right className="font-mono text-xs">{c.openTaskCount}</Td>
                </tr>
              )
            })}
          </tbody>
        </table>
      </div>
    </SectionCard>
  )
}

/* ── Right rail ─────────────────────────────────────────── */

function QuickActionsPanel({ setScreen }: { setScreen: (s: Screen) => void }) {
  // Registry-driven: each chef-shipped manifest under
  // features/<CODE>/V<N>/ surfaces as one Quick Action. The FORMS
  // metadata map is consulted for display label only — it is NOT a
  // gate for what appears here.
  const actions = [...formRegistry.values()]
    .map(m => ({ code: m.code, label: FORMS[m.code]?.zhLabel ?? m.code }))
    .sort((a, b) => a.code.localeCompare(b.code))

  return (
    <SectionCard>
      <SectionTitle>Quick Actions</SectionTitle>
      {actions.length === 0 ? (
        <div className="px-3 py-6 text-center text-[11px] text-ink-faint">
          目前沒有可用的流程 — 請聯絡管理員建置新流程。
        </div>
      ) : (
        <div className="grid grid-cols-2 gap-2 p-3">
          {actions.map(a => (
            <button
              key={a.code}
              onClick={() => setScreen({ kind: 'form', code: a.code })}
              className="flex items-center gap-2 rounded-md border border-rule bg-white px-2.5 py-2 text-left text-xs font-medium text-ink-muted transition-colors hover:bg-slate-50 hover:text-ink"
            >
              <ChefHat className="h-4 w-4 shrink-0 text-primary" />
              <span className="truncate">{a.label}</span>
            </button>
          ))}
        </div>
      )}
      <div className="border-t border-rule px-3 py-2 text-center">
        <button onClick={() => setScreen({ kind: 'create' })} className="inline-flex items-center gap-1 text-[11px] text-primary hover:underline">
          <Plus className="h-3 w-3" /> Browse all forms
        </button>
      </div>
    </SectionCard>
  )
}

function ActivityFeedPanel({ cases, loading }: { cases: MyInstanceSummaryDto[]; loading: boolean }) {
  const recent = [...cases]
    .sort((a, b) => b.lastActivityAt.localeCompare(a.lastActivityAt))
    .slice(0, 8)

  return (
    <SectionCard>
      <SectionTitle>Activity Feed</SectionTitle>
      <div className="divide-y divide-slate-100">
        {loading && recent.length === 0 && (
          <p className="px-3 py-6 text-center text-[11px] text-ink-faint">Loading…</p>
        )}
        {!loading && recent.length === 0 && (
          <p className="px-3 py-6 text-center text-[11px] text-ink-faint">
            尚無近期活動 — 從上方 Quick Actions 開新流程後會出現。
          </p>
        )}
        {recent.map(c => {
          const meta = ICON_FOR_ACTIVITY[statusToActivityKind(c.status)]
          const formLabel = FORMS[c.specCode as FormCode]?.zhLabel ?? c.specCode
          return (
            <div key={c.id} className="flex items-start gap-2.5 px-3 py-2.5 hover:bg-slate-50/60">
              <span className={cn('mt-0.5 inline-flex h-6 w-6 shrink-0 items-center justify-center rounded-full', meta.bg, meta.color)}>
                <meta.Icon className="h-3 w-3" strokeWidth={2.5} />
              </span>
              <div className="min-w-0 flex-1 text-xs">
                <p className="text-ink truncate">
                  <span className="font-medium">{formLabel}</span>
                  <span className="ml-1 text-ink-muted">{instanceLineFor(c)}</span>
                </p>
                <p className="mt-0.5 text-[10.5px] text-ink-faint">
                  <span className="font-mono">{c.id.slice(0, 8)}</span>
                  {' · '}{formatActivityTime(c.lastActivityAt)}
                </p>
              </div>
            </div>
          )
        })}
      </div>
    </SectionCard>
  )
}

function statusToActivityKind(status: InstanceStatus): keyof typeof ICON_FOR_ACTIVITY {
  switch (status) {
    case 'Completed': return 'approved'
    case 'Cancelled': return 'rejected'
    case 'Errored':   return 'returned'
    case 'Running':   return 'submitted'
    default:          return 'created'
  }
}

function instanceLineFor(c: MyInstanceSummaryDto): string {
  if (c.status === 'Completed') return '已完成'
  if (c.status === 'Cancelled') return '已取消'
  if (c.status === 'Errored') return '錯誤待處理'
  if (c.currentNodeLabel) return `· ${c.currentNodeLabel}`
  return `· ${c.openTaskCount} task open`
}

function formatActivityTime(iso: string): string {
  const d = new Date(iso)
  const diffMs = Date.now() - d.getTime()
  const mins = Math.floor(diffMs / 60_000)
  if (mins < 1) return 'just now'
  if (mins < 60) return `${mins} min ago`
  const hours = Math.floor(mins / 60)
  if (hours < 24) return `${hours} h ago`
  return d.toISOString().slice(0, 10)
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

const FORM_CODES: ReadonlyArray<FormCode> = ['LEAVE', 'GEE', 'GEV', 'APE', 'TRQ', 'TEO', 'HWP', 'ITPR', 'EXTOB', 'RESIGN', 'DEPTX']
function isFormCode(s: string): s is FormCode {
  return (FORM_CODES as readonly string[]).includes(s)
}

/** Map a runtime nodeId (e.g. "approval_manager") into the prettier
 *  step label declared in workflow.ts. Falls back to the raw nodeId
 *  when a spec adds a node we don't model in the workflow.ts FORMS map. */
function nodeIdToStepLabel(formCode: FormCode | null, nodeId: string): string {
  if (!formCode) return nodeId
  const step = FORMS[formCode].steps.find(s => nodeId.startsWith(s.id) || nodeId.includes(s.id))
  return step?.en ?? nodeId
}

function taskStatusToBadge(t: ProcessTaskDto): StatusKind {
  if (t.status === 'Completed') return 'closed'
  if (t.status === 'Cancelled') return 'rejected'
  if (t.status === 'InProgress') return 'pending'
  return 'pending'
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

function humanAgo(iso: string): string {
  const then = new Date(iso).getTime()
  if (Number.isNaN(then)) return iso
  const diff = Date.now() - then
  const mins = Math.round(diff / 60_000)
  if (mins < 1) return 'just now'
  if (mins < 60) return `${mins}m ago`
  const hrs = Math.round(mins / 60)
  if (hrs < 24) return `${hrs}h ago`
  const days = Math.round(hrs / 24)
  return `${days}d ago`
}
