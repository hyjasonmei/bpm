import { useCallback, useEffect, useState } from 'react'
import {
  Briefcase,
  Coffee,
  FileText,
  Folder,
  HeartPulse,
  Pencil,
  Plane,
  Plus,
  Settings,
  ShoppingCart,
  Sparkles,
  Trash2,
  Users,
  Wallet,
  Wrench,
  type LucideIcon,
} from 'lucide-react'
import { cn } from '@/lib/cn'
import {
  createFlowGroup,
  deleteFlowGroup,
  listFlowGroups,
  updateFlowGroup,
  type FlowGroupDto,
} from '@/flowcook/api/flowGroups'

/**
 * Curated icon catalog. Storing icon names as strings keeps the schema
 * flat — bpm-ui maps name → component on render. The picker is restricted
 * to this set so admin doesn't drift into icons that don't exist in
 * lucide-react. Extending the set is a one-line bump here.
 */
export const ICON_CATALOG: Record<string, LucideIcon> = {
  Users, ShoppingCart, Wrench, Plane, FileText, Briefcase,
  HeartPulse, Coffee, Wallet, Settings, Folder, Sparkles,
}
export const ICON_NAMES = Object.keys(ICON_CATALOG)
export const DEFAULT_ICON: LucideIcon = Folder

/** Resolve an icon name → component, with Folder as the fallback. */
export function resolveIcon(name: string | null | undefined): LucideIcon {
  if (!name) return DEFAULT_ICON
  return ICON_CATALOG[name] ?? DEFAULT_ICON
}

export function FlowGroupsTab() {
  const [groups, setGroups] = useState<FlowGroupDto[]>([])
  const [loading, setLoading] = useState(true)
  const [error, setError] = useState<string | null>(null)
  const [editing, setEditing] = useState<FlowGroupDto | 'new' | null>(null)

  const refresh = useCallback(async () => {
    setLoading(true)
    setError(null)
    try {
      setGroups(await listFlowGroups())
    } catch (err) {
      setError(err instanceof Error ? err.message : 'Failed to load')
    } finally {
      setLoading(false)
    }
  }, [])

  useEffect(() => { void refresh() }, [refresh])

  async function handleDelete(g: FlowGroupDto) {
    if (g.flowCount > 0) {
      if (!window.confirm(`刪除「${g.displayName['zh-TW'] ?? g.code}」？此群組目前有 ${g.flowCount} 個流程，它們會落到「其他」。`)) return
    } else if (!window.confirm(`刪除「${g.displayName['zh-TW'] ?? g.code}」？`)) {
      return
    }
    try {
      await deleteFlowGroup(g.id)
      await refresh()
    } catch (err) {
      window.alert(err instanceof Error ? err.message : 'Delete failed')
    }
  }

  return (
    <div className="flex h-full min-h-0 flex-col gap-4">
      <div className="flex items-center justify-between">
        <div>
          <h2 className="text-base font-semibold text-ink">Flow Groups</h2>
          <p className="text-xs text-ink-muted">
            bpm 首頁 Quick Actions 會按這個順序分區顯示；未指派的流程落到「其他」。
          </p>
        </div>
        <button
          onClick={() => setEditing('new')}
          className="inline-flex items-center gap-1.5 rounded bg-primary px-3 py-1.5 text-xs font-semibold text-white hover:bg-primary/90"
        >
          <Plus className="h-3.5 w-3.5" /> New group
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
              <th className="px-3 py-2 font-normal">zh-TW</th>
              <th className="px-3 py-2 font-normal">en</th>
              <th className="px-3 py-2 font-normal">Icon</th>
              <th className="px-3 py-2 font-normal">Sort</th>
              <th className="px-3 py-2 font-normal">Flows</th>
              <th className="w-20 px-3 py-2" />
            </tr>
          </thead>
          <tbody className="divide-y divide-rule">
            {loading && groups.length === 0 && (
              <tr><td colSpan={8} className="px-4 py-10 text-center text-sm text-ink-muted">Loading…</td></tr>
            )}
            {!loading && groups.length === 0 && (
              <tr><td colSpan={8} className="px-4 py-10 text-center text-sm text-ink-muted">No groups yet — click <span className="font-semibold text-ink">New group</span> to add one.</td></tr>
            )}
            {groups.map((g, idx) => {
              const Icon = resolveIcon(g.icon)
              return (
                <tr key={g.id} className="hover:bg-bg">
                  <td className="px-3 py-2 font-mono text-[11px] text-ink-faint">{idx + 1}</td>
                  <td className="px-3 py-2 font-mono text-xs text-ink-muted">{g.code}</td>
                  <td className="px-3 py-2 text-sm text-ink">{g.displayName['zh-TW'] ?? '—'}</td>
                  <td className="px-3 py-2 text-sm text-ink-muted">{g.displayName.en ?? '—'}</td>
                  <td className="px-3 py-2">
                    <div className="inline-flex h-7 w-7 items-center justify-center rounded border border-rule bg-card text-ink-muted">
                      <Icon className="h-3.5 w-3.5" />
                    </div>
                  </td>
                  <td className="px-3 py-2 font-mono text-xs text-ink-muted">{g.sortOrder}</td>
                  <td className="px-3 py-2 font-mono text-xs text-ink">{g.flowCount}</td>
                  <td className="px-3 py-2">
                    <div className="flex items-center justify-end gap-1">
                      <button
                        onClick={() => setEditing(g)}
                        title="Edit"
                        className="flex h-7 w-7 items-center justify-center rounded text-ink-muted hover:bg-bg hover:text-ink"
                      >
                        <Pencil className="h-3.5 w-3.5" />
                      </button>
                      <button
                        onClick={() => void handleDelete(g)}
                        title="Delete"
                        className="flex h-7 w-7 items-center justify-center rounded text-ink-faint hover:bg-danger/5 hover:text-danger"
                      >
                        <Trash2 className="h-3.5 w-3.5" />
                      </button>
                    </div>
                  </td>
                </tr>
              )
            })}
          </tbody>
        </table>
      </div>

      {editing && (
        <GroupEditModal
          initial={editing === 'new' ? null : editing}
          onClose={() => setEditing(null)}
          onSaved={() => { setEditing(null); void refresh() }}
        />
      )}
    </div>
  )
}

function GroupEditModal({
  initial,
  onClose,
  onSaved,
}: {
  initial: FlowGroupDto | null
  onClose: () => void
  onSaved: () => void
}) {
  const isNew = initial === null
  const [code, setCode] = useState(initial?.code ?? '')
  const [zhTw, setZhTw] = useState(initial?.displayName['zh-TW'] ?? '')
  const [en, setEn] = useState(initial?.displayName.en ?? '')
  const [sortOrder, setSortOrder] = useState(initial?.sortOrder ?? 50)
  const [icon, setIcon] = useState(initial?.icon ?? 'Folder')
  const [busy, setBusy] = useState(false)
  const [error, setError] = useState<string | null>(null)

  async function submit() {
    setBusy(true); setError(null)
    try {
      const displayName: Record<string, string> = { 'zh-TW': zhTw.trim() }
      if (en.trim()) displayName.en = en.trim()
      if (isNew) {
        await createFlowGroup({ code: code.trim().toLowerCase(), displayName, sortOrder, icon })
      } else {
        await updateFlowGroup(initial!.id, { code: code.trim().toLowerCase(), displayName, sortOrder, icon })
      }
      onSaved()
    } catch (err) {
      setError(err instanceof Error ? err.message : String(err))
    } finally {
      setBusy(false)
    }
  }

  const codeValid = /^[a-z0-9_-]+$/.test(code.trim())
  const zhValid = zhTw.trim().length > 0
  const canSubmit = !busy && codeValid && zhValid

  return (
    <div className="fixed inset-0 z-50 flex items-center justify-center bg-black/40 p-4" onClick={onClose}>
      <div
        onClick={e => e.stopPropagation()}
        className="w-full max-w-md rounded-lg border border-rule bg-card p-5 shadow-xl"
      >
        <h3 className="mb-3 text-sm font-semibold text-ink">{isNew ? 'New group' : 'Edit group'}</h3>
        <div className="space-y-3">
          <Field label="Code (slug)" hint="lowercase ascii / 數字 / - _，全站唯一">
            <input
              value={code}
              onChange={e => setCode(e.target.value)}
              placeholder="hr"
              className={cn(
                'w-full rounded border bg-white px-2 py-1.5 font-mono text-xs text-ink outline-none focus:border-primary',
                code && !codeValid ? 'border-danger' : 'border-rule',
              )}
            />
          </Field>
          <Field label="顯示名稱 (zh-TW)" required>
            <input
              value={zhTw}
              onChange={e => setZhTw(e.target.value)}
              placeholder="人事"
              className="w-full rounded border border-rule bg-white px-2 py-1.5 text-sm text-ink outline-none focus:border-primary"
            />
          </Field>
          <Field label="顯示名稱 (en)" hint="optional">
            <input
              value={en}
              onChange={e => setEn(e.target.value)}
              placeholder="HR"
              className="w-full rounded border border-rule bg-white px-2 py-1.5 text-sm text-ink outline-none focus:border-primary"
            />
          </Field>
          <Field label="Icon">
            <div className="flex flex-wrap gap-1.5">
              {ICON_NAMES.map(name => {
                const I = ICON_CATALOG[name]
                const active = icon === name
                return (
                  <button
                    key={name}
                    type="button"
                    onClick={() => setIcon(name)}
                    title={name}
                    className={cn(
                      'flex h-8 w-8 items-center justify-center rounded border transition-colors',
                      active
                        ? 'border-primary bg-primary/10 text-primary'
                        : 'border-rule bg-card text-ink-muted hover:border-primary hover:text-primary',
                    )}
                  >
                    <I className="h-3.5 w-3.5" />
                  </button>
                )
              })}
            </div>
          </Field>
          <Field label="Sort order" hint="低 → 高，由小到大顯示">
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
          <button
            onClick={onClose}
            className="rounded border border-rule bg-card px-3 py-1.5 text-xs font-medium text-ink-muted hover:bg-bg"
          >
            Cancel
          </button>
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
