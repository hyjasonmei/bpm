import { useCallback, useEffect, useState } from 'react'
import { Pencil, Plus, Trash2 } from 'lucide-react'
import { cn } from '@/lib/cn'
import {
  createEnvironment,
  deleteEnvironment,
  listEnvironments,
  updateEnvironment,
  type EnvironmentDto,
} from '@/flowcook/api/environments'

export function EnvironmentsTab() {
  const [rows, setRows] = useState<EnvironmentDto[]>([])
  const [loading, setLoading] = useState(true)
  const [error, setError] = useState<string | null>(null)
  const [editing, setEditing] = useState<EnvironmentDto | 'new' | null>(null)

  const refresh = useCallback(async () => {
    setLoading(true)
    setError(null)
    try {
      setRows(await listEnvironments())
    } catch (err) {
      setError(err instanceof Error ? err.message : 'Failed to load')
    } finally {
      setLoading(false)
    }
  }, [])

  useEffect(() => { void refresh() }, [refresh])

  async function handleDelete(e: EnvironmentDto) {
    if (!window.confirm(`刪除 environment "${e.displayName}"？已掛在 flow 上的 deployment 紀錄仍會留著。`)) return
    try {
      await deleteEnvironment(e.id)
      await refresh()
    } catch (err) {
      window.alert(err instanceof Error ? err.message : 'Delete failed')
    }
  }

  return (
    <div className="flex h-full min-h-0 flex-col gap-4">
      <div className="flex items-center justify-between">
        <div>
          <h2 className="text-base font-semibold text-ink">Environments</h2>
          <p className="text-xs text-ink-muted">
            部署環境定義；Serve tab 顯示這份清單，user 手動標 Deploy / Undeploy（不實際自動化）。
          </p>
        </div>
        <button
          onClick={() => setEditing('new')}
          className="inline-flex items-center gap-1.5 rounded bg-primary px-3 py-1.5 text-xs font-semibold text-white hover:bg-primary/90"
        >
          <Plus className="h-3.5 w-3.5" /> New environment
        </button>
      </div>

      {error && (
        <div className="rounded border border-danger/30 bg-danger/5 px-3 py-2 text-xs text-danger">{error}</div>
      )}

      <div className="flex-1 overflow-auto rounded-lg border border-rule bg-card shadow-sm">
        <table className="w-full text-sm">
          <thead className="border-b border-rule bg-label-bg text-left font-mono text-[10px] tracking-[0.14em] uppercase text-ink-muted">
            <tr>
              <th className="w-12 px-3 py-2 font-normal">#</th>
              <th className="px-3 py-2 font-normal">Code</th>
              <th className="px-3 py-2 font-normal">Name</th>
              <th className="px-3 py-2 font-normal">Sort</th>
              <th className="w-20 px-3 py-2" />
            </tr>
          </thead>
          <tbody className="divide-y divide-rule">
            {loading && rows.length === 0 && (
              <tr><td colSpan={5} className="px-4 py-10 text-center text-sm text-ink-muted">Loading…</td></tr>
            )}
            {!loading && rows.length === 0 && (
              <tr><td colSpan={5} className="px-4 py-10 text-center text-sm text-ink-muted">No environments yet — click <span className="font-semibold text-ink">New environment</span> to add one.</td></tr>
            )}
            {rows.map((e, idx) => (
              <tr key={e.id} className="hover:bg-bg">
                <td className="px-3 py-2 font-mono text-[11px] text-ink-faint">{idx + 1}</td>
                <td className="px-3 py-2 font-mono text-xs text-ink-muted">{e.code}</td>
                <td className="px-3 py-2 text-sm text-ink">{e.displayName}</td>
                <td className="px-3 py-2 font-mono text-xs text-ink-muted">{e.sortOrder}</td>
                <td className="px-3 py-2">
                  <div className="flex items-center justify-end gap-1">
                    <button
                      onClick={() => setEditing(e)}
                      title="Edit"
                      className="flex h-7 w-7 items-center justify-center rounded text-ink-muted hover:bg-bg hover:text-ink"
                    >
                      <Pencil className="h-3.5 w-3.5" />
                    </button>
                    <button
                      onClick={() => void handleDelete(e)}
                      title="Delete"
                      className="flex h-7 w-7 items-center justify-center rounded text-ink-faint hover:bg-danger/5 hover:text-danger"
                    >
                      <Trash2 className="h-3.5 w-3.5" />
                    </button>
                  </div>
                </td>
              </tr>
            ))}
          </tbody>
        </table>
      </div>

      {editing && (
        <EnvironmentEditModal
          initial={editing === 'new' ? null : editing}
          onClose={() => setEditing(null)}
          onSaved={() => { setEditing(null); void refresh() }}
        />
      )}
    </div>
  )
}

function EnvironmentEditModal({
  initial, onClose, onSaved,
}: {
  initial: EnvironmentDto | null
  onClose: () => void
  onSaved: () => void
}) {
  const isNew = initial === null
  const [code, setCode] = useState(initial?.code ?? '')
  const [displayName, setDisplayName] = useState(initial?.displayName ?? '')
  const [sortOrder, setSortOrder] = useState(initial?.sortOrder ?? 50)
  const [busy, setBusy] = useState(false)
  const [error, setError] = useState<string | null>(null)

  async function submit() {
    setBusy(true); setError(null)
    try {
      if (isNew) {
        await createEnvironment({ code: code.trim().toLowerCase(), displayName: displayName.trim(), sortOrder })
      } else {
        await updateEnvironment(initial!.id, { code: code.trim().toLowerCase(), displayName: displayName.trim(), sortOrder })
      }
      onSaved()
    } catch (err) {
      setError(err instanceof Error ? err.message : String(err))
    } finally {
      setBusy(false)
    }
  }

  const codeValid = /^[a-z0-9_-]+$/.test(code.trim())
  const canSubmit = !busy && codeValid && displayName.trim().length > 0

  return (
    <div className="fixed inset-0 z-50 flex items-center justify-center bg-black/40 p-4" onClick={onClose}>
      <div onClick={e => e.stopPropagation()} className="w-full max-w-md rounded-lg border border-rule bg-card p-5 shadow-xl">
        <h3 className="mb-3 text-sm font-semibold text-ink">{isNew ? 'New environment' : 'Edit environment'}</h3>
        <div className="space-y-3">
          <Field label="Code (slug)" hint="lowercase ascii / 數字 / - _，全站唯一">
            <input
              value={code}
              onChange={e => setCode(e.target.value)}
              placeholder="dev"
              className={cn(
                'w-full rounded border bg-white px-2 py-1.5 font-mono text-xs text-ink outline-none focus:border-primary',
                code && !codeValid ? 'border-danger' : 'border-rule',
              )}
            />
          </Field>
          <Field label="Display name" required>
            <input
              value={displayName}
              onChange={e => setDisplayName(e.target.value)}
              placeholder="DEV"
              className="w-full rounded border border-rule bg-white px-2 py-1.5 text-sm text-ink outline-none focus:border-primary"
            />
          </Field>
          <Field label="Sort order" hint="低 → 高">
            <input
              type="number"
              value={sortOrder}
              onChange={e => setSortOrder(Number(e.target.value))}
              className="w-32 rounded border border-rule bg-white px-2 py-1.5 text-sm text-ink outline-none focus:border-primary"
            />
          </Field>
        </div>
        {error && <p className="mt-3 text-xs text-danger">{error}</p>}
        <div className="mt-4 flex justify-end gap-2">
          <button onClick={onClose} className="rounded border border-rule bg-card px-3 py-1.5 text-xs font-medium text-ink-muted hover:bg-bg">Cancel</button>
          <button
            disabled={!canSubmit}
            onClick={() => void submit()}
            className="rounded bg-primary px-3 py-1.5 text-xs font-semibold text-white hover:bg-primary/90 disabled:opacity-50"
          >
            {busy ? 'Saving…' : 'Save'}
          </button>
        </div>
      </div>
    </div>
  )
}

function Field({ label, hint, required, children }: { label: string; hint?: string; required?: boolean; children: React.ReactNode }) {
  return (
    <div>
      <label className="mb-1 flex items-baseline gap-1.5 text-[11px] font-medium text-ink-muted">
        {label}
        {required && <span className="text-danger">*</span>}
        {hint && <span className="text-[10px] text-ink-faint">— {hint}</span>}
      </label>
      {children}
    </div>
  )
}
