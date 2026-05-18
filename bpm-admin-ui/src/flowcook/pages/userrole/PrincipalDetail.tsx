import { useCallback, useEffect, useMemo, useState } from 'react'
import { Building2, Star, Trash2, User, UsersRound, X } from 'lucide-react'
import { cn } from '@/lib/cn'
import { api } from '@/flowcook/api'
import {
  type Delegation,
  type EffectiveRole,
  type Principal,
  type PrincipalRole,
  type PrincipalType,
  type Role,
  principalTypeLabel,
} from '@/flowcook/types'
import { Cap, DetailRow, Empty, Section, formatDate, formatDateLocal } from '@/flowcook/pages/userrole/shared'
import { CheckPill, PrincipalPicker, RolePicker } from '@/flowcook/pages/userrole/Pickers'

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

interface PrincipalDetailProps {
  principal: Principal
  principals: Principal[]
  roles: Role[]
  delegations: Delegation[]
  effective: EffectiveRole[]
  refreshAll: () => Promise<void>
}

export function PrincipalDetail({
  principal: p, principals, roles, delegations, effective, refreshAll,
}: PrincipalDetailProps) {
  const Icon = TYPE_ICON[p.type]
  const roleNameById = useMemo(() => {
    const m: Record<string, string> = {}
    for (const r of roles) m[r.id] = r.name
    return m
  }, [roles])
  const principalNameById = useMemo(() => {
    const m: Record<string, string> = {}
    for (const x of principals) m[x.id] = x.displayName
    return m
  }, [principals])

  return (
    <div className="flex h-full flex-col overflow-hidden rounded-lg border border-rule bg-card shadow-sm">
      <div className="border-b border-rule bg-label-bg px-6 py-5">
        <div className="flex items-center gap-3">
          <div className={cn('flex h-10 w-10 items-center justify-center rounded', TYPE_TONE[p.type])}>
            <Icon className="h-5 w-5" />
          </div>
          <div className="min-w-0 flex-1">
            <Cap>{principalTypeLabel(p.type)} · {p.active ? 'active' : 'inactive'}</Cap>
            <h2 className="mt-1 truncate text-xl font-semibold text-ink">{p.displayName}</h2>
          </div>
        </div>
      </div>

      <div className="flex-1 space-y-6 overflow-auto px-6 py-5">
        <DetailGrid p={p} />

        <RoleAssignments principal={p} roles={roles} roleNameById={roleNameById} refresh={refreshAll} />

        {p.type === 0 && (
          <DeptMembershipsEditor principal={p} principals={principals} refresh={refreshAll} />
        )}

        {p.type === 0 && (
          <EffectiveRolesView effective={effective} roleNameById={roleNameById} principalNameById={principalNameById} />
        )}

        {p.type === 0 && (
          <DelegationsEditor
            principal={p}
            principals={principals}
            delegations={delegations.filter((d) => d.delegatorPrincipalId === p.id)}
            principalNameById={principalNameById}
            refresh={refreshAll}
          />
        )}

        {p.type === 1 && (
          <DeptParentEditor principal={p} principals={principals} principalNameById={principalNameById} refresh={refreshAll} />
        )}

        {p.type === 2 && (
          <GroupMembersEditor principal={p} principals={principals} principalNameById={principalNameById} refresh={refreshAll} />
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

// ──────────────────────────────────────────────────────────────
// Role assignments — direct PrincipalRole rows on this principal
// ──────────────────────────────────────────────────────────────

function RoleAssignments({
  principal: p, roles, roleNameById, refresh,
}: { principal: Principal; roles: Role[]; roleNameById: Record<string, string>; refresh: () => Promise<void> }) {
  const [rows, setRows] = useState<PrincipalRole[]>([])
  const [loading, setLoading] = useState(true)
  const [error, setError] = useState<string | null>(null)

  const load = useCallback(async () => {
    setLoading(true)
    setError(null)
    try {
      const data = await api<PrincipalRole[]>(`/api/principals/${p.id}/roles`)
      setRows(data)
    } catch (err) {
      setError(err instanceof Error ? err.message : 'Failed')
    } finally {
      setLoading(false)
    }
  }, [p.id])

  useEffect(() => { void load() }, [load])

  async function assign(roleId: string) {
    try {
      await api(`/api/principals/${p.id}/roles`, {
        method: 'POST',
        json: { roleId, inheritToMembers: p.type !== 0 },
      })
      await load()
      await refresh()
    } catch (err) {
      window.alert(err instanceof Error ? err.message : 'Failed')
    }
  }

  async function revoke(roleId: string) {
    if (!window.confirm(`Revoke role "${roleNameById[roleId] ?? roleId}"?`)) return
    try {
      await api(`/api/principals/${p.id}/roles/${roleId}`, { method: 'DELETE' })
      await load()
      await refresh()
    } catch (err) {
      window.alert(err instanceof Error ? err.message : 'Failed')
    }
  }

  async function toggleInherit(row: PrincipalRole) {
    try {
      await api(`/api/principals/${p.id}/roles`, {
        method: 'POST',
        json: { roleId: row.roleId, inheritToMembers: !row.inheritToMembers },
      })
      await load()
      await refresh()
    } catch (err) {
      window.alert(err instanceof Error ? err.message : 'Failed')
    }
  }

  const assignedIds = new Set(rows.map((r) => r.roleId))
  const inheritApplicable = p.type !== 0 // user-typed principals: inherit flag is meaningless

  return (
    <Section
      title="Role assignments"
      hint={loading ? '…' : `${rows.length} direct`}
    >
      <div className="space-y-1.5">
        {error && <p className="rounded border border-danger/30 bg-danger/5 px-3 py-2 text-xs text-danger">{error}</p>}
        {!loading && rows.length === 0 && <Empty>No roles assigned directly.</Empty>}
        {rows.map((row) => (
          <div
            key={row.roleId}
            className="flex items-center gap-3 rounded border border-rule bg-bg px-3 py-2"
          >
            <span className="flex-1 truncate text-sm font-medium text-ink">
              {roleNameById[row.roleId] ?? row.roleId}
            </span>
            {inheritApplicable && (
              <CheckPill
                checked={row.inheritToMembers}
                onClick={() => void toggleInherit(row)}
                label="inherit to members"
              />
            )}
            <button
              onClick={() => void revoke(row.roleId)}
              title="Revoke"
              className="flex h-7 w-7 items-center justify-center rounded text-ink-faint transition-colors hover:bg-danger/10 hover:text-danger"
            >
              <X className="h-3.5 w-3.5" />
            </button>
          </div>
        ))}
        <div className="pt-1">
          <RolePicker roles={roles} excludeIds={assignedIds} onPick={(id) => void assign(id)} />
        </div>
      </div>
    </Section>
  )
}

// ──────────────────────────────────────────────────────────────
// Effective roles — read-only resolved view (kept from v1)
// ──────────────────────────────────────────────────────────────

function EffectiveRolesView({
  effective, roleNameById, principalNameById,
}: { effective: EffectiveRole[]; roleNameById: Record<string, string>; principalNameById: Record<string, string> }) {
  return (
    <Section title="Effective roles" hint={`${effective.length} resolved`}>
      {effective.length === 0 ? (
        <Empty>No effective roles. Assign one above or inherit from a dept / group.</Empty>
      ) : (
        <ul className="space-y-1.5">
          {effective.map((er, i) => (
            <li
              key={`${er.roleId}-${i}`}
              className="flex items-center justify-between rounded border border-rule bg-bg px-3 py-2"
            >
              <div className="flex items-center gap-2">
                <span className="font-mono text-[10px] text-ink-faint">{String(i + 1).padStart(2, '0')}</span>
                <span className="font-medium text-ink">{roleNameById[er.roleId] ?? er.roleId}</span>
              </div>
              <span
                className={cn(
                  'rounded-full px-2 py-0.5 text-[11px]',
                  er.viaInherit ? 'bg-good/10 text-good' : 'bg-primary/10 text-primary',
                )}
              >
                {er.viaInherit ? `via ${principalNameById[er.sourcePrincipalId] ?? '…'}` : 'direct'}
              </span>
            </li>
          ))}
        </ul>
      )}
    </Section>
  )
}

// ──────────────────────────────────────────────────────────────
// Dept memberships — for user principals
// ──────────────────────────────────────────────────────────────

interface DeptMembership {
  userId: string
  deptId: string
  isPrimary: boolean
}

function DeptMembershipsEditor({
  principal: p, principals, refresh,
}: { principal: Principal; principals: Principal[]; refresh: () => Promise<void> }) {
  const [rows, setRows] = useState<DeptMembership[]>([])
  const [loading, setLoading] = useState(true)

  const load = useCallback(async () => {
    setLoading(true)
    try {
      const data = await api<DeptMembership[]>(`/api/principals/${p.id}/dept-memberships`)
      setRows(data)
    } catch {
      setRows([])
    } finally {
      setLoading(false)
    }
  }, [p.id])

  useEffect(() => { void load() }, [load])

  const deptNameById = useMemo(() => {
    const m: Record<string, string> = {}
    for (const x of principals) if (x.type === 1) m[x.id] = x.displayName
    return m
  }, [principals])

  async function add(deptId: string) {
    try {
      await api(`/api/principals/${p.id}/dept-memberships`, {
        method: 'POST',
        json: { deptId, isPrimary: rows.length === 0 },
      })
      await load()
      await refresh()
    } catch (err) {
      window.alert(err instanceof Error ? err.message : 'Failed')
    }
  }

  async function remove(deptId: string) {
    if (!window.confirm(`Remove from dept "${deptNameById[deptId] ?? deptId}"?`)) return
    try {
      await api(`/api/principals/${p.id}/dept-memberships/${deptId}`, { method: 'DELETE' })
      await load()
      await refresh()
    } catch (err) {
      window.alert(err instanceof Error ? err.message : 'Failed')
    }
  }

  async function setPrimary(deptId: string) {
    try {
      await api(`/api/principals/${p.id}/dept-memberships`, {
        method: 'POST',
        json: { deptId, isPrimary: true },
      })
      await load()
      await refresh()
    } catch (err) {
      window.alert(err instanceof Error ? err.message : 'Failed')
    }
  }

  const excludeIds = new Set(rows.map((r) => r.deptId))

  return (
    <Section title="Dept memberships" hint={loading ? '…' : `${rows.length} dept(s)`}>
      <div className="space-y-1.5">
        {!loading && rows.length === 0 && <Empty>Not a member of any dept.</Empty>}
        {rows.map((row) => (
          <div
            key={row.deptId}
            className="flex items-center gap-3 rounded border border-rule bg-bg px-3 py-2"
          >
            <Building2 className="h-3.5 w-3.5 text-good" />
            <span className="flex-1 truncate text-sm font-medium text-ink">
              {deptNameById[row.deptId] ?? row.deptId}
            </span>
            <button
              onClick={() => !row.isPrimary && void setPrimary(row.deptId)}
              title={row.isPrimary ? 'Primary dept' : 'Mark as primary'}
              className={cn(
                'flex h-7 w-7 items-center justify-center rounded transition-colors',
                row.isPrimary
                  ? 'text-accent'
                  : 'text-ink-faint hover:bg-accent/10 hover:text-accent',
              )}
            >
              <Star className={cn('h-3.5 w-3.5', row.isPrimary && 'fill-current')} />
            </button>
            <button
              onClick={() => void remove(row.deptId)}
              title="Remove"
              className="flex h-7 w-7 items-center justify-center rounded text-ink-faint transition-colors hover:bg-danger/10 hover:text-danger"
            >
              <X className="h-3.5 w-3.5" />
            </button>
          </div>
        ))}
        <div className="pt-1">
          <PrincipalPicker
            principals={principals}
            excludeIds={excludeIds}
            acceptTypes={[1]}
            onPick={(id) => void add(id)}
            buttonLabel="+ Add dept"
            placeholder="Search dept…"
          />
        </div>
      </div>
    </Section>
  )
}

// ──────────────────────────────────────────────────────────────
// Delegations — for user principals
// ──────────────────────────────────────────────────────────────

function DelegationsEditor({
  principal: p, principals, delegations, principalNameById, refresh,
}: {
  principal: Principal
  principals: Principal[]
  delegations: Delegation[]
  principalNameById: Record<string, string>
  refresh: () => Promise<void>
}) {
  const [creating, setCreating] = useState(false)

  async function cancelDel(id: string) {
    if (!window.confirm('Cancel this delegation?')) return
    try {
      await api(`/api/delegations/${id}`, { method: 'DELETE' })
      await refresh()
    } catch (err) {
      window.alert(err instanceof Error ? err.message : 'Failed')
    }
  }

  return (
    <Section title="Delegations" hint={`${delegations.length}`}>
      <div className="space-y-1.5">
        {delegations.length === 0 && !creating && (
          <Empty>No delegations on file.</Empty>
        )}
        {delegations.map((d) => (
          <div
            key={d.id}
            className="rounded border border-rule bg-bg px-3 py-2"
          >
            <div className="flex items-baseline justify-between gap-3">
              <span className="text-sm text-ink">
                → <span className="font-medium">{principalNameById[d.delegateToUserId] ?? d.delegateToUserId}</span>
              </span>
              <div className="flex items-center gap-2">
                <Cap className={cn(d.active ? 'text-good' : 'text-ink-faint')}>
                  {d.active ? 'active' : 'inactive'}
                </Cap>
                {d.active && (
                  <button
                    onClick={() => void cancelDel(d.id)}
                    title="Cancel"
                    className="flex h-6 w-6 items-center justify-center rounded text-ink-faint transition-colors hover:bg-danger/10 hover:text-danger"
                  >
                    <X className="h-3 w-3" />
                  </button>
                )}
              </div>
            </div>
            <div className="mt-0.5 font-mono text-[11px] text-ink-muted">
              {formatDate(d.startAt)} → {formatDate(d.endAt)}
            </div>
            {d.reason && (
              <div className="mt-1 text-[11px] italic text-ink-muted">"{d.reason}"</div>
            )}
          </div>
        ))}

        {creating ? (
          <NewDelegationForm
            delegatorId={p.id}
            principals={principals}
            onDone={async (didSave) => {
              setCreating(false)
              if (didSave) await refresh()
            }}
          />
        ) : (
          <div className="pt-1">
            <button
              onClick={() => setCreating(true)}
              className="inline-flex items-center gap-1 rounded border border-dashed border-rule bg-card px-2.5 py-1 text-xs font-medium text-ink-muted transition-colors hover:border-primary hover:bg-primary/10 hover:text-primary"
            >
              + New delegation
            </button>
          </div>
        )}
      </div>
    </Section>
  )
}

function NewDelegationForm({
  delegatorId, principals, onDone,
}: { delegatorId: string; principals: Principal[]; onDone: (saved: boolean) => Promise<void> | void }) {
  const now = useMemo(() => new Date(), [])
  const oneWeekLater = useMemo(() => new Date(now.getTime() + 7 * 24 * 60 * 60 * 1000), [now])

  const [target, setTarget] = useState<string | null>(null)
  const [startAt, setStartAt] = useState(formatDateLocal(now))
  const [endAt, setEndAt] = useState(formatDateLocal(oneWeekLater))
  const [reason, setReason] = useState('')
  const [saving, setSaving] = useState(false)
  const [error, setError] = useState<string | null>(null)

  const targetUser = principals.find((x) => x.id === target)
  const targetExcludeIds = new Set([delegatorId])

  async function save() {
    if (!target) { setError('Pick a delegate user.'); return }
    setSaving(true)
    setError(null)
    try {
      await api('/api/delegations', {
        method: 'POST',
        json: {
          delegatorPrincipalId: delegatorId,
          delegateToUserId: target,
          startAt: new Date(startAt).toISOString(),
          endAt: new Date(endAt).toISOString(),
          reason: reason.trim() || null,
        },
      })
      await onDone(true)
    } catch (err) {
      setError(err instanceof Error ? err.message : 'Failed')
      setSaving(false)
    }
  }

  return (
    <div className="rounded border border-primary/40 bg-primary/5 p-3 space-y-3">
      <Cap>new delegation</Cap>

      <div>
        <span className="mb-1 block font-mono text-[10px] tracking-[0.14em] uppercase text-ink-muted">
          Delegate to
        </span>
        {targetUser ? (
          <div className="flex items-center gap-2 rounded border border-rule bg-card px-2 py-1 text-xs">
            <User className="h-3 w-3 text-primary" />
            <span className="flex-1 text-ink">{targetUser.displayName}</span>
            <button
              onClick={() => setTarget(null)}
              className="text-ink-faint hover:text-ink"
              title="Clear"
            >
              <X className="h-3 w-3" />
            </button>
          </div>
        ) : (
          <PrincipalPicker
            principals={principals}
            excludeIds={targetExcludeIds}
            acceptTypes={[0]}
            onPick={(id) => setTarget(id)}
            buttonLabel="+ Pick user"
            placeholder="Search user…"
          />
        )}
      </div>

      <div className="grid grid-cols-2 gap-3">
        <label className="block">
          <span className="mb-1 block font-mono text-[10px] tracking-[0.14em] uppercase text-ink-muted">Start</span>
          <input
            type="datetime-local"
            value={startAt}
            onChange={(e) => setStartAt(e.target.value)}
            className="block w-full rounded border border-rule bg-white px-2 py-1 text-xs text-ink outline-none focus:border-primary focus:ring-2 focus:ring-primary/20"
          />
        </label>
        <label className="block">
          <span className="mb-1 block font-mono text-[10px] tracking-[0.14em] uppercase text-ink-muted">End</span>
          <input
            type="datetime-local"
            value={endAt}
            onChange={(e) => setEndAt(e.target.value)}
            className="block w-full rounded border border-rule bg-white px-2 py-1 text-xs text-ink outline-none focus:border-primary focus:ring-2 focus:ring-primary/20"
          />
        </label>
      </div>

      <label className="block">
        <span className="mb-1 block font-mono text-[10px] tracking-[0.14em] uppercase text-ink-muted">Reason</span>
        <input
          value={reason}
          onChange={(e) => setReason(e.target.value)}
          placeholder="e.g. parental leave"
          className="block w-full rounded border border-rule bg-white px-2 py-1 text-xs text-ink outline-none placeholder:text-ink-faint focus:border-primary focus:ring-2 focus:ring-primary/20"
        />
      </label>

      {error && <p className="rounded border border-danger/30 bg-danger/5 px-2 py-1 text-xs text-danger">{error}</p>}

      <div className="flex items-center justify-end gap-2">
        <button
          onClick={() => onDone(false)}
          disabled={saving}
          className="rounded px-2 py-1 text-xs text-ink-muted hover:text-ink"
        >
          Cancel
        </button>
        <button
          onClick={() => void save()}
          disabled={saving || !target}
          className="rounded bg-primary px-2.5 py-1 text-xs font-medium text-white transition-colors hover:bg-primary/90 disabled:opacity-50"
        >
          {saving ? 'Saving…' : 'Create'}
        </button>
      </div>
    </div>
  )
}

// ──────────────────────────────────────────────────────────────
// Dept parent — for dept principals
// ──────────────────────────────────────────────────────────────

function DeptParentEditor({
  principal: p, principals, principalNameById, refresh,
}: { principal: Principal; principals: Principal[]; principalNameById: Record<string, string>; refresh: () => Promise<void> }) {
  const [currentParent, setCurrentParent] = useState<string | null>(null)
  const [loading, setLoading] = useState(true)
  const [working, setWorking] = useState(false)

  const load = useCallback(async () => {
    setLoading(true)
    try {
      // No GET on /parent — DeptParent rows are written through PUT. Probe by
      // attempting a list on principals dept-parents endpoint? Spec hasn't
      // shipped it. Fall back to reading the join-table row would need a new
      // endpoint; for now we infer from null after save and from response of
      // PUT. As a workaround we 200/404 probe with GET if available.
      const data = await api<{ parentDeptId: string | null } | null>(`/api/principals/${p.id}/parent`)
        .catch(() => null)
      setCurrentParent(data?.parentDeptId ?? null)
    } finally {
      setLoading(false)
    }
  }, [p.id])

  useEffect(() => { void load() }, [load])

  async function setParent(parentDeptId: string | null) {
    setWorking(true)
    try {
      await api(`/api/principals/${p.id}/parent`, {
        method: 'PUT',
        json: { parentDeptId },
      })
      await load()
      await refresh()
    } catch (err) {
      window.alert(err instanceof Error ? err.message : 'Failed')
    } finally {
      setWorking(false)
    }
  }

  const excludeIds = new Set([p.id, ...(currentParent ? [currentParent] : [])])

  return (
    <Section title="Parent dept" hint={loading ? '…' : (currentParent ? 'set' : 'top-level')}>
      <div className="flex items-center gap-2">
        {currentParent ? (
          <div className="flex flex-1 items-center gap-2 rounded border border-rule bg-bg px-3 py-2">
            <Building2 className="h-3.5 w-3.5 text-good" />
            <span className="flex-1 truncate text-sm font-medium text-ink">
              {principalNameById[currentParent] ?? currentParent}
            </span>
            <button
              onClick={() => void setParent(null)}
              disabled={working}
              title="Clear parent"
              className="flex h-7 w-7 items-center justify-center rounded text-ink-faint transition-colors hover:bg-danger/10 hover:text-danger"
            >
              <X className="h-3.5 w-3.5" />
            </button>
          </div>
        ) : (
          <p className="flex-1 rounded border border-dashed border-rule bg-bg/50 px-3 py-2 text-xs italic text-ink-muted">
            Top-level dept (no parent).
          </p>
        )}
        <PrincipalPicker
          principals={principals}
          excludeIds={excludeIds}
          acceptTypes={[1]}
          onPick={(id) => void setParent(id)}
          buttonLabel={currentParent ? '↻ Change' : '+ Set parent'}
          placeholder="Search dept…"
        />
      </div>
    </Section>
  )
}

// ──────────────────────────────────────────────────────────────
// Group members — for group principals
// ──────────────────────────────────────────────────────────────

interface GroupMember {
  groupId: string
  memberPrincipalId: string
}

function GroupMembersEditor({
  principal: p, principals, principalNameById, refresh,
}: { principal: Principal; principals: Principal[]; principalNameById: Record<string, string>; refresh: () => Promise<void> }) {
  const [rows, setRows] = useState<GroupMember[]>([])
  const [loading, setLoading] = useState(true)

  const load = useCallback(async () => {
    setLoading(true)
    try {
      const data = await api<GroupMember[]>(`/api/principals/${p.id}/members`)
      setRows(data)
    } catch {
      setRows([])
    } finally {
      setLoading(false)
    }
  }, [p.id])

  useEffect(() => { void load() }, [load])

  async function add(memberId: string) {
    try {
      await api(`/api/principals/${p.id}/members`, {
        method: 'POST',
        json: { memberPrincipalId: memberId },
      })
      await load()
      await refresh()
    } catch (err) {
      window.alert(err instanceof Error ? err.message : 'Failed')
    }
  }

  async function remove(memberId: string) {
    if (!window.confirm(`Remove "${principalNameById[memberId] ?? memberId}" from group?`)) return
    try {
      await api(`/api/principals/${p.id}/members/${memberId}`, { method: 'DELETE' })
      await load()
      await refresh()
    } catch (err) {
      window.alert(err instanceof Error ? err.message : 'Failed')
    }
  }

  const excludeIds = new Set([p.id, ...rows.map((r) => r.memberPrincipalId)])

  return (
    <Section title="Members" hint={loading ? '…' : `${rows.length}`}>
      <div className="space-y-1.5">
        {!loading && rows.length === 0 && <Empty>No members yet.</Empty>}
        {rows.map((row) => {
          const member = principals.find((x) => x.id === row.memberPrincipalId)
          const Icon = member ? TYPE_ICON[member.type] : User
          return (
            <div
              key={row.memberPrincipalId}
              className="flex items-center gap-3 rounded border border-rule bg-bg px-3 py-2"
            >
              <Icon className="h-3.5 w-3.5 text-ink-faint" />
              <span className="flex-1 truncate text-sm font-medium text-ink">
                {member?.displayName ?? row.memberPrincipalId}
              </span>
              {member && (
                <Cap>{principalTypeLabel(member.type)}</Cap>
              )}
              <button
                onClick={() => void remove(row.memberPrincipalId)}
                title="Remove"
                className="flex h-7 w-7 items-center justify-center rounded text-ink-faint transition-colors hover:bg-danger/10 hover:text-danger"
              >
                <Trash2 className="h-3.5 w-3.5" />
              </button>
            </div>
          )
        })}
        <div className="pt-1">
          <PrincipalPicker
            principals={principals}
            excludeIds={excludeIds}
            acceptTypes={[0, 1, 2]}
            onPick={(id) => void add(id)}
            buttonLabel="+ Add member"
            placeholder="Search user / dept / group…"
          />
        </div>
      </div>
    </Section>
  )
}
