import { Fragment, useCallback, useEffect, useState } from 'react'
import {
  FlaskConical, Clock, UserCog, Mailbox, Layers, RefreshCw, Loader2,
  ChevronRight, ChevronDown, ExternalLink, Trash2, AlertTriangle,
} from 'lucide-react'
import { cn } from '@/lib/cn'
import { SectionCard, SectionTitle } from '@/components/ui/card'
import { Button } from '@/components/ui/button'
import { Input, Select } from '@/components/ui/form'
import { Badge } from '@/components/ui/badge'
import { ConfirmDialog } from '@/components/ui/confirm-dialog'
import {
  getSandboxStatus, setSandboxStatus,
  getSandboxClock, advanceSandboxClock, resetSandboxClock,
  listSandboxPersonas, mintPersonaToken,
  listCaptured, getCaptured,
  listFlowSandbox, setFlowCapture,
  resetAll, resetFlow,
  type SandboxStatus, type SandboxClock, type SandboxPersona,
  type FlowSandboxState, type CapturedSummary, type CapturedDetail,
} from '@/flowcook/api/sandbox'

// POC: bpm-ui dev origin. Prod wires this from config alongside the /bpmsvc bridge.
const BPM_UI_URL = 'http://localhost:5173'

export function SandboxPage() {
  const [status, setStatus] = useState<SandboxStatus | null>(null)
  const [clock, setClock] = useState<SandboxClock | null>(null)
  const [flows, setFlows] = useState<FlowSandboxState[]>([])
  const [personas, setPersonas] = useState<SandboxPersona[]>([])
  const [loading, setLoading] = useState(true)
  const [err, setErr] = useState<string | null>(null)
  const [busy, setBusy] = useState<string | null>(null)

  const loadCore = useCallback(async () => {
    const [s, c, f] = await Promise.all([getSandboxStatus(), getSandboxClock(), listFlowSandbox()])
    setStatus(s); setClock(c); setFlows(f)
  }, [])

  const reload = useCallback(async () => {
    setLoading(true); setErr(null)
    try {
      await loadCore()
      setPersonas(await listSandboxPersonas())
    } catch (e) {
      setErr(e instanceof Error ? e.message : 'load failed')
    } finally {
      setLoading(false)
    }
  }, [loadCore])

  useEffect(() => { void reload() }, [reload])

  const on = !!status?.enabled

  async function run(key: string, fn: () => Promise<unknown>) {
    setBusy(key); setErr(null)
    try { await fn(); await loadCore() }
    catch (e) { setErr(e instanceof Error ? e.message : 'action failed') }
    finally { setBusy(null) }
  }

  if (loading && !status) {
    return <div className="flex items-center gap-2 p-8 text-sm text-ink-faint"><Loader2 className="h-4 w-4 animate-spin" /> 載入中…</div>
  }

  return (
    <div className="space-y-4">
      {/* Header / master toggle */}
      <SectionCard>
        <div className="flex flex-wrap items-center justify-between gap-4 p-4">
          <div className="flex items-center gap-3">
            <div className={cn('flex h-10 w-10 items-center justify-center rounded-lg', on ? 'bg-primary/10 text-primary' : 'bg-slate-100 text-ink-faint')}>
              <FlaskConical className="h-5 w-5" />
            </div>
            <div>
              <h1 className="text-lg font-bold text-ink">Sandbox</h1>
              <p className="text-sm text-ink-muted">
                驗收環境：攔信 · persona · 時間快轉 · 清空重跑
              </p>
            </div>
          </div>
          <div className="flex items-center gap-4">
            {clock && <ClockReadout clock={clock} />}
            <div className="flex items-center gap-2">
              <span className={cn('text-sm font-semibold', on ? 'text-primary' : 'text-ink-faint')}>
                {on ? '全域 ON' : '全域 OFF'}
              </span>
              <Switch on={on} disabled={busy === 'status'} onChange={v => run('status', () => setSandboxStatus(v))} />
            </div>
          </div>
        </div>
      </SectionCard>

      {err && (
        <div className="flex items-center gap-2 rounded-md border border-red-200 bg-red-50 px-3 py-2 text-sm text-danger">
          <AlertTriangle className="h-4 w-4" /> {err}
        </div>
      )}

      {!on && (
        <div className="rounded-md border border-amber-200 bg-amber-50 px-3 py-2 text-xs text-amber-700">
          全域 sandbox 關閉中。時間快轉 / persona / 全部清空需要先開全域；逐流程攔信可單獨開（下方）。
        </div>
      )}

      <ClockCard clock={clock} on={on} busy={busy} run={run} />
      <PersonaCard personas={personas} on={on} setErr={setErr} />
      <MailboxCard flows={flows} />
      <FlowsCard flows={flows} on={on} busy={busy} run={run} />
    </div>
  )
}

// ---------------- Clock ----------------

function ClockReadout({ clock }: { clock: SandboxClock }) {
  return (
    <div className="text-right text-xs leading-tight">
      <div className="font-mono text-ink">{fmt(clock.sandboxNow)}</div>
      <div className="text-ink-faint">沙箱時間 · {humanizeOffset(clock.offsetSeconds)}</div>
    </div>
  )
}

function ClockCard({ clock, on, busy, run }: {
  clock: SandboxClock | null; on: boolean; busy: string | null
  run: (k: string, fn: () => Promise<unknown>) => Promise<void>
}) {
  const [d, setD] = useState(0)
  const [h, setH] = useState(0)
  const [m, setM] = useState(0)
  const quick = (days: number, hours = 0, minutes = 0) =>
    run('clock', () => advanceSandboxClock({ days, hours, minutes }))

  return (
    <SectionCard>
      <SectionTitle right={clock && <span className="font-mono text-xs text-ink-faint">真實 {fmt(clock.realNow)}</span>}>
        <span className="inline-flex items-center gap-2"><Clock className="h-4 w-4" /> 時間快轉</span>
      </SectionTitle>
      <div className="space-y-3 p-4">
        <div className="flex flex-wrap items-center gap-2">
          <Button variant="outline" disabled={!on || busy === 'clock'} onClick={() => quick(1)}>+1 天</Button>
          <Button variant="outline" disabled={!on || busy === 'clock'} onClick={() => quick(7)}>+7 天</Button>
          <Button variant="outline" disabled={!on || busy === 'clock'} onClick={() => quick(0, 1)}>+1 時</Button>
          <Button variant="outline" disabled={!on || busy === 'clock'} onClick={() => quick(0, 0, 15)}>+15 分</Button>
          <span className="mx-1 h-5 w-px bg-rule" />
          <Button variant="ghost" disabled={!on || busy === 'clock'} onClick={() => run('clock', () => resetSandboxClock())}>
            歸零
          </Button>
        </div>
        <div className="flex flex-wrap items-end gap-2">
          <NumField label="天" value={d} onChange={setD} />
          <NumField label="時" value={h} onChange={setH} />
          <NumField label="分" value={m} onChange={setM} />
          <Button variant="primary" disabled={!on || busy === 'clock' || (d === 0 && h === 0 && m === 0)}
            onClick={() => run('clock', () => advanceSandboxClock({ days: d, hours: h, minutes: m }))}>
            {busy === 'clock' ? <Loader2 className="h-4 w-4 animate-spin" /> : '快轉'}
          </Button>
        </div>
      </div>
    </SectionCard>
  )
}

// ---------------- Persona ----------------

function PersonaCard({ personas, on, setErr }: {
  personas: SandboxPersona[]; on: boolean
  setErr: (s: string | null) => void
}) {
  const [opening, setOpening] = useState<string | null>(null)
  async function openAs(p: SandboxPersona) {
    setOpening(p.id); setErr(null)
    try {
      const res = await mintPersonaToken(p.id)
      window.open(`${BPM_UI_URL}/?sandboxToken=${encodeURIComponent(res.token)}`, '_blank', 'noopener')
    } catch (e) {
      setErr(e instanceof Error ? e.message : 'persona failed')
    } finally {
      setOpening(null)
    }
  }
  return (
    <SectionCard>
      <SectionTitle right={<span className="text-xs text-ink-faint">{personas.length} 人</span>}>
        <span className="inline-flex items-center gap-2"><UserCog className="h-4 w-4" /> Persona 切換</span>
      </SectionTitle>
      <div className="p-4">
        {!on && <p className="mb-2 text-xs text-amber-700">需先開全域 sandbox 才能產生 persona session。</p>}
        <div className="grid grid-cols-1 gap-2 sm:grid-cols-2 lg:grid-cols-3">
          {personas.map(p => (
            <div key={p.id} className="flex items-center justify-between gap-2 rounded-md border border-rule bg-card px-3 py-2">
              <div className="min-w-0">
                <div className="truncate text-sm font-medium text-ink">{p.fullName}</div>
                <div className="truncate text-xs text-ink-faint">{p.email}</div>
              </div>
              <Button variant="outline" size="xs" disabled={!on || opening === p.id} onClick={() => openAs(p)}>
                {opening === p.id ? <Loader2 className="h-3.5 w-3.5 animate-spin" /> : <><ExternalLink className="h-3.5 w-3.5" /> 開 BPM</>}
              </Button>
            </div>
          ))}
          {personas.length === 0 && <p className="text-sm text-ink-faint">沒有可用人員。</p>}
        </div>
      </div>
    </SectionCard>
  )
}

// ---------------- Mailbox ----------------

function MailboxCard({ flows }: { flows: FlowSandboxState[] }) {
  const [rows, setRows] = useState<CapturedSummary[]>([])
  const [flowCode, setFlowCode] = useState('')
  const [loading, setLoading] = useState(true)
  const [expanded, setExpanded] = useState<string | null>(null)
  const [detail, setDetail] = useState<Record<string, CapturedDetail>>({})

  const load = useCallback(async () => {
    setLoading(true)
    try { setRows(await listCaptured({ flowCode: flowCode || undefined })) }
    finally { setLoading(false) }
  }, [flowCode])
  useEffect(() => { void load() }, [load])

  async function toggle(id: string) {
    if (expanded === id) { setExpanded(null); return }
    setExpanded(id)
    if (!detail[id]) {
      try { const d = await getCaptured(id); setDetail(prev => ({ ...prev, [id]: d })) } catch { /* ignore */ }
    }
  }

  return (
    <SectionCard>
      <SectionTitle right={
        <div className="flex items-center gap-2">
          <Select value={flowCode} onChange={e => setFlowCode(e.target.value)} className="h-7 text-xs">
            <option value="">全部流程</option>
            {flows.map(f => <option key={f.flowCode} value={f.flowCode}>{f.flowCode}</option>)}
          </Select>
          <button onClick={() => void load()} className="text-ink-faint hover:text-primary"><RefreshCw className="h-3.5 w-3.5" /></button>
        </div>
      }>
        <span className="inline-flex items-center gap-2"><Mailbox className="h-4 w-4" /> 攔信信箱 <span className="text-xs font-normal text-ink-faint">({rows.length})</span></span>
      </SectionTitle>
      <div className="overflow-x-auto">
        <table className="w-full text-sm">
          <tbody>
            {loading && rows.length === 0 ? (
              <tr><td className="px-4 py-8 text-center text-sm text-ink-faint">載入中…</td></tr>
            ) : rows.length === 0 ? (
              <tr><td className="px-4 py-8 text-center text-sm text-ink-faint">沒有攔截到的通知。流程在 sandbox 開啟時發出的通知會出現在這裡。</td></tr>
            ) : rows.map(r => {
              const open = expanded === r.id
              const d = detail[r.id]
              return (
                <Fragment key={r.id}>
                  <tr onClick={() => void toggle(r.id)} className={cn('cursor-pointer border-b border-slate-100 hover:bg-slate-50/60', open && 'bg-slate-50/60')}>
                    <td className="w-6 pl-4 text-ink-faint">{open ? <ChevronDown className="h-3.5 w-3.5" /> : <ChevronRight className="h-3.5 w-3.5" />}</td>
                    <td className="py-2.5 pr-2"><Badge tone="info">{r.flowCode ?? '—'}</Badge></td>
                    <td className="py-2.5 pr-2 font-medium text-ink">{r.subject ?? '(無主旨)'}</td>
                    <td className="py-2.5 pr-2 text-xs text-ink-muted">{r.channel}</td>
                    <td className="py-2.5 pr-4 text-right font-mono text-[11px] text-ink-faint">{fmt(r.capturedAt)}</td>
                  </tr>
                  {open && (
                    <tr className="border-b border-slate-100 bg-slate-50/40">
                      <td colSpan={5} className="px-6 py-3 text-sm">
                        {!d ? <span className="text-ink-faint">載入內容…</span> : (
                          <div className="space-y-2">
                            <div className="text-xs text-ink-muted"><span className="font-semibold">收件人：</span>{d.intendedRecipients.join(', ') || '—'}</div>
                            <div className="whitespace-pre-wrap rounded border border-rule bg-card px-3 py-2 text-xs text-ink">{d.bodyText || d.bodyHtml || d.body || '(無內文)'}</div>
                            {d.originatingNotificationId && <div className="font-mono text-[10px] text-ink-faint">{d.originatingNotificationId}</div>}
                          </div>
                        )}
                      </td>
                    </tr>
                  )}
                </Fragment>
              )
            })}
          </tbody>
        </table>
      </div>
    </SectionCard>
  )
}

// ---------------- Per-flow + reset ----------------

function FlowsCard({ flows, on, busy, run }: {
  flows: FlowSandboxState[]; on: boolean; busy: string | null
  run: (k: string, fn: () => Promise<unknown>) => Promise<void>
}) {
  const [confirm, setConfirm] = useState<{ kind: 'all' } | { kind: 'flow'; code: string } | null>(null)

  return (
    <SectionCard>
      <SectionTitle right={
        <Button variant="destructive" size="xs" disabled={!on || busy?.startsWith('reset')} onClick={() => setConfirm({ kind: 'all' })}>
          <Trash2 className="h-3.5 w-3.5" /> 全部清空
        </Button>
      }>
        <span className="inline-flex items-center gap-2"><Layers className="h-4 w-4" /> 逐流程</span>
      </SectionTitle>
      <div className="overflow-x-auto">
        <table className="w-full text-sm">
          <thead className="bg-slate-50">
            <tr className="border-b border-rule text-[11px] uppercase tracking-wider text-ink-muted">
              <th className="px-4 py-2 text-left font-semibold">流程</th>
              <th className="px-4 py-2 text-left font-semibold">攔信（此流程）</th>
              <th className="px-4 py-2 text-left font-semibold">生效</th>
              <th className="px-4 py-2 text-right font-semibold">清空</th>
            </tr>
          </thead>
          <tbody>
            {flows.map(f => {
              const effective = on || f.captureEnabled
              return (
                <tr key={f.flowCode} className="border-b border-slate-100">
                  <td className="px-4 py-2.5 font-medium text-ink">{f.flowCode}</td>
                  <td className="px-4 py-2.5">
                    <Switch on={f.captureEnabled} disabled={busy === `flow:${f.flowCode}`}
                      onChange={v => run(`flow:${f.flowCode}`, () => setFlowCapture(f.flowCode, v))} />
                  </td>
                  <td className="px-4 py-2.5">
                    {effective
                      ? <Badge tone="good">攔信中{on && !f.captureEnabled ? '（全域）' : ''}</Badge>
                      : <Badge>關閉</Badge>}
                  </td>
                  <td className="px-4 py-2.5 text-right">
                    <Button variant="ghost" size="xs" disabled={!on || busy?.startsWith('reset')}
                      onClick={() => setConfirm({ kind: 'flow', code: f.flowCode })}>清此流程</Button>
                  </td>
                </tr>
              )
            })}
            {flows.length === 0 && <tr><td colSpan={4} className="px-4 py-8 text-center text-ink-faint">沒有已部署的流程。</td></tr>}
          </tbody>
        </table>
      </div>

      <ConfirmDialog
        open={confirm !== null}
        title={confirm?.kind === 'all' ? 'Reset ALL sandbox cases' : `Reset flow ${confirm?.kind === 'flow' ? confirm.code : ''}`}
        titleZh={confirm?.kind === 'all' ? '清空所有測試案件' : '清空此流程的測試案件'}
        description={confirm?.kind === 'all'
          ? '刪除所有流程的案件 + 攔截通知，並把快轉時間歸零。spec / 組織 / 帳號不受影響。'
          : '刪除此流程的案件 + 它的攔截通知。'}
        confirmText="清空 / Reset"
        onCancel={() => setConfirm(null)}
        onConfirm={() => {
          const c = confirm
          setConfirm(null)
          if (c?.kind === 'all') void run('reset:all', () => resetAll())
          else if (c?.kind === 'flow') void run(`reset:${c.code}`, () => resetFlow(c.code))
        }}
      />
    </SectionCard>
  )
}

// ---------------- small bits ----------------

function Switch({ on, onChange, disabled }: { on: boolean; onChange: (v: boolean) => void; disabled?: boolean }) {
  return (
    <button type="button" disabled={disabled} onClick={() => onChange(!on)}
      className={cn('relative inline-flex h-5 w-9 shrink-0 items-center rounded-full transition-colors',
        on ? 'bg-primary' : 'bg-slate-300', disabled && 'opacity-50')}>
      <span className={cn('inline-block h-4 w-4 transform rounded-full bg-white shadow transition-transform', on ? 'translate-x-4' : 'translate-x-0.5')} />
    </button>
  )
}

function NumField({ label, value, onChange }: { label: string; value: number; onChange: (n: number) => void }) {
  return (
    <label className="flex items-center gap-1.5 text-xs text-ink-muted">
      <Input type="number" min={0} value={value} onChange={e => onChange(Math.max(0, parseInt(e.target.value || '0', 10)))}
        className="h-8 w-16 text-sm" />
      {label}
    </label>
  )
}

function fmt(iso: string): string {
  const d = new Date(iso)
  if (Number.isNaN(d.getTime())) return iso
  const p = (n: number) => String(n).padStart(2, '0')
  return `${p(d.getMonth() + 1)}/${p(d.getDate())} ${p(d.getHours())}:${p(d.getMinutes())}`
}

function humanizeOffset(sec: number): string {
  if (sec === 0) return '即時'
  const sign = sec > 0 ? '+' : '−'
  let s = Math.abs(sec)
  const d = Math.floor(s / 86400); s -= d * 86400
  const h = Math.floor(s / 3600); s -= h * 3600
  const m = Math.floor(s / 60)
  const parts: string[] = []
  if (d) parts.push(`${d}天`)
  if (h) parts.push(`${h}時`)
  if (m) parts.push(`${m}分`)
  return sign + (parts.join(' ') || '0分')
}
