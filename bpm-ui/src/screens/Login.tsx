import { useState, type FormEvent } from 'react'
import { LogIn, Loader2, AlertCircle } from 'lucide-react'
import { Button } from '@/components/ui/button'
import { Input, Field } from '@/components/ui/form'
import { login } from '@/lib/api/auth'
import { rememberPersonaForRoles } from '@/lib/role'

// Demo credentials match admin-svc's seeded Bob (employee persona,
// reports to Alice the Backend dept head). Pre-filling matches the
// admin-ui Login page UX so the team can fly through dev login.
const DEMO_EMAIL = 'bob@acme.example'
const DEMO_PASSWORD = 'flowcook2026'

export function Login({ onLoggedIn }: { onLoggedIn?: () => void }) {
  const [email, setEmail] = useState(DEMO_EMAIL)
  const [password, setPassword] = useState(DEMO_PASSWORD)
  const [busy, setBusy] = useState(false)
  const [err, setErr] = useState<string | null>(null)

  async function submit(e: FormEvent) {
    e.preventDefault()
    if (!email.trim() || !password) {
      setErr('請輸入 Email 與密碼。')
      return
    }
    setBusy(true)
    setErr(null)
    try {
      const res = await login(email.trim(), password)
      // Sync the saved persona to the identity we just authenticated as, so
      // a stale persona from a previous user/session doesn't leak into this
      // login's view (the token itself carries no roles to derive from).
      rememberPersonaForRoles(res.user.roles)
      if (onLoggedIn) onLoggedIn()
      else window.location.reload()
    } catch (e) {
      setErr(e instanceof Error ? e.message : String(e))
      setBusy(false)
    }
  }

  return (
    <div className="min-h-screen bg-gradient-to-br from-slate-50 to-slate-100 flex items-center justify-center px-4">
      <div className="w-full max-w-sm">
        <div className="text-center mb-6">
          <div className="inline-flex h-12 w-12 items-center justify-center rounded-xl bg-primary text-white text-lg font-bold mb-3">
            BPM
          </div>
          <h1 className="text-xl font-semibold text-ink">flowcook BPM</h1>
          <p className="text-xs text-ink-muted mt-1">員工流程平台</p>
        </div>

        <form onSubmit={submit} className="space-y-4 rounded-xl border border-rule bg-card p-6 shadow-sm">
          <Field label="Email">
            <Input
              type="email"
              autoComplete="email"
              value={email}
              onChange={e => setEmail(e.target.value)}
              placeholder="wilson@acme.example"
              disabled={busy}
              autoFocus
            />
          </Field>
          <Field label="密碼">
            <Input
              type="password"
              autoComplete="current-password"
              value={password}
              onChange={e => setPassword(e.target.value)}
              placeholder="••••••••"
              disabled={busy}
            />
          </Field>
          {err && (
            <div className="flex items-start gap-2 rounded-md bg-rose-50 px-3 py-2 text-xs text-rose-700">
              <AlertCircle className="h-3.5 w-3.5 mt-0.5 shrink-0" />
              <span>{err}</span>
            </div>
          )}
          <Button type="submit" variant="primary" className="w-full" disabled={busy}>
            {busy ? <Loader2 className="h-4 w-4 animate-spin" /> : <LogIn className="h-4 w-4" />}
            登入
          </Button>

          <div className="relative flex items-center py-2">
            <div className="flex-grow border-t border-rule" />
            <span className="mx-3 text-[10px] uppercase tracking-wider text-ink-faint">或</span>
            <div className="flex-grow border-t border-rule" />
          </div>

          <button
            type="button"
            disabled
            title="Microsoft 登入即將推出（add-sso-oidc）"
            className="w-full inline-flex items-center justify-center gap-2 rounded-md border border-rule bg-slate-50 px-3 py-2 text-xs font-medium text-ink-faint cursor-not-allowed"
          >
            <svg className="h-4 w-4" viewBox="0 0 23 23" fill="none">
              <path d="M11 0h11v11H11z" fill="#7fba00"/>
              <path d="M0 12h11v11H0z" fill="#00a4ef"/>
              <path d="M0 0h11v11H0z" fill="#f25022"/>
              <path d="M12 12h11v11H12z" fill="#ffb900"/>
            </svg>
            使用 Microsoft 帳號登入（即將推出）
          </button>

          <p className="text-center text-[11px] text-ink-faint pt-2">
            Demo · <span className="font-mono">{DEMO_EMAIL}</span> / <span className="font-mono">{DEMO_PASSWORD}</span>
          </p>
          <p className="text-center text-[11px] text-ink-faint">
            忘記密碼？請聯絡 <a href="mailto:support@flowcook.example" className="text-primary hover:underline">support@flowcook</a>
          </p>
        </form>
      </div>
    </div>
  )
}
