/**
 * Flow Definitions list (PR-K1 §2). Two stacked sections:
 *   - "Installed bundles" — SpecBundles surfaced via the new
 *     /api/admin/process-admin/definitions endpoint, source = "bundle".
 *   - "Filesystem specs"  — same endpoint, source = "filesystem".
 *
 * Filesystem rows are still shown (a) because the runtime FileSystemSpecLoader
 * resolves them today and (b) so admins can see what flows exist outside the
 * Flow Library. The backend already dedups: a bundle for FlowCode hides the
 * filesystem entry of the same code.
 *
 * Per row actions:
 *   - Open in Designer       — placeholder navigation; PR-K2 wires the real editor
 *   - View versions          — side panel with bundle version history (§2.3)
 *   - View spec              — modal showing raw spec.json
 *   - Diff against previous  — side-by-side text view of v_n vs v_(n-1) (§2.4)
 */

import { useCallback, useEffect, useState } from 'react'
import { FileText, Plus, Eye, History, GitCompare, AlertCircle, FolderOpen, Pencil } from 'lucide-react'
import { Button } from '@/components/ui/button'
import { listDefinitions, listVersions } from '@/lib/api/processAdmin'
import type { FlowDefinitionDto, FlowVersionDto } from '@/lib/api/processAdmin'
import { getBundleFileText } from '@/lib/api/flowLibrary'

interface DefinitionsListProps {
  /**
   * Open the Designer for an existing flow (pass its flowCode) or for a
   * brand-new flow (pass null). The shell stashes the value and switches
   * to the designer section in one shot.
   */
  onOpenDesigner: (flowCode: string | null) => void
}

export function DefinitionsList({ onOpenDesigner }: DefinitionsListProps) {
  const [items, setItems] = useState<FlowDefinitionDto[] | null>(null)
  const [loadError, setLoadError] = useState<string | null>(null)

  // Side panel + modal state.
  const [versionsFor, setVersionsFor] = useState<string | null>(null)
  const [viewSpec, setViewSpec] = useState<{ flowCode: string; bundleId: string } | null>(null)
  const [diffFor, setDiffFor] = useState<{ flowCode: string } | null>(null)

  const refresh = useCallback(async () => {
    setLoadError(null)
    try {
      setItems(await listDefinitions())
    } catch (e) {
      setLoadError(e instanceof Error ? e.message : String(e))
      setItems([])
    }
  }, [])

  useEffect(() => { void refresh() }, [refresh])

  const bundles = (items ?? []).filter(i => i.source === 'bundle')
  const fsSpecs = (items ?? []).filter(i => i.source === 'filesystem')

  return (
    <div className="space-y-6">
      <div className="flex items-center justify-between">
        <div className="flex items-center gap-2">
          <FileText className="h-5 w-5 text-ink-muted" />
          <h2 className="text-lg font-semibold text-ink">Definitions</h2>
        </div>
        <Button variant="primary" onClick={() => onOpenDesigner(null)} title="Open the Designer to author a new flow">
          <Plus className="h-4 w-4" /> New Flow
        </Button>
      </div>

      {loadError && (
        <div className="rounded-md border border-red-200 bg-red-50 p-3 text-sm text-red-800">
          <AlertCircle className="mr-2 inline h-4 w-4" /> Failed to load definitions: {loadError}
        </div>
      )}

      {items === null && !loadError && (
        <div className="text-sm text-ink-muted">Loading…</div>
      )}

      {items !== null && (
        <>
          <Section
            title="Installed bundles"
            subtitle="From the Flow Library — these are the canonical definitions."
            icon={<FileText className="h-4 w-4" />}
            empty="No bundles installed yet. Import a bundle in Flow Library to populate."
            rows={bundles}
            renderRow={(d) => (
              <DefinitionRow
                key={`bundle-${d.bundleId ?? d.flowCode}`}
                def={d}
                onOpenDesigner={onOpenDesigner}
                onViewVersions={() => setVersionsFor(d.flowCode)}
                onViewSpec={() => d.bundleId && setViewSpec({ flowCode: d.flowCode, bundleId: d.bundleId })}
                onDiff={() => setDiffFor({ flowCode: d.flowCode })}
              />
            )}
          />

          <Section
            title="Filesystem specs"
            subtitle={`Loose JSON files in sample_specs/. The FileSystemSpecLoader still resolves these at runtime.`}
            icon={<FolderOpen className="h-4 w-4" />}
            empty="No loose spec files."
            rows={fsSpecs}
            renderRow={(d) => (
              <DefinitionRow
                key={`fs-${d.flowCode}`}
                def={d}
                onOpenDesigner={onOpenDesigner}
                onViewVersions={() => setVersionsFor(d.flowCode)}
                onViewSpec={undefined}
                onDiff={undefined}
              />
            )}
          />
        </>
      )}

      {versionsFor && (
        <VersionsPanel
          flowCode={versionsFor}
          onClose={() => setVersionsFor(null)}
          onViewSpec={(bundleId) => {
            setViewSpec({ flowCode: versionsFor, bundleId })
          }}
        />
      )}

      {viewSpec && (
        <SpecViewerModal
          flowCode={viewSpec.flowCode}
          bundleId={viewSpec.bundleId}
          onClose={() => setViewSpec(null)}
        />
      )}

      {diffFor && (
        <DiffModal flowCode={diffFor.flowCode} onClose={() => setDiffFor(null)} />
      )}
    </div>
  )
}

interface SectionProps<T> {
  title: string
  subtitle: string
  icon: React.ReactNode
  empty: string
  rows: T[]
  renderRow: (row: T) => React.ReactNode
}

function Section<T>({ title, subtitle, icon, empty, rows, renderRow }: SectionProps<T>) {
  return (
    <section>
      <div className="mb-2 flex items-center gap-2">
        <span className="text-ink-muted">{icon}</span>
        <h3 className="text-sm font-semibold text-ink">{title}</h3>
        <span className="rounded bg-slate-100 px-1.5 py-0.5 text-[10px] font-medium text-slate-600">
          {rows.length}
        </span>
      </div>
      <p className="mb-2 text-xs text-ink-muted">{subtitle}</p>
      {rows.length === 0 ? (
        <div className="rounded-md border border-dashed border-rule bg-card px-4 py-6 text-center text-xs text-ink-muted">
          {empty}
        </div>
      ) : (
        <div className="space-y-2">{rows.map(renderRow)}</div>
      )}
    </section>
  )
}

interface DefinitionRowProps {
  def: FlowDefinitionDto
  onOpenDesigner: (flowCode: string) => void
  onViewVersions: () => void
  onViewSpec?: () => void
  onDiff?: () => void
}

function DefinitionRow({ def, onOpenDesigner, onViewVersions, onViewSpec, onDiff }: DefinitionRowProps) {
  const lastModified = def.lastModifiedAt ? formatDate(def.lastModifiedAt) : '—'
  const statusLabel = def.source === 'bundle' ? statusPill(def.status) : <FsBadge />
  return (
    <div className="flex items-start justify-between gap-4 rounded-lg border border-rule bg-card p-3">
      <div className="min-w-0 flex-1 space-y-1">
        <div className="flex items-center gap-2">
          <h4 className="truncate text-sm font-semibold text-ink">
            {def.flowCode} <span className="text-ink-muted">v{def.version}</span>
          </h4>
          {statusLabel}
        </div>
        <div className="flex flex-wrap gap-x-4 gap-y-0.5 text-[11px] text-ink-muted">
          <span>Updated {lastModified}</span>
          {def.bundleId && (
            <span className="font-mono opacity-70" title={def.bundleId}>
              bundle {def.bundleId.slice(0, 8)}…
            </span>
          )}
        </div>
      </div>
      <div className="flex shrink-0 items-center gap-1.5">
        <Button variant="outline" size="xs" onClick={() => onOpenDesigner(def.flowCode)}>
          <Pencil className="h-3.5 w-3.5" /> Open in Designer
        </Button>
        <Button variant="outline" size="xs" onClick={onViewVersions}>
          <History className="h-3.5 w-3.5" /> Versions
        </Button>
        {onViewSpec && (
          <Button variant="outline" size="xs" onClick={onViewSpec}>
            <Eye className="h-3.5 w-3.5" /> View spec
          </Button>
        )}
        {onDiff && (
          <Button variant="outline" size="xs" onClick={onDiff} title="Side-by-side diff against the previous version">
            <GitCompare className="h-3.5 w-3.5" /> Diff
          </Button>
        )}
      </div>
    </div>
  )
}

function statusPill(status: string | null): React.ReactNode {
  if (!status) return null
  const tone = (() => {
    switch (status) {
      case 'installed': return 'bg-emerald-100 text-emerald-700'
      case 'pending':
      case 'draft': return 'bg-amber-100 text-amber-700'
      case 'failed': return 'bg-rose-100 text-rose-700'
      case 'installedUnverified': return 'bg-sky-100 text-sky-700'
      default: return 'bg-slate-100 text-slate-700'
    }
  })()
  return (
    <span className={`rounded px-1.5 py-0.5 text-[10px] font-medium ${tone}`}>
      {status}
    </span>
  )
}

function FsBadge() {
  return (
    <span
      title="Loose spec file under sample_specs/"
      className="rounded bg-slate-100 px-1.5 py-0.5 text-[10px] font-medium text-slate-600"
    >
      FS
    </span>
  )
}

/* ============================================================ *
 * Side panel — version history (§2.3)
 * ============================================================ */

interface VersionsPanelProps {
  flowCode: string
  onClose: () => void
  onViewSpec: (bundleId: string) => void
}

function VersionsPanel({ flowCode, onClose, onViewSpec }: VersionsPanelProps) {
  const [versions, setVersions] = useState<FlowVersionDto[] | null>(null)
  const [error, setError] = useState<string | null>(null)

  useEffect(() => {
    void (async () => {
      try {
        setVersions(await listVersions(flowCode))
      } catch (e) {
        setError(e instanceof Error ? e.message : String(e))
        setVersions([])
      }
    })()
  }, [flowCode])

  return (
    <Drawer onClose={onClose} title={`${flowCode} — version history`}>
      {error && (
        <div className="rounded-md border border-red-200 bg-red-50 p-3 text-sm text-red-800">
          {error}
        </div>
      )}
      {versions === null && !error && <p className="text-sm text-ink-muted">Loading…</p>}
      {versions !== null && versions.length === 0 && (
        <p className="text-sm text-ink-muted">
          No bundle versions for this flow code. (Filesystem-only definitions don't have a version history.)
        </p>
      )}
      {versions !== null && versions.length > 0 && (
        <table className="w-full text-sm">
          <thead className="text-left text-[11px] uppercase tracking-wider text-ink-muted">
            <tr>
              <th className="py-2">Version</th>
              <th>Status</th>
              <th>Exported</th>
              <th>Last repro</th>
              <th>Checksum</th>
              <th />
            </tr>
          </thead>
          <tbody>
            {versions.map(v => (
              <tr key={v.bundleId} className="border-t border-rule">
                <td className="py-2 font-medium text-ink">v{v.flowVersion}</td>
                <td>{statusPill(v.status)}</td>
                <td className="text-ink-muted">{formatDate(v.exportedAt)}</td>
                <td className="text-ink-muted">{v.lastReproCheckAt ? formatDate(v.lastReproCheckAt) : '—'}</td>
                <td className="font-mono text-[11px] text-ink-muted" title={v.manifestChecksum}>
                  {v.manifestChecksum.slice(0, 8)}…
                </td>
                <td className="text-right">
                  <Button variant="outline" size="xs" onClick={() => onViewSpec(v.bundleId)}>
                    <Eye className="h-3.5 w-3.5" /> spec
                  </Button>
                </td>
              </tr>
            ))}
          </tbody>
        </table>
      )}
    </Drawer>
  )
}

/* ============================================================ *
 * Spec viewer modal (§2.1 "View spec")
 * ============================================================ */

interface SpecViewerModalProps {
  flowCode: string
  bundleId: string
  onClose: () => void
}

function SpecViewerModal({ flowCode, bundleId, onClose }: SpecViewerModalProps) {
  const [text, setText] = useState<string | null>(null)
  const [error, setError] = useState<string | null>(null)

  useEffect(() => {
    void (async () => {
      try {
        const raw = await getBundleFileText(bundleId, 'spec.json')
        // Pretty-print so the modal is readable.
        try {
          setText(JSON.stringify(JSON.parse(raw), null, 2))
        } catch {
          setText(raw)
        }
      } catch (e) {
        setError(e instanceof Error ? e.message : String(e))
      }
    })()
  }, [bundleId])

  return (
    <Modal onClose={onClose} title={`${flowCode} — spec.json`}>
      {error && <div className="text-sm text-red-700">{error}</div>}
      {text === null && !error && <p className="text-sm text-ink-muted">Loading…</p>}
      {text !== null && (
        <pre className="max-h-[70vh] overflow-auto rounded border border-rule bg-slate-50 p-3 text-[11px] leading-snug text-ink">
{text}
        </pre>
      )}
    </Modal>
  )
}

/* ============================================================ *
 * Diff modal — side-by-side v_n vs v_(n-1) (§2.4)
 * Line-diff highlighting deferred — for v1 we just put two <pre>
 * blocks next to each other so authors can eyeball changes.
 * ============================================================ */

interface DiffModalProps {
  flowCode: string
  onClose: () => void
}

function DiffModal({ flowCode, onClose }: DiffModalProps) {
  const [state, setState] = useState<{
    older: { version: number; text: string } | null
    newer: { version: number; text: string } | null
  } | null>(null)
  const [error, setError] = useState<string | null>(null)

  useEffect(() => {
    void (async () => {
      try {
        const versions = await listVersions(flowCode)
        if (versions.length < 2) {
          setError('Need at least two bundle versions to diff. This flow only has one (or none).')
          return
        }
        // versions arrive newest-first.
        const newerV = versions[0]
        const olderV = versions[1]
        const [newerText, olderText] = await Promise.all([
          getBundleFileText(newerV.bundleId, 'spec.json').then(prettyJson),
          getBundleFileText(olderV.bundleId, 'spec.json').then(prettyJson),
        ])
        setState({
          older: { version: olderV.flowVersion, text: olderText },
          newer: { version: newerV.flowVersion, text: newerText },
        })
      } catch (e) {
        setError(e instanceof Error ? e.message : String(e))
      }
    })()
  }, [flowCode])

  return (
    <Modal onClose={onClose} title={`${flowCode} — diff (latest vs previous)`} wide>
      {error && <div className="text-sm text-red-700">{error}</div>}
      {!error && state === null && <p className="text-sm text-ink-muted">Loading…</p>}
      {state && (
        <div className="grid grid-cols-2 gap-3">
          <div>
            <h4 className="mb-1 text-xs font-semibold text-ink-muted">Previous (v{state.older!.version})</h4>
            <pre className="max-h-[70vh] overflow-auto rounded border border-rule bg-slate-50 p-3 text-[11px] leading-snug text-ink">
{state.older!.text}
            </pre>
          </div>
          <div>
            <h4 className="mb-1 text-xs font-semibold text-ink-muted">Latest (v{state.newer!.version})</h4>
            <pre className="max-h-[70vh] overflow-auto rounded border border-rule bg-slate-50 p-3 text-[11px] leading-snug text-ink">
{state.newer!.text}
            </pre>
          </div>
        </div>
      )}
      <p className="mt-2 text-[11px] text-ink-faint">
        Side-by-side text view; line-diff highlighting deferred.
      </p>
    </Modal>
  )
}

/* ============================================================ *
 * Lightweight modal + drawer primitives. We keep them local rather
 * than introducing a new shared component since the existing admin
 * app's modal patterns are bespoke per screen (BundleDetail rolls
 * its own X-button overlay too).
 * ============================================================ */

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
        className={`my-auto w-full ${wide ? 'max-w-6xl' : 'max-w-3xl'} rounded-lg bg-white p-4 shadow-xl`}
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

interface DrawerProps {
  title: string
  onClose: () => void
  children: React.ReactNode
}

function Drawer({ title, onClose, children }: DrawerProps) {
  return (
    <div
      className="fixed inset-0 z-50 flex justify-end bg-black/40"
      onClick={onClose}
    >
      <div
        className="h-full w-full max-w-2xl overflow-auto bg-white p-4 shadow-xl"
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

// ===== helpers =====

function formatDate(iso: string): string {
  if (!iso) return '—'
  const d = new Date(iso)
  if (Number.isNaN(d.getTime())) return iso
  const pad = (n: number) => String(n).padStart(2, '0')
  return `${d.getFullYear()}-${pad(d.getMonth() + 1)}-${pad(d.getDate())} ${pad(d.getHours())}:${pad(d.getMinutes())}`
}

function prettyJson(raw: string): string {
  try { return JSON.stringify(JSON.parse(raw), null, 2) }
  catch { return raw }
}
