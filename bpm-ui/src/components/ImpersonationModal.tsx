import { useEffect, useMemo, useState } from 'react'
import { X, Eye, Loader2, Search } from 'lucide-react'
import { Button } from '@/components/ui/button'
import { Input, Textarea, Field } from '@/components/ui/form'
import { Modal } from '@/components/ui/Modal'
import { startImpersonation } from '@/lib/api/impersonation'
import { listAdminUsers, type AdminUserSummary } from '@/lib/api/admin-users'
import { enterImpersonation } from '@/lib/impersonationToken'
import { cn } from '@/lib/cn'

interface Props {
  open: boolean
  onClose: () => void
}

export function ImpersonationModal({ open, onClose }: Props) {
  const [query, setQuery] = useState('')
  const [target, setTarget] = useState<AdminUserSummary | null>(null)
  const [reason, setReason] = useState('')
  const [users, setUsers] = useState<AdminUserSummary[]>([])
  const [loadingUsers, setLoadingUsers] = useState(false)
  const [usersError, setUsersError] = useState<string | null>(null)
  const [busy, setBusy] = useState(false)
  const [err, setErr] = useState<string | null>(null)

  useEffect(() => {
    if (!open) { setQuery(''); setTarget(null); setReason(''); setErr(null); setUsers([]); setUsersError(null) }
  }, [open])

  // Server-side typeahead (the directory may be thousands of users) — debounce
  // and ask /api/admin/users for matches instead of pulling the whole list.
  useEffect(() => {
    if (!open) return
    const q = query.trim()
    if (q === '') { setUsers([]); setLoadingUsers(false); return }
    let cancelled = false
    setLoadingUsers(true); setUsersError(null)
    const t = setTimeout(() => {
      listAdminUsers({ q, pageSize: 20 })
        .then(res => { if (!cancelled) setUsers(res.items.filter(u => u.isActive)) })
        .catch(e => { if (!cancelled) setUsersError(e instanceof Error ? e.message : String(e)) })
        .finally(() => { if (!cancelled) setLoadingUsers(false) })
    }, 250)
    return () => { cancelled = true; clearTimeout(t) }
  }, [open, query])

  const filtered = useMemo(() => users.slice(0, 12), [users])

  async function submit() {
    if (!target) { setErr('Pick a target user first.'); return }
    if (!reason.trim()) { setErr('Reason is required.'); return }
    setBusy(true)
    setErr(null)
    try {
      const result = await startImpersonation(target.id, reason.trim())
      enterImpersonation(result.token)
      window.location.reload()
    } catch (e) {
      setErr((e as Error).message)
      setBusy(false)
    }
  }

  const titleId = 'impersonation-modal-title'

  return (
    <Modal open={open} onClose={onClose} ariaLabelledBy={titleId} dismissOnBackdrop={!busy}>
      <div className="flex items-center justify-between border-b border-rule px-4 py-3">
        <div className="flex items-center gap-2">
          <Eye className="h-4 w-4 text-amber-600" />
          <h2 id={titleId} className="text-sm font-semibold text-ink">Act as another user</h2>
        </div>
        <button onClick={onClose} className="text-ink-muted hover:text-ink"><X className="h-4 w-4" /></button>
      </div>
      <div className="space-y-4 p-4">
          <Field label="Target user" hint="Search by name or email; pick from active org users.">
            <div className="relative">
              <Search className="pointer-events-none absolute left-2 top-2 h-4 w-4 text-ink-faint" />
              <Input
                value={query}
                onChange={e => { setQuery(e.target.value); setTarget(null) }}
                placeholder={loadingUsers ? 'Loading users…' : 'Search name or email'}
                className="pl-8"
                disabled={loadingUsers}
              />
            </div>
            {usersError && <p className="mt-1 text-[11px] text-danger">User list unavailable: {usersError}</p>}
            {!loadingUsers && !usersError && (
              <div className="mt-1 max-h-44 overflow-y-auto rounded border border-rule bg-white">
                {filtered.length === 0 ? (
                  <div className="px-3 py-2 text-xs text-ink-faint">No matches.</div>
                ) : (
                  filtered.map(u => (
                    <button
                      key={u.id}
                      type="button"
                      onClick={() => { setTarget(u); setQuery(`${u.fullName} <${u.email}>`) }}
                      className={cn(
                        'flex w-full items-start gap-2 px-3 py-1.5 text-left text-xs hover:bg-slate-50',
                        target?.id === u.id && 'bg-amber-50',
                      )}
                    >
                      <span className="flex-1">
                        <span className="block font-medium text-ink truncate">{u.fullName}</span>
                        <span className="block text-[10.5px] text-ink-muted truncate">{u.email}{u.departmentCode ? ` · ${u.departmentCode}` : ''}</span>
                      </span>
                    </button>
                  ))
                )}
              </div>
            )}
          </Field>
          <Field label="Reason" required hint="Logged in the audit trail.">
            <Textarea rows={3} value={reason} onChange={e => setReason(e.target.value)} placeholder="e.g. UAT — verify Wilson's leave flow" />
        </Field>
        {err && <p className="text-xs text-danger">{err}</p>}
        <div className="flex justify-end gap-2 border-t border-rule pt-3">
          <Button variant="outline" size="sm" onClick={onClose} disabled={busy}>Cancel</Button>
          <Button variant="primary" size="sm" onClick={submit} disabled={busy || !target}>
            {busy && <Loader2 className="h-3.5 w-3.5 animate-spin" />}
            Start session (30 min)
          </Button>
        </div>
      </div>
    </Modal>
  )
}
