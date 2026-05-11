/**
 * Process Simulator (PR-K3 §4.4) — dry-run a flow against a sample form
 * payload + initiator, surfacing the runtime's own trace, resolved
 * approvers, fired notifications, and final form data.
 *
 * <p>v1 input pane: a generic JSON textarea labelled "Form data (JSON)".
 * The brief calls for `DynamicForm` against the start userTask, but
 * `DynamicForm` lives in `bpm-ui/` and the demo guard forbids cross-package
 * import. The textarea is pragmatic: a wizard author already knows the
 * field IDs and is comfortable typing `{"days":3}`. Live form rendering
 * lands in a future enhancement once `DynamicForm` is extracted into a
 * shared package.</p>
 *
 * <p>v1 trace visualisation: each row is colour-coded by outcome (green /
 * amber / red / gray). Canvas-overlay highlighting on the BPMN editor is
 * deferred — that requires `BpmnEditor` to grow a status overlay prop.</p>
 *
 * <p>Backend contract: `POST /api/admin/process-admin/simulate` returns a
 * `SimulationResultDto` with `success: false` on flow-not-found / runtime
 * error rather than HTTP 4xx. The UI shows the error message inline so
 * the admin can iterate on the form data without losing context.</p>
 */

import { useCallback, useEffect, useMemo, useState } from 'react'
import {
  Play, AlertTriangle, CheckCircle2, ChevronDown, ChevronRight,
  Mail, FileJson, Activity, Loader2,
} from 'lucide-react'
import { Button } from '@/components/ui/button'
import { listDefinitions, simulate } from '@/lib/api/processAdmin'
import type {
  FlowDefinitionDto, SimulationResultDto, SimulationStepDto,
} from '@/lib/api/processAdmin'
import { listSandboxPersonas } from '@/lib/api/sandbox'
import type { SandboxPersonaDto } from '@/types/sandbox'

const DEFAULT_FORM_TEMPLATE = `{
  "leave_type": "特休",
  "days": 3,
  "reason": "family"
}`

export function Simulator() {
  const [defs, setDefs] = useState<FlowDefinitionDto[] | null>(null)
  const [personas, setPersonas] = useState<SandboxPersonaDto[] | null>(null)
  const [loadError, setLoadError] = useState<string | null>(null)

  const [flowCode, setFlowCode] = useState<string>('')
  const [initiatorId, setInitiatorId] = useState<string>('')
  const [formText, setFormText] = useState<string>(DEFAULT_FORM_TEMPLATE)

  const [running, setRunning] = useState(false)
  const [result, setResult] = useState<SimulationResultDto | null>(null)
  const [runError, setRunError] = useState<string | null>(null)

  // Initial load — definitions + personas in parallel.
  useEffect(() => {
    let cancelled = false
    void (async () => {
      try {
        const [d, p] = await Promise.all([
          listDefinitions(),
          listSandboxPersonas().catch(() => [] as SandboxPersonaDto[]),
        ])
        if (cancelled) return
        setDefs(d)
        setPersonas(p)
        // Default to the first definition so the user sees something
        // selected immediately.
        if (d.length > 0 && !flowCode) setFlowCode(d[0].flowCode)
      } catch (e) {
        if (cancelled) return
        setLoadError(e instanceof Error ? e.message : String(e))
      }
    })()
    return () => { cancelled = true }
  // Intentionally only on mount — re-running on flowCode change would
  // clobber the user's selection.
  // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [])

  const handleRun = useCallback(async () => {
    setRunError(null)
    setResult(null)

    let parsedForm: unknown
    try {
      parsedForm = JSON.parse(formText || '{}')
    } catch (e) {
      setRunError(`Form data is not valid JSON: ${e instanceof Error ? e.message : String(e)}`)
      return
    }

    if (!flowCode) {
      setRunError('Pick a flow before running.')
      return
    }

    setRunning(true)
    try {
      const r = await simulate({
        flowCode,
        formData: parsedForm,
        initiatorUserId: initiatorId || null,
      })
      setResult(r)
    } catch (e) {
      setRunError(e instanceof Error ? e.message : String(e))
    } finally {
      setRunning(false)
    }
  }, [flowCode, formText, initiatorId])

  return (
    <div className="space-y-4">
      <header>
        <h2 className="text-base font-semibold text-ink">Simulator</h2>
        <p className="mt-0.5 text-xs text-ink-muted">
          Drive a flow forward against a sample form payload — no
          ProcessInstance / Task / History rows are persisted; notifications
          surface in the trace below without requiring sandbox to be on.
        </p>
      </header>

      {loadError && (
        <div className="rounded-md border border-red-200 bg-red-50 p-3 text-sm text-red-800">
          <AlertTriangle className="mr-2 inline h-4 w-4" />
          Failed to load definitions: {loadError}
        </div>
      )}

      <InputCard
        defs={defs}
        personas={personas}
        flowCode={flowCode}
        setFlowCode={setFlowCode}
        initiatorId={initiatorId}
        setInitiatorId={setInitiatorId}
        formText={formText}
        setFormText={setFormText}
        running={running}
        onRun={handleRun}
      />

      {runError && (
        <div className="rounded-md border border-red-200 bg-red-50 p-3 text-sm text-red-800">
          <AlertTriangle className="mr-2 inline h-4 w-4" /> {runError}
        </div>
      )}

      {result && <ResultPanel result={result} />}
    </div>
  )
}

/* ============================================================ *
 * Input card — flow / initiator / form data + Run button
 * ============================================================ */

interface InputCardProps {
  defs: FlowDefinitionDto[] | null
  personas: SandboxPersonaDto[] | null
  flowCode: string
  setFlowCode: (s: string) => void
  initiatorId: string
  setInitiatorId: (s: string) => void
  formText: string
  setFormText: (s: string) => void
  running: boolean
  onRun: () => void
}

function InputCard(p: InputCardProps) {
  return (
    <div className="space-y-3 rounded-lg border border-rule bg-card p-4">
      <div className="grid gap-3 md:grid-cols-2">
        <label className="block">
          <span className="mb-1 block text-xs font-medium text-ink-muted">Flow</span>
          <select
            value={p.flowCode}
            onChange={(e) => p.setFlowCode(e.target.value)}
            className="w-full rounded border border-rule bg-white px-2 py-1.5 text-sm text-ink"
          >
            {p.defs === null && <option value="">Loading…</option>}
            {p.defs !== null && p.defs.length === 0 && <option value="">(no flows installed)</option>}
            {(p.defs ?? []).map(d => (
              <option key={`${d.source}-${d.flowCode}`} value={d.flowCode}>
                {d.flowCode} (v{d.version}, {d.source})
              </option>
            ))}
          </select>
        </label>
        <label className="block">
          <span className="mb-1 block text-xs font-medium text-ink-muted">
            Initiator <span className="text-ink-faint">(optional — defaults to first user with a manager)</span>
          </span>
          <select
            value={p.initiatorId}
            onChange={(e) => p.setInitiatorId(e.target.value)}
            className="w-full rounded border border-rule bg-white px-2 py-1.5 text-sm text-ink"
          >
            <option value="">(auto)</option>
            {(p.personas ?? []).map(u => (
              <option key={u.id} value={u.id}>{u.fullName} — {u.email}</option>
            ))}
          </select>
        </label>
      </div>

      <label className="block">
        <span className="mb-1 block text-xs font-medium text-ink-muted">
          Form data (JSON) <span className="text-ink-faint">— sample payload for the start userTask</span>
        </span>
        <textarea
          value={p.formText}
          onChange={(e) => p.setFormText(e.target.value)}
          rows={8}
          spellCheck={false}
          className="w-full rounded border border-rule bg-slate-50 px-2 py-1.5 font-mono text-[12px] leading-snug text-ink"
        />
      </label>

      <div className="flex items-center gap-2">
        <Button onClick={p.onRun} disabled={p.running || !p.flowCode} variant="primary" size="sm">
          {p.running ? <Loader2 className="h-3.5 w-3.5 animate-spin" /> : <Play className="h-3.5 w-3.5" />}
          {p.running ? 'Running…' : 'Run simulation'}
        </Button>
        <span className="text-[11px] text-ink-faint">
          Form-renderer preview deferred (DynamicForm lives in bpm-ui).
        </span>
      </div>
    </div>
  )
}

/* ============================================================ *
 * Result panel — status + trace + notifications + final form
 * ============================================================ */

interface ResultPanelProps {
  result: SimulationResultDto
}

function ResultPanel({ result }: ResultPanelProps) {
  return (
    <div className="space-y-4">
      <StatusBanner result={result} />
      <TraceTable steps={result.trace} />
      <NotificationsPanel notifications={result.notifications} />
      <FinalFormPanel data={result.finalFormData} />
    </div>
  )
}

function StatusBanner({ result }: { result: SimulationResultDto }) {
  if (!result.success) {
    return (
      <div className="rounded-md border border-red-200 bg-red-50 p-3 text-sm text-red-800">
        <AlertTriangle className="mr-2 inline h-4 w-4" />
        Simulation failed: {result.error ?? 'unknown error'}
      </div>
    )
  }
  const status = result.finalStatus ?? '(unknown)'
  const tone = status === 'Completed'
    ? 'border-emerald-200 bg-emerald-50 text-emerald-800'
    : status === 'Cancelled' || status === 'Errored'
      ? 'border-red-200 bg-red-50 text-red-800'
      : 'border-amber-200 bg-amber-50 text-amber-800'
  return (
    <div className={`rounded-md border p-3 text-sm ${tone}`}>
      <CheckCircle2 className="mr-2 inline h-4 w-4" />
      Simulation succeeded — final status: <strong>{status}</strong>
    </div>
  )
}

function TraceTable({ steps }: { steps: SimulationStepDto[] }) {
  return (
    <section className="rounded-lg border border-rule bg-card">
      <header className="flex items-center gap-2 border-b border-rule px-3 py-2 text-sm font-semibold text-ink">
        <Activity className="h-4 w-4 text-ink-muted" />
        Trace <span className="text-ink-faint">({steps.length} step{steps.length === 1 ? '' : 's'})</span>
      </header>
      {steps.length === 0 ? (
        <div className="px-3 py-6 text-center text-xs text-ink-faint">
          (no steps recorded)
        </div>
      ) : (
        <table className="w-full text-sm">
          <thead className="bg-slate-50 text-[11px] uppercase tracking-wider text-ink-muted">
            <tr>
              <th className="px-3 py-1.5 text-left">Step</th>
              <th className="px-3 py-1.5 text-left">Node</th>
              <th className="px-3 py-1.5 text-left">Kind</th>
              <th className="px-3 py-1.5 text-left">Outcome</th>
              <th className="px-3 py-1.5 text-left">Assignee</th>
              <th className="px-3 py-1.5 text-left">Decision</th>
              <th className="px-3 py-1.5 text-left">Notes</th>
            </tr>
          </thead>
          <tbody>
            {steps.map((s, i) => (
              <tr key={`${i}-${s.nodeId}`} className={`border-t border-rule ${rowToneClass(s.outcome)}`}>
                <td className="px-3 py-1.5 text-ink-muted">{i + 1}</td>
                <td className="px-3 py-1.5 font-mono text-[11px]">{s.nodeId}</td>
                <td className="px-3 py-1.5"><KindBadge kind={s.nodeKind} /></td>
                <td className="px-3 py-1.5"><OutcomeBadge outcome={s.outcome} /></td>
                <td className="px-3 py-1.5">{s.assigneeName ?? <span className="text-ink-faint">—</span>}</td>
                <td className="px-3 py-1.5">{s.decision ?? <span className="text-ink-faint">—</span>}</td>
                <td className="px-3 py-1.5 text-xs text-ink-muted">{s.notes ?? ''}</td>
              </tr>
            ))}
          </tbody>
        </table>
      )}
    </section>
  )
}

function rowToneClass(outcome: string): string {
  switch (outcome) {
    case 'completed':     return 'bg-emerald-50/50'
    case 'auto-advanced': return ''
    case 'spawned':       return 'bg-amber-50/50'
    case 'errored':       return 'bg-red-50/60'
    default:              return ''
  }
}

function OutcomeBadge({ outcome }: { outcome: string }) {
  const cls =
    outcome === 'completed'     ? 'bg-emerald-100 text-emerald-800'
  : outcome === 'auto-advanced' ? 'bg-slate-100 text-slate-700'
  : outcome === 'spawned'       ? 'bg-amber-100 text-amber-800'
  : outcome === 'errored'       ? 'bg-red-100 text-red-800'
                                : 'bg-slate-100 text-slate-700'
  return <span className={`inline-block rounded px-1.5 py-0.5 text-[10px] font-medium uppercase tracking-wider ${cls}`}>{outcome}</span>
}

function KindBadge({ kind }: { kind: string }) {
  return (
    <span className="inline-block rounded bg-slate-100 px-1.5 py-0.5 text-[10px] font-medium text-slate-700">
      {kind}
    </span>
  )
}

function NotificationsPanel({ notifications }: { notifications: SimulationResultDto['notifications'] }) {
  const [open, setOpen] = useState(true)
  return (
    <section className="rounded-lg border border-rule bg-card">
      <header className="flex items-center justify-between border-b border-rule px-3 py-2 text-sm font-semibold text-ink">
        <span className="flex items-center gap-2">
          <Mail className="h-4 w-4 text-ink-muted" />
          Notifications <span className="text-ink-faint">({notifications.length})</span>
        </span>
        <button onClick={() => setOpen(o => !o)} className="text-ink-muted hover:text-ink">
          {open ? <ChevronDown className="h-4 w-4" /> : <ChevronRight className="h-4 w-4" />}
        </button>
      </header>
      {open && (
        notifications.length === 0 ? (
          <div className="px-3 py-4 text-xs text-ink-faint">
            No notifications fired during this run.
          </div>
        ) : (
          <ul className="divide-y divide-rule">
            {notifications.map((n, i) => (
              <li key={`${i}-${n.notificationId}`} className="px-3 py-2 text-sm">
                <div className="flex items-center gap-2">
                  <span className="font-mono text-[11px] text-ink-muted">{n.notificationId || '(no id)'}</span>
                  {n.trigger && (
                    <span className="rounded bg-blue-100 px-1.5 py-0.5 text-[10px] font-medium uppercase tracking-wider text-blue-800">
                      {n.trigger}
                    </span>
                  )}
                </div>
                <div className="mt-0.5 text-ink">{n.subject || <span className="text-ink-faint italic">(no subject)</span>}</div>
                {n.recipients.length > 0 && (
                  <div className="mt-0.5 text-[11px] text-ink-muted">
                    To: {n.recipients.join(', ')}
                  </div>
                )}
              </li>
            ))}
          </ul>
        )
      )}
    </section>
  )
}

function FinalFormPanel({ data }: { data: unknown }) {
  const [open, setOpen] = useState(false)
  const text = useMemo(() => {
    try { return JSON.stringify(data ?? null, null, 2) } catch { return String(data) }
  }, [data])
  return (
    <section className="rounded-lg border border-rule bg-card">
      <header className="flex items-center justify-between border-b border-rule px-3 py-2 text-sm font-semibold text-ink">
        <span className="flex items-center gap-2">
          <FileJson className="h-4 w-4 text-ink-muted" />
          Final form data
        </span>
        <button onClick={() => setOpen(o => !o)} className="text-ink-muted hover:text-ink">
          {open ? <ChevronDown className="h-4 w-4" /> : <ChevronRight className="h-4 w-4" />}
        </button>
      </header>
      {open && (
        <pre className="max-h-[40vh] overflow-auto rounded-b-lg bg-slate-50 px-3 py-2 text-[11px] leading-snug text-ink">
{text}
        </pre>
      )}
    </section>
  )
}
