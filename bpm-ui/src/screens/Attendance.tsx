import { useEffect, useState } from 'react'
import { Clock, Loader2, FileEdit, LogIn, LogOut, RotateCcw } from 'lucide-react'

import { SectionCard, SectionTitle } from '@/components/ui/card'
import { CaseCard, CaseCardList } from '@/components/ui/case-card'
import { Button } from '@/components/ui/button'
import { Modal } from '@/components/ui/Modal'
import { Input, Textarea, Field, Select } from '@/components/ui/form'
import { checkIn, checkOut, getHistory, getMyCorrections, getToday, submitCorrection } from '@/lib/api/attendance'
import {
  CorrectionStatus, PunchType, TodayState,
  type CorrectionDto, type DailySummaryDto, type PunchTypeValue, type TodayStatusDto,
} from '@/types/attendance'

export function Attendance() {
  const [today, setToday] = useState<TodayStatusDto | null>(null)
  const [history, setHistory] = useState<DailySummaryDto[]>([])
  const [corrections, setCorrections] = useState<CorrectionDto[]>([])
  const [busy, setBusy] = useState(false)
  const [err, setErr] = useState<string | null>(null)
  const [toast, setToast] = useState<string | null>(null)
  const [correctionOpen, setCorrectionOpen] = useState(false)

  const fireToast = (m: string) => { setToast(m); setTimeout(() => setToast(null), 2400) }

  async function refresh() {
    setErr(null)
    try {
      const [t, h, c] = await Promise.all([getToday(), getHistory(30), getMyCorrections()])
      setToday(t)
      setHistory(h)
      setCorrections(c)
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

      <div className="flex flex-wrap items-center justify-between gap-2">
        <div>
          <h1 className="text-xl font-bold text-ink">Attendance / 打卡</h1>
          <p className="text-[11px] uppercase tracking-wider text-ink-muted">Daily check-in / check-out · Tenant TZ: Asia/Taipei</p>
        </div>
        <Button variant="outline" size="sm" onClick={() => setCorrectionOpen(true)}>
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
            <div className="grid grid-cols-1 gap-3 md:grid-cols-3 md:gap-4">
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
        <SectionTitle>Correction Requests / 我的補卡申請</SectionTitle>
        {corrections.length === 0 ? (
          <div className="px-5 py-4 text-sm text-ink-muted">No correction requests. 忘了打卡時從右上角「申請補打卡」送出，主管核准後紀錄自動補上。</div>
        ) : (<>
          {/* Mobile cards — same rows as the table below */}
          <CaseCardList className="p-3 md:hidden">
            {corrections.map(c => (
              <CaseCard
                key={c.id}
                top={<>
                  <span className="text-xs font-medium text-ink">{c.punchType === PunchType.In ? '上班卡' : '下班卡'}</span>
                  <CorrectionChip status={c.status} />
                </>}
                title={c.reason}
                meta={<>
                  <span>{c.date}</span>
                  <span>{formatTime(c.requestedPunchAt)}</span>
                  {c.deciderName && <span>審核 {c.deciderName}{c.decisionNote ? `（${c.decisionNote}）` : ''}</span>}
                </>}
              />
            ))}
          </CaseCardList>

          <div className="hidden overflow-x-auto md:block">
            <table className="w-full text-sm">
              <thead className="border-b border-rule bg-slate-50 text-[11px] uppercase tracking-wider text-ink-muted">
                <tr>
                  <th className="px-4 py-2 text-left">Date / 日期</th>
                  <th className="px-4 py-2 text-left">Type</th>
                  <th className="px-4 py-2 text-left">Time / 補卡時間</th>
                  <th className="px-4 py-2 text-left">Reason / 事由</th>
                  <th className="px-4 py-2 text-left">Status / 狀態</th>
                  <th className="px-4 py-2 text-left">Reviewer / 審核</th>
                </tr>
              </thead>
              <tbody>
                {corrections.map(c => (
                  <tr key={c.id} className="border-b border-rule last:border-b-0">
                    <td className="px-4 py-2 font-mono">{c.date}</td>
                    <td className="px-4 py-2">{c.punchType === PunchType.In ? '上班卡' : '下班卡'}</td>
                    <td className="px-4 py-2 font-mono">{formatTime(c.requestedPunchAt)}</td>
                    <td className="max-w-[280px] truncate px-4 py-2" title={c.reason}>{c.reason}</td>
                    <td className="px-4 py-2"><CorrectionChip status={c.status} /></td>
                    <td className="px-4 py-2 text-ink-muted">{c.deciderName ?? '—'}{c.decisionNote ? `（${c.decisionNote}）` : ''}</td>
                  </tr>
                ))}
              </tbody>
            </table>
          </div>
        </>)}
      </SectionCard>

      <CorrectionDialog
        open={correctionOpen}
        onClose={() => setCorrectionOpen(false)}
        onSubmitted={async () => { setCorrectionOpen(false); fireToast('補卡申請已送出，等待主管核准。'); await refresh() }}
      />

      <SectionCard>
        <SectionTitle>History / 近 30 天紀錄</SectionTitle>
        {history.length === 0 ? (
          <div className="px-5 py-6 text-sm text-ink-muted">No punches in the last 30 days.</div>
        ) : (<>
          {/* Mobile cards — same rows as the table below */}
          <CaseCardList className="p-3 md:hidden">
            {history.map(d => (
              <CaseCard
                key={d.date}
                title={<span className="font-mono">{d.date}</span>}
                meta={<>
                  <span>In {d.firstIn ? formatTime(d.firstIn) : '—'}</span>
                  <span>Out {d.lastOut ? formatTime(d.lastOut) : '—'}</span>
                  <span>{d.workHours.toFixed(2)} hr</span>
                  <span>{d.punchCount} punches</span>
                </>}
              />
            ))}
          </CaseCardList>

          <div className="hidden overflow-x-auto md:block">
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
        </>)}
      </SectionCard>
    </div>
  )
}

function CorrectionChip({ status }: { status: number }) {
  const map: Record<number, { label: string; cls: string }> = {
    [CorrectionStatus.Pending]:  { label: '待主管核准', cls: 'bg-amber-50 text-amber-700 border-amber-200' },
    [CorrectionStatus.Approved]: { label: '已核准補卡', cls: 'bg-green-50 text-green-700 border-green-200' },
    [CorrectionStatus.Rejected]: { label: '已駁回', cls: 'bg-red-50 text-red-700 border-red-200' },
  }
  const m = map[status] ?? { label: String(status), cls: 'bg-slate-50 text-ink border-slate-200' }
  return <span className={`inline-block rounded-full border px-2 py-0.5 text-[11px] ${m.cls}`}>{m.label}</span>
}

function CorrectionDialog({ open, onClose, onSubmitted }: { open: boolean; onClose: () => void; onSubmitted: () => Promise<void> }) {
  const [date, setDate] = useState(() => new Date().toISOString().slice(0, 10))
  const [punchType, setPunchType] = useState<PunchTypeValue>(PunchType.In)
  const [time, setTime] = useState('09:00')
  const [reason, setReason] = useState('')
  const [saving, setSaving] = useState(false)
  const [error, setError] = useState<string | null>(null)

  useEffect(() => {
    if (!open) {
      setDate(new Date().toISOString().slice(0, 10)); setPunchType(PunchType.In); setTime('09:00'); setReason(''); setSaving(false); setError(null)
    }
  }, [open])

  async function save() {
    if (!reason.trim()) { setError('補卡事由為必填'); return }
    setSaving(true)
    setError(null)
    try {
      await submitCorrection({ date, punchType, time, reason: reason.trim() })
      await onSubmitted()
    } catch (e) {
      setError(e instanceof Error ? e.message : 'Failed')
      setSaving(false)
    }
  }

  return (
    <Modal open={open} onClose={onClose} ariaLabelledBy="correction-title">
      <div className="space-y-3 p-4">
        <div className="flex items-center gap-2">
          <FileEdit className="h-4 w-4 text-primary" />
          <h2 id="correction-title" className="text-sm font-semibold text-ink">申請補打卡 / Request Correction</h2>
        </div>
        <p className="text-xs text-ink-muted">送出後由你的直屬主管審核，核准後系統自動補上該筆打卡紀錄。</p>
        <div className="grid grid-cols-2 gap-3">
          <Field label="日期 / Date" required>
            <Input type="date" value={date} onChange={e => setDate(e.target.value)} />
          </Field>
          <Field label="卡別 / Type" required>
            <Select value={String(punchType)} onChange={e => setPunchType(Number(e.target.value) as PunchTypeValue)}>
              <option value={PunchType.In}>上班卡 / Check-in</option>
              <option value={PunchType.Out}>下班卡 / Check-out</option>
            </Select>
          </Field>
        </div>
        <Field label="時間 / Time" required hint="以台北時區填寫實際上下班時間">
          <Input type="time" value={time} onChange={e => setTime(e.target.value)} />
        </Field>
        <Field label="事由 / Reason" required>
          <Textarea rows={3} value={reason} onChange={e => setReason(e.target.value)} placeholder="e.g. 早上外出拜訪客戶，直接到客戶端未打卡" />
        </Field>
        {error && <p className="rounded border border-red-200 bg-red-50 px-3 py-2 text-xs text-red-700">{error}</p>}
        <div className="flex justify-end gap-2 border-t border-rule pt-3">
          <Button variant="outline" size="sm" onClick={onClose} disabled={saving}>Cancel</Button>
          <Button variant="primary" size="sm" onClick={save} disabled={saving || !reason.trim()}>
            {saving && <Loader2 className="h-3.5 w-3.5 animate-spin" />}
            送出申請
          </Button>
        </div>
      </div>
    </Modal>
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

