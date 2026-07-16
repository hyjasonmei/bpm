import {
  Plus, FileText, Laptop, DollarSign, Building2,
  Check, AlertCircle, Inbox, Pencil, Calendar,
} from 'lucide-react'
import { Link, useNavigate } from 'react-router-dom'

import { cn } from '@/lib/cn'
import { Button } from '@/components/ui/button'
import { SectionCard, SectionTitle } from '@/components/ui/card'
import { CaseCard, CaseCardList } from '@/components/ui/case-card'
import { TypeChip } from '@/components/ui/badge'
import { PERSONAS, type PersonaCode } from '@/lib/role'
import { getJwt } from '@/lib/apiFetch'
import { decodeJwt } from '@/lib/jwt'
import { FORMS, type FormCode } from '@/lib/workflow'
import { formRegistry } from '@/features/registry'
import { inboxRowUrl, useInboxMine, useInboxPending, type InboxRow } from '@/hooks/useUnifiedInbox'
import { useFlowRegistry, entryForVersion, useFlowLabel } from '@/hooks/useFlowRegistry'
import { resolveLauncherIcon } from '@/lib/launcherIcons'
import { routes } from '@/router'

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
}

export function Home({ persona }: HomeProps) {
  const navigate = useNavigate()
  const personaInfo = PERSONAS[persona]
  // Unified inbox: chef-cooked features each implement ITypedInboxProvider
  // and InboxController fans out across them. Polls every 30s; manual
  // refresh after a row action is a follow-up.
  const inbox = useInboxPending()
  const myCases = useInboxMine()

  const today = new Date().toISOString().slice(0, 10).replace(/-/g, '/')

  return (
    <div className="space-y-4">
      {/* Greeting */}
      <div className="flex flex-col gap-1 md:flex-row md:items-end md:justify-between">
        <div>
          <h1 className="text-lg font-bold text-ink">
            {greetingFor(persona, currentDisplayName())}
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

      {/* Two-column grid — single column below md, inbox first */}
      <div className="grid grid-cols-1 gap-4 md:grid-cols-[1fr_320px]">
        <div className="min-w-0 space-y-4">
          <PendingTable persona={persona} rows={inbox.data ?? []} loading={inbox.loading} error={inbox.error} navigate={navigate} />
          <MyCasesTable rows={myCases.data ?? []} loading={myCases.loading} error={myCases.error} navigate={navigate} />
        </div>
        <div className="min-w-0 space-y-4">
          <QuickActionsPanel />
          <ActivityFeedPanel rows={myCases.data ?? []} loading={myCases.loading} />
        </div>
      </div>
    </div>
  )
}

function currentDisplayName(): string | null {
  const token = getJwt()
  if (!token) return null
  const d = decodeJwt(token)
  return d?.full_name ?? d?.email ?? null
}

function greetingFor(persona: PersonaCode, authedFullName?: string | null) {
  const display = authedFullName ?? PERSONAS[persona].user.name
  const firstName = display.split(' ')[0]
  const hour = new Date().getHours()
  const tod = hour < 5 ? '夜深了'
    : hour < 12 ? 'Good morning'
    : hour < 18 ? 'Good afternoon'
    : 'Good evening'
  switch (persona) {
    case 'employee': return `👋 ${tod}, ${firstName}`
    case 'manager':  return `👋 ${tod}, ${firstName} — Manager view`
    case 'finance':  return `👋 ${tod}, ${firstName} — Finance review queue`
    case 'it':       return `👋 ${tod}, ${firstName} — IT spec & quoting queue`
    case 'hr':       return `👋 ${tod}, ${firstName} — HR records queue`
    case 'admin':    return `👋 ${tod}, ${firstName} — Admin view`
  }
}

/* ── Stat cards ─────────────────────────────────────────── */
function StatCards({ persona, inboxCount, myCases }: { persona: PersonaCode; inboxCount: number; myCases: InboxRow[] }) {
  // Count on the canonical `lifecycle` field (Open/Completed/Cancelled/Rejected)
  // set structurally by each ITypedInboxProvider — NOT the localized `status`
  // display text (e.g. "已核准"), which never equals "Completed".
  const myActive = myCases.filter(c => c.lifecycle === 'Open').length
  const myCompleted = myCases.filter(c => c.lifecycle === 'Completed').length
  const myCancelled = myCases.filter(c => c.lifecycle === 'Cancelled' || c.lifecycle === 'Rejected').length
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
    <div className="grid grid-cols-2 gap-3 md:grid-cols-4">
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
        {/* Mobile (2×2 grid): let the title wrap and drop the sub line —
            truncated "MY PENDING A…" told the user nothing. md+: original. */}
        <div className="text-[11px] font-semibold uppercase leading-tight tracking-wider text-ink-muted md:truncate md:leading-normal">{title}</div>
        <div className="flex items-baseline gap-1.5">
          <span className="text-2xl font-bold tabular text-ink">{value}</span>
          {sub && <span className="hidden text-[10.5px] text-ink-faint md:inline md:truncate">{sub}</span>}
        </div>
      </div>
    </div>
  )
}

/* ── Pending action table ───────────────────────────────── */

function PendingTable({ persona, rows, loading, error, navigate }: {
  persona: PersonaCode
  rows: InboxRow[]
  loading: boolean
  error: Error | null
  navigate: ReturnType<typeof useNavigate>
}) {
  const flowLabel = useFlowLabel()
  const titlePerPersona: Record<PersonaCode, string> = {
    employee: 'Pending My Action',
    manager:  'Pending My Approval',
    finance:  'FIN Review Queue',
    it:       'IT Spec Queue',
    procurement: 'Procurement Queue',
    hr:       'HR Queue',
    admin:    'All Open Cases',
  }
  return (
    <SectionCard>
      <SectionTitle right={<span className="text-xs text-ink-muted">{rows.length} task{rows.length === 1 ? '' : 's'}</span>}>
        {titlePerPersona[persona]}
      </SectionTitle>

      {/* Mobile cards — same rows + navigate handler as the table below */}
      <CaseCardList
        className="p-3 md:hidden"
        state={
          loading && rows.length === 0 ? 'Loading inbox…'
          : error && rows.length === 0 ? `Inbox load failed: ${error.message}`
          : rows.length === 0 ? '✨ Inbox zero. No pending action right now.'
          : null
        }
      >
        {rows.map(r => (
          <CaseCard
            key={r.caseId}
            onClick={() => navigate(inboxRowUrl(r))}
            top={<>
              <TypeChip type={flowLabel(r.flowCode, r.flowVersion)} />
              <span className="text-xs text-ink-muted">{r.status}</span>
            </>}
            title={r.title}
            meta={<>
              <span>{r.caseId.slice(0, 8)}</span>
              <span>{humanAgo(r.submittedAt)}</span>
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
              <Th>Status</Th>
              <Th right></Th>
            </tr>
          </thead>
          <tbody>
            {loading && rows.length === 0 ? (
              <tr><td colSpan={6} className="px-4 py-12 text-center text-sm text-ink-faint">Loading inbox…</td></tr>
            ) : error && rows.length === 0 ? (
              <tr><td colSpan={6} className="px-4 py-12 text-center text-sm text-red-600">Inbox load failed: {error.message}</td></tr>
            ) : rows.length === 0 ? (
              <tr><td colSpan={6} className="px-4 py-12 text-center text-sm text-ink-faint">✨ Inbox zero. No pending action right now.</td></tr>
            ) : rows.map(r => {
              return (
                <tr key={r.caseId} className="border-b border-slate-100 hover:bg-slate-50/60 transition-colors">
                  <Td><span className="font-mono text-[11px] text-ink-muted">{r.caseId.slice(0, 8)}</span></Td>
                  <Td><TypeChip type={flowLabel(r.flowCode, r.flowVersion)} /></Td>
                  <Td className="text-xs text-ink-muted truncate">{r.title}</Td>
                  <Td className="font-mono text-[11px] text-ink-muted">{humanAgo(r.submittedAt)}</Td>
                  <Td><span className="text-xs text-ink-muted">{r.status}</span></Td>
                  <Td right>
                    <Button
                      variant="primary"
                      size="xs"
                      onClick={() => navigate(inboxRowUrl(r))}
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

function MyCasesTable({ rows, loading, error, navigate }: { rows: InboxRow[]; loading: boolean; error: Error | null; navigate: ReturnType<typeof useNavigate> }) {
  const flowLabel = useFlowLabel()
  return (
    <SectionCard>
      <SectionTitle right={<span className="text-xs text-ink-muted">{rows.length} case{rows.length === 1 ? '' : 's'}</span>}>
        My Recent Cases
      </SectionTitle>

      {/* Mobile cards — same rows + navigate handler as the table below */}
      <CaseCardList
        className="p-3 md:hidden"
        state={
          loading && rows.length === 0 ? 'Loading cases…'
          : error && rows.length === 0 ? `Cases load failed: ${error.message}`
          : rows.length === 0 ? 'No cases yet — start one from Quick Actions.'
          : null
        }
      >
        {rows.slice(0, 8).map(r => (
          <CaseCard
            key={r.caseId}
            onClick={() => navigate(inboxRowUrl(r))}
            top={<>
              <TypeChip type={flowLabel(r.flowCode, r.flowVersion)} />
              <span className="text-xs text-ink-muted">{r.status}</span>
            </>}
            title={r.title}
            meta={<>
              <span>{r.caseId.slice(0, 8)}</span>
              <span>Started {formatDate(r.submittedAt)}</span>
              <span>{humanAgo(r.lastActivityAt)}</span>
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
              <Th>Status</Th>
              <Th>Started</Th>
              <Th right>Last activity</Th>
            </tr>
          </thead>
          <tbody>
            {loading && rows.length === 0 ? (
              <tr><td colSpan={6} className="px-4 py-12 text-center text-sm text-ink-faint">Loading cases…</td></tr>
            ) : error && rows.length === 0 ? (
              <tr><td colSpan={6} className="px-4 py-12 text-center text-sm text-red-600">Cases load failed: {error.message}</td></tr>
            ) : rows.length === 0 ? (
              <tr><td colSpan={6} className="px-4 py-12 text-center text-sm text-ink-faint">No cases yet — start one from Quick Actions.</td></tr>
            ) : rows.slice(0, 8).map(r => {
              return (
                <tr
                  key={r.caseId}
                  onClick={() => navigate(inboxRowUrl(r))}
                  className="cursor-pointer border-b border-slate-100 transition-colors hover:bg-slate-50/60"
                >
                  <Td><span className="font-mono text-[11px] text-ink">{r.caseId.slice(0, 8)}</span></Td>
                  <Td><TypeChip type={flowLabel(r.flowCode, r.flowVersion)} /></Td>
                  <Td className="text-xs text-ink-muted truncate">{r.title}</Td>
                  <Td><span className="text-xs text-ink-muted">{r.status}</span></Td>
                  <Td className="font-mono text-xs text-ink-muted">{formatDate(r.submittedAt)}</Td>
                  <Td right className="font-mono text-xs text-ink-muted">{humanAgo(r.lastActivityAt)}</Td>
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

function QuickActionsPanel() {
  // Registry-driven: each chef-shipped manifest under
  // features/<CODE>/V<N>/ surfaces as one Quick Action. The FORMS
  // metadata map is consulted for display label only — it is NOT a
  // gate for what appears here.
  //
  // PR-L1: cross-check the chef-cooked manifest set against admin's
  // flow registry. Only show flows whose latest admin row is in
  // `Approved` state — Retired / Draft / Cooking flows are hidden so
  // end-users can't launch new cases against an unfinished or
  // shelved version. While the registry is still loading we fall
  // back to showing everything (avoids flashing an empty panel on
  // page load); a transient registry-fetch failure is treated the
  // same way.
  //
  // PR-G3: actions group by groupCode (sourced from admin Site
  // Setting → Flow Groups). Sections render in groupSortOrder; the
  // catch-all '__other__' bucket sinks to the bottom for unassigned
  // entries.
  const { entries: registry } = useFlowRegistry()
  const filterByState = registry !== null
  type Action = { code: FormCode; label: string; iconKey: string | null; order: number; groupKey: string; groupLabel: string; groupIcon: string | null; groupSort: number }
  // Gate each manifest on its own version's published state (see CreateIndex):
  // a newer Draft version must not hide the still-published version we render.
  const actions: Action[] = [...formRegistry.values()]
    .filter(m => {
      if (!filterByState) return true
      return entryForVersion(registry, m.code, m.version)?.state === 'Published'
    })
    .map(m => {
      const entry = entryForVersion(registry, m.code, m.version)
      const groupKey = entry?.groupCode ?? '__other__'
      const groupLabel = entry?.groupDisplayName?.['zh-TW'] ?? entry?.groupCode ?? '其他'
      return {
        code: m.code,
        label: entry?.displayName || FORMS[m.code]?.zhLabel || m.code,
        iconKey: entry?.iconKey ?? null,
        order: entry?.displayOrder ?? 0,
        groupKey,
        groupLabel,
        groupIcon: entry?.groupIcon ?? null,
        groupSort: entry?.groupSortOrder ?? Number.MAX_SAFE_INTEGER,
      }
    })

  const sections = groupAndSort(actions)

  return (
    <SectionCard>
      <SectionTitle>Quick Actions</SectionTitle>
      {sections.length === 0 ? (
        <div className="px-3 py-6 text-center text-[11px] text-ink-faint">
          目前沒有可用的流程 — 請聯絡管理員建置新流程。
        </div>
      ) : (
        <div className="space-y-3 p-3">
          {sections.map(s => (
            <div key={s.key}>
              <div className="mb-1 flex items-center gap-1.5 text-[10px] font-semibold uppercase tracking-[0.14em] text-ink-faint">
                <GroupGlyph name={s.icon} />
                {s.label}
                <span className="ml-1 font-mono text-[10px] text-ink-faint">{s.items.length}</span>
              </div>
              <div className="grid grid-cols-2 gap-2">
                {s.items.map(a => {
                  const Glyph = resolveLauncherIcon(a.iconKey)
                  return (
                    <Link
                      key={a.code}
                      to={routes.formCreate(a.code)}
                      className="flex items-center gap-2 rounded-md border border-rule bg-white px-2.5 py-2 text-left text-xs font-medium text-ink-muted transition-colors hover:bg-slate-50 hover:text-ink"
                    >
                      <Glyph className="h-4 w-4 shrink-0 text-primary" />
                      <span className="truncate">{a.label}</span>
                    </Link>
                  )
                })}
              </div>
            </div>
          ))}
        </div>
      )}
      <div className="border-t border-rule px-3 py-2 text-center">
        <Link to="/create" className="inline-flex items-center gap-1 text-[11px] text-primary hover:underline">
          <Plus className="h-3 w-3" /> Browse all forms
        </Link>
      </div>
    </SectionCard>
  )
}

interface GroupedSection {
  key: string
  label: string
  icon: string | null
  sort: number
  items: { code: FormCode; label: string; iconKey: string | null; order: number }[]
}

function groupAndSort(actions: Array<{ code: FormCode; label: string; iconKey: string | null; order: number; groupKey: string; groupLabel: string; groupIcon: string | null; groupSort: number }>): GroupedSection[] {
  const map = new Map<string, GroupedSection>()
  for (const a of actions) {
    const item = { code: a.code, label: a.label, iconKey: a.iconKey, order: a.order }
    const existing = map.get(a.groupKey)
    if (existing) {
      existing.items.push(item)
    } else {
      map.set(a.groupKey, {
        key: a.groupKey,
        label: a.groupLabel,
        icon: a.groupIcon,
        sort: a.groupSort,
        items: [item],
      })
    }
  }
  // Within a group: admin-curated DisplayOrder first, code as the tie-break.
  for (const s of map.values()) s.items.sort((x, y) => (x.order - y.order) || x.code.localeCompare(y.code))
  return [...map.values()].sort((x, y) => x.sort - y.sort || x.label.localeCompare(y.label))
}

/** Group-header glyph — resolves a stored lucide icon name (shared
 *  catalog) with a graceful Folder fallback. */
function GroupGlyph({ name }: { name: string | null }) {
  const Icon = resolveLauncherIcon(name)
  return <Icon className="h-3 w-3 text-ink-muted" />
}

function ActivityFeedPanel({ rows, loading }: { rows: InboxRow[]; loading: boolean }) {
  const flowLabel = useFlowLabel()
  const recent = [...rows]
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
        {recent.map(r => {
          const meta = ICON_FOR_ACTIVITY[statusToActivityKind(r.lifecycle)]
          const formLabel = flowLabel(r.flowCode, r.flowVersion)
          return (
            <div key={r.caseId} className="flex items-start gap-2.5 px-3 py-2.5 hover:bg-slate-50/60">
              <span className={cn('mt-0.5 inline-flex h-6 w-6 shrink-0 items-center justify-center rounded-full', meta.bg, meta.color)}>
                <meta.Icon className="h-3 w-3" strokeWidth={2.5} />
              </span>
              <div className="min-w-0 flex-1 text-xs">
                <p className="text-ink truncate">
                  <span className="font-medium">{formLabel}</span>
                  <span className="ml-1 text-ink-muted">· {r.title}</span>
                </p>
                <p className="mt-0.5 text-[10.5px] text-ink-faint">
                  <span className="font-mono">{r.caseId.slice(0, 8)}</span>
                  {' · '}{formatActivityTime(r.lastActivityAt)}
                </p>
              </div>
            </div>
          )
        })}
      </div>
    </SectionCard>
  )
}

function statusToActivityKind(lifecycle: string): keyof typeof ICON_FOR_ACTIVITY {
  if (lifecycle === 'Completed') return 'approved'
  if (lifecycle === 'Cancelled' || lifecycle === 'Rejected') return 'rejected'
  return 'submitted'   // Open (or anything non-terminal)
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
