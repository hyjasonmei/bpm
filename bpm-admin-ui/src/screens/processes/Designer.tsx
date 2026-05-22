/**
 * Process Designer (PR-K2 §3). Three-pane editor for an existing flow:
 *
 *   ┌─ toolbar (Save / Publish / Preview / Simulate) ─────────────────┐
 *   │ tree (~220px) │ BpmnEditor canvas             │ right pane (~360px) │
 *   └───────────────┴───────────────────────────────┴─────────────────────┘
 *
 * The Designer owns the in-memory `DraftSpec`. Two writers mutate it:
 *   - `BpmnEditor` (middle pane) for nodes / edges / labels
 *   - The Step* components (right pane) for everything else
 *
 * Selection flows both ways:
 *   - Tree click → set selectedId → BpmnEditor highlights the shape
 *   - Canvas click → BpmnEditor.onSelect → set selectedId → tree row marks
 *     active and the right pane router swaps Step components
 *
 * Per the task brief we deliberately ship Step* components UNFILTERED
 * (Option B): adding a `focusNodeId` prop to all five steps would touch
 * five files and rework their state for marginal v1 value. The user picks
 * a node from the tree/canvas and finds it inside the right-pane Step.
 *
 * Loading: when `flowCode` is provided we hydrate via either the latest
 * bundle (FlowLibrary) or — for filesystem-only definitions — a raw
 * spec.json fetch from the Process Admin endpoint added in §3.6.
 *
 * Concurrency (§3.5): the Designer remembers the source bundle's
 * manifestChecksum. Pre-publish we re-hit `listVersions` and warn if a
 * newer bundle has appeared since we loaded. The `POST /build` endpoint
 * is checksum-idempotent so there's no real 409 path on the wire.
 */

import { useCallback, useEffect, useMemo, useState } from 'react'
import {
  Save, Upload, Eye, Play as PlayIcon, GitFork, Workflow, Bell,
  CheckSquare, ListChecks, Settings, AlertTriangle, CheckCircle2,
} from 'lucide-react'
import { Button } from '@/components/ui/button'
import { ConfirmDialog } from '@/components/ui/confirm-dialog'
import { BpmnEditor } from '@/components/BpmnEditor'
import { StepForms } from '@/screens/onboarding/steps/StepForms'
import { StepApprovers } from '@/screens/onboarding/steps/StepApprovers'
import { StepNotify } from '@/screens/onboarding/steps/StepNotify'
import { StepDecisions } from '@/screens/onboarding/steps/StepDecisions'
import { StepSla } from '@/screens/onboarding/steps/StepSla'
import {
  EMPTY_DRAFT, migrateDraft, type DraftSpec, type FlowNode,
} from '@/lib/onboarding'
import { flowToBpmnXml } from '@/lib/bpmnXml'
import {
  buildAndSaveBundle, getBundleDraftHydration, listBundles,
} from '@/lib/api/flowLibrary'
import { getSpecJson, listVersions } from '@/lib/api/processAdmin'

interface DesignerProps {
  /**
   * If set, load this flow on mount. Null / undefined means "+ New Flow"
   * → start from EMPTY_DRAFT.
   */
  flowCode?: string | null
  /** Caller supplies this so the toolbar's "Simulate" can hop sections. */
  onJumpToSimulator?: () => void
}

interface PublishResult {
  bundleId: string
  status: string
  manifestChecksum: string
}

function draftStorageKey(flowCode: string | null | undefined): string {
  return `bpm_designer_draft_${flowCode ?? '__new__'}`
}

export function Designer({ flowCode, onJumpToSimulator }: DesignerProps) {
  const [draft, setDraft] = useState<DraftSpec>(EMPTY_DRAFT)
  const [loading, setLoading] = useState<boolean>(!!flowCode)
  const [loadError, setLoadError] = useState<string | null>(null)
  const [selectedId, setSelectedId] = useState<string | null>(null)

  // Source-bundle bookkeeping for §3.5 OCC. Set when we hydrate from a
  // SpecBundle row; null for filesystem-only and "+ New Flow".
  const [sourceChecksum, setSourceChecksum] = useState<string | null>(null)
  const [sourceBundleId, setSourceBundleId] = useState<string | null>(null)
  const [sourceVersion, setSourceVersion] = useState<number | null>(null)

  const [savedAt, setSavedAt] = useState<string | null>(null)
  const [publishResult, setPublishResult] = useState<PublishResult | null>(null)
  const [publishing, setPublishing] = useState(false)
  const [publishError, setPublishError] = useState<string | null>(null)
  const [publishWarning, setPublishWarning] = useState<string | null>(null)
  const [confirmPublish, setConfirmPublish] = useState(false)
  const [showPreview, setShowPreview] = useState(false)

  // ── Hydrate the draft on mount / when flowCode changes ──
  useEffect(() => {
    let cancelled = false
    void (async () => {
      setLoading(true)
      setLoadError(null)
      // Always check localStorage first — Save persists drafts there per
      // (flowCode); reload should resurrect mid-edit work.
      const lsKey = draftStorageKey(flowCode ?? null)
      try {
        const cached = localStorage.getItem(lsKey)
        if (cached) {
          const parsed = JSON.parse(cached)
          if (cancelled) return
          setDraft(migrateDraft(parsed))
          setSavedAt('(loaded from local draft)')
          // Even when loaded from localStorage we still want the
          // server-side version metadata for OCC, so we fall through.
        }
      } catch { /* ignore corrupt cache */ }

      if (!flowCode) {
        if (!cancelled) setLoading(false)
        return
      }

      try {
        // 1. Find the latest bundle for this flowCode (if any).
        const bundles = await listBundles()
        const matching = bundles.filter(b => b.flowCode === flowCode)
          .sort((a, b) => b.flowVersion - a.flowVersion)
        const latest = matching[0]

        if (latest) {
          const hydration = await getBundleDraftHydration(latest.id)
          if (cancelled) return
          // hydration.specJson is the wizard-shape draft. Push it through
          // migrateDraft so the sampleOrg / testCases pieces are
          // back-filled if missing.
          const merged = migrateDraft({
            ...(hydration.specJson as object),
            sampleOrg: hydration.sampleOrg,
            testCases: hydration.testCases,
          })
          setDraft(merged)
          // The manifest's own checksum field isn't present (the manifest
          // is *what's* checksummed); pull it from the SpecBundle row.
          setSourceChecksum(latest.manifestChecksum)
          setSourceBundleId(latest.id)
          setSourceVersion(latest.flowVersion)
        } else {
          // 2. No bundle — pull raw spec.json from the Process Admin endpoint.
          const text = await getSpecJson(flowCode)
          if (cancelled) return
          const parsed = JSON.parse(text)
          setDraft(migrateDraft(parsed))
          setSourceChecksum(null)
          setSourceBundleId(null)
          setSourceVersion(null)
        }
      } catch (e) {
        if (cancelled) return
        setLoadError(e instanceof Error ? e.message : String(e))
      } finally {
        if (!cancelled) setLoading(false)
      }
    })()
    return () => { cancelled = true }
  }, [flowCode])

  const selectedNode: FlowNode | null = useMemo(() => {
    if (!selectedId) return null
    return draft.flow.nodes.find(n => n.id === selectedId) ?? null
  }, [selectedId, draft.flow.nodes])

  const handleSave = useCallback(() => {
    try {
      const key = draftStorageKey(flowCode ?? null)
      localStorage.setItem(key, JSON.stringify(draft))
      setSavedAt(new Date().toLocaleTimeString())
    } catch (e) {
      setLoadError(`Save failed: ${e instanceof Error ? e.message : String(e)}`)
    }
  }, [draft, flowCode])

  const handlePublishConfirm = useCallback(async () => {
    setConfirmPublish(false)
    setPublishing(true)
    setPublishError(null)
    setPublishWarning(null)
    try {
      // Pre-publish OCC check — only meaningful when we hydrated from a
      // bundle. If a newer version exists since we loaded, warn first.
      if (flowCode && sourceVersion !== null) {
        try {
          const versions = await listVersions(flowCode)
          const latestOnServer = versions[0]?.flowVersion ?? 0
          if (latestOnServer > sourceVersion) {
            setPublishWarning(
              `This flow has been updated by another admin (v${latestOnServer}) `
              + `since you started editing (v${sourceVersion}). The new bundle `
              + `will still be created, but consider reloading first.`,
            )
          }
        } catch { /* OCC check is advisory; never block on its failure */ }
      }

      const bpmnXml = await flowToBpmnXml(draft)
      const result = await buildAndSaveBundle({
        specJson: draft,
        bpmnXml,
        sampleOrg: draft.sampleOrg,
        testCases: draft.testCases,
      })
      setPublishResult({
        bundleId: result.id,
        status: result.status,
        manifestChecksum: result.manifestChecksum,
      })
    } catch (e) {
      setPublishError(e instanceof Error ? e.message : String(e))
    } finally {
      setPublishing(false)
    }
  }, [draft, flowCode, sourceVersion])

  if (loading) {
    return <div className="rounded border border-rule bg-card p-10 text-center text-sm text-ink-muted">Loading designer…</div>
  }

  return (
    <div className="space-y-3">
      {loadError && (
        <div className="rounded-md border border-red-200 bg-red-50 p-3 text-sm text-red-800">
          <AlertTriangle className="mr-2 inline h-4 w-4" /> {loadError}
        </div>
      )}

      <DesignerToolbar
        flowCode={draft.meta.flowCode || flowCode || '(unsaved)'}
        flowName={draft.meta.flowName}
        sourceVersion={sourceVersion}
        sourceBundleId={sourceBundleId}
        sourceChecksum={sourceChecksum}
        savedAt={savedAt}
        onSave={handleSave}
        onPublish={() => setConfirmPublish(true)}
        onPreview={() => setShowPreview(true)}
        onSimulate={() => onJumpToSimulator?.()}
        publishing={publishing}
      />

      <div className="grid grid-cols-[220px_minmax(0,1fr)_360px] gap-3">
        {/* Left — node tree */}
        <NodeTree
          nodes={draft.flow.nodes}
          selectedId={selectedId}
          onSelect={setSelectedId}
        />

        {/* Middle — canvas */}
        <div className="min-w-0">
          <BpmnEditor
            draft={draft}
            onChange={setDraft}
            onSelect={setSelectedId}
            selectedId={selectedId}
            height={520}
          />
        </div>

        {/* Right — per-node editor */}
        <RightPane
          node={selectedNode}
          draft={draft}
          setDraft={setDraft}
        />
      </div>

      <ConfirmDialog
        open={confirmPublish}
        title="Publish a new version?"
        titleZh="發佈新版本"
        description={
          sourceVersion
            ? `This builds a new SpecBundle (Pending) for ${flowCode}. The current latest is v${sourceVersion}.`
            : `This builds a new SpecBundle (Pending) from the current draft.`
        }
        confirmText="Publish"
        tone="default"
        onConfirm={handlePublishConfirm}
        onCancel={() => setConfirmPublish(false)}
      />

      {publishResult && (
        <PublishResultModal result={publishResult} warning={publishWarning} onClose={() => setPublishResult(null)} />
      )}

      {publishError && (
        <div className="rounded-md border border-red-200 bg-red-50 p-3 text-sm text-red-800">
          <AlertTriangle className="mr-2 inline h-4 w-4" /> Publish failed: {publishError}
        </div>
      )}

      {showPreview && (
        <PreviewModal draft={draft} onClose={() => setShowPreview(false)} />
      )}
    </div>
  )
}

/* ============================================================ *
 * Toolbar
 * ============================================================ */

interface ToolbarProps {
  flowCode: string
  flowName: string
  sourceVersion: number | null
  sourceBundleId: string | null
  sourceChecksum: string | null
  savedAt: string | null
  onSave: () => void
  onPublish: () => void
  onPreview: () => void
  onSimulate: () => void
  publishing: boolean
}

function DesignerToolbar(p: ToolbarProps) {
  return (
    <div className="flex flex-wrap items-center justify-between gap-2 rounded-lg border border-rule bg-card p-2">
      <div className="flex min-w-0 items-center gap-2 px-2">
        <Workflow className="h-4 w-4 text-ink-muted" />
        <h2 className="truncate text-sm font-semibold text-ink">
          {p.flowName || p.flowCode}
          <span className="ml-2 text-ink-muted">
            {p.sourceVersion ? `(loaded v${p.sourceVersion})` : '(new)'}
          </span>
        </h2>
        {p.sourceChecksum && (
          <span className="font-mono text-[10px] text-ink-faint" title={p.sourceChecksum}>
            {p.sourceChecksum.slice(0, 8)}…
          </span>
        )}
        {p.savedAt && <span className="text-[11px] text-ink-faint">Saved {p.savedAt}</span>}
      </div>
      <div className="flex items-center gap-1.5">
        <Button variant="outline" size="sm" onClick={p.onSave} title="Persist draft to localStorage">
          <Save className="h-3.5 w-3.5" /> Save
        </Button>
        <Button variant="outline" size="sm" onClick={p.onPreview}>
          <Eye className="h-3.5 w-3.5" /> Preview
        </Button>
        <Button variant="outline" size="sm" onClick={p.onSimulate}>
          <PlayIcon className="h-3.5 w-3.5" /> Simulate
        </Button>
        <Button variant="primary" size="sm" onClick={p.onPublish} disabled={p.publishing}>
          <Upload className="h-3.5 w-3.5" /> {p.publishing ? 'Publishing…' : 'Publish'}
        </Button>
      </div>
    </div>
  )
}

/* ============================================================ *
 * Left pane — hierarchical node tree
 * ============================================================ */

interface NodeTreeProps {
  nodes: FlowNode[]
  selectedId: string | null
  onSelect: (id: string) => void
}

const TYPE_GROUPS: { type: FlowNode['type']; label: string; icon: React.ReactNode }[] = [
  { type: 'startEvent',  label: 'Start',         icon: <PlayIcon className="h-3.5 w-3.5" /> },
  { type: 'userTask',    label: 'User Tasks',    icon: <ListChecks className="h-3.5 w-3.5" /> },
  { type: 'approval',    label: 'Approvals',     icon: <CheckSquare className="h-3.5 w-3.5" /> },
  { type: 'gateway',     label: 'Gateways',      icon: <GitFork className="h-3.5 w-3.5" /> },
  { type: 'notify',      label: 'Notifications', icon: <Bell className="h-3.5 w-3.5" /> },
  { type: 'serviceTask', label: 'System Tasks',  icon: <Settings className="h-3.5 w-3.5" /> },
  { type: 'endEvent',    label: 'End',           icon: <CheckCircle2 className="h-3.5 w-3.5" /> },
]

function NodeTree({ nodes, selectedId, onSelect }: NodeTreeProps) {
  return (
    <aside className="rounded-lg border border-rule bg-card p-2 text-sm">
      <div className="mb-2 px-1 text-[11px] font-semibold uppercase tracking-wider text-ink-muted">
        Nodes
      </div>
      {nodes.length === 0 && (
        <div className="px-2 py-3 text-xs text-ink-faint">
          No nodes yet. Add shapes from the canvas palette.
        </div>
      )}
      {TYPE_GROUPS.map(g => {
        const rows = nodes.filter(n => n.type === g.type)
        if (rows.length === 0) return null
        return (
          <div key={g.type} className="mb-2">
            <div className="flex items-center gap-1.5 px-1 text-[11px] font-medium text-ink-muted">
              {g.icon}
              <span>{g.label}</span>
              <span className="ml-auto text-ink-faint">{rows.length}</span>
            </div>
            <ul className="mt-1 space-y-0.5">
              {rows.map(n => {
                const active = n.id === selectedId
                return (
                  <li key={n.id}>
                    <button
                      onClick={() => onSelect(n.id)}
                      className={
                        'block w-full truncate rounded px-2 py-1 text-left text-xs '
                        + (active
                          ? 'bg-slate-200 font-medium text-ink'
                          : 'text-ink-muted hover:bg-slate-50 hover:text-ink')
                      }
                      title={`${n.id} — ${n.label}`}
                    >
                      {n.label || n.id}
                    </button>
                  </li>
                )
              })}
            </ul>
          </div>
        )
      })}
    </aside>
  )
}

/* ============================================================ *
 * Right pane — per-node editor router
 * ============================================================ */

interface RightPaneProps {
  node: FlowNode | null
  draft: DraftSpec
  setDraft: (d: DraftSpec) => void
}

function RightPane({ node, draft, setDraft }: RightPaneProps) {
  const containerCls = 'min-w-0 overflow-auto rounded-lg border border-rule bg-card p-3'
  if (!node) {
    return (
      <aside className={containerCls + ' text-sm text-ink-muted'}>
        Pick a node from the tree or canvas to edit.
      </aside>
    )
  }
  return (
    <aside className={containerCls}>
      <div className="mb-3 border-b border-rule pb-2">
        <div className="text-[11px] uppercase tracking-wider text-ink-muted">{node.type}</div>
        <div className="text-sm font-semibold text-ink">{node.label}</div>
        <div className="font-mono text-[10px] text-ink-faint">{node.id}</div>
      </div>
      <RightPaneEditor node={node} draft={draft} setDraft={setDraft} />
    </aside>
  )
}

function RightPaneEditor({ node, draft, setDraft }: RightPaneProps) {
  switch (node!.type) {
    case 'userTask':
      return <StepForms draft={draft} setDraft={setDraft} />
    case 'approval':
      return <StepApprovers draft={draft} setDraft={setDraft} />
    case 'notify':
      return <StepNotify draft={draft} setDraft={setDraft} />
    case 'gateway':
      return <StepDecisions draft={draft} setDraft={setDraft} />
    case 'serviceTask':
      return (
        <div className="space-y-2 text-sm">
          <p className="text-ink-muted">No editor for ServiceTask yet.</p>
          <p className="text-xs text-ink-faint">SLA timers (the closest existing config) are below.</p>
          <StepSla draft={draft} setDraft={setDraft} />
        </div>
      )
    case 'startEvent':
    case 'endEvent':
      return (
        <p className="text-sm text-ink-muted">
          No per-node editor; rename via the canvas (double-click the shape).
        </p>
      )
    default:
      return <p className="text-sm text-ink-muted">Unrecognised node type.</p>
  }
}

/* ============================================================ *
 * Modals — Publish result + Preview
 * ============================================================ */

interface PublishResultModalProps {
  result: PublishResult
  warning: string | null
  onClose: () => void
}

function PublishResultModal({ result, warning, onClose }: PublishResultModalProps) {
  return (
    <Modal title="Bundle published" onClose={onClose}>
      <div className="space-y-3 text-sm">
        {warning && (
          <div className="flex items-start gap-2 rounded border border-amber-300 bg-amber-50 p-2 text-amber-800">
            <AlertTriangle className="mt-0.5 h-4 w-4 shrink-0" />
            <div>{warning}</div>
          </div>
        )}
        <div>
          New SpecBundle row: <span className="font-mono text-xs">{result.bundleId}</span>
        </div>
        <div>Status: <span className="font-medium">{result.status}</span></div>
        <div>Checksum: <span className="font-mono text-xs">{result.manifestChecksum}</span></div>
        <p className="text-xs text-ink-muted">
          Open the Flow Library screen (Admin sidebar → Flow Library) to verify and Install.
        </p>
      </div>
    </Modal>
  )
}

interface PreviewModalProps {
  draft: DraftSpec
  onClose: () => void
}

function PreviewModal({ draft, onClose }: PreviewModalProps) {
  // [~] spec.json text preview; live form preview deferred (DynamicForm
  // lives in bpm-ui and the demo guard forbids cross-package import).
  const text = JSON.stringify(draft, null, 2)
  return (
    <Modal title="Preview — spec.json" onClose={onClose} wide>
      <pre className="max-h-[70vh] overflow-auto rounded border border-rule bg-slate-50 p-3 text-[11px] leading-snug text-ink">
{text}
      </pre>
      <p className="mt-2 text-[11px] text-ink-faint">
        Spec text preview; live form preview deferred.
      </p>
    </Modal>
  )
}

interface ModalProps {
  title: string
  onClose: () => void
  wide?: boolean
  children: React.ReactNode
}

function Modal({ title, onClose, wide, children }: ModalProps) {
  return (
    <div
      className="fixed inset-0 z-50 flex items-start justify-center overflow-auto bg-black/40 p-6"
      onClick={onClose}
    >
      <div
        className={`my-auto w-full ${wide ? 'max-w-6xl' : 'max-w-2xl'} rounded-lg bg-white p-4 shadow-xl`}
        onClick={(e) => e.stopPropagation()}
      >
        <div className="mb-3 flex items-center justify-between">
          <h3 className="text-base font-semibold text-ink">{title}</h3>
          <button
            onClick={onClose}
            className="rounded px-2 py-1 text-sm text-ink-muted hover:bg-slate-100 hover:text-ink"
          >
            Close
          </button>
        </div>
        {children}
      </div>
    </div>
  )
}
