import { useEffect, useMemo, useState } from 'react'
import { Building2, ChevronRight, Plus, Trash2, User, UsersRound } from 'lucide-react'
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
import { Cap, FilterChip } from '@/flowcook/pages/userrole/shared'
import { PrincipalDetail } from '@/flowcook/pages/userrole/PrincipalDetail'

const TYPE_ICON: Record<PrincipalType, React.ComponentType<{ className?: string }>> = {
  0: User,
  1: Building2,
  2: UsersRound,
}

const TYPE_TONE: Record<PrincipalType, string> = {
  0: 'bg-primary/10 text-primary',
  1: 'bg-good/10 text-good',
  2: 'bg-accent/15 text-accent',
}

interface PrincipalsTabProps {
  principals: Principal[]
  roles: Role[]
  delegations: Delegation[]
  loading: boolean
  error: string | null
  refreshAll: () => Promise<void>
}

export function PrincipalsTab({
  principals, roles, delegations, loading, error, refreshAll,
}: PrincipalsTabProps) {
  const [filter, setFilter] = useState<PrincipalType | 'all'>('all')
  const [selectedId, setSelectedId] = useState<string | null>(null)
  const [effective, setEffective] = useState<EffectiveRole[]>([])

  const selected = useMemo(
    () => (selectedId ? principals.find((p) => p.id === selectedId) ?? null : null),
    [selectedId, principals],
  )

  useEffect(() => {
    if (!selected || selected.type !== 0) { setEffective([]); return }
    let cancelled = false
    void api<EffectiveRole[]>(`/api/principals/${selected.id}/effective-roles`).then((data) => {
      if (!cancelled) setEffective(data)
    }).catch(() => { if (!cancelled) setEffective([]) })
    return () => { cancelled = true }
  }, [selected, principals, delegations])

  const filtered = useMemo(() => {
    if (filter === 'all') return principals
    return principals.filter((p) => p.type === filter)
  }, [principals, filter])

  const counts = useMemo(() => {
    const c = { 0: 0, 1: 0, 2: 0 } as Record<PrincipalType, number>
    for (const p of principals) c[p.type] += 1
    return c
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
      await refreshAll()
    } catch (err) {
      window.alert(err instanceof Error ? err.message : 'Failed')
    }
  }

  async function softDelete(p: Principal) {
    if (!window.confirm(`Soft-delete "${p.displayName}"?`)) return
    try {
      await api(`/api/principals/${p.id}`, { method: 'DELETE' })
      if (selectedId === p.id) setSelectedId(null)
      await refreshAll()
    } catch (err) {
      window.alert(err instanceof Error ? err.message : 'Failed')
    }
  }

  return (
    <div className="grid h-full grid-cols-12 gap-6">
      <section className="col-span-7 flex min-h-0 flex-col rounded-lg border border-rule bg-card shadow-sm">
        <header className="flex items-center justify-between border-b border-rule px-5 py-3">
          <div className="flex items-baseline gap-3">
            <h2 className="text-base font-semibold text-ink">Principals</h2>
            <Cap>{principals.length} entries</Cap>
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
            <div className="px-5 py-12 text-center text-sm text-ink-muted">No entries.</div>
          )}
          {!loading && !error && filtered.length > 0 && (
            <ul className="divide-y divide-rule">
              {filtered.map((p) => {
                const Icon = TYPE_ICON[p.type]
                const isSelected = selected?.id === p.id
                return (
                  <li key={p.id}>
                    <button
                      onClick={() => setSelectedId(p.id)}
                      data-testid={`principal-row-${p.displayName}`}
                      className={cn(
                        'group flex w-full items-center gap-4 px-5 py-3 text-left transition-colors',
                        isSelected ? 'bg-primary/10' : 'hover:bg-bg',
                      )}
                    >
                      <div className={cn('flex h-9 w-9 items-center justify-center rounded', TYPE_TONE[p.type])}>
                        <Icon className="h-4 w-4" />
                      </div>
                      <div className="min-w-0 flex-1">
                        <div className="truncate text-[15px] font-medium text-ink">{p.displayName}</div>
                        <div className="mt-0.5 truncate text-xs text-ink-muted">
                          {p.email ?? <span className="text-ink-faint">— no email</span>}
                        </div>
                      </div>
                      <div className="shrink-0">
                        <Cap>{principalTypeLabel(p.type)}</Cap>
                      </div>
                      <button
                        onClick={(e) => { e.stopPropagation(); void softDelete(p) }}
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

      <section className="col-span-5 flex min-h-0 flex-col">
        {!selected && (
          <div className="flex h-full flex-col items-center justify-center rounded-lg border border-dashed border-rule bg-card/50 px-8 text-center shadow-sm">
            <Cap className="mb-2">no selection</Cap>
            <p className="text-lg text-ink-muted">Pick someone from the list.</p>
          </div>
        )}
        {selected && (
          <PrincipalDetail
            key={selected.id}
            principal={selected}
            principals={principals}
            roles={roles}
            delegations={delegations}
            effective={effective}
            refreshAll={refreshAll}
          />
        )}
      </section>
    </div>
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
