import { useCallback, useEffect, useMemo, useState } from 'react'
import { Building2, ChevronRight, Plus, Trash2, Users, UsersRound } from 'lucide-react'
import { cn } from '@/lib/cn'
import { api } from '@/flowcook/api'
import {
  type Delegation,
  type EffectiveRole,
  type Principal,
  type PrincipalType,
  type Role,
  principalTypeLabel,
} from '@/flowcook/types'

const TYPE_ICON: Record<PrincipalType, React.ComponentType<{ className?: string }>> = {
  0: Users,
  1: Building2,
  2: UsersRound,
}

const TYPE_TONE: Record<PrincipalType, string> = {
  0: 'bg-primary/10 text-primary',
  1: 'bg-good/10 text-good',
  2: 'bg-accent/15 text-accent',
}

export function UserRolePage() {
  const [principals, setPrincipals] = useState<Principal[]>([])
  const [roles, setRoles] = useState<Role[]>([])
  const [delegations, setDelegations] = useState<Delegation[]>([])
  const [filter, setFilter] = useState<PrincipalType | 'all'>('all')
  const [selected, setSelected] = useState<Principal | null>(null)
  const [effective, setEffective] = useState<EffectiveRole[]>([])
  const [loading, setLoading] = useState(true)
  const [error, setError] = useState<string | null>(null)

  const refresh = useCallback(async () => {
    setLoading(true)
    setError(null)
    try {
      const [principalsRes, rolesRes, delegationsRes] = await Promise.all([
        api<Principal[]>('/api/principals'),
        api<Role[]>('/api/roles'),
        api<Delegation[]>('/api/delegations'),
      ])
      setPrincipals(principalsRes)
      setRoles(rolesRes)
      setDelegations(delegationsRes)
    } catch (err) {
      setError(err instanceof Error ? err.message : 'Failed to load')
    } finally {
      setLoading(false)
    }
  }, [])

  useEffect(() => {
    void refresh()
  }, [refresh])

  useEffect(() => {
    if (!selected || selected.type !== 0) {
      setEffective([])
      return
    }
    let cancelled = false
    void api<EffectiveRole[]>(`/api/principals/${selected.id}/effective-roles`).then((data) => {
      if (!cancelled) setEffective(data)
    })
    return () => { cancelled = true }
  }, [selected])

  const filtered = useMemo(() => {
    if (filter === 'all') return principals
    return principals.filter((p) => p.type === filter)
  }, [principals, filter])

  const counts = useMemo(() => {
    const c = { 0: 0, 1: 0, 2: 0 } as Record<PrincipalType, number>
    for (const p of principals) c[p.type] += 1
    return c
  }, [principals])

  const roleNameById = useMemo(() => {
    const map: Record<string, string> = {}
    for (const r of roles) map[r.id] = r.name
    return map
  }, [roles])

  const principalNameById = useMemo(() => {
    const map: Record<string, string> = {}
    for (const p of principals) map[p.id] = p.displayName
    return map
  }, [principals])

  async function createPrincipal(type: PrincipalType) {
    const displayName = window.prompt(`Display name for new ${principalTypeLabel(type)}:`)
    if (!displayName) return
    const email = type === 0 ? window.prompt('Email (optional):') ?? null : null
    try {
      await api('/api/principals', {
        method: 'POST',
        json: { type, displayName, email },
      })
      await refresh()
    } catch (err) {
      window.alert(err instanceof Error ? err.message : 'Failed')
    }
  }

  async function softDelete(p: Principal) {
    if (!window.confirm(`Soft-delete "${p.displayName}"?`)) return
    try {
      await api(`/api/principals/${p.id}`, { method: 'DELETE' })
      if (selected?.id === p.id) setSelected(null)
      await refresh()
    } catch (err) {
      window.alert(err instanceof Error ? err.message : 'Failed')
    }
  }

  return (
    <div className="grid h-full grid-cols-12 gap-6">
      {/* left: filter + list */}
      <section className="col-span-7 flex min-h-0 flex-col rounded-lg border border-rule bg-card shadow-sm">
        <header className="flex items-center justify-between border-b border-rule px-5 py-3">
          <div className="flex items-baseline gap-3">
            <h2 className="text-base font-semibold text-ink">Principals</h2>
            <span className="font-mono text-[10px] tracking-[0.14em] uppercase text-ink-muted">
              {principals.length} entries
            </span>
          </div>
          <div className="flex items-center gap-2">
            <FilterChip label="All" count={principals.length} active={filter === 'all'} onClick={() => setFilter('all')} />
            <FilterChip label="Users" count={counts[0]} active={filter === 0} onClick={() => setFilter(0)} />
            <FilterChip label="Depts" count={counts[1]} active={filter === 1} onClick={() => setFilter(1)} />
            <FilterChip label="Groups" count={counts[2]} active={filter === 2} onClick={() => setFilter(2)} />
          </div>
        </header>

        <div className="flex items-center justify-end gap-2 border-b border-rule bg-label-bg px-5 py-2">
          <AddButton label="User" onClick={() => createPrincipal(0)} testId="add-user" />
          <AddButton label="Dept" onClick={() => createPrincipal(1)} testId="add-dept" />
          <AddButton label="Group" onClick={() => createPrincipal(2)} testId="add-group" />
        </div>

        <div className="flex-1 overflow-auto">
          {loading && <div className="px-5 py-6 text-sm text-ink-muted">Loading…</div>}
          {error && <div className="px-5 py-6 text-sm text-danger">{error}</div>}
          {!loading && !error && filtered.length === 0 && (
            <div className="px-5 py-12 text-center text-sm text-ink-muted">
              No entries.
            </div>
          )}
          {!loading && !error && filtered.length > 0 && (
            <ul className="divide-y divide-rule">
              {filtered.map((p) => {
                const Icon = TYPE_ICON[p.type]
                const isSelected = selected?.id === p.id
                return (
                  <li key={p.id}>
                    <button
                      onClick={() => setSelected(p)}
                      data-testid={`principal-row-${p.displayName}`}
                      className={cn(
                        'group flex w-full items-center gap-4 px-5 py-3 text-left transition-colors',
                        isSelected
                          ? 'bg-primary/10'
                          : 'hover:bg-bg',
                      )}
                    >
                      <div className={cn('flex h-9 w-9 items-center justify-center rounded', TYPE_TONE[p.type])}>
                        <Icon className="h-4 w-4" />
                      </div>
                      <div className="min-w-0 flex-1">
                        <div className="truncate text-[15px] font-medium text-ink">
                          {p.displayName}
                        </div>
                        <div className="mt-0.5 truncate text-xs text-ink-muted">
                          {p.email ?? <span className="text-ink-faint">— no email</span>}
                        </div>
                      </div>
                      <div className="shrink-0 font-mono text-[10px] tracking-[0.14em] uppercase text-ink-muted">
                        {principalTypeLabel(p.type)}
                      </div>
                      <button
                        onClick={(e) => {
                          e.stopPropagation()
                          void softDelete(p)
                        }}
                        title="Soft-delete"
                        className="flex h-7 w-7 items-center justify-center rounded text-ink-faint opacity-0 transition-opacity group-hover:opacity-100 hover:bg-danger/10 hover:text-danger"
                      >
                        <Trash2 className="h-3.5 w-3.5" />
                      </button>
                      <ChevronRight className={cn('h-4 w-4 shrink-0', isSelected ? 'text-primary' : 'text-ink-faint')} />
                    </button>
                  </li>
                )
              })}
            </ul>
          )}
        </div>
      </section>

      {/* right: detail */}
      <section className="col-span-5 flex min-h-0 flex-col">
        {!selected && (
          <div className="flex h-full flex-col items-center justify-center rounded-lg border border-dashed border-rule bg-card/50 px-8 text-center shadow-sm">
            <div className="mb-2 font-mono text-[10px] tracking-[0.14em] uppercase text-ink-muted">
              no selection
            </div>
            <p className="text-lg text-ink-muted">
              Pick someone from the list.
            </p>
          </div>
        )}
        {selected && <PrincipalDetail p={selected} effective={effective} delegations={delegations} roleNameById={roleNameById} principalNameById={principalNameById} />}
      </section>
    </div>
  )
}

interface FilterChipProps {
  label: string
  count: number
  active: boolean
  onClick: () => void
}

function FilterChip({ label, count, active, onClick }: FilterChipProps) {
  return (
    <button
      onClick={onClick}
      className={cn(
        'inline-flex items-center gap-1.5 rounded-full border px-3 py-1 text-xs transition-colors',
        active
          ? 'border-primary bg-primary text-white'
          : 'border-rule bg-card text-ink-muted hover:border-primary/40 hover:text-ink',
      )}
    >
      <span className="font-medium">{label}</span>
      <span className={cn('font-mono text-[10px]', active ? 'text-white/80' : 'text-ink-faint')}>
        {count}
      </span>
    </button>
  )
}

function AddButton({ label, onClick, testId }: { label: string; onClick: () => void; testId: string }) {
  return (
    <button
      onClick={onClick}
      data-testid={testId}
      className="inline-flex items-center gap-1 rounded border border-rule bg-card px-2.5 py-1 text-xs font-medium text-ink-muted transition-colors hover:border-primary hover:bg-primary/10 hover:text-primary"
    >
      <Plus className="h-3 w-3" /> {label}
    </button>
  )
}

interface PrincipalDetailProps {
  p: Principal
  effective: EffectiveRole[]
  delegations: Delegation[]
  roleNameById: Record<string, string>
  principalNameById: Record<string, string>
}

function PrincipalDetail({ p, effective, delegations, roleNameById, principalNameById }: PrincipalDetailProps) {
  const Icon = TYPE_ICON[p.type]
  const myDelegations = delegations.filter((d) => d.delegatorPrincipalId === p.id)

  return (
    <div className="flex h-full flex-col overflow-hidden rounded-lg border border-rule bg-card shadow-sm">
      <div className="border-b border-rule bg-label-bg px-6 py-5">
        <div className="flex items-center gap-3">
          <div className={cn('flex h-10 w-10 items-center justify-center rounded', TYPE_TONE[p.type])}>
            <Icon className="h-5 w-5" />
          </div>
          <div className="min-w-0 flex-1">
            <div className="font-mono text-[10px] tracking-[0.14em] uppercase text-ink-muted">
              {principalTypeLabel(p.type)} · {p.active ? 'active' : 'inactive'}
            </div>
            <h2 className="mt-1 truncate text-xl font-semibold text-ink">
              {p.displayName}
            </h2>
          </div>
        </div>
      </div>

      <div className="flex-1 space-y-6 overflow-auto px-6 py-5">
        <DetailGrid p={p} />

        {p.type === 0 && (
          <Section title="Effective roles" hint={`${effective.length} resolved`}>
            {effective.length === 0 ? (
              <Empty>No effective roles assigned.</Empty>
            ) : (
              <ul className="space-y-1.5">
                {effective.map((er, i) => (
                  <li
                    key={`${er.roleId}-${i}`}
                    className="flex items-center justify-between rounded border border-rule bg-bg px-3 py-2"
                  >
                    <div className="flex items-center gap-2">
                      <span className="font-mono text-[10px] text-ink-faint">{String(i + 1).padStart(2, '0')}</span>
                      <span className="font-medium text-ink">
                        {roleNameById[er.roleId] ?? er.roleId}
                      </span>
                    </div>
                    <span
                      className={cn(
                        'rounded-full px-2 py-0.5 text-[11px]',
                        er.viaInherit
                          ? 'bg-good/10 text-good'
                          : 'bg-primary/10 text-primary',
                      )}
                    >
                      {er.viaInherit
                        ? `via ${principalNameById[er.sourcePrincipalId] ?? '…'}`
                        : 'direct'}
                    </span>
                  </li>
                ))}
              </ul>
            )}
          </Section>
        )}

        {p.type === 0 && (
          <Section title="Delegations" hint={`${myDelegations.length}`}>
            {myDelegations.length === 0 ? (
              <Empty>No delegations on file.</Empty>
            ) : (
              <ul className="space-y-1.5">
                {myDelegations.map((d) => (
                  <li
                    key={d.id}
                    className="rounded border border-rule bg-bg px-3 py-2"
                  >
                    <div className="flex items-baseline justify-between">
                      <span className="text-sm text-ink">
                        →{' '}
                        <span className="font-medium">
                          {principalNameById[d.delegateToUserId] ?? d.delegateToUserId}
                        </span>
                      </span>
                      <span
                        className={cn(
                          'font-mono text-[10px] tracking-[0.14em] uppercase',
                          d.active ? 'text-good' : 'text-ink-faint',
                        )}
                      >
                        {d.active ? 'active' : 'inactive'}
                      </span>
                    </div>
                    <div className="mt-0.5 font-mono text-[11px] text-ink-muted">
                      {formatDate(d.startAt)} → {formatDate(d.endAt)}
                    </div>
                    {d.reason && (
                      <div className="mt-1 text-[11px] italic text-ink-muted">"{d.reason}"</div>
                    )}
                  </li>
                ))}
              </ul>
            )}
          </Section>
        )}
      </div>
    </div>
  )
}

function DetailGrid({ p }: { p: Principal }) {
  return (
    <dl className="grid grid-cols-2 gap-x-6 gap-y-3 text-xs">
      <DetailRow label="ID">
        <span className="font-mono text-[11px] text-ink">{p.id.slice(0, 8)}…{p.id.slice(-4)}</span>
      </DetailRow>
      <DetailRow label="Email">
        <span className="text-ink">{p.email ?? <span className="text-ink-faint">—</span>}</span>
      </DetailRow>
      <DetailRow label="Created">
        <span className="font-mono text-[11px] text-ink">{formatDate(p.createdAt)}</span>
      </DetailRow>
      <DetailRow label="Updated">
        <span className="font-mono text-[11px] text-ink">{formatDate(p.updatedAt)}</span>
      </DetailRow>
    </dl>
  )
}

function DetailRow({ label, children }: { label: string; children: React.ReactNode }) {
  return (
    <div>
      <dt className="font-mono text-[10px] tracking-[0.14em] uppercase text-ink-muted">{label}</dt>
      <dd className="mt-1">{children}</dd>
    </div>
  )
}

function Section({ title, hint, children }: { title: string; hint?: string; children: React.ReactNode }) {
  return (
    <section>
      <header className="mb-2.5 flex items-baseline justify-between">
        <h3 className="text-sm font-semibold text-ink">{title}</h3>
        {hint && <span className="font-mono text-[10px] tracking-[0.14em] uppercase text-ink-muted">{hint}</span>}
      </header>
      {children}
    </section>
  )
}

function Empty({ children }: { children: React.ReactNode }) {
  return <p className="rounded border border-dashed border-rule bg-bg/50 px-3 py-2 text-xs italic text-ink-muted">{children}</p>
}

function formatDate(s: string): string {
  try {
    return new Date(s).toISOString().slice(0, 16).replace('T', ' ')
  } catch {
    return s
  }
}
