import { useEffect, useState } from 'react'
import { Clock, RotateCcw, Forward } from 'lucide-react'
import { getClock, advanceClock, resetClock } from '@/lib/api/sandbox'
import type { SandboxClockDto } from '@/types/sandbox'

export function ClockTab() {
  const [clock, setClock] = useState<SandboxClockDto | null>(null)
  const [error, setError] = useState<string | null>(null)
  const [busy, setBusy] = useState(false)
  const [days, setDays]       = useState('0')
  const [hours, setHours]     = useState('0')
  const [minutes, setMinutes] = useState('0')
  const [seconds, setSeconds] = useState('0')

  async function refresh() {
    try {
      setError(null)
      setClock(await getClock())
    } catch (e) { setError(e instanceof Error ? e.message : String(e)) }
  }

  useEffect(() => { void refresh() }, [])

  async function quickAdvance(parts: { days?: number; hours?: number; minutes?: number; seconds?: number }) {
    setBusy(true)
    try {
      const c = await advanceClock(parts)
      setClock(c)
    } catch (e) { setError(e instanceof Error ? e.message : String(e)) }
    finally { setBusy(false) }
  }

  async function handlePreciseAdvance() {
    const parts = {
      days:    parseInt(days, 10)    || 0,
      hours:   parseInt(hours, 10)   || 0,
      minutes: parseInt(minutes, 10) || 0,
      seconds: parseInt(seconds, 10) || 0,
    }
    await quickAdvance(parts)
  }

  async function handleReset() {
    if (!confirm('Reset sandbox clock offset to 0? This does NOT delete instances.')) return
    setBusy(true)
    try {
      const c = await resetClock()
      setClock(c)
    } catch (e) { setError(e instanceof Error ? e.message : String(e)) }
    finally { setBusy(false) }
  }

  return (
    <div className="space-y-5">
      {error && (
        <div className="rounded border border-rose-200 bg-rose-50 p-2 text-xs text-rose-800">
          {error}
        </div>
      )}

      <section className="rounded border border-rule bg-slate-50 p-4">
        <h3 className="mb-2 flex items-center gap-2 text-sm font-bold text-ink">
          <Clock className="h-4 w-4" /> Current state
        </h3>
        {!clock ? (
          <p className="text-xs text-ink-muted">Loading…</p>
        ) : (
          <div className="grid grid-cols-3 gap-4 text-xs">
            <KV label="Real time" value={formatIso(clock.realNow)} />
            <KV label="Sandbox time" value={formatIso(clock.sandboxNow)} />
            <KV label="Offset" value={formatOffset(clock.offsetSeconds)} />
            <KV label="Sandbox status" value={clock.sandboxOn ? 'ON' : 'OFF'} />
          </div>
        )}
      </section>

      <section className="rounded border border-rule p-4">
        <h3 className="mb-3 text-sm font-bold text-ink">Quick advance</h3>
        <div className="flex flex-wrap gap-2">
          {QUICK.map(q => (
            <button
              key={q.label}
              disabled={busy}
              onClick={() => quickAdvance(q.parts)}
              className="inline-flex items-center gap-1 rounded border border-rule bg-white px-3 py-1.5 text-xs hover:bg-slate-50 disabled:cursor-not-allowed disabled:opacity-40"
            >
              <Forward className="h-3 w-3" /> {q.label}
            </button>
          ))}
        </div>
      </section>

      <section className="rounded border border-rule p-4">
        <h3 className="mb-3 text-sm font-bold text-ink">Precise advance</h3>
        <div className="flex flex-wrap items-end gap-3 text-xs">
          <NumField label="Days"    value={days}    setValue={setDays} />
          <NumField label="Hours"   value={hours}   setValue={setHours} />
          <NumField label="Minutes" value={minutes} setValue={setMinutes} />
          <NumField label="Seconds" value={seconds} setValue={setSeconds} />
          <button
            disabled={busy}
            onClick={handlePreciseAdvance}
            className="rounded bg-amber-500 px-3 py-1.5 text-xs font-semibold text-white shadow hover:bg-amber-600 disabled:cursor-not-allowed disabled:opacity-40"
          >
            Advance
          </button>
        </div>
      </section>

      <section className="rounded border border-rule p-4">
        <h3 className="mb-3 text-sm font-bold text-ink">Reset</h3>
        <button
          disabled={busy}
          onClick={handleReset}
          className="inline-flex items-center gap-1 rounded border border-rose-300 bg-white px-3 py-1.5 text-xs font-semibold text-rose-700 hover:bg-rose-50 disabled:cursor-not-allowed disabled:opacity-40"
        >
          <RotateCcw className="h-3 w-3" /> Reset clock offset to 0
        </button>
        <p className="mt-2 text-[10.5px] text-ink-faint">
          (no audit log in v1) — clock advances are logged at Info level only.
        </p>
      </section>
    </div>
  )
}

const QUICK = [
  { label: '+1h',  parts: { hours: 1 } },
  { label: '+1d',  parts: { days: 1 } },
  { label: '+1w',  parts: { days: 7 } },
  { label: '+1mo', parts: { days: 30 } },
]

function NumField({ label, value, setValue }: { label: string; value: string; setValue: (v: string) => void }) {
  return (
    <label className="flex flex-col gap-0.5 text-[10.5px] font-semibold uppercase tracking-wider text-ink-faint">
      {label}
      <input
        type="number"
        value={value}
        onChange={e => setValue(e.target.value)}
        className="h-8 w-24 rounded border border-rule px-2 text-xs font-normal normal-case text-ink"
      />
    </label>
  )
}

function KV({ label, value }: { label: string; value: string }) {
  return (
    <div>
      <p className="text-[10.5px] font-semibold uppercase tracking-wider text-ink-faint">{label}</p>
      <p className="font-mono text-[11.5px] text-ink">{value}</p>
    </div>
  )
}

function formatIso(s: string): string {
  return new Date(s).toISOString().replace('T', ' ').replace(/\.\d+Z$/, 'Z')
}

function formatOffset(seconds: number): string {
  if (seconds === 0) return '+0s'
  const sign = seconds >= 0 ? '+' : '-'
  const abs = Math.abs(seconds)
  const days  = Math.floor(abs / 86_400)
  const hours = Math.floor((abs % 86_400) / 3_600)
  const mins  = Math.floor((abs % 3_600) / 60)
  const secs  = abs % 60
  const parts: string[] = []
  if (days)  parts.push(`${days}d`)
  if (hours) parts.push(`${hours}h`)
  if (mins)  parts.push(`${mins}m`)
  if (!parts.length) parts.push(`${secs}s`)
  return sign + parts.join(' ')
}
