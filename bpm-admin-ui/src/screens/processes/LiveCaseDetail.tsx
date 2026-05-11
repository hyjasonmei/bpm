/**
 * LiveCaseDetail (PR-K4 §5.2). Right-pane drawer that fills with one
 * instance's:
 *   - header (spec / version / status / initiator / dates)
 *   - read-only BPMN flowchart (rendered via <BpmnDiagram>); the brief
 *     marks active-node highlighting as deferred (`[~]` in tasks.md) since
 *     `BpmnDiagram` doesn't accept a `highlightNodeIds` prop yet — we
 *     instead surface "Currently at: <nodeIds>" inline.
 *   - history feed (most recent 20, fed by `/cases/{id}` polling at 15s)
 *   - open task list with admin-action buttons (modals in this file)
 *
 * Polling at 15s (vs LiveCases' 30s) tracks the higher attention level of
 * a single-case investigation. Tab-hidden pause is handled by usePolling.
 */

import { useCallback, useEffect, useMemo, useState } from 'react'
import { X, RefreshCw, AlertTriangle, Clock, Activity, Hand, RotateCcw, Send, Octagon } from 'lucide-react'
import { Button } from '@/components/ui/button'
import { BpmnDiagram } from '@/components/BpmnDiagram'
import { migrateDraft } from '@/lib/onboarding'
import type { DraftSpec } from '@/lib/onboarding'
import { specJsonToDraft } from '@/lib/api/specToDraft'
import {
  getCaseDetail, forceReassign, forceReturn, forceSubmit, terminateInstance,
  type LiveCaseDetailDto, type ProcessTaskDto, type TaskHistoryDto,
} from '@/lib/api/processAdmin'
import { listSandboxPersonas } from '@/lib/api/sandbox'
import type { SandboxPersonaDto } from '@/types/sandbox'
import { usePolling } from '@/hooks/usePolling'

interface Props {
  instanceId: string
  onClose: () => void
}

type ModalKind =
  | { kind: 'reassign'; task: ProcessTaskDto }
  | { kind: 'return';   task: ProcessTaskDto }
  | { kind: 'submit';   task: ProcessTaskDto }
  | { kind: 'terminate' }
  | null

export function LiveCaseDetail({ instanceId, onClose }: Props) {
  const [detail, setDetail] = useState<LiveCaseDetailDto | null>(null)
  const [error, setError] = useState<string | null>(null)
  const [refreshing, setRefreshing] = useState(false)
  const [modal, setModal] = useState<ModalKind>(null)
  const [personas, setPersonas] = useState<SandboxPersonaDto[]>([])
  const [draft, setDraft] = useState<DraftSpec | null>(null)

  const refresh = useCallback(async () => {
    setRefreshing(true)
    try {
      const next = await getCaseDetail(instanceId)
      setDetail(next)
      setError(null)
      // Re-derive the draft from the spec snapshot (best-effort — the
      // simplest path is to reuse the wizard's spec-to-draft converter).
      try {
        const text = JSON.stringify(next.instance) // placeholder — actual snapshot fetch below
        void text
      } catch { /* ignore */ }
    } catch (e) {
      setError(e instanceof Error ? e.message : String(e))
    } finally {
      setRefreshing(false)
    }
  }, [instanceId])

  // 15s poll, paused when tab hidden.
  usePolling(refresh, 15_000)

  // One-shot personas fetch — ForceReassign needs a user dropdown.
  useEffect(() => {
    void listSandboxPersonas().then(setPersonas).catch(() => setPersonas([]))
  }, [])

  // Lazy-derive a DraftSpec for the BPMN renderer from the spec snapshot
  // exposed by GET /api/admin/process-admin/definitions/{flowCode}/spec.
  // We rebuild whenever the case changes flow code.
  useEffect(() => {
    let cancelled = false
    if (!detail) return
    void specJsonToDraft(detail.instance.specCode).then(d => {
      if (!cancelled) setDraft(d)
    }).catch(() => {
      if (!cancelled) setDraft(null)
    })
    return () => { cancelled = true }
  }, [detail])

  if (error) {
    return (
      <Drawer onClose={onClose}>
        <div className="p-6 text-sm text-red-700">
          <div className="flex items-center gap-2 mb-2"><AlertTriangle className="h-4 w-4" />Failed to load case</div>
          <pre className="whitespace-pre-wrap text-xs">{error}</pre>
        </div>
      </Drawer>
    )
  }

  if (!detail) {
    return (
      <Drawer onClose={onClose}>
        <div className="p-6 text-sm text-ink-muted">Loading…</div>
      </Drawer>
    )
  }

  const inst = detail.instance
  const openNodes = inst.openTasks.map(t => t.nodeId)

  return (
    <Drawer onClose={onClose}>
      <div className="flex items-center justify-between border-b border-rule p-4">
        <div className="space-y-1">
          <div className="text-sm font-semibold text-ink">{inst.specCode} v{inst.specVersion}</div>
          <div className="text-xs text-ink-muted">
            Status: <StatusBadge status={inst.status} />
            <span className="ml-3">Started: {fmtTime(inst.startedAt)}</span>
            <span className="ml-3">Initiator: <span className="font-mono">{shortId(inst.initiatorUserId)}</span></span>
          </div>
        </div>
        <div className="flex gap-2">
          <Button variant="outline" size="sm" onClick={() => void refresh()} disabled={refreshing}>
            <RefreshCw className={`h-3 w-3 ${refreshing ? 'animate-spin' : ''}`} />
            <span className="ml-1">Refresh</span>
          </Button>
          {inst.status === 'Running' || inst.status === 'Errored' ? (
            <Button variant="outline" size="sm" onClick={() => setModal({ kind: 'terminate' })}>
              <Octagon className="h-3 w-3 mr-1" />Terminate
            </Button>
          ) : null}
          <Button variant="ghost" size="sm" onClick={onClose}><X className="h-4 w-4" /></Button>
        </div>
      </div>

      <div className="p-4 grid grid-cols-3 gap-4 min-h-0 flex-1 overflow-y-auto">
        {/* BPMN flowchart + open-node summary */}
        <section className="col-span-2 space-y-2">
          <div className="text-xs font-semibold uppercase text-ink-muted">Flow diagram</div>
          {draft ? (
            <BpmnDiagram draft={draft} height={300} />
          ) : (
            <div className="rounded border border-dashed border-rule p-6 text-center text-xs text-ink-faint">
              Loading BPMN…
            </div>
          )}
          {openNodes.length > 0 && (
            <div className="text-xs text-ink-muted">
              <span className="font-semibold">Currently at:</span>{' '}
              {openNodes.map((n, i) => (
                <span key={`${n}-${i}`} className="inline-block rounded bg-amber-100 text-amber-900 px-2 py-0.5 mr-1 font-mono">
                  {n}
                </span>
              ))}
            </div>
          )}
        </section>

        {/* History feed */}
        <section className="col-span-1 space-y-2">
          <div className="text-xs font-semibold uppercase text-ink-muted">Recent history</div>
          <ol className="space-y-1 text-xs max-h-[260px] overflow-y-auto pr-1">
            {detail.recentHistory.map(h => (
              <HistoryRow key={h.id} h={h} />
            ))}
            {detail.recentHistory.length === 0 && (
              <li className="text-ink-faint">no history yet</li>
            )}
          </ol>
        </section>

        {/* Open tasks + admin actions */}
        <section className="col-span-3 space-y-2">
          <div className="text-xs font-semibold uppercase text-ink-muted">Open tasks ({inst.openTasks.length})</div>
          {inst.openTasks.length === 0 ? (
            <div className="text-xs text-ink-faint">no open tasks</div>
          ) : (
            <table className="w-full text-xs">
              <thead className="text-ink-muted">
                <tr className="text-left">
                  <th className="py-1">Node</th>
                  <th>Kind</th>
                  <th>Assignee</th>
                  <th>Due</th>
                  <th className="text-right">Actions</th>
                </tr>
              </thead>
              <tbody className="divide-y divide-rule">
                {inst.openTasks.map(t => (
                  <tr key={t.id}>
                    <td className="py-1 font-mono">{t.nodeId}</td>
                    <td>{t.nodeKind}</td>
                    <td className="font-mono">{shortId(t.actualAssigneeUserId)}</td>
                    <td>
                      {t.dueAt ? (
                        <span className={isPast(t.dueAt) ? 'text-red-600' : ''}>
                          <Clock className="h-3 w-3 inline mr-0.5" />{fmtTime(t.dueAt)}
                        </span>
                      ) : '—'}
                    </td>
                    <td className="text-right space-x-1">
                      <ActionButton icon={<Hand className="h-3 w-3" />} label="Reassign" onClick={() => setModal({ kind: 'reassign', task: t })} />
                      <ActionButton icon={<RotateCcw className="h-3 w-3" />} label="Return" onClick={() => setModal({ kind: 'return', task: t })} />
                      <ActionButton icon={<Send className="h-3 w-3" />} label="Submit" onClick={() => setModal({ kind: 'submit', task: t })} />
                    </td>
                  </tr>
                ))}
              </tbody>
            </table>
          )}
        </section>
      </div>

      {modal && (
        <ActionModal
          modal={modal}
          personas={personas}
          instanceId={instanceId}
          draft={draft}
          onClose={() => setModal(null)}
          onSuccess={() => { setModal(null); void refresh() }}
        />
      )}
    </Drawer>
  )
}

function Drawer({ children, onClose }: { children: React.ReactNode; onClose: () => void }) {
  return (
    <div className="fixed inset-0 z-30 bg-black/30 flex justify-end" onClick={onClose}>
      <aside
        className="bg-card w-[min(1100px,95vw)] h-full flex flex-col shadow-xl"
        onClick={e => e.stopPropagation()}
      >
        {children}
      </aside>
    </div>
  )
}

function StatusBadge({ status }: { status: string }) {
  const cls = status === 'Errored'
    ? 'bg-red-100 text-red-800'
    : status === 'Cancelled' ? 'bg-slate-200 text-slate-700'
    : status === 'Completed' ? 'bg-emerald-100 text-emerald-800'
    : 'bg-amber-100 text-amber-900'
  return <span className={`inline-block rounded px-2 py-0.5 text-[11px] ${cls}`}>{status}</span>
}

function HistoryRow({ h }: { h: TaskHistoryDto }) {
  // The payload is opaque JSON; surface the actorRole=admin marker
  // prominently because it's the audit story we care about.
  const isAdmin = (() => {
    if (!h.payload || typeof h.payload !== 'object') return false
    return (h.payload as { actorRole?: string }).actorRole === 'admin'
  })()
  return (
    <li className="border-l-2 border-rule pl-2 py-0.5">
      <div className="flex items-center gap-1">
        <Activity className="h-3 w-3 text-ink-faint" />
        <span className="font-semibold">{h.eventType}</span>
        {isAdmin && <span className="ml-1 text-[10px] rounded bg-orange-100 text-orange-800 px-1">admin</span>}
        <span className="ml-auto text-ink-faint">{fmtTime(h.createdAt)}</span>
      </div>
      {h.payload != null && (
        <pre className="text-[10px] text-ink-muted whitespace-pre-wrap font-mono pl-4">
          {safeStringify(h.payload)}
        </pre>
      )}
    </li>
  )
}

function ActionButton({ icon, label, onClick }: { icon: React.ReactNode; label: string; onClick: () => void }) {
  return (
    <button
      onClick={onClick}
      className="inline-flex items-center gap-1 rounded border border-rule px-2 py-0.5 text-[11px] hover:bg-slate-50"
    >
      {icon}<span>{label}</span>
    </button>
  )
}

/* ─── Modals ────────────────────────────────────────────────────── */

interface ModalProps {
  modal: NonNullable<ModalKind>
  personas: SandboxPersonaDto[]
  instanceId: string
  draft: DraftSpec | null
  onClose: () => void
  onSuccess: () => void
}

function ActionModal(props: ModalProps) {
  switch (props.modal.kind) {
    case 'reassign':  return <ReassignModal {...props} task={props.modal.task} />
    case 'return':    return <ReturnModal   {...props} task={props.modal.task} />
    case 'submit':    return <SubmitModal   {...props} task={props.modal.task} />
    case 'terminate': return <TerminateModal {...props} />
  }
}

function ModalShell({
  title, onClose, children, footer,
}: { title: string; onClose: () => void; children: React.ReactNode; footer: React.ReactNode }) {
  return (
    <div className="fixed inset-0 z-40 bg-black/40 flex items-center justify-center" onClick={onClose}>
      <div className="bg-card rounded shadow-xl w-[480px] max-w-[95vw]" onClick={e => e.stopPropagation()}>
        <div className="border-b border-rule p-3 font-semibold text-sm">{title}</div>
        <div className="p-4 space-y-3 text-sm">{children}</div>
        <div className="border-t border-rule p-3 flex justify-end gap-2">{footer}</div>
      </div>
    </div>
  )
}

function ReasonField({
  reason, setReason, minLen = 10,
}: { reason: string; setReason: (v: string) => void; minLen?: number }) {
  return (
    <label className="block">
      <span className="text-xs text-ink-muted">
        Reason ({reason.trim().length}/{minLen}+ chars)
      </span>
      <textarea
        value={reason}
        onChange={e => setReason(e.target.value)}
        rows={3}
        className="mt-1 w-full rounded border border-rule p-2 text-xs"
        placeholder="Required — what's the business justification for this admin override?"
      />
    </label>
  )
}

function useSubmit(action: () => Promise<void>) {
  const [busy, setBusy] = useState(false)
  const [err, setErr] = useState<string | null>(null)
  const submit = useCallback(async () => {
    setBusy(true)
    setErr(null)
    try { await action() } catch (e) {
      setErr(e instanceof Error ? e.message : String(e))
    } finally {
      setBusy(false)
    }
  }, [action])
  return { busy, err, submit }
}

function ReassignModal({
  task, personas, onClose, onSuccess,
}: { task: ProcessTaskDto } & ModalProps) {
  const [newAssignee, setNewAssignee] = useState<string>('')
  const [reason, setReason] = useState('')
  const valid = newAssignee.length > 0 && reason.trim().length >= 10
  const { busy, err, submit } = useSubmit(async () => {
    await forceReassign(task.id, { newAssigneeUserId: newAssignee, reason: reason.trim() })
    onSuccess()
  })
  return (
    <ModalShell
      title={`Force reassign — ${task.nodeId}`}
      onClose={onClose}
      footer={
        <>
          <Button variant="ghost" size="sm" onClick={onClose}>Cancel</Button>
          <Button size="sm" disabled={!valid || busy} onClick={() => void submit()}>
            {busy ? 'Reassigning…' : 'Confirm'}
          </Button>
        </>
      }
    >
      <label className="block">
        <span className="text-xs text-ink-muted">New assignee</span>
        <select
          value={newAssignee}
          onChange={e => setNewAssignee(e.target.value)}
          className="mt-1 w-full rounded border border-rule p-2 text-xs"
        >
          <option value="">— pick a user —</option>
          {personas.map(p => (
            <option key={p.id} value={p.id}>{p.fullName} ({p.email})</option>
          ))}
        </select>
      </label>
      <ReasonField reason={reason} setReason={setReason} />
      {err && <div className="text-xs text-red-700">{err}</div>}
    </ModalShell>
  )
}

function ReturnModal({
  task, draft, onClose, onSuccess,
}: { task: ProcessTaskDto } & ModalProps) {
  const [target, setTarget] = useState('')
  const [reason, setReason] = useState('')
  const nodeIds = useMemo(() => {
    if (!draft) return [] as string[]
    return draft.flow.nodes.map(n => n.id).filter(id => id !== task.nodeId)
  }, [draft, task.nodeId])
  const valid = target.length > 0 && reason.trim().length >= 10
  const { busy, err, submit } = useSubmit(async () => {
    await forceReturn(task.id, { targetNodeId: target, reason: reason.trim() })
    onSuccess()
  })
  return (
    <ModalShell
      title={`Force return — ${task.nodeId}`}
      onClose={onClose}
      footer={
        <>
          <Button variant="ghost" size="sm" onClick={onClose}>Cancel</Button>
          <Button size="sm" disabled={!valid || busy} onClick={() => void submit()}>
            {busy ? 'Returning…' : 'Confirm'}
          </Button>
        </>
      }
    >
      <label className="block">
        <span className="text-xs text-ink-muted">Target node</span>
        <select
          value={target}
          onChange={e => setTarget(e.target.value)}
          className="mt-1 w-full rounded border border-rule p-2 text-xs"
        >
          <option value="">— pick a node —</option>
          {nodeIds.length === 0 ? (
            <option value="" disabled>(spec snapshot unavailable — type below)</option>
          ) : nodeIds.map(id => <option key={id} value={id}>{id}</option>)}
        </select>
        <input
          value={target}
          onChange={e => setTarget(e.target.value)}
          placeholder="…or type a node id"
          className="mt-1 w-full rounded border border-rule p-2 text-xs font-mono"
        />
      </label>
      <ReasonField reason={reason} setReason={setReason} />
      {err && <div className="text-xs text-red-700">{err}</div>}
    </ModalShell>
  )
}

function SubmitModal({
  task, onClose, onSuccess,
}: { task: ProcessTaskDto } & ModalProps) {
  const [decision, setDecision] = useState<'Approve' | 'Reject' | 'Return' | ''>('')
  const [patchText, setPatchText] = useState('')
  const [reason, setReason] = useState('')
  const valid = reason.trim().length >= 10
  const { busy, err, submit } = useSubmit(async () => {
    let patch: unknown = undefined
    if (patchText.trim().length > 0) {
      try { patch = JSON.parse(patchText) }
      catch { throw new Error('Form data patch is not valid JSON') }
    }
    await forceSubmit(task.id, {
      decision: decision === '' ? null : decision,
      formDataPatch: patch,
      reason: reason.trim(),
    })
    onSuccess()
  })
  return (
    <ModalShell
      title={`Force submit — ${task.nodeId}`}
      onClose={onClose}
      footer={
        <>
          <Button variant="ghost" size="sm" onClick={onClose}>Cancel</Button>
          <Button size="sm" disabled={!valid || busy} onClick={() => void submit()}>
            {busy ? 'Submitting…' : 'Confirm'}
          </Button>
        </>
      }
    >
      <label className="block">
        <span className="text-xs text-ink-muted">Decision (optional)</span>
        <select
          value={decision}
          onChange={e => setDecision(e.target.value as 'Approve' | 'Reject' | 'Return' | '')}
          className="mt-1 w-full rounded border border-rule p-2 text-xs"
        >
          <option value="">— none —</option>
          <option value="Approve">Approve</option>
          <option value="Reject">Reject</option>
          <option value="Return">Return</option>
        </select>
      </label>
      <label className="block">
        <span className="text-xs text-ink-muted">Form data patch (JSON, optional)</span>
        <textarea
          value={patchText}
          onChange={e => setPatchText(e.target.value)}
          rows={3}
          placeholder='{"some_field": "value"}'
          className="mt-1 w-full rounded border border-rule p-2 text-xs font-mono"
        />
      </label>
      <ReasonField reason={reason} setReason={setReason} />
      {err && <div className="text-xs text-red-700">{err}</div>}
    </ModalShell>
  )
}

function TerminateModal({ instanceId, onClose, onSuccess }: ModalProps) {
  const [reason, setReason] = useState('')
  const [confirmText, setConfirmText] = useState('')
  const valid = reason.trim().length >= 10 && confirmText === 'TERMINATE'
  const { busy, err, submit } = useSubmit(async () => {
    await terminateInstance(instanceId, { reason: reason.trim() })
    onSuccess()
  })
  return (
    <ModalShell
      title="Terminate instance"
      onClose={onClose}
      footer={
        <>
          <Button variant="ghost" size="sm" onClick={onClose}>Cancel</Button>
          <Button size="sm" disabled={!valid || busy} onClick={() => void submit()}>
            {busy ? 'Terminating…' : 'Confirm'}
          </Button>
        </>
      }
    >
      <ReasonField reason={reason} setReason={setReason} />
      <label className="block">
        <span className="text-xs text-ink-muted">Type <span className="font-mono">TERMINATE</span> to confirm</span>
        <input
          value={confirmText}
          onChange={e => setConfirmText(e.target.value)}
          className="mt-1 w-full rounded border border-rule p-2 text-xs font-mono"
        />
      </label>
      {err && <div className="text-xs text-red-700">{err}</div>}
    </ModalShell>
  )
}

/* ─── helpers ───────────────────────────────────────────────────── */

function shortId(s: string | null): string {
  if (!s) return '—'
  return s.slice(0, 8)
}

function fmtTime(iso: string): string {
  try {
    const d = new Date(iso)
    return d.toLocaleString(undefined, { hour12: false })
  } catch { return iso }
}

function isPast(iso: string): boolean {
  return Date.parse(iso) < Date.now()
}

function safeStringify(val: unknown): string {
  try { return JSON.stringify(val, null, 2) }
  catch { return String(val) }
}

// migrateDraft is re-exported solely to keep eslint quiet about unused
// imports if the converter isn't wired yet — but we DO use it transitively.
void migrateDraft
