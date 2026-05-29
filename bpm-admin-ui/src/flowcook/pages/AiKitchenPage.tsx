import { useCallback, useEffect, useMemo, useRef, useState } from 'react'
import {
  ArchiveRestore,
  ArchiveX,
  CheckCircle2,
  ChefHat,
  Copy,
  Download,
  RefreshCw,
  Save,
  Sparkles,
  Trash2,
  Undo2,
  X,
} from 'lucide-react'
import { Navigate, Route, Routes, useNavigate, useParams } from 'react-router-dom'
import { cn } from '@/lib/cn'
import { Onboarding } from '@/screens/onboarding/Onboarding'
import { EMPTY_DRAFT, migrateDraft, ONBOARDING_STEPS, stripFieldUids, validators, type DraftSpec } from '@/lib/onboarding'
import { flowToBpmnXml } from '@/lib/bpmnXml'
import { useSetPageHeader, type PageHeader } from '@/flowcook/app/pageHeader'
import { PhaseTabs, type PhaseId, type PhaseDef } from '@/flowcook/app/PhaseTabs'
import { OverflowMenu, type OverflowGroup } from '@/flowcook/app/OverflowMenu'
import {
  cancelFlow,
  cloneFlowVersion,
  createFlow,
  deleteFlow,
  retireFlow,
  unretireFlow,
  type FlowDetail,
  type FlowState,
  type FlowSummary,
  getFlow,
  listFlows,
  submitFlow,
  updateFlowSpec,
} from '@/flowcook/api/flows'
import { assignFlowGroup, listFlowGroups, type FlowGroupDto } from '@/flowcook/api/flowGroups'
import { resolveIcon } from '@/flowcook/pages/sitesetting/FlowGroupsTab'
import { FolderPlus, GitBranch, Tag } from 'lucide-react'
import { parseChefWorkContext } from '@/flowcook/api/flows'
import { CookPanel } from './aiKitchen/CookPanel'
import { ServePanel } from './aiKitchen/ServePanel'
import { useCookMessages } from './aiKitchen/useCookMessages'
import {
  DEFAULT_DEPLOYMENTS,
  type DeployState,
  type EnvId,
} from './aiKitchen/types'

export function AiKitchenPage() {
  // Nested routes under /ai-kitchen:
  //   /ai-kitchen           → list
  //   /ai-kitchen/:flowId   → wizard for that flow
  // Each render fetches its own data so a refresh / paste-link path
  // lands the same way.
  return (
    <Routes>
      <Route index element={<CookedFlowsListRoute />} />
      <Route path=":flowId" element={<WizardRoute />} />
      <Route path="*" element={<Navigate to="" replace />} />
    </Routes>
  )
}

function CookedFlowsListRoute() {
  const navigate = useNavigate()
  return <CookedFlowsList onOpenFlow={(id) => { navigate(id); return Promise.resolve() }} />
}

function WizardRoute() {
  const { flowId } = useParams<{ flowId: string }>()
  const navigate = useNavigate()
  const [flow, setFlow] = useState<FlowDetail | null>(null)
  const [error, setError] = useState<string | null>(null)

  useEffect(() => {
    if (!flowId) return
    let cancelled = false
    setError(null)
    void getFlow(flowId)
      .then((f) => { if (!cancelled) setFlow(f) })
      .catch((err) => { if (!cancelled) setError(err instanceof Error ? err.message : 'Failed to load') })
    return () => { cancelled = true }
  }, [flowId])

  if (error) {
    return (
      <div className="rounded border border-danger/30 bg-danger/5 px-4 py-3 text-sm text-danger">
        {error} · <button onClick={() => navigate('..')} className="underline">回 kitchen</button>
      </div>
    )
  }
  if (!flow) {
    return <div className="text-sm text-ink-muted">Loading flow…</div>
  }
  return (
    <WizardView
      flow={flow}
      onFlowChange={setFlow}
      onClose={() => navigate('..')}
    />
  )
}

// ──────────────────────────────────────────────────────────────
// List
// ──────────────────────────────────────────────────────────────

function CookedFlowsList({ onOpenFlow }: { onOpenFlow: (id: string) => Promise<void> }) {
  const navigate = useNavigate()
  const [flows, setFlows] = useState<FlowSummary[]>([])
  const [loading, setLoading] = useState(true)
  const [error, setError] = useState<string | null>(null)
  const [creating, setCreating] = useState(false)
  const [rowPending, setRowPending] = useState<string | null>(null)
  // PR-G2: groups for the row chip + assignment sub-menu. Loaded
  // alongside flows on mount.
  const [groups, setGroups] = useState<FlowGroupDto[]>([])

  const refresh = useCallback(async () => {
    setLoading(true)
    setError(null)
    try {
      const [flowsRes, groupsRes] = await Promise.all([listFlows(), listFlowGroups()])
      setFlows(flowsRes)
      setGroups(groupsRes)
    } catch (err) {
      setError(err instanceof Error ? err.message : 'Failed to load flows')
    } finally {
      setLoading(false)
    }
  }, [])

  useEffect(() => { void refresh() }, [refresh])

  // Row-level lifecycle actions. Each runs the relevant POST, refreshes
  // the list, then navigates if the action creates a new flow (clone).
  async function withPending<T>(rowId: string, fn: () => Promise<T>): Promise<T | null> {
    setRowPending(rowId)
    try {
      return await fn()
    } catch (err) {
      window.alert(err instanceof Error ? err.message : 'Action failed')
      return null
    } finally {
      setRowPending(null)
    }
  }

  async function handleCloneAsNewVersion(row: FlowSummary) {
    const cloned = await withPending(row.id, () => cloneFlowVersion(row.id))
    if (!cloned) return
    await refresh()
    navigate(cloned.id)
  }

  async function handleRetire(row: FlowSummary) {
    if (!window.confirm(`下架 "${row.displayName}" v${row.version}？new case 將不能再啟動，現有案件仍可走完。`)) return
    await withPending(row.id, () => retireFlow(row.id))
    await refresh()
  }

  async function handleUnretire(row: FlowSummary) {
    await withPending(row.id, () => unretireFlow(row.id))
    await refresh()
  }

  async function handleDelete(row: FlowSummary) {
    if (!window.confirm(`刪除 draft "${row.displayName}"？此為 soft-delete，可由 admin DB 還原。`)) return
    await withPending(row.id, () => deleteFlow(row.id))
    await refresh()
  }

  async function handleAssignGroup(row: FlowSummary, groupId: string | null) {
    await withPending(row.id, () => assignFlowGroup(row.id, groupId))
    await refresh()
  }

  async function cookNew(flowCode: string, displayName: string) {
    // Seed `spec.meta` from the row inputs so the wizard's SOURCE step
    // opens with the name + code the user just typed, not the blank
    // EMPTY_DRAFT meta. (Fixes the "flow name 沒有從 list 打完帶進來"
    // observation — row.displayName lives on the row, spec.meta.flowName
    // lives on the spec; we mirror at create time.)
    const seeded: DraftSpec = {
      ...EMPTY_DRAFT,
      meta: { ...EMPTY_DRAFT.meta, flowName: displayName, flowCode },
    }
    const flow = await createFlow({
      flowCode,
      displayName,
      specJson: JSON.stringify(seeded),
    })
    setCreating(false)
    await onOpenFlow(flow.id)
  }

  return (
    <div className="grid h-full grid-cols-12 gap-6">
      <section className="col-span-8 flex min-h-0 flex-col rounded-lg border border-rule bg-card shadow-sm">
        <header className="flex items-center justify-between border-b border-rule px-5 py-3">
          <div className="flex items-baseline gap-3">
            <h2 className="text-base font-semibold text-ink">Cooked flows</h2>
            <span className="font-mono text-[10px] tracking-[0.14em] uppercase text-ink-muted">
              {loading ? '…' : `${flows.length} on the line`}
            </span>
          </div>
          <div className="flex items-center gap-2">
            <button
              onClick={() => void refresh()}
              disabled={loading}
              title="Refresh"
              className="flex h-7 w-7 items-center justify-center rounded border border-rule bg-card text-ink-muted transition-colors hover:border-primary hover:text-primary disabled:opacity-50"
            >
              <RefreshCw className={cn('h-3.5 w-3.5', loading && 'animate-spin')} />
            </button>
            <button
              onClick={() => setCreating(true)}
              data-testid="cook-new-flow"
              className="inline-flex items-center gap-1.5 rounded bg-primary px-3 py-1.5 text-xs font-semibold text-white transition-colors hover:bg-primary/90"
            >
              <ChefHat className="h-3.5 w-3.5" />
              Cook new flow
            </button>
          </div>
        </header>

        {error && (
          <div className="border-b border-danger/30 bg-danger/5 px-5 py-2 text-xs text-danger">
            {error}
          </div>
        )}

        {loading && flows.length === 0 && (
          <div className="flex flex-1 items-center justify-center text-sm text-ink-muted">
            Loading…
          </div>
        )}

        {!loading && flows.length === 0 && !error && (
          <div className="flex flex-1 flex-col items-center justify-center px-8 py-12 text-center">
            <div className="mb-4 flex h-14 w-14 items-center justify-center rounded-full bg-primary/10 text-primary">
              <ChefHat className="h-7 w-7" />
            </div>
            <h3 className="text-base font-semibold text-ink">No flows cooked yet</h3>
            <p className="mt-2 max-w-sm text-sm text-ink-muted">
              Start by cooking a new flow. The 9-step kitchen walks you through
              source, structure, forms, decisions, approvers, notify, SLA, test,
              and go-live — chat on the left, canvas on the right — and produces
              a portable spec bundle.
            </p>
            <button
              onClick={() => setCreating(true)}
              className="mt-6 inline-flex items-center gap-1.5 rounded bg-primary px-4 py-2 text-sm font-semibold text-white transition-colors hover:bg-primary/90"
            >
              <ChefHat className="h-4 w-4" />
              Cook new flow
            </button>
          </div>
        )}

        {flows.length > 0 && (
          <div className="flex-1 overflow-auto">
            <table className="w-full text-sm">
              <thead className="sticky top-0 border-b border-rule bg-label-bg text-left font-mono text-[10px] tracking-[0.14em] uppercase text-ink-muted">
                <tr>
                  <th className="px-5 py-2 font-normal">Flow</th>
                  <th className="px-3 py-2 font-normal">Version</th>
                  <th className="px-3 py-2 font-normal">State</th>
                  <th className="px-3 py-2 font-normal">Group</th>
                  <th className="px-3 py-2 font-normal">Updated</th>
                  <th className="w-10 px-3 py-2" />
                </tr>
              </thead>
              <tbody className="divide-y divide-rule">
                {flows.map((f) => {
                  const isDraft    = f.state === 'Draft'
                  const isApproved = f.state === 'Approved'
                  const isRetired  = f.state === 'Retired'
                  const rowBusy    = rowPending === f.id
                  const menuGroups: OverflowGroup[] = [
                    {
                      items: [
                        {
                          id: 'open',
                          label: 'Open',
                          icon: <ChefHat className="h-3 w-3" />,
                          onClick: () => { void onOpenFlow(f.id) },
                        },
                        ...((isApproved || isRetired) ? [{
                          id: 'clone',
                          label: 'Clone as new version draft',
                          icon: <Copy className="h-3 w-3" />,
                          tone: 'productive' as const,
                          disabled: rowBusy,
                          hint: '複製成 v+1，原版本維持不動',
                          onClick: () => { void handleCloneAsNewVersion(f) },
                        }] : []),
                      ],
                    },
                    {
                      label: 'Group',
                      items: [
                        ...groups.map(g => ({
                          id: `assign-${g.id}`,
                          label: `${g.displayName['zh-TW'] ?? g.code}${f.groupId === g.id ? ' ✓' : ''}`,
                          icon: <Tag className="h-3 w-3" />,
                          tone: 'productive' as const,
                          disabled: rowBusy || f.groupId === g.id,
                          onClick: () => { void handleAssignGroup(f, g.id) },
                        })),
                        ...(f.groupId ? [{
                          id: 'unassign-group',
                          label: 'Unassign group',
                          icon: <FolderPlus className="h-3 w-3" />,
                          tone: 'risky' as const,
                          disabled: rowBusy,
                          onClick: () => { void handleAssignGroup(f, null) },
                        }] : []),
                      ],
                    },
                    {
                      items: [
                        ...(isApproved ? [{
                          id: 'retire',
                          label: 'Retire flow',
                          icon: <ArchiveX className="h-3 w-3" />,
                          tone: 'risky' as const,
                          disabled: rowBusy,
                          hint: '下架：end-user launcher 不再列出此版本',
                          onClick: () => { void handleRetire(f) },
                        }] : []),
                        ...(isRetired ? [{
                          id: 'unretire',
                          label: 'Unretire flow',
                          icon: <ArchiveRestore className="h-3 w-3" />,
                          tone: 'productive' as const,
                          disabled: rowBusy,
                          hint: '上架回 Approved',
                          onClick: () => { void handleUnretire(f) },
                        }] : []),
                        ...(isDraft ? [{
                          id: 'delete',
                          label: 'Delete draft',
                          icon: <Trash2 className="h-3 w-3" />,
                          tone: 'danger' as const,
                          disabled: rowBusy,
                          hint: 'Soft-delete — 可由 admin DB 還原',
                          onClick: () => { void handleDelete(f) },
                        }] : []),
                      ],
                    },
                  ]
                  return (
                    <tr
                      key={f.id}
                      onClick={() => void onOpenFlow(f.id)}
                      data-testid={`flow-row-${f.flowCode}`}
                      className={cn(
                        'cursor-pointer hover:bg-bg',
                        isRetired && 'opacity-60',
                      )}
                    >
                      <td className="px-5 py-3">
                        <div className="font-medium text-ink">{f.displayName}</div>
                        <div className="mt-0.5 font-mono text-[11px] text-ink-muted">{f.flowCode}</div>
                      </td>
                      <td className="px-3 py-3 font-mono text-xs text-ink-muted">v{f.version}</td>
                      <td className="px-3 py-3">
                        <div className="flex items-center gap-1.5">
                          <StatePill state={f.state} />
                          {isChefStalled(f) && (
                            <span
                              title={`Cooking but no chef heartbeat for >30 min (last: ${f.lastChefHeartbeatAt ?? 'never'})`}
                              className="inline-flex items-center gap-1 rounded-full bg-warn/15 px-1.5 py-0.5 font-mono text-[9px] tracking-wide text-warn"
                            >
                              <span className="h-1.5 w-1.5 rounded-full bg-warn" /> stalled
                            </span>
                          )}
                          <ChefBranchChip flow={f} />
                        </div>
                      </td>
                      <td className="px-3 py-3">
                        <GroupChip flow={f} groups={groups} />
                      </td>
                      <td className="px-3 py-3 font-mono text-[11px] text-ink-muted">
                        {formatDate(f.updatedAt)}
                      </td>
                      <td
                        className="px-3 py-3"
                        onClick={e => e.stopPropagation()}
                      >
                        <OverflowMenu groups={menuGroups} buttonTitle="Flow actions" />
                      </td>
                    </tr>
                  )
                })}
              </tbody>
            </table>
          </div>
        )}
      </section>

      <aside className="col-span-4 flex min-h-0 flex-col gap-4">
        <div className="rounded-lg border border-rule bg-card p-5 shadow-sm">
          <div className="mb-2 flex items-center gap-2">
            <Sparkles className="h-4 w-4 text-accent" />
            <span className="font-mono text-[10px] tracking-[0.14em] uppercase text-ink-muted">
              kitchen brief
            </span>
          </div>
          <h3 className="text-sm font-semibold text-ink">How AI Kitchen works</h3>
          <ol className="mt-3 space-y-2 text-xs text-ink-muted">
            <li><span className="font-semibold text-ink">1. Source — </span>upload an image, BPMN, or start from a preset / blank.</li>
            <li><span className="font-semibold text-ink">2. Iterate — </span>chat with the assistant or edit the canvas directly. Either side updates the same draft.</li>
            <li><span className="font-semibold text-ink">3. Author — </span>flesh out forms, decisions, approvers, notifications, SLA.</li>
            <li><span className="font-semibold text-ink">4. Verify — </span>run sandbox test cases.</li>
            <li><span className="font-semibold text-ink">5. Ship — </span>Submit hands the draft to chef. Export a portable <code className="font-mono text-[11px]">.zip</code> bundle on Step 9 if you want a snapshot.</li>
          </ol>
        </div>

        <div className="rounded-lg border border-dashed border-rule bg-bg/50 p-5">
          <div className="mb-2 font-mono text-[10px] tracking-[0.14em] uppercase text-ink-muted">
            phase status
          </div>
          <p className="text-xs leading-relaxed text-ink-muted">
            Drafts now auto-save to admin-svc as you edit. Click a row to
            resume an existing draft. Submit moves it into chef's queue
            (state = submitted); chef-side handlers land in step 7.
          </p>
        </div>
      </aside>

      {creating && (
        <CookNewFlowModal onCreate={cookNew} onCancel={() => setCreating(false)} />
      )}
    </div>
  )
}

// ──────────────────────────────────────────────────────────────
// Cook new flow modal
// ──────────────────────────────────────────────────────────────

function CookNewFlowModal({
  onCreate,
  onCancel,
}: {
  onCreate: (flowCode: string, displayName: string) => Promise<void>
  onCancel: () => void
}) {
  const [flowCode, setFlowCode] = useState('')
  const [displayName, setDisplayName] = useState('')
  const [saving, setSaving] = useState(false)
  const [error, setError] = useState<string | null>(null)

  async function submit(e: React.FormEvent) {
    e.preventDefault()
    if (!flowCode.trim() || !displayName.trim()) {
      setError('Both fields are required.')
      return
    }
    setSaving(true)
    setError(null)
    try {
      await onCreate(flowCode.trim().toUpperCase(), displayName.trim())
    } catch (err) {
      setError(err instanceof Error ? err.message : 'Failed')
      setSaving(false)
    }
  }

  return (
    <div className="fixed inset-0 z-30 flex items-center justify-center bg-ink/30 p-4">
      <form
        onSubmit={submit}
        className="w-full max-w-md rounded-lg border border-rule bg-card p-6 shadow-lg"
      >
        <div className="mb-2 flex items-center gap-2">
          <ChefHat className="h-4 w-4 text-primary" />
          <h3 className="text-base font-semibold text-ink">Cook a new flow</h3>
        </div>
        <p className="mb-5 text-xs text-ink-muted">
          Name the flow first. You'll be able to edit everything inside the wizard.
        </p>

        <label className="mb-3 block">
          <span className="mb-1 block font-mono text-[10px] tracking-[0.14em] uppercase text-ink-muted">
            Flow code
          </span>
          <input
            value={flowCode}
            onChange={(e) => setFlowCode(e.target.value)}
            placeholder="e.g. LEAVE"
            autoFocus
            className="block w-full rounded border border-rule bg-white px-3 py-2 text-sm uppercase text-ink outline-none placeholder:text-ink-faint focus:border-primary focus:ring-2 focus:ring-primary/20"
          />
        </label>
        <label className="mb-4 block">
          <span className="mb-1 block font-mono text-[10px] tracking-[0.14em] uppercase text-ink-muted">
            Display name
          </span>
          <input
            value={displayName}
            onChange={(e) => setDisplayName(e.target.value)}
            placeholder="e.g. Leave Request"
            className="block w-full rounded border border-rule bg-white px-3 py-2 text-sm text-ink outline-none placeholder:text-ink-faint focus:border-primary focus:ring-2 focus:ring-primary/20"
          />
        </label>

        {error && (
          <p className="mb-3 rounded border border-danger/30 bg-danger/5 px-3 py-2 text-xs text-danger">
            {error}
          </p>
        )}

        <div className="flex items-center justify-end gap-2">
          <button
            type="button"
            onClick={onCancel}
            disabled={saving}
            className="rounded px-3 py-1.5 text-xs text-ink-muted hover:text-ink"
          >
            Cancel
          </button>
          <button
            type="submit"
            disabled={saving}
            className="inline-flex items-center gap-1.5 rounded bg-primary px-3 py-1.5 text-xs font-semibold text-white transition-colors hover:bg-primary/90 disabled:opacity-50"
          >
            <ChefHat className="h-3.5 w-3.5" />
            {saving ? 'Creating…' : 'Open the kitchen'}
          </button>
        </div>
      </form>
    </div>
  )
}

// ──────────────────────────────────────────────────────────────
// Wizard view — wraps legacy Onboarding in controlled mode
// ──────────────────────────────────────────────────────────────

const AUTOSAVE_DEBOUNCE_MS = 600

function WizardView({
  flow,
  onFlowChange,
  onClose,
}: {
  flow: FlowDetail
  onFlowChange: (f: FlowDetail) => void
  onClose: () => void
}) {
  const navigate = useNavigate()
  const [saveState, setSaveState] = useState<'idle' | 'saving' | 'saved' | 'error'>('idle')
  const [saveError, setSaveError] = useState<string | null>(null)
  const [transitionPending, setTransitionPending] = useState<null | 'submit' | 'cancel' | 'delete' | 'clone'>(null)
  const [downloading, setDownloading] = useState(false)
  const [phase, setPhase] = useState<PhaseId>('prep')
  const timerRef = useRef<number | null>(null)
  const latestDraftRef = useRef<DraftSpec | null>(null)

  // ── FE-only POC state for Cook / Serve tabs ─────────────────
  // `mockState` overrides flow.state once we're past Draft. The
  // initial value mirrors the real backend state so refreshing the
  // page keeps the StatePill consistent; subsequent simulate-chef
  // and user actions only update local state (no admin-svc calls).
  // When the real chef pipeline + serve runtime exist, replace
  // these with proper API-backed state.
  const [mockState, setMockState] = useState<FlowState>(flow.state)
  useEffect(() => { setMockState(flow.state) }, [flow.id, flow.state])
  // PR-K3: cookedCount + chat thread now live on admin-svc. The Cook
  // panel polls /api/flows/{id}/messages directly; this wrapper just
  // mirrors the count to phaseDefs + ServePanel so they don't unlock
  // before chef has ever finished a cook.
  const { messages: cookMessages } = useCookMessages(flow.id)
  const cookedCount = useMemo(
    () => cookMessages.filter(m => m.kind === 'completion').length,
    [cookMessages],
  )
  const [deployments, setDeployments] = useState<Record<EnvId, DeployState>>(DEFAULT_DEPLOYMENTS)

  // Parse initial spec into a DraftSpec; tolerate parse errors by falling
  // back to EMPTY_DRAFT (a partial spec from chef-on-hold or an external
  // import is the only realistic way to land here with bad JSON).
  // Route through migrateDraft so legacy schema (hint→note, role/user/group
  // → principal, recursive ActorRef fallback → text) is normalised on load
  // and the rest of the UI never sees the old shapes.
  const initialDraft: DraftSpec = (() => {
    try {
      if (!flow.specJson || flow.specJson === '{}') return EMPTY_DRAFT
      return migrateDraft(JSON.parse(flow.specJson))
    } catch {
      return EMPTY_DRAFT
    }
  })()

  // PR-X2: shadow the wizard's live draft so the Submit button can
  // gate on every step's validator, not just the current step's. The
  // ref already exists for autosave; this state is purely for the
  // render path.
  const [currentDraft, setCurrentDraft] = useState<DraftSpec>(initialDraft)

  const persistDraft = useCallback(async (draft: DraftSpec) => {
    setSaveState('saving')
    setSaveError(null)
    try {
      const updated = await updateFlowSpec(flow.id, {
        specJson: JSON.stringify(stripFieldUids(draft)),
      })
      onFlowChange(updated)
      setSaveState('saved')
    } catch (err) {
      setSaveState('error')
      setSaveError(err instanceof Error ? err.message : 'Save failed')
    }
  }, [flow.id, onFlowChange])

  const handleDraftChange = useCallback((draft: DraftSpec) => {
    latestDraftRef.current = draft
    setCurrentDraft(draft)
    if (flow.state !== 'Draft') return // read-only once submitted
    if (timerRef.current) window.clearTimeout(timerRef.current)
    timerRef.current = window.setTimeout(() => {
      void persistDraft(draft)
    }, AUTOSAVE_DEBOUNCE_MS)
  }, [flow.state, persistDraft])

  useEffect(() => () => {
    if (timerRef.current) window.clearTimeout(timerRef.current)
  }, [])

  async function submit() {
    setTransitionPending('submit')
    try {
      // Flush any pending autosave first.
      if (timerRef.current) {
        window.clearTimeout(timerRef.current)
        if (latestDraftRef.current) await persistDraft(latestDraftRef.current)
      }
      // PR-X3: build the bundle server-side at submit time so the zip is
      // cached on the Flow row for chef MCP download. Failure here
      // shouldn't block submit (chef can still get spec via chef_get_flow);
      // surface a warning instead of bailing.
      try {
        await buildBundleSilent()
      } catch (err) {
        console.warn('Bundle pre-cache failed; chef MCP download will return 409 until admin clicks Download bundle', err)
      }
      const updated = await submitFlow(flow.id)
      onFlowChange(updated)
      // Stay on the flow page (don't bounce back to the kitchen list).
      // Land on Prep so the user sees the now-locked spec; Cook tab
      // becomes enabled and they can click in to watch chef work.
      setMockState(updated.state)
      // PR-K3: chat thread persists server-side now; no FE seed needed.
      setPhase('prep')
    } catch (err) {
      window.alert(err instanceof Error ? err.message : 'Submit failed')
    } finally {
      setTransitionPending(null)
    }
  }

  /** Build + cache the bundle on the server without triggering a
   *  download. Reuses the same /bundle endpoint downloadBundle uses;
   *  the server persists bytes to Flow.BundleBlob unconditionally so
   *  chef can later pull via MCP. */
  async function buildBundleSilent() {
    const draft = latestDraftRef.current ?? initialDraft
    const bpmnXml = await flowToBpmnXml(draft)
    const testCases = draft.testCases.length > 0 ? draft.testCases : [{
      id: 'placeholder-happy',
      name: 'Happy path (placeholder — fill in step 8)',
      inputs: {},
      expectedTrace: [],
      expectedFinalStatus: 'Completed',
    }]
    const res = await fetch(`/api/flows/${flow.id}/bundle`, {
      method: 'POST',
      credentials: 'include',
      headers: { 'Content-Type': 'application/json' },
      body: JSON.stringify({
        bpmnXml,
        sampleOrg: draft.sampleOrg,
        testCases,
        sourceInstanceId: `flow:${flow.id}`,
      }),
    })
    if (!res.ok) throw new Error(`HTTP ${res.status}: ${await res.text()}`)
    await res.blob()  // drain so the connection releases
  }

  async function cancel() {
    if (!window.confirm('Cancel this flow and return it to Draft?')) return
    setTransitionPending('cancel')
    try {
      const updated = await cancelFlow(flow.id)
      onFlowChange(updated)
    } catch (err) {
      window.alert(err instanceof Error ? err.message : 'Cancel failed')
    } finally {
      setTransitionPending(null)
    }
  }

  async function downloadBundle() {
    setDownloading(true)
    try {
      // Flush pending autosave so the server-side spec matches what the
      // user just saw on screen.
      if (timerRef.current) {
        window.clearTimeout(timerRef.current)
        if (latestDraftRef.current) await persistDraft(latestDraftRef.current)
      }
      const draft = latestDraftRef.current ?? initialDraft
      const bpmnXml = await flowToBpmnXml(draft)
      // The bundle validator requires >=1 user + >=1 test-case so the
      // produced zip is "runnable". If the wizard hasn't gathered them
      // yet (early-stage Cook new flow), stub a placeholder so download
      // remains demoable. The TODO marker keeps the unfinished signal
      // present in the spec.md the builder emits.
      const testCases = draft.testCases.length > 0 ? draft.testCases : [{
        id: 'placeholder-happy',
        name: 'Happy path (placeholder — fill in step 8)',
        inputs: {},
        expectedTrace: [],
        expectedFinalStatus: 'Completed',
      }]
      const res = await fetch(`/api/flows/${flow.id}/bundle`, {
        method: 'POST',
        credentials: 'include',
        headers: { 'Content-Type': 'application/json' },
        body: JSON.stringify({
          bpmnXml,
          sampleOrg: draft.sampleOrg,
          testCases,
          sourceInstanceId: `flow:${flow.id}`,
        }),
      })
      if (!res.ok) throw new Error(`HTTP ${res.status}: ${await res.text()}`)
      const blob = await res.blob()
      const url = URL.createObjectURL(blob)
      const a = document.createElement('a')
      a.href = url
      a.download = `${flow.flowCode}_v${flow.version}.zip`
      document.body.appendChild(a)
      a.click()
      a.remove()
      URL.revokeObjectURL(url)
    } catch (err) {
      window.alert(err instanceof Error ? err.message : 'Bundle download failed')
    } finally {
      setDownloading(false)
    }
  }

  async function softDelete() {
    if (!window.confirm(`Delete draft "${flow.displayName}"? This soft-deletes the row.`)) return
    setTransitionPending('delete')
    try {
      await deleteFlow(flow.id)
      onClose()
    } catch (err) {
      window.alert(err instanceof Error ? err.message : 'Delete failed')
      setTransitionPending(null)
    }
  }

  async function clone() {
    setTransitionPending('clone')
    try {
      // Flush autosave so the new version snapshots the latest spec.
      if (timerRef.current) {
        window.clearTimeout(timerRef.current)
        if (latestDraftRef.current) await persistDraft(latestDraftRef.current)
      }
      const next = await cloneFlowVersion(flow.id)
      // Navigate to the freshly-cloned draft (sibling route under /ai-kitchen).
      navigate(`../${next.id}`)
    } catch (err) {
      window.alert(err instanceof Error ? err.message : 'Clone failed')
    } finally {
      setTransitionPending(null)
    }
  }

  const canSubmit = flow.state === 'Draft'
  const canCancel = flow.state === 'Submitted' || flow.state === 'Cooking' || flow.state === 'OnHold'
  const canDelete = flow.state === 'Draft'

  // PR-X2: run every step's validator before allowing Submit; surfaces
  // the failing steps in the disabled button's tooltip so the user
  // knows what to fix without diving back into the wizard.
  const submitGate = useMemo(() => {
    const failures = ONBOARDING_STEPS
      .map(s => ({ step: s, result: validators[s.id](currentDraft) }))
      .filter(x => !x.result.valid)
    return { ready: failures.length === 0, failures }
  }, [currentDraft])

  // Push flow context into the AppShell top bar (kicker + back + title +
  // version + state). Save status sits with the action buttons in the
  // main page header, not here. Memoize so the effect only fires when
  // values actually change. StatePill reflects `mockState` so cook
  // simulations (Cooking ↔ OnHold ↔ Committed) update the top-bar pill.
  const pageHeader = useMemo<PageHeader>(() => ({
    back: { label: 'Back to kitchen', onClick: onClose },
    title: flow.displayName,
    subtitle: `${flow.flowCode} · v${flow.version}`,
    badges: <StatePill state={mockState} />,
  }), [flow.displayName, flow.flowCode, flow.version, mockState, onClose])
  useSetPageHeader(pageHeader)

  // PhaseTabs: Cook unlocks once chef takes over (state !== Draft).
  // Serve unlocks once at least one cook has completed.
  const phaseDefs = useMemo<PhaseDef[]>(() => {
    const cookOpen  = mockState !== 'Draft'
    const serveOpen = cookedCount > 0
    return [
      { id: 'prep',  label: 'Prep',  hint: '備料 — 用 wizard 把 spec 寫清楚' },
      {
        id: 'cook',  label: 'Cook',  hint: '烹調 — chef 與你的對話、版本紀錄',
        disabled: !cookOpen,
        disabledHint: 'Submit to chef 之後才會啟用',
      },
      {
        id: 'serve', label: 'Serve', hint: '上菜 — 把 cooked version 部署到 DEV / STG / PRD',
        disabled: !serveOpen,
        disabledHint: 'chef 完成第一次 cook 後才會啟用',
      },
    ]
  }, [mockState, cookedCount])

  const overflowGroups: OverflowGroup[] = [
    {
      items: [
        {
          id: 'clone',
          label: transitionPending === 'clone' ? 'Cloning…' : 'Clone to new draft',
          icon: <Copy className="h-3 w-3" />,
          tone: 'productive',
          disabled: transitionPending !== null,
          hint: '把現在的 spec 複製一份成新草稿（v+1）',
          onClick: () => void clone(),
        },
      ],
    },
    {
      items: [
        ...(canCancel ? [{
          id: 'cancel',
          label: 'Cancel flow',
          icon: <Undo2 className="h-3 w-3" />,
          tone: 'risky' as const,
          disabled: transitionPending !== null,
          hint: '把流程回到 Draft 狀態',
          onClick: () => void cancel(),
        }] : []),
        ...(canDelete ? [{
          id: 'delete',
          label: 'Delete draft',
          icon: <Trash2 className="h-3 w-3" />,
          tone: 'danger' as const,
          disabled: transitionPending !== null,
          hint: 'Soft-delete — 可由 admin DB 還原',
          onClick: () => void softDelete(),
        }] : []),
      ],
    },
  ]

  return (
    <div className="flex h-full flex-col">
      {/* Phase tabs (Prep/Cook/Serve) on the left, primary actions +
          overflow menu on the right. Flow title / version / state pill /
          save status now live in the AppShell top bar via pageHeader. */}
      <div className="mb-4 flex items-center justify-between gap-3 flex-wrap">
        <PhaseTabs active={phase} onChange={setPhase} phases={phaseDefs} />

        <div className="flex items-center gap-2">
          {phase === 'prep' && <SaveStatusBadge state={saveState} error={saveError} />}
          <button
            onClick={() => void downloadBundle()}
            disabled={downloading || transitionPending !== null}
            title="Build a portable .zip bundle from the current draft"
            className="inline-flex items-center gap-1 rounded border border-rule bg-card px-2.5 py-1 text-xs font-medium text-ink-muted transition-colors hover:border-primary hover:text-primary disabled:opacity-50"
          >
            <Download className="h-3 w-3" />
            {downloading ? 'Bundling…' : 'Download bundle'}
          </button>
          {canSubmit && (
            <button
              onClick={() => void submit()}
              disabled={transitionPending !== null || !submitGate.ready}
              title={submitGate.ready
                ? 'Hand the draft to chef. State flips to Submitted.'
                : `尚未可送出 (${submitGate.failures.length} step 未通過)：\n${submitGate.failures.slice(0, 3).map(f => `· ${f.step.en} — ${f.result.errors[0] ?? 'validator failed'}`).join('\n')}${submitGate.failures.length > 3 ? `\n…還有 ${submitGate.failures.length - 3} 條` : ''}`}
              className="inline-flex items-center gap-1.5 rounded bg-primary px-3 py-1 text-xs font-semibold text-white transition-colors hover:bg-primary/90 disabled:opacity-50"
            >
              <CheckCircle2 className="h-3.5 w-3.5" />
              {transitionPending === 'submit'
                ? 'Submitting…'
                : submitGate.ready
                  ? 'Submit to chef'
                  : `Submit to chef (${submitGate.failures.length} step 未通過)`}
            </button>
          )}
          <OverflowMenu groups={overflowGroups} />
        </div>
      </div>

      <div className="flex-1 min-h-0">
        {phase === 'prep' && (
          <Onboarding
            initialDraft={initialDraft}
            onDraftChange={handleDraftChange}
            hideTopBar
          />
        )}
        {phase === 'cook' && (
          <CookPanel
            flowId={flow.id}
            flowVersion={flow.version}
            state={mockState}
            onStateChange={setMockState}
          />
        )}
        {phase === 'serve' && (
          <ServePanel
            flowVersion={flow.version}
            cookedCount={cookedCount}
            deployments={deployments}
            setDeployments={setDeployments}
          />
        )}
      </div>
    </div>
  )
}

function SaveStatusBadge({ state, error }: { state: 'idle' | 'saving' | 'saved' | 'error'; error: string | null }) {
  if (state === 'saving') {
    return (
      <span className="inline-flex items-center gap-1 font-mono text-[10px] tracking-[0.14em] uppercase text-ink-muted">
        <Save className="h-3 w-3 animate-pulse" /> Saving…
      </span>
    )
  }
  if (state === 'saved') {
    return (
      <span className="inline-flex items-center gap-1 font-mono text-[10px] tracking-[0.14em] uppercase text-good">
        <CheckCircle2 className="h-3 w-3" /> Saved
      </span>
    )
  }
  if (state === 'error') {
    return (
      <span title={error ?? undefined} className="inline-flex items-center gap-1 font-mono text-[10px] tracking-[0.14em] uppercase text-danger">
        <X className="h-3 w-3" /> Save failed
      </span>
    )
  }
  return null
}

// ──────────────────────────────────────────────────────────────
// Shared bits
// ──────────────────────────────────────────────────────────────

const STATE_TONE: Record<FlowState, string> = {
  Draft:     'bg-ink-muted/15 text-ink-muted',
  Submitted: 'bg-primary/10 text-primary',
  Cooking:   'bg-accent/15 text-accent',
  OnHold:    'bg-warn/15 text-warn',
  Committed: 'bg-good/10 text-good',
  Approved:  'bg-good/15 text-good',
  Rejected:  'bg-danger/10 text-danger',
  Retired:   'bg-slate-200 text-slate-600',
}

function StatePill({ state }: { state: FlowState }) {
  return (
    <span className={cn(
      'inline-block rounded-full px-2 py-0.5 font-mono text-[10px] tracking-[0.12em] uppercase',
      STATE_TONE[state],
    )}>
      {state.replace(/([a-z])([A-Z])/g, '$1 $2').toLowerCase()}
    </span>
  )
}

function GroupChip({ flow, groups }: { flow: FlowSummary; groups: FlowGroupDto[] }) {
  if (!flow.groupId) {
    return <span className="rounded-full bg-slate-100 px-2 py-0.5 font-mono text-[10px] tracking-wide text-ink-faint">未分組</span>
  }
  const group = groups.find(g => g.id === flow.groupId)
  if (!group) {
    return <span className="rounded-full bg-warn/15 px-2 py-0.5 font-mono text-[10px] tracking-wide text-warn">已刪除</span>
  }
  const Icon = resolveIcon(group.icon)
  return (
    <span className="inline-flex items-center gap-1 rounded-full bg-primary/10 px-2 py-0.5 text-[11px] text-primary">
      <Icon className="h-3 w-3" />
      {group.displayName['zh-TW'] ?? group.code}
    </span>
  )
}

function ChefBranchChip({ flow }: { flow: FlowSummary }) {
  const ctx = parseChefWorkContext(flow.chefWorkContextJson)
  if (!ctx?.branch) return null
  return (
    <span
      title={ctx.notes ? `${ctx.notes}\nset ${ctx.setAt ?? ''}` : `set ${ctx.setAt ?? ''}`}
      className="inline-flex items-center gap-1 rounded-full border border-primary/30 bg-primary/10 px-1.5 py-0.5 font-mono text-[9px] tracking-wide text-primary"
    >
      <GitBranch className="h-2.5 w-2.5" />
      {ctx.branch}
    </span>
  )
}

const CHEF_STALL_TTL_MS = 30 * 60 * 1000

function isChefStalled(f: FlowSummary): boolean {
  if (f.state !== 'Cooking') return false
  if (!f.lastChefHeartbeatAt) return true
  const last = new Date(f.lastChefHeartbeatAt).getTime()
  if (Number.isNaN(last)) return false
  return Date.now() - last > CHEF_STALL_TTL_MS
}

function formatDate(s: string): string {
  try { return new Date(s).toISOString().slice(0, 16).replace('T', ' ') }
  catch { return s }
}
