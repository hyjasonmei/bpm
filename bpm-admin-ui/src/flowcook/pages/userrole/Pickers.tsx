import { useEffect, useMemo, useRef, useState } from 'react'
import { Building2, Check, ChevronDown, Search as SearchIcon, User, UsersRound, X } from 'lucide-react'
import { cn } from '@/lib/cn'
import { type Principal, type PrincipalType, type Role } from '@/flowcook/types'

const TYPE_ICON: Record<PrincipalType, React.ComponentType<{ className?: string }>> = {
  0: User,
  1: Building2,
  2: UsersRound,
}

interface RolePickerProps {
  roles: Role[]
  excludeIds: Set<string>
  onPick: (roleId: string) => void
  buttonLabel?: string
}

export function RolePicker({ roles, excludeIds, onPick, buttonLabel = '+ Assign role' }: RolePickerProps) {
  const [open, setOpen] = useState(false)
  const ref = useRef<HTMLDivElement>(null)

  useEffect(() => {
    if (!open) return
    function onDoc(e: MouseEvent) {
      if (ref.current && !ref.current.contains(e.target as Node)) setOpen(false)
    }
    document.addEventListener('mousedown', onDoc)
    return () => document.removeEventListener('mousedown', onDoc)
  }, [open])

  const available = roles.filter((r) => !excludeIds.has(r.id))

  return (
    <div ref={ref} className="relative inline-block">
      <button
        type="button"
        onClick={() => setOpen((o) => !o)}
        disabled={available.length === 0}
        className="inline-flex items-center gap-1 rounded border border-dashed border-rule bg-card px-2.5 py-1 text-xs font-medium text-ink-muted transition-colors hover:border-primary hover:bg-primary/10 hover:text-primary disabled:cursor-not-allowed disabled:opacity-50"
      >
        {buttonLabel}
        <ChevronDown className="h-3 w-3" />
      </button>
      {open && (
        <div className="absolute right-0 z-20 mt-1 w-64 overflow-hidden rounded-md border border-rule bg-card shadow-md">
          {available.length === 0 ? (
            <div className="px-3 py-2 text-xs text-ink-muted">All roles already assigned.</div>
          ) : (
            <ul className="max-h-64 overflow-auto">
              {available.map((r) => (
                <li key={r.id}>
                  <button
                    type="button"
                    onClick={() => { onPick(r.id); setOpen(false) }}
                    className="flex w-full items-center justify-between px-3 py-1.5 text-left text-xs hover:bg-bg"
                  >
                    <span className="text-ink">{r.name}</span>
                    {r.isSystem && <span className="font-mono text-[10px] tracking-wider text-ink-faint">SYS</span>}
                  </button>
                </li>
              ))}
            </ul>
          )}
        </div>
      )}
    </div>
  )
}

interface PrincipalPickerProps {
  principals: Principal[]
  excludeIds: Set<string>
  acceptTypes: PrincipalType[]
  onPick: (principalId: string) => void
  buttonLabel?: string
  placeholder?: string
}

export function PrincipalPicker({
  principals, excludeIds, acceptTypes, onPick, buttonLabel = '+ Add', placeholder = 'Search…',
}: PrincipalPickerProps) {
  const [open, setOpen] = useState(false)
  const [q, setQ] = useState('')
  const ref = useRef<HTMLDivElement>(null)
  const inputRef = useRef<HTMLInputElement>(null)

  useEffect(() => {
    if (!open) return
    function onDoc(e: MouseEvent) {
      if (ref.current && !ref.current.contains(e.target as Node)) { setOpen(false); setQ('') }
    }
    document.addEventListener('mousedown', onDoc)
    inputRef.current?.focus()
    return () => document.removeEventListener('mousedown', onDoc)
  }, [open])

  const filtered = useMemo(() => {
    const types = new Set(acceptTypes)
    const needle = q.trim().toLowerCase()
    return principals.filter((p) => {
      if (!types.has(p.type)) return false
      if (excludeIds.has(p.id)) return false
      if (!needle) return true
      return p.displayName.toLowerCase().includes(needle) || (p.email ?? '').toLowerCase().includes(needle)
    }).slice(0, 30)
  }, [principals, excludeIds, acceptTypes, q])

  return (
    <div ref={ref} className="relative inline-block">
      <button
        type="button"
        onClick={() => setOpen((o) => !o)}
        className="inline-flex items-center gap-1 rounded border border-dashed border-rule bg-card px-2.5 py-1 text-xs font-medium text-ink-muted transition-colors hover:border-primary hover:bg-primary/10 hover:text-primary"
      >
        {buttonLabel}
        <ChevronDown className="h-3 w-3" />
      </button>
      {open && (
        <div className="absolute right-0 z-20 mt-1 w-72 overflow-hidden rounded-md border border-rule bg-card shadow-md">
          <div className="flex items-center gap-2 border-b border-rule px-2.5 py-1.5">
            <SearchIcon className="h-3.5 w-3.5 text-ink-faint" />
            <input
              ref={inputRef}
              value={q}
              onChange={(e) => setQ(e.target.value)}
              placeholder={placeholder}
              className="flex-1 bg-transparent text-xs text-ink outline-none placeholder:text-ink-faint"
            />
            {q && (
              <button onClick={() => setQ('')} className="text-ink-faint hover:text-ink">
                <X className="h-3 w-3" />
              </button>
            )}
          </div>
          {filtered.length === 0 ? (
            <div className="px-3 py-2 text-xs text-ink-muted">No matches.</div>
          ) : (
            <ul className="max-h-64 overflow-auto">
              {filtered.map((p) => {
                const Icon = TYPE_ICON[p.type]
                return (
                  <li key={p.id}>
                    <button
                      type="button"
                      onClick={() => { onPick(p.id); setOpen(false); setQ('') }}
                      className="flex w-full items-center gap-2 px-2.5 py-1.5 text-left text-xs hover:bg-bg"
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
  )
}

export function CheckPill({ checked, onClick, label }: { checked: boolean; onClick: () => void; label: string }) {
  return (
    <button
      type="button"
      onClick={onClick}
      className={cn(
        'inline-flex items-center gap-1.5 rounded-full border px-2 py-0.5 text-[11px] transition-colors',
        checked
          ? 'border-primary bg-primary/10 text-primary'
          : 'border-rule bg-card text-ink-muted hover:border-primary/40',
      )}
    >
      <Check className={cn('h-3 w-3', checked ? 'opacity-100' : 'opacity-0')} />
      <span>{label}</span>
    </button>
  )
}
