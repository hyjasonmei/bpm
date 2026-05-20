/**
 * Reusable principal picker.
 *
 *   <PrincipalPickerField
 *     label="可啟動"
 *     helper="…"
 *     value={list}          // string[] of `${kind}:${id}` refs
 *     onChange={setList}
 *   />
 *
 * Renders the inline label / helper / chip row / "+ 加 principal" button.
 * Clicking the button opens `PrincipalPickerModal` — a tabbed dialog with
 * USER / DEPT / GROUP / ROLE (role visually offset from the first three),
 * a search box, multi-select buffer, and Cancel / Select footer that only
 * commits on Select. Cancel discards the buffer.
 *
 * The component fetches `/api/principals` (×3 by type) and `/api/roles`
 * lazily on first open and caches the directory across re-opens for the
 * lifetime of the page.
 *
 * Storage format on the wire is the prefixed form (e.g. `user:abc-uuid`,
 * `role:r-uuid`). Legacy unprefixed uuid is read as `user:` so existing
 * drafts keep working.
 */
import { useCallback, useEffect, useMemo, useRef, useState } from 'react'
import { Building2, Check, ChevronDown, Plus, Search, Shield, User, UsersRound, X } from 'lucide-react'
import { Button } from '@/components/ui/button'
import { Modal } from '@/components/ui/modal'
import { cn } from '@/lib/cn'
import { api } from '@/flowcook/api'
import {
  formatPrincipalRef,
  parsePrincipalRef,
  type PrincipalRefKind,
} from '@/lib/onboarding'

interface PrincipalDto {
  id: string
  type: 0 | 1 | 2 // user / dept / group
  displayName: string
  email: string | null
  active: boolean
}

interface RoleDto {
  id: string
  name: string
  isSystem: boolean
  description: string | null
}

export interface DirectoryRow {
  kind: PrincipalRefKind
  id: string
  displayName: string
  /** secondary line — email for user, description for role, member-count hint later */
  subtitle: string | null
  /** for role: whether it's a system role (display as locked badge) */
  isSystem?: boolean
}

interface DirectoryState {
  rows: DirectoryRow[]
  loaded: boolean
  error: string | null
}

const KIND_ICON: Record<PrincipalRefKind, React.ComponentType<{ className?: string }>> = {
  user: User,
  dept: Building2,
  group: UsersRound,
  role: Shield,
}

const KIND_LABEL: Record<PrincipalRefKind, string> = {
  user: 'User',
  dept: 'Dept',
  group: 'Group',
  role: 'Role',
}

// One module-level cache so the modal opens instantly on re-open and so
// every <PrincipalPickerField> on a page shares the same directory.
let directoryCache: DirectoryState = { rows: [], loaded: false, error: null }
let directoryPromise: Promise<void> | null = null

async function loadDirectory(): Promise<void> {
  if (directoryCache.loaded || directoryCache.error) return
  if (directoryPromise) return directoryPromise
  directoryPromise = (async () => {
    try {
      const [users, depts, groups, roles] = await Promise.all([
        api<PrincipalDto[]>('/api/principals?type=0'),
        api<PrincipalDto[]>('/api/principals?type=1'),
        api<PrincipalDto[]>('/api/principals?type=2'),
        api<RoleDto[]>('/api/roles'),
      ])
      const rows: DirectoryRow[] = [
        ...users.map(u => ({ kind: 'user' as const, id: u.id, displayName: u.displayName, subtitle: u.email })),
        ...depts.map(d => ({ kind: 'dept' as const, id: d.id, displayName: d.displayName, subtitle: d.email })),
        ...groups.map(g => ({ kind: 'group' as const, id: g.id, displayName: g.displayName, subtitle: g.email })),
        ...roles.map(r => ({ kind: 'role' as const, id: r.id, displayName: r.name, subtitle: r.description, isSystem: r.isSystem })),
      ]
      directoryCache = { rows, loaded: true, error: null }
    } catch (e) {
      directoryCache = { rows: [], loaded: false, error: e instanceof Error ? e.message : String(e) }
    } finally {
      directoryPromise = null
    }
  })()
  return directoryPromise
}

function useDirectory(): DirectoryState {
  const [state, setState] = useState<DirectoryState>(directoryCache)
  useEffect(() => {
    if (directoryCache.loaded) return
    let cancelled = false
    void loadDirectory().then(() => { if (!cancelled) setState({ ...directoryCache }) })
    return () => { cancelled = true }
  }, [])
  return state
}

/**
 * Snapshot of the principal directory at this moment. Loads lazily on
 * first access. Other modules (e.g. AI tool handlers) read this without
 * mounting a picker — they should call {@link ensurePrincipalDirectoryLoaded}
 * first if they need the data to be present.
 */
export function getPrincipalDirectory(): DirectoryRow[] {
  return directoryCache.rows
}

export function ensurePrincipalDirectoryLoaded(): Promise<void> {
  return loadDirectory()
}

/**
 * Resolve `(kind, name)` to a `${kind}:${id}` ref string, or null if the
 * name doesn't match any cached principal. Match is case-insensitive +
 * trims whitespace; if multiple rows share the name (unlikely), returns
 * the first.
 */
export function findPrincipalRefByName(kind: PrincipalRefKind, name: string): string | null {
  const needle = name.trim().toLowerCase()
  if (!needle) return null
  const row = directoryCache.rows.find(r =>
    r.kind === kind && r.displayName.trim().toLowerCase() === needle
  )
  return row ? formatPrincipalRef({ kind: row.kind, id: row.id }) : null
}

/**
 * Inverse of {@link findPrincipalRefByName}: given a stored ref string,
 * return `{ kind, name }`. Falls back to `{ kind, name: id }` when the
 * directory doesn't know about that id — keeps AI context legible even
 * when a ref dangles (deleted principal, partially-synced cache).
 */
export function describePrincipalRef(ref: string): { kind: PrincipalRefKind; name: string } {
  const parsed = parsePrincipalRef(ref)
  const row = directoryCache.rows.find(r => r.kind === parsed.kind && r.id === parsed.id)
  return { kind: parsed.kind, name: row?.displayName ?? parsed.id }
}

function resolveRef(rows: DirectoryRow[], ref: string): DirectoryRow | null {
  const parsed = parsePrincipalRef(ref)
  return rows.find(r => r.kind === parsed.kind && r.id === parsed.id) ?? null
}

// ─────────────────────────────────────────────────────────────────────────
// Public — Field (button + chips + modal)
// ─────────────────────────────────────────────────────────────────────────

interface FieldProps {
  label: string
  helper?: string
  value: string[]
  onChange: (next: string[]) => void
  /** Modal title; defaults to the label. */
  modalTitle?: string
}

export function PrincipalPickerField({ label, helper, value, onChange, modalTitle }: FieldProps) {
  const [open, setOpen] = useState(false)
  const dir = useDirectory()

  return (
    <div className="rounded-md border border-rule bg-card p-4">
      <span className="mb-1 block font-mono text-[10px] tracking-[0.14em] uppercase text-ink-muted">{label}</span>
      {helper && <p className="-mt-0.5 mb-2 text-xs leading-relaxed text-ink-muted">{helper}</p>}

      <div className="flex flex-wrap items-center gap-1.5">
        {value.length === 0 && (
          <span className="text-xs text-ink-faint">尚未指定</span>
        )}
        {value.map(ref => (
          <PrincipalChip
            key={ref}
            refValue={ref}
            row={resolveRef(dir.rows, ref)}
            onRemove={() => onChange(value.filter(x => x !== ref))}
          />
        ))}
        <button
          type="button"
          onClick={() => setOpen(true)}
          className="inline-flex items-center gap-1 rounded border border-dashed border-rule bg-card px-2 py-0.5 text-xs font-medium text-ink-muted hover:border-primary hover:text-primary"
        >
          <Plus className="h-3 w-3" /> 加 principal
        </button>
      </div>

      {dir.error && (
        <p className="mt-2 text-xs text-warn">無法載入 principal 目錄：{dir.error}</p>
      )}

      {open && (
        <PrincipalPickerModal
          title={modalTitle ?? label}
          initial={value}
          directory={dir}
          onCancel={() => setOpen(false)}
          onCommit={(next) => { onChange(next); setOpen(false) }}
        />
      )}
    </div>
  )
}

// ─────────────────────────────────────────────────────────────────────────
// Public — Chip (also used standalone if needed)
// ─────────────────────────────────────────────────────────────────────────

export function PrincipalChip({
  refValue, row, onRemove,
}: { refValue: string; row: DirectoryRow | null; onRemove?: () => void }) {
  const parsed = parsePrincipalRef(refValue)
  const kind = row?.kind ?? parsed.kind
  const Icon = KIND_ICON[kind]
  const isRole = kind === 'role'
  return (
    <span
      className={cn(
        'inline-flex items-center gap-1 rounded-full border px-2 py-0.5 text-xs',
        isRole
          ? 'border-primary/30 bg-primary/5 text-ink'
          : 'border-rule bg-bg text-ink',
      )}
    >
      <Icon className={cn('h-3 w-3', isRole ? 'text-primary' : 'text-ink-faint')} />
      <span>{row?.displayName ?? parsed.id}</span>
      {isRole && <span className="font-mono text-[10px] uppercase tracking-wider text-primary">role</span>}
      {onRemove && (
        <button
          type="button"
          onClick={onRemove}
          className="text-ink-faint hover:text-danger"
          title="移除"
        >
          <X className="h-3 w-3" />
        </button>
      )}
    </span>
  )
}

// ─────────────────────────────────────────────────────────────────────────
// Internal — Modal
// ─────────────────────────────────────────────────────────────────────────

interface ModalProps {
  title: string
  initial: string[]
  directory: DirectoryState
  onCancel: () => void
  onCommit: (next: string[]) => void
}

const TAB_ORDER: PrincipalRefKind[] = ['user', 'dept', 'group', 'role']

function PrincipalPickerModal({ title, initial, directory, onCancel, onCommit }: ModalProps) {
  const [tab, setTab] = useState<PrincipalRefKind>('user')
  const [q, setQ] = useState('')
  // Normalize legacy unprefixed uuids to `user:…` so the buffer's keys
  // always match the row keys rendered in the list.
  const [buffer, setBuffer] = useState<Set<string>>(() =>
    new Set(initial.map(s => formatPrincipalRef(parsePrincipalRef(s))))
  )
  const searchRef = useRef<HTMLInputElement>(null)

  useEffect(() => { searchRef.current?.focus() }, [tab])

  const toggle = useCallback((ref: string) => {
    setBuffer(prev => {
      const next = new Set(prev)
      if (next.has(ref)) next.delete(ref)
      else next.add(ref)
      return next
    })
  }, [])

  const tabRows = useMemo(() => {
    const needle = q.trim().toLowerCase()
    const rows = directory.rows.filter(r => r.kind === tab)
    if (!needle) return rows
    return rows.filter(r =>
      r.displayName.toLowerCase().includes(needle)
      || (r.subtitle ?? '').toLowerCase().includes(needle)
    )
  }, [directory.rows, tab, q])

  const counts = useMemo(() => {
    const m: Record<PrincipalRefKind, number> = { user: 0, dept: 0, group: 0, role: 0 }
    for (const r of directory.rows) m[r.kind]++
    return m
  }, [directory.rows])

  const bufferList = useMemo(() => Array.from(buffer), [buffer])

  function handleCommit() {
    onCommit(Array.from(buffer))
  }

  return (
    <Modal
      open
      onClose={onCancel}
      title={title}
      size="md"
      footer={
        <>
          <div className="mr-auto text-xs text-ink-muted">
            已選 <span className="font-semibold text-ink">{bufferList.length}</span> 項
          </div>
          <Button variant="ghost" onClick={onCancel}>Cancel</Button>
          <Button variant="primary" onClick={handleCommit}>
            <ChevronDown className="h-4 w-4 -rotate-90" />
            Select
          </Button>
        </>
      }
    >
      <div className="flex h-full flex-col">
        {/* Tabs */}
        <div className="flex items-center gap-1 border-b border-rule px-3 pt-2">
          {TAB_ORDER.map((k, i) => {
            const Icon = KIND_ICON[k]
            const isRole = k === 'role'
            const isActive = tab === k
            return (
              <div key={k} className="flex items-center">
                {isRole && <div className="mx-2 h-5 w-px bg-rule" aria-hidden />}
                <button
                  type="button"
                  onClick={() => setTab(k)}
                  className={cn(
                    'flex items-center gap-1.5 rounded-t-md px-3 py-1.5 text-xs font-medium transition-colors',
                    isActive
                      ? isRole
                        ? 'border-x border-t border-primary/30 bg-primary/5 text-primary'
                        : 'border-x border-t border-rule bg-white text-ink'
                      : isRole
                        ? 'text-primary/70 hover:bg-primary/5'
                        : 'text-ink-muted hover:bg-slate-50',
                  )}
                >
                  <Icon className="h-3.5 w-3.5" />
                  {KIND_LABEL[k]}
                  <span className={cn('rounded-full px-1.5 text-[10px]', isActive ? 'bg-slate-100 text-ink-muted' : 'text-ink-faint')}>
                    {counts[k]}
                  </span>
                </button>
                {i === 2 && null}
              </div>
            )
          })}
        </div>

        {/* Search */}
        <div className="flex items-center gap-2 border-b border-rule px-4 py-2">
          <Search className="h-3.5 w-3.5 text-ink-faint" />
          <input
            ref={searchRef}
            value={q}
            onChange={e => setQ(e.target.value)}
            placeholder={`搜尋 ${KIND_LABEL[tab]} 名稱 / ${tab === 'role' ? '說明' : 'email'}…`}
            className="flex-1 bg-transparent text-sm text-ink outline-none placeholder:text-ink-faint"
          />
          {q && (
            <button onClick={() => setQ('')} className="text-ink-faint hover:text-ink" title="清除">
              <X className="h-3.5 w-3.5" />
            </button>
          )}
        </div>

        {/* List */}
        <div className="min-h-[280px] flex-1 overflow-auto">
          {!directory.loaded && !directory.error && (
            <div className="px-4 py-10 text-center text-sm text-ink-muted">載入中…</div>
          )}
          {directory.error && (
            <div className="px-4 py-10 text-center text-sm text-warn">無法載入：{directory.error}</div>
          )}
          {directory.loaded && tabRows.length === 0 && (
            <div className="px-4 py-10 text-center text-sm text-ink-muted">
              {q ? '沒有符合的項目。' : `目前沒有 ${KIND_LABEL[tab]}。`}
            </div>
          )}
          {directory.loaded && tabRows.length > 0 && (
            <ul className="divide-y divide-rule">
              {tabRows.map(row => {
                const ref = formatPrincipalRef({ kind: row.kind, id: row.id })
                const selected = buffer.has(ref)
                const Icon = KIND_ICON[row.kind]
                return (
                  <li key={ref}>
                    <button
                      type="button"
                      onClick={() => toggle(ref)}
                      className={cn(
                        'flex w-full items-center gap-3 px-4 py-2.5 text-left hover:bg-slate-50',
                        selected && 'bg-primary/5',
                      )}
                    >
                      <span
                        className={cn(
                          'flex h-4 w-4 items-center justify-center rounded border',
                          selected ? 'border-primary bg-primary text-white' : 'border-rule bg-white',
                        )}
                      >
                        {selected && <Check className="h-3 w-3" />}
                      </span>
                      <Icon className={cn('h-4 w-4', row.kind === 'role' ? 'text-primary' : 'text-ink-faint')} />
                      <span className="flex-1 truncate text-sm text-ink">{row.displayName}</span>
                      {row.kind === 'role' && row.isSystem && (
                        <span className="rounded bg-slate-100 px-1.5 py-0.5 font-mono text-[10px] uppercase tracking-wider text-ink-muted">
                          system
                        </span>
                      )}
                      {row.subtitle && (
                        <span className="truncate font-mono text-[11px] text-ink-faint">{row.subtitle}</span>
                      )}
                    </button>
                  </li>
                )
              })}
            </ul>
          )}
        </div>

      </div>
    </Modal>
  )
}
