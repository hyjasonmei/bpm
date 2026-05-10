import { useEffect, useMemo, useState } from 'react'
import { Users, X, Plus, Search, Trash2 } from 'lucide-react'
import { cn } from '@/lib/cn'
import { Button } from '@/components/ui/button'
import { SectionCard, SectionTitle } from '@/components/ui/card'
import { Input, Field } from '@/components/ui/form'
import { ConfirmDialog } from '@/components/ui/confirm-dialog'
import {
  listRoles, listUsers, getUserDetail, assignRole, revokeAssignment,
} from '@/lib/api/adminRoles'
import type {
  RoleSummaryDto, UserDetailDto, UserSummaryDto,
} from '@/types/adminRoles'

export function UsersRoles() {
  const [roles, setRoles] = useState<RoleSummaryDto[]>([])
  const [users, setUsers] = useState<UserSummaryDto[]>([])
  const [q, setQ] = useState('')
  const [roleFilter, setRoleFilter] = useState<string | null>(null)
  const [selectedId, setSelectedId] = useState<string | null>(null)
  const [detail, setDetail] = useState<UserDetailDto | null>(null)
  const [busy, setBusy] = useState(false)
  const [toast, setToast] = useState<string | null>(null)
  const [confirm, setConfirm] = useState<null | { title: string; description: string; tone: 'danger' | 'default'; onConfirm: () => void }>(null)
  const [addOpen, setAddOpen] = useState(false)

  const fireToast = (m: string) => { setToast(m); setTimeout(() => setToast(null), 2800) }

  async function refreshList() {
    try {
      const [r, u] = await Promise.all([
        listRoles(),
        listUsers({ q: q || undefined, roleCode: roleFilter ?? undefined, pageSize: 100 }),
      ])
      setRoles(r)
      setUsers(u.items)
    } catch (e) {
      fireToast(`Load failed: ${(e as Error).message}`)
    }
  }

  useEffect(() => { void refreshList() }, [q, roleFilter])

  useEffect(() => {
    if (!selectedId) { setDetail(null); return }
    getUserDetail(selectedId).then(setDetail).catch(e => fireToast(`Load detail failed: ${(e as Error).message}`))
  }, [selectedId])

  async function refreshDetail() {
    if (!selectedId) return
    try {
      const d = await getUserDetail(selectedId)
      setDetail(d)
    } catch { /* swallow */ }
    void refreshList()
  }

  async function onRevoke(assignmentId: string, roleCode: string) {
    if (!detail) return
    setBusy(true)
    try {
      await revokeAssignment(detail.profile.id, assignmentId)
      await refreshDetail()
      fireToast(`Removed ${roleCode}`)
    } catch (e) {
      fireToast(`Revoke failed: ${(e as Error).message}`)
    } finally { setBusy(false) }
  }

  const selectedDetail = detail
  const sortedRoles = useMemo(() => roles.slice().sort((a, b) => a.code.localeCompare(b.code)), [roles])

  return (
    <div className="space-y-4">
      {toast && <div className="fixed bottom-6 right-6 z-50 rounded-lg bg-slate-800 px-4 py-2.5 text-sm text-white shadow-2xl">{toast}</div>}

      <div className="flex items-center gap-2">
        <Users className="h-5 w-5 text-ink-muted" />
        <h1 className="text-xl font-bold text-ink">Users &amp; Roles</h1>
      </div>

      <div className="grid grid-cols-12 gap-4">
        {/* Left: list */}
        <div className="col-span-5 space-y-3">
          <SectionCard>
            <div className="space-y-3 p-3">
              <Field label="Search">
                <div className="relative">
                  <Search className="pointer-events-none absolute left-2 top-2.5 h-3.5 w-3.5 text-ink-faint" />
                  <Input value={q} onChange={e => setQ(e.target.value)} placeholder="Name or email…" className="pl-7" />
                </div>
              </Field>
              <div className="flex flex-wrap gap-1">
                <button
                  onClick={() => setRoleFilter(null)}
                  className={cn('rounded-full border px-2 py-0.5 text-[11px]',
                    roleFilter === null ? 'border-primary bg-primary text-white' : 'border-rule text-ink-muted hover:bg-slate-50')}
                >All</button>
                {sortedRoles.map(r => (
                  <button
                    key={r.code}
                    onClick={() => setRoleFilter(roleFilter === r.code ? null : r.code)}
                    className={cn('rounded-full border px-2 py-0.5 text-[11px]',
                      roleFilter === r.code ? 'border-primary bg-primary text-white' : 'border-rule text-ink-muted hover:bg-slate-50')}
                  >{r.code} ({r.assignedCount})</button>
                ))}
              </div>
            </div>
          </SectionCard>

          <SectionCard>
            <div className="divide-y divide-rule">
              {users.length === 0 && <div className="px-4 py-6 text-sm text-ink-muted">No users match.</div>}
              {users.map(u => (
                <button
                  key={u.id}
                  onClick={() => setSelectedId(u.id)}
                  className={cn('flex w-full items-center justify-between px-4 py-2.5 text-left text-sm transition-colors',
                    selectedId === u.id ? 'bg-amber-50' : 'hover:bg-slate-50',
                    !u.isActive && 'opacity-50')}
                >
                  <div>
                    <div className="font-medium text-ink">{u.fullName}</div>
                    <div className="text-xs text-ink-muted">{u.email} · {u.departmentCode ?? '—'}</div>
                  </div>
                  <span className="rounded-full bg-slate-100 px-2 py-0.5 text-[10px] font-mono">{u.roleCount} role{u.roleCount === 1 ? '' : 's'}</span>
                </button>
              ))}
            </div>
          </SectionCard>
        </div>

        {/* Right: detail */}
        <div className="col-span-7">
          {!selectedDetail ? (
            <SectionCard>
              <div className="px-5 py-12 text-center text-sm text-ink-muted">Select a user to view their roles.</div>
            </SectionCard>
          ) : (
            <SectionCard>
              <SectionTitle>{selectedDetail.profile.fullName}</SectionTitle>
              <div className="space-y-4 p-5">
                <div className="grid grid-cols-2 gap-3 text-sm">
                  <InfoLine label="Email" value={selectedDetail.profile.email} />
                  <InfoLine label="Department" value={selectedDetail.profile.departmentCode ?? '—'} />
                  <InfoLine label="Active" value={selectedDetail.profile.isActive ? 'Yes' : 'No'} />
                  <InfoLine label="User ID" value={<span className="font-mono text-xs">{selectedDetail.profile.id}</span>} />
                </div>

                <div className="border-t border-rule pt-4">
                  <div className="mb-2 flex items-center justify-between">
                    <h3 className="text-sm font-semibold text-ink">Assigned Roles</h3>
                    <Button variant="primary" size="sm" onClick={() => setAddOpen(true)} disabled={busy}>
                      <Plus className="h-3.5 w-3.5" />
                      Add Role
                    </Button>
                  </div>
                  {selectedDetail.assignments.length === 0 ? (
                    <p className="text-sm text-ink-muted">No roles assigned.</p>
                  ) : (
                    <table className="w-full text-sm">
                      <thead className="border-b border-rule bg-slate-50 text-[11px] uppercase tracking-wider text-ink-muted">
                        <tr>
                          <th className="px-3 py-1.5 text-left">Code</th>
                          <th className="px-3 py-1.5 text-left">Name</th>
                          <th className="px-3 py-1.5 text-left">Scope</th>
                          <th className="px-3 py-1.5 text-left">Since</th>
                          <th className="px-3 py-1.5"></th>
                        </tr>
                      </thead>
                      <tbody>
                        {selectedDetail.assignments.map(a => (
                          <tr key={a.id} className="border-b border-rule last:border-b-0">
                            <td className="px-3 py-2 font-mono">{a.roleCode}</td>
                            <td className="px-3 py-2">{a.roleName}</td>
                            <td className="px-3 py-2 text-xs">{a.scope === 1 ? 'Tenant' : 'Flow'}{a.scopeRef ? ` (${a.scopeRef})` : ''}</td>
                            <td className="px-3 py-2 text-xs">{new Date(a.assignedAt).toLocaleDateString()}</td>
                            <td className="px-3 py-2 text-right">
                              <button
                                onClick={() => setConfirm({
                                  title: a.roleCode === 'admin' ? `Remove ADMIN role from ${selectedDetail.profile.fullName}?` : `Remove ${a.roleCode} from ${selectedDetail.profile.fullName}?`,
                                  description: a.roleCode === 'admin' ? 'This will revoke admin privileges. Make sure another admin exists.' : 'This is reversible — you can re-assign anytime.',
                                  tone: 'danger',
                                  onConfirm: () => onRevoke(a.id, a.roleCode),
                                })}
                                className="rounded p-1 text-danger hover:bg-rose-50"
                                title="Revoke"
                              ><Trash2 className="h-3.5 w-3.5" /></button>
                            </td>
                          </tr>
                        ))}
                      </tbody>
                    </table>
                  )}
                </div>
              </div>
            </SectionCard>
          )}
        </div>
      </div>

      <AddRoleModal
        open={addOpen}
        roles={roles}
        currentAssignments={selectedDetail?.assignments ?? []}
        onClose={() => setAddOpen(false)}
        onConfirm={async (roleCode) => {
          if (!selectedDetail) return
          setBusy(true)
          try {
            await assignRole(selectedDetail.profile.id, roleCode)
            await refreshDetail()
            fireToast(`Assigned ${roleCode}`)
            setAddOpen(false)
          } catch (e) {
            fireToast(`Assign failed: ${(e as Error).message}`)
          } finally { setBusy(false) }
        }}
      />

      <ConfirmDialog
        open={!!confirm}
        title={confirm?.title ?? ''}
        description={confirm?.description}
        tone={confirm?.tone}
        confirmText="Confirm"
        onCancel={() => setConfirm(null)}
        onConfirm={() => { confirm?.onConfirm(); setConfirm(null) }}
      />
    </div>
  )
}

function InfoLine({ label, value }: { label: string; value: React.ReactNode }) {
  return (
    <div>
      <div className="text-[11px] font-semibold uppercase tracking-wider text-ink-faint">{label}</div>
      <div className="text-ink">{value}</div>
    </div>
  )
}

function AddRoleModal({ open, roles, currentAssignments, onClose, onConfirm }: {
  open: boolean
  roles: RoleSummaryDto[]
  currentAssignments: { roleCode: string }[]
  onClose: () => void
  onConfirm: (roleCode: string) => Promise<void> | void
}) {
  const [selected, setSelected] = useState('')
  if (!open) return null
  const haveCodes = new Set(currentAssignments.map(a => a.roleCode))
  const available = roles.filter(r => !haveCodes.has(r.code))
  return (
    <div className="fixed inset-0 z-50 flex items-center justify-center bg-black/40 p-4">
      <div className="w-full max-w-md rounded-lg border border-rule bg-card shadow-2xl">
        <div className="flex items-center justify-between border-b border-rule px-4 py-3">
          <h2 className="text-sm font-semibold text-ink">Add role</h2>
          <button onClick={onClose} className="text-ink-muted hover:text-ink"><X className="h-4 w-4" /></button>
        </div>
        <div className="space-y-3 p-4">
          {available.length === 0 ? (
            <p className="text-sm text-ink-muted">No more roles to assign — user already has all available roles.</p>
          ) : (
            <>
              <Field label="Role">
                <select className="h-8 w-full rounded-md border border-rule px-3 text-sm" value={selected} onChange={e => setSelected(e.target.value)}>
                  <option value="">— Select role —</option>
                  {available.map(r => <option key={r.code} value={r.code}>{r.code} — {r.name} ({r.scope === 1 ? 'System' : 'Tenant'})</option>)}
                </select>
              </Field>
            </>
          )}
          <div className="flex justify-end gap-2 border-t border-rule pt-3">
            <Button variant="outline" size="sm" onClick={onClose}>Cancel</Button>
            <Button variant="primary" size="sm" disabled={!selected} onClick={() => onConfirm(selected)}>
              {!selected ? 'Pick a role' : 'Assign'}
            </Button>
          </div>
        </div>
      </div>
    </div>
  )
}
