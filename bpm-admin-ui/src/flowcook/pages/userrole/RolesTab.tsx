import { useCallback, useEffect, useMemo, useState } from 'react'
import { ChevronRight, Plus, ShieldCheck, Trash2 } from 'lucide-react'
import { cn } from '@/lib/cn'
import { api } from '@/flowcook/api'
import { type PrincipalRole, type Role } from '@/flowcook/types'
import { Cap, Empty, Section } from '@/flowcook/pages/userrole/shared'

interface RolesTabProps {
  roles: Role[]
  refreshRoles: () => Promise<void>
}

export function RolesTab({ roles, refreshRoles }: RolesTabProps) {
  const [selected, setSelected] = useState<Role | null>(null)
  const [creating, setCreating] = useState(false)

  useEffect(() => {
    if (selected && !roles.find((r) => r.id === selected.id)) setSelected(null)
  }, [roles, selected])

  const sorted = useMemo(() => {
    return [...roles].sort((a, b) => {
      if (a.isSystem !== b.isSystem) return a.isSystem ? -1 : 1
      return a.name.localeCompare(b.name)
    })
  }, [roles])

  return (
    <div className="grid h-full grid-cols-12 gap-6">
      <section className="col-span-7 flex min-h-0 flex-col rounded-lg border border-rule bg-card shadow-sm">
        <header className="flex items-center justify-between border-b border-rule px-5 py-3">
          <div className="flex items-baseline gap-3">
            <h2 className="text-base font-semibold text-ink">Roles</h2>
            <Cap>{roles.length} total</Cap>
          </div>
          <button
            onClick={() => { setCreating(true); setSelected(null) }}
            data-testid="role-create"
            className="inline-flex items-center gap-1 rounded border border-rule bg-card px-2.5 py-1 text-xs font-medium text-ink-muted transition-colors hover:border-primary hover:bg-primary/10 hover:text-primary"
          >
            <Plus className="h-3 w-3" /> New role
          </button>
        </header>

        <div className="flex-1 overflow-auto">
          {sorted.length === 0 && !creating && (
            <div className="px-5 py-12 text-center text-sm text-ink-muted">No roles defined.</div>
          )}
          {sorted.length > 0 && (
            <ul className="divide-y divide-rule">
              {sorted.map((r) => {
                const isSelected = selected?.id === r.id
                return (
                  <li key={r.id}>
                    <button
                      onClick={() => { setSelected(r); setCreating(false) }}
                      data-testid={`role-row-${r.name}`}
                      className={cn(
                        'flex w-full items-center gap-4 px-5 py-3 text-left transition-colors',
                        isSelected ? 'bg-primary/10' : 'hover:bg-bg',
                      )}
                    >
                      <div className={cn(
                        'flex h-9 w-9 items-center justify-center rounded',
                        r.isSystem ? 'bg-accent/15 text-accent' : 'bg-primary/10 text-primary',
                      )}>
                        <ShieldCheck className="h-4 w-4" />
                      </div>
                      <div className="min-w-0 flex-1">
                        <div className="truncate text-[15px] font-medium text-ink">{r.name}</div>
                        {r.description && (
                          <div className="mt-0.5 truncate text-xs text-ink-muted">{r.description}</div>
                        )}
                      </div>
                      <div className="shrink-0">
                        <Cap>{r.isSystem ? 'system' : 'custom'}</Cap>
                      </div>
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
        {creating && (
          <RoleEditor
            mode="create"
            onCancel={() => setCreating(false)}
            onSaved={async (role) => {
              setCreating(false)
              await refreshRoles()
              setSelected(role)
            }}
          />
        )}
        {!creating && !selected && (
          <div className="flex h-full flex-col items-center justify-center rounded-lg border border-dashed border-rule bg-card/50 px-8 text-center shadow-sm">
            <Cap className="mb-2">no selection</Cap>
            <p className="text-lg text-ink-muted">Pick a role to view or edit.</p>
          </div>
        )}
        {!creating && selected && (
          <RoleEditor
            key={selected.id}
            mode="edit"
            role={selected}
            onCancel={() => setSelected(null)}
            onSaved={async (role) => {
              await refreshRoles()
              setSelected(role)
            }}
            onDeleted={async () => {
              setSelected(null)
              await refreshRoles()
            }}
          />
        )}
      </section>
    </div>
  )
}

interface RoleEditorProps {
  mode: 'create' | 'edit'
  role?: Role
  onCancel: () => void
  onSaved: (role: Role) => Promise<void> | void
  onDeleted?: () => Promise<void> | void
}

function RoleEditor({ mode, role, onCancel, onSaved, onDeleted }: RoleEditorProps) {
  const [name, setName] = useState(role?.name ?? '')
  const [description, setDescription] = useState(role?.description ?? '')
  const [saving, setSaving] = useState(false)
  const [error, setError] = useState<string | null>(null)
  const [assignedCount, setAssignedCount] = useState<number | null>(null)

  const isSystem = role?.isSystem ?? false
  const readOnly = mode === 'edit' && isSystem

  const loadAssignments = useCallback(async () => {
    if (!role) return
    try {
      // No "list assignments by role" endpoint exists, so cheap-count via probing
      // is the smallest viable check. Replace with a dedicated endpoint when we
      // surface usage in the table.
      const principals = await api<Array<{ id: string }>>('/api/principals')
      let count = 0
      for (const p of principals) {
        const pr = await api<PrincipalRole[]>(`/api/principals/${p.id}/roles`)
        if (pr.some((row) => row.roleId === role.id)) count += 1
      }
      setAssignedCount(count)
    } catch {
      setAssignedCount(null)
    }
  }, [role])

  useEffect(() => {
    if (mode === 'edit') void loadAssignments()
  }, [mode, loadAssignments])

  async function save() {
    setSaving(true)
    setError(null)
    try {
      if (mode === 'create') {
        const created = await api<Role>('/api/roles', {
          method: 'POST',
          json: { name: name.trim(), description: description.trim() || null, isSystem: false },
        })
        await onSaved(created)
      } else if (role) {
        const updated = await api<Role>(`/api/roles/${role.id}`, {
          method: 'PUT',
          json: { name: name.trim(), description: description.trim() || null },
        })
        await onSaved(updated)
      }
    } catch (err) {
      setError(err instanceof Error ? err.message : 'Failed')
    } finally {
      setSaving(false)
    }
  }

  async function remove() {
    if (!role) return
    if (!window.confirm(`Delete role "${role.name}"? This cannot be undone.`)) return
    setSaving(true)
    setError(null)
    try {
      await api(`/api/roles/${role.id}`, { method: 'DELETE' })
      await onDeleted?.()
    } catch (err) {
      setError(err instanceof Error ? err.message : 'Failed')
    } finally {
      setSaving(false)
    }
  }

  const dirty = mode === 'create'
    ? name.trim().length > 0
    : (role !== undefined && (name.trim() !== role.name || (description.trim() || null) !== (role.description ?? null)))

  return (
    <div className="flex h-full flex-col overflow-hidden rounded-lg border border-rule bg-card shadow-sm">
      <div className="border-b border-rule bg-label-bg px-6 py-5">
        <div className="flex items-center gap-3">
          <div className={cn(
            'flex h-10 w-10 items-center justify-center rounded',
            isSystem ? 'bg-accent/15 text-accent' : 'bg-primary/10 text-primary',
          )}>
            <ShieldCheck className="h-5 w-5" />
          </div>
          <div className="min-w-0 flex-1">
            <Cap>{mode === 'create' ? 'new role' : (isSystem ? 'system role · read-only' : 'custom role')}</Cap>
            <h2 className="mt-1 truncate text-xl font-semibold text-ink">
              {mode === 'create' ? (name || 'Untitled') : role?.name}
            </h2>
          </div>
        </div>
      </div>

      <div className="flex-1 space-y-6 overflow-auto px-6 py-5">
        <Section title="Definition">
          <div className="space-y-3">
            <label className="block">
              <span className="mb-1 block font-mono text-[10px] tracking-[0.14em] uppercase text-ink-muted">Name</span>
              <input
                value={name}
                onChange={(e) => setName(e.target.value)}
                disabled={readOnly}
                placeholder="e.g. ApproverFinance"
                className="block w-full rounded border border-rule bg-white px-3 py-2 text-sm text-ink outline-none transition-colors placeholder:text-ink-faint focus:border-primary focus:ring-2 focus:ring-primary/20 disabled:bg-bg disabled:text-ink-muted"
              />
            </label>
            <label className="block">
              <span className="mb-1 block font-mono text-[10px] tracking-[0.14em] uppercase text-ink-muted">Description</span>
              <textarea
                value={description ?? ''}
                onChange={(e) => setDescription(e.target.value)}
                disabled={readOnly}
                rows={2}
                placeholder="What this role grants — optional"
                className="block w-full rounded border border-rule bg-white px-3 py-2 text-sm text-ink outline-none transition-colors placeholder:text-ink-faint focus:border-primary focus:ring-2 focus:ring-primary/20 disabled:bg-bg disabled:text-ink-muted"
              />
            </label>
          </div>
        </Section>

        {mode === 'edit' && role && (
          <Section title="Usage" hint={assignedCount === null ? '…' : `${assignedCount} principal(s)`}>
            {assignedCount === 0 ? (
              <Empty>Not assigned to any principal yet.</Empty>
            ) : assignedCount === null ? (
              <Empty>Loading…</Empty>
            ) : (
              <p className="rounded border border-rule bg-bg px-3 py-2 text-xs text-ink">
                Assigned to {assignedCount} principal(s). Go to <strong>Principals</strong> tab to inspect each.
              </p>
            )}
          </Section>
        )}

        {error && (
          <p className="rounded border border-danger/30 bg-danger/5 px-3 py-2 text-xs text-danger">{error}</p>
        )}
      </div>

      <footer className="flex items-center justify-between border-t border-rule bg-label-bg px-6 py-3">
        <div>
          {mode === 'edit' && role && !isSystem && (
            <button
              onClick={() => void remove()}
              disabled={saving}
              className="inline-flex items-center gap-1 rounded border border-danger/30 bg-card px-2.5 py-1 text-xs font-medium text-danger transition-colors hover:bg-danger/10 disabled:opacity-50"
            >
              <Trash2 className="h-3 w-3" /> Delete
            </button>
          )}
        </div>
        <div className="flex items-center gap-2">
          <button
            onClick={onCancel}
            disabled={saving}
            className="rounded px-3 py-1 text-xs text-ink-muted hover:text-ink"
          >
            {mode === 'create' ? 'Cancel' : 'Close'}
          </button>
          {!readOnly && (
            <button
              onClick={() => void save()}
              disabled={saving || !name.trim() || (mode === 'edit' && !dirty)}
              className="rounded bg-primary px-3 py-1 text-xs font-medium text-white transition-colors hover:bg-primary/90 disabled:opacity-50"
            >
              {saving ? 'Saving…' : (mode === 'create' ? 'Create role' : 'Save changes')}
            </button>
          )}
        </div>
      </footer>
    </div>
  )
}
