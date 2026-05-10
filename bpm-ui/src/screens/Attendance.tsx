import { useEffect, useState } from 'react'
import { Clock, Loader2, FileEdit, LogIn, LogOut, RotateCcw } from 'lucide-react'

import { SectionCard, SectionTitle } from '@/components/ui/card'
import { Button } from '@/components/ui/button'
import { checkIn, checkOut, getHistory, getToday } from '@/lib/api/attendance'
import { TodayState, type DailySummaryDto, type TodayStatusDto } from '@/types/attendance'

export function Attendance() {
  const [today, setToday] = useState<TodayStatusDto | null>(null)
  const [history, setHistory] = useState<DailySummaryDto[]>([])
  const [busy, setBusy] = useState(false)
  const [err, setErr] = useState<string | null>(null)
  const [toast, setToast] = useState<string | null>(null)

  const fireToast = (m: string) => { setToast(m); setTimeout(() => setToast(null), 2400) }

  async function refresh() {
    setErr(null)
    try {
      const [t, h] = await Promise.all([getToday(), getHistory(30)])
      setToday(t)
      setHistory(h)
    } catch (e) {
      setErr((e as Error).message)
    }
  }

  useEffect(() => {
    refresh()
  }, [])

  async function onPunch(direction: 'in' | 'out') {
    setBusy(true)
    try {
      if (direction === 'in') await checkIn(); else await checkOut()
      await refresh()
      fireToast(direction === 'in' ? 'Checked in.' : 'Checked out.')
    } catch (e) {
      fireToast(`Failed: ${(e as Error).message}`)
    } finally {
      setBusy(false)
    }
  }

  if (err && !today) {
    return (
      <div className="mx-auto max-w-screen-xl px-4 py-6">
        <SectionCard>
          <div className="px-5 py-4 text-sm text-danger">Failed to load attendance: {err}</div>
        </SectionCard>
      </div>
    )
  }

  return (
    <div className="mx-auto max-w-screen-xl space-y-4 px-4 py-6">
      {toast && (
        <div className="fixed bottom-6 right-6 z-50 rounded-lg bg-slate-800 px-4 py-2.5 text-sm text-white shadow-2xl">
          {toast}
        </div>
      )}

      <div className="flex items-center justify-between">
        <div>
          <h1 className="text-xl font-bold text-ink">Attendance / 打卡</h1>
          <p className="text-[11px] uppercase tracking-wider text-ink-muted">Daily check-in / check-out · Tenant TZ: Asia/Taipei</p>
        </div>
        <Button variant="outline" size="sm" onClick={() => alert('Request Correction — placeholder. Separate change pending.')}>
          <FileEdit className="h-3.5 w-3.5" />
          Request Correction / 申請補打卡
        </Button>
      </div>

      <SectionCard>
        <SectionTitle>
          <span className="inline-flex items-center gap-2"><Clock className="h-4 w-4 text-primary" /> Today / 今日</span>
        </SectionTitle>
        {today ? (
          <div className="space-y-4 p-5">
            <div className="grid grid-cols-3 gap-4">
              <StatusTile label="Status / 狀態" value={statusLabel(today.status)} tone={statusTone(today.status)} />
              <StatusTile label="Work hours / 累計工時" value={`${today.workHours.toFixed(2)} hr`} tone={today.inProgress ? 'amber' : 'blue'} note={today.inProgress ? 'in progress…' : undefined} />
              <StatusTile label="Punches today / 今日打卡次數" value={String(today.punches.length)} tone="slate" />
            </div>

            <div className="grid grid-cols-2 gap-4 border-t border-rule pt-4">
              <InfoLine label="Most recent Check-in" value={today.lastInAt ? formatTime(today.lastInAt) : '—'} />
              <InfoLine label="Most recent Check-out" value={today.lastOutAt ? formatTime(today.lastOutAt) : '—'} />
            </div>

            <div className="flex justify-end gap-2 border-t border-rule pt-4">
              {primaryAction(today.status, busy, onPunch)}
            </div>
          </div>
        ) : (
          <div className="px-5 py-6 text-sm text-ink-muted">Loading…</div>
        )}
      </SectionCard>

      <SectionCard>
        <SectionTitle>History / 近 30 天紀錄</SectionTitle>
        {history.length === 0 ? (
          <div className="px-5 py-6 text-sm text-ink-muted">No punches in the last 30 days.</div>
        ) : (
          <div className="overflow-x-auto">
            <table className="w-full text-sm">
              <thead className="border-b border-rule bg-slate-50 text-[11px] uppercase tracking-wider text-ink-muted">
                <tr>
                  <th className="px-4 py-2 text-left">Date / 日期</th>
                  <th className="px-4 py-2 text-left">First In</th>
                  <th className="px-4 py-2 text-left">Last Out</th>
                  <th className="px-4 py-2 text-right">Hours / 工時</th>
                  <th className="px-4 py-2 text-right">Punches</th>
                </tr>
              </thead>
              <tbody>
                {history.map(d => (
                  <tr key={d.date} className="border-b border-rule last:border-b-0">
                    <td className="px-4 py-2 font-mono">{d.date}</td>
                    <td className="px-4 py-2">{d.firstIn ? formatTime(d.firstIn) : '—'}</td>
                    <td className="px-4 py-2">{d.lastOut ? formatTime(d.lastOut) : '—'}</td>
                    <td className="px-4 py-2 text-right font-mono">{d.workHours.toFixed(2)}</td>
                    <td className="px-4 py-2 text-right font-mono">{d.punchCount}</td>
                  </tr>
                ))}
              </tbody>
            </table>
          </div>
        )}
      </SectionCard>
    </div>
  )
}

function primaryAction(status: number, busy: boolean, onPunch: (d: 'in' | 'out') => void) {
  if (status === TodayState.OnDuty) {
    return (
      <Button variant="primary" size="lg" onClick={() => onPunch('out')} disabled={busy}>
        {busy ? <Loader2 className="h-4 w-4 animate-spin" /> : <LogOut className="h-4 w-4" />}
        Check out
      </Button>
    )
  }
  if (status === TodayState.OffDuty) {
    return (
      <Button variant="primary" size="lg" onClick={() => onPunch('in')} disabled={busy}>
        {busy ? <Loader2 className="h-4 w-4 animate-spin" /> : <RotateCcw className="h-4 w-4" />}
        Check in again
      </Button>
    )
  }
  return (
    <Button variant="primary" size="lg" onClick={() => onPunch('in')} disabled={busy}>
      {busy ? <Loader2 className="h-4 w-4 animate-spin" /> : <LogIn className="h-4 w-4" />}
      Check in
    </Button>
  )
}

function StatusTile({ label, value, tone, note }: { label: string; value: string; tone: 'green' | 'blue' | 'amber' | 'slate'; note?: string }) {
  const ring = { green: 'border-green-200 bg-green-50/40', blue: 'border-blue-200 bg-blue-50/40', amber: 'border-amber-200 bg-amber-50/40', slate: 'border-slate-200 bg-slate-50/60' }[tone]
  const text = { green: 'text-green-700', blue: 'text-blue-700', amber: 'text-amber-700', slate: 'text-ink' }[tone]
  return (
    <div className={`rounded-lg border p-4 ${ring}`}>
      <div className="text-[11px] font-semibold uppercase tracking-wider text-ink-muted">{label}</div>
      <div className={`mt-1 text-2xl font-bold tabular ${text}`}>{value}</div>
      {note && <div className="mt-0.5 text-[11px] text-ink-faint">{note}</div>}
    </div>
  )
}

function InfoLine({ label, value }: { label: string; value: string }) {
  return (
    <div className="flex items-baseline justify-between text-sm">
      <span className="text-ink-muted">{label}</span>
      <span className="font-mono text-ink">{value}</span>
    </div>
  )
}

function statusLabel(s: number): string {
  switch (s) {
    case TodayState.NotCheckedIn: return 'Not Checked In'
    case TodayState.OnDuty: return 'On Duty'
    case TodayState.OffDuty: return 'Off Duty'
  }
  return 'Unknown'
}

function statusTone(s: number): 'green' | 'blue' | 'amber' | 'slate' {
  if (s === TodayState.OnDuty) return 'green'
  if (s === TodayState.OffDuty) return 'blue'
  return 'slate'
}

function formatTime(iso: string): string {
  const d = new Date(iso)
  return d.toLocaleString('zh-TW', { hour: '2-digit', minute: '2-digit', month: '2-digit', day: '2-digit' })
}

