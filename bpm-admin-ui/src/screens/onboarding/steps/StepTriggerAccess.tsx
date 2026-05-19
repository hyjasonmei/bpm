import { useEffect, useMemo, useRef, useState } from 'react'
import { Building2, ChevronDown, Plus, Search, Trash2, User, UsersRound, X } from 'lucide-react'
import { cn } from '@/lib/cn'
import { api } from '@/flowcook/api'
import type { DraftSpec, FlowTrigger } from '@/lib/onboarding'

interface Props {
  draft: DraftSpec
  setDraft: (d: DraftSpec) => void
}

interface PrincipalRef {
  id: string
  type: 0 | 1 | 2 // user / dept / group
  displayName: string
  email: string | null
}

const PTYPE_ICON: Record<0 | 1 | 2, React.ComponentType<{ className?: string }>> = {
  0: User,
  1: Building2,
  2: UsersRound,
}

/**
 * Step 2 — TRIGGER & ACCESS.
 *
 * v0/v1 accepts exactly one form trigger; the schema is still
 * `triggers[]` so future versions can add cron / webhook / mail.
 *
 * Principal picker reads admin-svc `/api/principals` so the selected
 * ids resolve to the same Principal model the rest of the platform
 * uses. Free-text fallback still works when the API is unreachable
 * (the wizard is robust offline-ish for early drafts).
 */
export function StepTriggerAccess({ draft, setDraft }: Props) {
  const trigger = draft.triggers[0]
  const userTaskCodes = Array.from(new Set(draft.userTasks.map(t => t.formCode).filter(Boolean)))

  const [principals, setPrincipals] = useState<PrincipalRef[]>([])
  const [principalErr, setPrincipalErr] = useState<string | null>(null)

  useEffect(() => {
    let cancelled = false
    void api<PrincipalRef[]>('/api/principals')
      .then(rows => { if (!cancelled) setPrincipals(rows) })
      .catch(err => { if (!cancelled) setPrincipalErr(err instanceof Error ? err.message : 'Failed to load principals') })
    return () => { cancelled = true }
  }, [])

  function updateTrigger(next: FlowTrigger | null) {
    setDraft({ ...draft, triggers: next ? [next] : [] })
  }

  function setLaunchableBy(list: string[]) {
    setDraft({ ...draft, access: { ...draft.access, launchableBy: list } })
  }
  function setVisibleTo(list: string[]) {
    setDraft({ ...draft, access: { ...draft.access, visibleTo: list } })
  }
  function setWatcher(list: string[]) {
    setDraft({ ...draft, access: { ...draft.access, watcher: list } })
  }

  return (
    <div className="space-y-6">
      <SectionHeading>觸發 · Trigger</SectionHeading>
      <p className="text-xs text-ink-muted">
        指定一個觸發本流程的表單。v0 只支援 form trigger；未來會加 cron / webhook / mail。
      </p>

      <div className="rounded-md border border-rule bg-card p-4">
        <Field label="Trigger 名稱（ID）">
          <input
            value={trigger?.id ?? ''}
            onChange={(e) => updateTrigger({
              id: e.target.value,
              type: 'form',
              formCode: trigger?.formCode ?? '',
            })}
            placeholder="e.g. leave-form"
            className={inputCls}
          />
        </Field>
        <Field label="Form Code（綁定 user task）">
          {userTaskCodes.length === 0 ? (
            <input
              value={trigger?.formCode ?? ''}
              onChange={(e) => updateTrigger({
                id: trigger?.id ?? 'main',
                type: 'form',
                formCode: e.target.value.toUpperCase(),
              })}
              placeholder="LEAVE"
              className={inputCls}
            />
          ) : (
            <select
              value={trigger?.formCode ?? ''}
              onChange={(e) => updateTrigger({
                id: trigger?.id ?? 'main',
                type: 'form',
                formCode: e.target.value,
              })}
              className={inputCls}
            >
              <option value="">— select —</option>
              {userTaskCodes.map(c => <option key={c} value={c}>{c}</option>)}
            </select>
          )}
        </Field>
        {trigger && (
          <button
            onClick={() => updateTrigger(null)}
            className="mt-2 inline-flex items-center gap-1 rounded border border-danger/30 bg-card px-2.5 py-1 text-xs font-medium text-danger transition-colors hover:bg-danger/10"
          >
            <Trash2 className="h-3 w-3" /> 移除 trigger
          </button>
        )}
      </div>

      <SectionHeading>存取權限 · Access</SectionHeading>
      {principalErr && (
        <p className="rounded border border-warn/30 bg-warn/5 px-3 py-2 text-xs text-warn">
          無法載入 principal 清單，將 fallback 為手動輸入：{principalErr}
        </p>
      )}
      <PrincipalMultiSelect
        label="launchable_by — 誰可以啟動"
        items={draft.access.launchableBy}
        onChange={setLaunchableBy}
        principals={principals}
      />
      <PrincipalMultiSelect
        label="visible_to — 誰可以在目錄看到"
        items={draft.access.visibleTo}
        onChange={setVisibleTo}
        principals={principals}
      />
      <PrincipalMultiSelect
        label="watcher — 旁觀者（可看別人的 instance）"
        items={draft.access.watcher}
        onChange={setWatcher}
        principals={principals}
      />
    </div>
  )
}

function PrincipalMultiSelect({
  label, items, onChange, principals,
}: { label: string; items: string[]; onChange: (l: string[]) => void; principals: PrincipalRef[] }) {
  const [open, setOpen] = useState(false)
  const [q, setQ] = useState('')
  const containerRef = useRef<HTMLDivElement>(null)

  useEffect(() => {
    if (!open) return
    function onDoc(e: MouseEvent) {
      if (containerRef.current && !containerRef.current.contains(e.target as Node)) {
        setOpen(false); setQ('')
      }
    }
    document.addEventListener('mousedown', onDoc)
    return () => document.removeEventListener('mousedown', onDoc)
  }, [open])

  const byId = useMemo(() => {
    const m: Record<string, PrincipalRef> = {}
    for (const p of principals) m[p.id] = p
    return m
  }, [principals])

  const filteredOptions = useMemo(() => {
    const selected = new Set(items)
    const needle = q.trim().toLowerCase()
    return principals.filter(p => {
      if (selected.has(p.id)) return false
      if (!needle) return true
      return p.displayName.toLowerCase().includes(needle) || (p.email ?? '').toLowerCase().includes(needle)
    }).slice(0, 20)
  }, [principals, items, q])

  function pick(id: string) {
    onChange([...items, id])
    setOpen(false)
    setQ('')
  }

  function remove(id: string) {
    onChange(items.filter(x => x !== id))
  }

  return (
    <div className="rounded-md border border-rule bg-card p-4">
      <Field label={label}>
        <div className="flex flex-wrap items-center gap-1.5">
          {items.length === 0 && (
            <span className="text-xs text-ink-faint">尚未指定</span>
          )}
          {items.map((id) => {
            const p = byId[id]
            const Icon = p ? PTYPE_ICON[p.type] : User
            return (
              <span
                key={id}
                className="inline-flex items-center gap-1 rounded-full border border-rule bg-bg px-2 py-0.5 text-xs"
              >
                <Icon className="h-3 w-3 text-ink-faint" />
                <span className="text-ink">{p?.displayName ?? id}</span>
                <button
                  onClick={() => remove(id)}
                  className="text-ink-faint hover:text-danger"
                  title="移除"
                >
                  <X className="h-3 w-3" />
                </button>
              </span>
            )
          })}
          <div ref={containerRef} className="relative inline-block">
            <button
              type="button"
              onClick={() => setOpen(o => !o)}
              className="inline-flex items-center gap-1 rounded border border-dashed border-rule bg-card px-2 py-0.5 text-xs font-medium text-ink-muted hover:border-primary hover:text-primary"
            >
              <Plus className="h-3 w-3" /> 加 principal
              <ChevronDown className="h-3 w-3" />
            </button>
            {open && (
              <div className="absolute left-0 z-20 mt-1 w-72 overflow-hidden rounded-md border border-rule bg-card shadow-md">
                <div className="flex items-center gap-2 border-b border-rule px-2.5 py-1.5">
                  <Search className="h-3.5 w-3.5 text-ink-faint" />
                  <input
                    autoFocus
                    value={q}
                    onChange={(e) => setQ(e.target.value)}
                    placeholder="搜尋 user / dept / group…"
                    className="flex-1 bg-transparent text-xs text-ink outline-none placeholder:text-ink-faint"
                  />
                </div>
                {principals.length === 0 && (
                  <div className="px-3 py-2 text-xs text-ink-muted">
                    沒有可用 principal — 在 User & Role 頁面建立。
                  </div>
                )}
                {filteredOptions.length === 0 && principals.length > 0 && (
                  <div className="px-3 py-2 text-xs text-ink-muted">沒有符合的項目。</div>
                )}
                {filteredOptions.length > 0 && (
                  <ul className="max-h-64 overflow-auto">
                    {filteredOptions.map(p => {
                      const Icon = PTYPE_ICON[p.type]
                      return (
                        <li key={p.id}>
                          <button
                            type="button"
                            onClick={() => pick(p.id)}
                            className={cn(
                              'flex w-full items-center gap-2 px-2.5 py-1.5 text-left text-xs hover:bg-bg',
                            )}
                          >
                            <Icon className="h-3.5 w-3.5 text-ink-faint" />
                            <span className="flex-1 truncate text-ink">{p.displayName}</span>
                            <span className="font-mono text-[10px] tracking-wider text-ink-faint">
                              {p.type === 0 ? 'USER' : p.type === 1 ? 'DEPT' : 'GROUP'}
                            </span>
                          </button>
                        </li>
                      )
                    })}
                  </ul>
                )}
              </div>
            )}
          </div>
        </div>
      </Field>
    </div>
  )
}

function SectionHeading({ children }: { children: React.ReactNode }) {
  return <h3 className="text-sm font-semibold text-ink">{children}</h3>
}

function Field({ label, children }: { label: string; children: React.ReactNode }) {
  return (
    <label className="block">
      <span className="mb-1 block font-mono text-[10px] tracking-[0.14em] uppercase text-ink-muted">{label}</span>
      {children}
    </label>
  )
}

const inputCls = "block w-full rounded border border-rule bg-white px-3 py-1.5 text-sm text-ink outline-none placeholder:text-ink-faint focus:border-primary focus:ring-2 focus:ring-primary/20"
