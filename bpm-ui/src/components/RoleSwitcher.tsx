import { useEffect, useRef, useState } from 'react'
import { ChevronDown, Check, Loader2, AlertCircle, ExternalLink, Eye, FlaskConical } from 'lucide-react'
import { cn } from '@/lib/cn'
import { PERSONAS, type PersonaCode, type Persona } from '@/lib/role'
import { decodeJwt, isAdmin, isSandboxActor } from '@/lib/jwt'
import { getJwt, setJwt } from '@/lib/apiFetch'
import { isImpersonating } from '@/lib/impersonationToken'
import { ImpersonationModal } from '@/components/ImpersonationModal'
import { getSandboxStatus, listSandboxPersonas, switchSandboxPersona } from '@/lib/api/sandbox'
import type { SandboxPersonaDto } from '@/types/sandbox'

export interface RoleSwitcherProps {
  active: PersonaCode
  onChange: (next: PersonaCode) => void | Promise<void>
  pending?: boolean
  error?: string | null
  authedFullName?: string | null
}

export function RoleSwitcher({ active, onChange, pending = false, error = null, authedFullName = null }: RoleSwitcherProps) {
  const [open, setOpen] = useState(false)
  const [impersonateOpen, setImpersonateOpen] = useState(false)
  const [sandboxOn, setSandboxOn] = useState(false)
  const [sandboxPersonas, setSandboxPersonas] = useState<SandboxPersonaDto[]>([])
  const [sandboxBusy, setSandboxBusy] = useState(false)
  const [sandboxError, setSandboxError] = useState<string | null>(null)
  const ref = useRef<HTMLDivElement>(null)

  // Detect sandbox status on mount (and when the dropdown opens, so a fresh
  // toggle from another tab is reflected without a hard reload).
  useEffect(() => {
    let cancelled = false
    async function tick() {
      try {
        const status = await getSandboxStatus()
        if (cancelled) return
        setSandboxOn(status.enabled)
        if (status.enabled) {
          const list = await listSandboxPersonas()
          if (!cancelled) setSandboxPersonas(list)
        } else {
          setSandboxPersonas([])
        }
      } catch {
        // best-effort; sandbox UI degrades to original PERSONAS list on failure
      }
    }
    void tick()
    return () => { cancelled = true }
  }, [open])

  useEffect(() => {
    if (!open) return
    const onDocClick = (e: MouseEvent) => {
      if (ref.current && !ref.current.contains(e.target as Node)) setOpen(false)
    }
    const onEsc = (e: KeyboardEvent) => { if (e.key === 'Escape') setOpen(false) }
    document.addEventListener('mousedown', onDocClick)
    document.addEventListener('keydown', onEsc)
    return () => {
      document.removeEventListener('mousedown', onDocClick)
      document.removeEventListener('keydown', onEsc)
    }
  }, [open])

  const current = PERSONAS[active]
  const all = Object.values(PERSONAS) as Persona[]
  const jwt = getJwt()
  const decoded = jwt ? decodeJwt(jwt) : null
  const callerIsAdmin = isAdmin(decoded)
  const isSandboxPersona = isSandboxActor(decoded)
  const sandboxPersonaName = isSandboxPersona ? (decoded?.full_name ?? decoded?.email ?? null) : null

  async function handleSandboxPersonaSelect(p: SandboxPersonaDto) {
    setSandboxBusy(true)
    setSandboxError(null)
    try {
      const res = await switchSandboxPersona(p.id)
      setJwt(res.token)
      setOpen(false)
      // Reload so all screens (Home, NotificationsMenu, etc.) refetch under
      // the new persona. Same pattern useActivePersona doesn't cover because
      // it works on persona codes, not arbitrary user ids.
      window.location.reload()
    } catch (e) {
      setSandboxError(e instanceof Error ? e.message : String(e))
    } finally {
      setSandboxBusy(false)
    }
  }

  return (
    <div ref={ref} className="relative flex items-center gap-2">
      {sandboxPersonaName && (
        <span
          title="Acting as a sandbox persona — every action is logged with your real admin id"
          className="hidden md:inline-flex items-center gap-1 rounded-full bg-amber-500/90 px-2 py-0.5 text-[10.5px] font-bold uppercase tracking-wider text-white"
        >
          <FlaskConical className="h-3 w-3" />
          Acting as {sandboxPersonaName} (sandbox)
        </span>
      )}
      <button
        onClick={() => setOpen(o => !o)}
        className={cn(
          'inline-flex items-center gap-2 rounded-md px-2 py-1 text-sm text-white/90 transition-colors',
          'hover:bg-white/10',
          open && 'bg-white/10',
        )}
        title="Switch demo persona"
      >
        <span className="inline-flex h-6 w-6 items-center justify-center rounded-full bg-white/15 text-[14px]">
          {pending ? <Loader2 className="h-3 w-3 animate-spin" /> : current.emoji}
        </span>
        <span className="text-left leading-tight">
          <span className="block text-[12px] font-medium">{authedFullName ?? current.user.name.split(' (')[0]}</span>
          <span className="block text-[10px] text-white/55">{current.displayName} · {current.zhName}</span>
        </span>
        {error && <AlertCircle className="h-3.5 w-3.5 text-amber-300" aria-label={error} />}
        <ChevronDown className={cn('h-3.5 w-3.5 text-white/60 transition-transform', open && 'rotate-180')} />
      </button>

      {open && (
        <div className="absolute right-0 top-[calc(100%+6px)] z-30 w-80 origin-top-right rounded-lg border border-rule bg-card shadow-2xl">
          <div className="border-b border-rule px-4 py-2.5">
            <p className="text-[10.5px] font-semibold uppercase tracking-wider text-ink-faint">Demo Persona</p>
            <p className="text-xs text-ink-muted">Switch role to see the same data from each persona's perspective.</p>
          </div>
          <div className="p-1">
            {all.map(p => {
              const isActive = p.id === active
              return (
                <button
                  key={p.id}
                  onClick={() => { onChange(p.id); setOpen(false) }}
                  className={cn(
                    'flex w-full items-start gap-3 rounded-md px-3 py-2 text-left transition-colors',
                    isActive ? 'bg-amber-50' : 'hover:bg-slate-50',
                  )}
                >
                  <span className="mt-0.5 inline-flex h-8 w-8 shrink-0 items-center justify-center rounded-full bg-slate-100 text-base">
                    {p.emoji}
                  </span>
                  <div className="min-w-0 flex-1">
                    <div className="flex items-center gap-2">
                      <span className="text-sm font-semibold text-ink">{p.displayName}</span>
                      <span className="text-xs text-ink-muted">{p.zhName}</span>
                      {isActive && <Check className="h-3.5 w-3.5 text-accent" strokeWidth={3} />}
                    </div>
                    <p className="text-[11.5px] leading-snug text-ink-muted truncate">{p.user.name}</p>
                    <p className="text-[10.5px] leading-snug text-ink-faint">{p.description}</p>
                  </div>
                </button>
              )
            })}
          </div>

          {sandboxOn && sandboxPersonas.length > 0 && (
            <div className="border-t border-rule">
              <div className="bg-amber-50/60 px-4 py-2 text-[10.5px] font-semibold uppercase tracking-wider text-amber-900">
                Sandbox personas — act-as via /api/sandbox/persona
              </div>
              <div className="max-h-60 overflow-y-auto p-1">
                {sandboxPersonas.map(p => (
                  <button
                    key={p.id}
                    disabled={sandboxBusy}
                    onClick={() => handleSandboxPersonaSelect(p)}
                    className="flex w-full items-start gap-3 rounded-md px-3 py-2 text-left hover:bg-amber-50/40 disabled:cursor-not-allowed disabled:opacity-40"
                  >
                    <FlaskConical className="mt-0.5 h-4 w-4 text-amber-700 shrink-0" />
                    <div className="min-w-0 flex-1">
                      <div className="flex items-center gap-1.5">
                        <span className="text-sm font-semibold text-ink truncate">{p.fullName}</span>
                        <span className="text-[10.5px] text-amber-700 font-medium">(sandbox)</span>
                      </div>
                      <p className="text-[11px] leading-snug text-ink-muted truncate">{p.email}</p>
                      {p.departmentName && <p className="text-[10.5px] leading-snug text-ink-faint truncate">{p.departmentName}</p>}
                    </div>
                  </button>
                ))}
              </div>
              {sandboxError && (
                <div className="border-t border-rule bg-rose-50 px-4 py-2 text-[11px] text-rose-700">
                  Sandbox switch failed: {sandboxError}
                </div>
              )}
            </div>
          )}

          {callerIsAdmin && !isImpersonating() && (
            <button
              onClick={() => { setOpen(false); setImpersonateOpen(true) }}
              className="flex w-full items-center gap-2 border-t border-rule bg-amber-50/30 px-4 py-2 text-left text-xs font-medium text-amber-800 hover:bg-amber-50"
            >
              <Eye className="h-3.5 w-3.5" />
              🎭 Act as another user…
            </button>
          )}
          {callerIsAdmin && (
            <a
              href="/admin/"
              className="flex items-center gap-2 border-t border-rule bg-slate-50/40 px-4 py-2 text-xs font-medium text-slate-700 hover:bg-slate-100"
            >
              <ExternalLink className="h-3.5 w-3.5" />
              Open Admin Console →
            </a>
          )}
          {error && (
            <div className="border-t border-rule bg-rose-50 px-4 py-2 text-[11px] text-rose-700">
              <span className="font-medium">登入失敗 · </span>{error}
            </div>
          )}
          <div className="border-t border-rule bg-slate-50/50 px-4 py-2 text-[10.5px] text-ink-faint">
            Persona 切換會打 /api/dev/login 取 JWT，存於 localStorage.bpm_jwt
          </div>
        </div>
      )}
      <ImpersonationModal open={impersonateOpen} onClose={() => setImpersonateOpen(false)} />
    </div>
  )
}
