import { useEffect, useRef, useState } from 'react'
import {
  ChevronDown, Check, Loader2, AlertCircle, ExternalLink, Eye, FlaskConical, LogOut, Mail, ShieldCheck, Undo2,
} from 'lucide-react'

import { cn } from '@/lib/cn'
import { PERSONAS, type PersonaCode, type Persona } from '@/lib/role'
import { decodeJwt, isAdmin, isSandboxActor, isPersonaSwitcher } from '@/lib/jwt'
import { clearJwt, getJwt } from '@/lib/apiFetch'
import { isImpersonating, enterImpersonation, exitImpersonationLocal, realIdentityToken } from '@/lib/impersonationToken'
import { ImpersonationModal } from '@/components/ImpersonationModal'
import { getSandboxStatus, listSandboxPersonas, switchSandboxPersona } from '@/lib/api/sandbox'
import type { SandboxPersonaDto } from '@/types/sandbox'

export interface AccountMenuProps {
  active: PersonaCode
  onChange: (next: PersonaCode) => void | Promise<void>
  pending?: boolean
  error?: string | null
  authedFullName?: string | null
}

/**
 * Top-right account button + popover. Combines identity display + dev-mode
 * role quick-switch + sandbox persona switch + admin impersonation + logout.
 * Replaces the standalone `<RoleSwitcher>` per the openspec change
 * `add-account-menu-and-logout`.
 */
export function AccountMenu({
  active,
  onChange,
  pending = false,
  error = null,
  authedFullName = null,
}: AccountMenuProps) {
  const [open, setOpen] = useState(false)
  const [impersonateOpen, setImpersonateOpen] = useState(false)
  const [sandboxOn, setSandboxOn] = useState(false)
  const [sandboxPersonas, setSandboxPersonas] = useState<SandboxPersonaDto[]>([])
  const [sandboxBusy, setSandboxBusy] = useState(false)
  const [sandboxError, setSandboxError] = useState<string | null>(null)
  const [accountInput, setAccountInput] = useState('')
  const [devModeAvailable, setDevModeAvailable] = useState<boolean | null>(null)
  const ref = useRef<HTMLDivElement>(null)

  // Probe /api/dev/login once on mount. In prod mode it 404s — that's our
  // signal to hide the dev quick-switch submenu entirely. We don't actually
  // log in; an OPTIONS-style probe via POST without credentials returns
  // either 400 (registered, missing body) or 404 (not registered).
  useEffect(() => {
    let cancelled = false
    void (async () => {
      try {
        const base = (import.meta as ImportMeta & { env: { VITE_BPM_SVC_URL?: string } }).env?.VITE_BPM_SVC_URL ?? 'http://localhost:5290'
        const res = await fetch(`${base}/api/dev/login`, { method: 'POST', headers: { 'Content-Type': 'application/json' }, body: '{}' })
        if (cancelled) return
        // Anything other than 404 = endpoint exists; treat as dev mode.
        setDevModeAvailable(res.status !== 404)
      } catch {
        if (!cancelled) setDevModeAvailable(false)
      }
    })()
    return () => { cancelled = true }
  }, [])

  // Refresh sandbox status when the menu opens so a fresh toggle is reflected.
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
      } catch { /* best effort */ }
    }
    void tick()
    return () => { cancelled = true }
  }, [open])

  // Click-outside + Escape close.
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

  // Authorize the persona switcher against the REAL identity (the stored
  // pre-token when already acting as a persona), so an admin who switched into
  // a non-admin persona can keep switching and return instead of being locked out.
  const realToken = realIdentityToken()
  const realCanSwitch = isPersonaSwitcher(realToken ? decodeJwt(realToken) : null)
  const canSwitchPersona = sandboxOn && realCanSwitch
  const canReturn = isSandboxPersona && realToken != null && realToken !== jwt

  const displayName = authedFullName ?? decoded?.full_name ?? decoded?.email ?? current.user.name.split(' (')[0]
  const initials = computeInitials(displayName)
  const email = decoded?.email ?? null
  // ASP.NET JWT serializer emits the `roles` claim as a string when there's
  // exactly one role and a string[] when there's more. Normalize so consumers
  // always get an array.
  const rolesClaim = decoded?.roles as unknown
  const roles: string[] = Array.isArray(rolesClaim)
    ? (rolesClaim as string[])
    : typeof rolesClaim === 'string' && rolesClaim.length > 0
      ? [rolesClaim]
      : []

  // Type an account (email or name) → resolve against the personas list →
  // switch. No dropdown list; the list is fetched only for resolution.
  function resolvePersona(input: string): SandboxPersonaDto | null {
    const q = input.trim().toLowerCase()
    if (!q) return null
    return (
      sandboxPersonas.find(p => p.email.toLowerCase() === q || p.fullName.toLowerCase() === q) ??
      sandboxPersonas.find(p => p.email.toLowerCase().startsWith(q + '@')) ??
      sandboxPersonas.find(p => p.email.toLowerCase().startsWith(q)) ??
      null
    )
  }

  async function switchToPersona(userId: string) {
    setSandboxBusy(true)
    setSandboxError(null)
    try {
      // If already acting as a persona, restore the real admin token first so
      // the mint authenticates as the privileged user (never persona-on-persona).
      if (isSandboxActor(decoded)) {
        if (!exitImpersonationLocal()) {
          throw new Error('此分頁沒有可用的真實管理者身分（可能是用 persona 連結開的）')
        }
      }
      const res = await switchSandboxPersona(userId)
      enterImpersonation(res.token) // stores real token in pre-key, swaps in persona token
      setOpen(false)
      window.location.reload()
    } catch (e) {
      setSandboxError(e instanceof Error ? e.message : String(e))
    } finally {
      setSandboxBusy(false)
    }
  }

  function handleAccountSwitch() {
    const match = resolvePersona(accountInput)
    if (!match) {
      setSandboxError(`找不到帳號「${accountInput.trim()}」`)
      return
    }
    void switchToPersona(match.id)
  }

  function returnToReal() {
    if (exitImpersonationLocal()) window.location.reload()
  }

  function handleLogout() {
    setOpen(false)
    clearJwt()
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
        title={displayName}
        aria-label="Account menu"
      >
        <span className="inline-flex h-6 w-6 items-center justify-center rounded-full bg-white/15 text-[11px] font-semibold">
          {pending ? <Loader2 className="h-3 w-3 animate-spin" /> : initials}
        </span>
        <span className="hidden text-left leading-tight md:block">
          <span className="block text-[12px] font-medium">{displayName}</span>
          <span className="block text-[10px] text-white/55">{current.displayName} · {current.zhName}</span>
        </span>
        {error && <AlertCircle className="h-3.5 w-3.5 text-amber-300" aria-label={error} />}
        <ChevronDown className={cn('h-3.5 w-3.5 text-white/60 transition-transform', open && 'rotate-180')} />
      </button>

      {open && (
        <div className="absolute right-0 top-[calc(100%+6px)] z-30 w-80 origin-top-right rounded-lg border border-rule bg-card shadow-2xl">
          {/* ── Identity block ───────────────────────────────── */}
          <div className="border-b border-rule px-4 py-3">
            <div className="flex items-start gap-3">
              <span className="inline-flex h-10 w-10 shrink-0 items-center justify-center rounded-full bg-slate-100 text-sm font-semibold text-ink">
                {initials}
              </span>
              <div className="min-w-0 flex-1">
                <div className="text-sm font-semibold text-ink truncate">{displayName}</div>
                {email && (
                  <div className="mt-0.5 flex items-center gap-1 text-[11.5px] text-ink-muted truncate">
                    <Mail className="h-3 w-3 shrink-0" />
                    <span className="truncate">{email}</span>
                  </div>
                )}
                <div className="mt-1 flex flex-wrap items-center gap-1">
                  <span className="inline-flex items-center gap-1 rounded-full bg-blue-50 px-2 py-0.5 text-[10.5px] font-medium text-blue-700">
                    <ShieldCheck className="h-3 w-3" />
                    {current.displayName} · {current.zhName}
                  </span>
                  {roles.slice(0, 3).map(r => (
                    <span key={r} className="rounded-full bg-slate-100 px-1.5 py-0.5 text-[10px] text-ink-muted">{r}</span>
                  ))}
                </div>
              </div>
            </div>
          </div>

          {/* ── Dev-mode role quick-switch ───────────────────── */}
          {devModeAvailable && (
            <>
              <div className="border-b border-rule px-4 py-2">
                <p className="text-[10.5px] font-semibold uppercase tracking-wider text-ink-faint">Switch role (dev mode)</p>
                <p className="text-[11px] text-ink-muted">Quick-login as a seeded persona. Hidden in production.</p>
              </div>
              <div className="p-1">
                {all.map(p => {
                  const isActive = p.id === active
                  return (
                    <button
                      key={p.id}
                      onClick={() => { void onChange(p.id); setOpen(false) }}
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
                      </div>
                    </button>
                  )
                })}
              </div>
            </>
          )}

          {/* ── Sandbox: act-as any account by typing (admin + sandbox on) ─ */}
          {canSwitchPersona && (
            <div className="border-t border-rule">
              <div className="flex items-center gap-1.5 bg-amber-50/60 px-4 py-2 text-[10.5px] font-semibold uppercase tracking-wider text-amber-900">
                <FlaskConical className="h-3 w-3" /> Sandbox — 以任一帳號操作
              </div>
              <div className="space-y-2 p-3">
                <div className="flex gap-2">
                  <input
                    value={accountInput}
                    onChange={e => { setAccountInput(e.target.value); setSandboxError(null) }}
                    onKeyDown={e => { if (e.key === 'Enter') handleAccountSwitch() }}
                    placeholder="輸入帳號 email（例 bob@acme.example）"
                    disabled={sandboxBusy}
                    className="flex-1 rounded-md border border-rule px-2.5 py-1.5 text-sm text-ink placeholder:text-ink-faint focus:outline-none focus:ring-1 focus:ring-primary disabled:opacity-50"
                  />
                  <button
                    onClick={handleAccountSwitch}
                    disabled={sandboxBusy || !accountInput.trim()}
                    className="inline-flex items-center gap-1 rounded-md bg-amber-500 px-3 py-1.5 text-sm font-medium text-white hover:bg-amber-600 disabled:opacity-40"
                  >
                    {sandboxBusy ? <Loader2 className="h-3.5 w-3.5 animate-spin" /> : '切換'}
                  </button>
                </div>
                <p className="text-[10.5px] text-ink-faint">以該帳號身分操作；所有動作仍記在你的管理者 id 下。</p>
                {sandboxError && <p className="text-[11px] text-rose-700">{sandboxError}</p>}
              </div>
            </div>
          )}

          {canReturn && (
            <button
              onClick={returnToReal}
              className="flex w-full items-center gap-2 border-t border-rule bg-slate-50/60 px-4 py-2 text-left text-xs font-medium text-slate-700 hover:bg-slate-100"
            >
              <Undo2 className="h-3.5 w-3.5" />
              返回真實身分
            </button>
          )}

          {/* ── Admin tools (admin only) ─────────────────────── */}
          {callerIsAdmin && !isImpersonating() && (
            <button
              onClick={() => { setOpen(false); setImpersonateOpen(true) }}
              className="flex w-full items-center gap-2 border-t border-rule bg-amber-50/30 px-4 py-2 text-left text-xs font-medium text-amber-800 hover:bg-amber-50"
            >
              <Eye className="h-3.5 w-3.5" />
              Act as another user…
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

          {/* ── Logout (always) ──────────────────────────────── */}
          <button
            onClick={handleLogout}
            className="flex w-full items-center gap-2 border-t border-rule bg-slate-50/60 px-4 py-2 text-left text-xs font-medium text-ink hover:bg-slate-100"
          >
            <LogOut className="h-3.5 w-3.5 text-ink-muted" />
            Logout / 登出
          </button>

          {error && (
            <div className="border-t border-rule bg-rose-50 px-4 py-2 text-[11px] text-rose-700">
              <span className="font-medium">登入失敗 · </span>{error}
            </div>
          )}
        </div>
      )}
      <ImpersonationModal open={impersonateOpen} onClose={() => setImpersonateOpen(false)} />
    </div>
  )
}

function computeInitials(name: string): string {
  const parts = name.trim().split(/\s+/).filter(Boolean)
  if (parts.length === 0) return '?'
  if (parts.length === 1) return parts[0].slice(0, 2).toUpperCase()
  return (parts[0][0] + parts[parts.length - 1][0]).toUpperCase()
}
