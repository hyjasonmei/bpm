import { useState } from 'react'
import { ArrowRight } from 'lucide-react'
import { useAuth } from '@/flowcook/auth/useAuth'

export function LoginPage() {
  const { login } = useAuth()
  const [username, setUsername] = useState('alice@acme.example')
  const [password, setPassword] = useState('flowcook2026')
  const [submitting, setSubmitting] = useState(false)
  const [error, setError] = useState<string | null>(null)

  async function onSubmit(e: React.FormEvent) {
    e.preventDefault()
    setSubmitting(true)
    setError(null)
    try {
      await login(username, password)
    } catch {
      setError('Email or password not recognised.')
    } finally {
      setSubmitting(false)
    }
  }

  return (
    <div className="relative flex min-h-screen overflow-hidden bg-bg text-ink">
      {/* decorative left panel — dark navy, matches bpm-ui top header */}
      <aside className="relative hidden w-1/2 flex-col justify-between bg-header p-10 text-white lg:flex">
        <div className="relative z-10 flex items-center gap-2.5">
          <div className="flex h-8 w-8 items-center justify-center rounded bg-red-500 text-[10.5px] font-bold tracking-wider text-white">
            BPM
          </div>
          <span className="text-sm font-bold tracking-wide">
            flowcook · admin
          </span>
        </div>

        <div className="relative z-10 max-w-md">
          <div className="mb-4 font-mono text-[10px] tracking-[0.14em] uppercase text-white/55">
            a flowcook brief
          </div>
          <h2 className="text-4xl font-bold leading-[1.1] text-white">
            Design workflows. Cook them with AI. Ship without surprises.
          </h2>
          <p className="mt-6 max-w-sm text-sm leading-relaxed text-white/70">
            flowcook turns operations into specs: clients prep in AI&nbsp;Kitchen,
            chef cooks them into runnable workflows, the bpm runtime serves them
            daily.
          </p>
        </div>

        <div className="relative z-10 flex items-baseline gap-3 font-mono text-[10px] tracking-[0.14em] uppercase text-white/45">
          <span>est. 2026</span>
          <span className="h-px flex-1 bg-white/15"></span>
          <span>admin · v0</span>
        </div>
      </aside>

      {/* form panel */}
      <main className="relative flex flex-1 items-center justify-center p-8">
        <form
          onSubmit={onSubmit}
          className="relative w-full max-w-sm rounded-lg border border-rule bg-card p-8 shadow-sm"
        >
          <div className="mb-6 flex items-center gap-2 lg:hidden">
            <div className="flex h-7 w-7 items-center justify-center rounded bg-red-500 text-[10px] font-bold tracking-wider text-white">
              BPM
            </div>
            <span className="text-sm font-bold tracking-wide text-ink">
              flowcook · admin
            </span>
          </div>

          <div className="mb-2 font-mono text-[10px] tracking-[0.14em] uppercase text-ink-muted">
            sign in
          </div>
          <h1 className="text-2xl font-bold leading-tight text-ink">
            Welcome back.
          </h1>
          <p className="mt-2 text-sm text-ink-muted">
            Please identify yourself to continue.
          </p>

          <div className="mt-6 space-y-4">
            <Field
              label="Email"
              value={username}
              onChange={setUsername}
              type="email"
              autoComplete="username"
              autoFocus
            />
            <Field
              label="Password"
              value={password}
              onChange={setPassword}
              type="password"
              autoComplete="current-password"
            />
          </div>

          {error && (
            <p className="mt-4 rounded border border-danger/30 bg-danger/5 px-3 py-2 text-xs text-danger">
              {error}
            </p>
          )}

          <button
            type="submit"
            disabled={submitting}
            className="group mt-6 flex w-full items-center justify-center gap-2 rounded bg-primary px-3 py-2.5 text-sm font-medium text-white transition-colors hover:bg-primary/90 disabled:opacity-60"
          >
            <span>{submitting ? 'Signing in…' : 'Sign in'}</span>
            <ArrowRight className="h-4 w-4 transition-transform group-hover:translate-x-0.5" />
          </button>

          <p className="mt-6 border-t border-rule pt-4 text-center text-xs text-ink-muted">
            Demo · <code className="font-mono text-ink">alice@acme.example</code>{' '}
            / <code className="font-mono text-ink">flowcook2026</code>
          </p>
        </form>
      </main>
    </div>
  )
}

interface FieldProps {
  label: string
  value: string
  onChange: (v: string) => void
  type?: 'text' | 'email' | 'password'
  autoComplete?: string
  autoFocus?: boolean
}

function Field({ label, value, onChange, type = 'text', autoComplete, autoFocus }: FieldProps) {
  return (
    <label className="block">
      <span className="mb-1.5 block font-mono text-[10px] tracking-[0.14em] uppercase text-ink-muted">
        {label}
      </span>
      <input
        type={type}
        value={value}
        onChange={(e) => onChange(e.target.value)}
        autoComplete={autoComplete}
        autoFocus={autoFocus}
        className="block w-full rounded border border-rule bg-white px-3 py-2 text-sm text-ink outline-none transition-colors placeholder:text-ink-faint focus:border-primary focus:ring-2 focus:ring-primary/20"
      />
    </label>
  )
}
