/**
 * Flow Library — list of every imported / built spec bundle.
 *
 * Behaviour mirrors PR-I6 §7 of `add-spec-bundle-and-flow-library`:
 *
 *  - On mount: GET `/api/admin/flow-library` and render one card per bundle.
 *  - Header: "Import bundle" button → ImportModal (drag-drop + preview).
 *  - Per-row actions: View / Repro Check / Export / Open as draft / Delete.
 *  - Empty state nudges to import or finish a flow in Onboarding.
 *
 * "View" shows the BundleDetail modal in-place. We don't introduce a
 * second router stage — the existing admin app uses a flat screen-switch,
 * not URL routes, so a modal keeps that contract.
 */

import { useCallback, useEffect, useState } from 'react'
import { Library, Upload, Trash2, Download, Eye, RefreshCw, FileEdit, AlertCircle } from 'lucide-react'
import {
  listBundles,
  reproCheckBundle,
  deleteBundle,
  importBundleInstall,
  importBundleDraft,
  downloadBundle,
} from '@/lib/api/flowLibrary'
import type { FlowLibraryItemDto, ReproReport, ImportDraftResult, ImportInstallResult } from '@/lib/api/flowLibrary'
import { Button } from '@/components/ui/button'
import { ConfirmDialog } from '@/components/ui/confirm-dialog'
import { BundleDetail } from './BundleDetail'
import { ImportModal } from './ImportModal'
import { ReproReportModal } from './ReproReportModal'
import { StatusPill } from './StatusPill'

export function FlowLibrary() {
  const [items, setItems] = useState<FlowLibraryItemDto[] | null>(null)
  const [loadError, setLoadError] = useState<string | null>(null)
  const [busyId, setBusyId] = useState<string | null>(null)
  const [highlightId, setHighlightId] = useState<string | null>(null)

  const [viewId, setViewId] = useState<string | null>(null)
  const [reproShow, setReproShow] = useState<{ id: string; report: ReproReport } | null>(null)
  const [importOpen, setImportOpen] = useState(false)
  const [confirmDelete, setConfirmDelete] = useState<FlowLibraryItemDto | null>(null)

  const refresh = useCallback(async () => {
    setLoadError(null)
    try {
      const list = await listBundles()
      setItems(list)
    } catch (e) {
      setLoadError(e instanceof Error ? e.message : String(e))
      setItems([])
    }
  }, [])

  useEffect(() => { void refresh() }, [refresh])

  // PR-I7 §9.3 — flash-highlight the just-saved bundle when StepGoLive
  // navigates here. Consume the localStorage marker so subsequent visits
  // don't re-flash an old row.
  useEffect(() => {
    try {
      const focus = localStorage.getItem('bpm_admin_screen_focus')
      if (focus) {
        setHighlightId(focus)
        localStorage.removeItem('bpm_admin_screen_focus')
      }
    } catch { /* ignore */ }
  }, [])

  const handleRepro = async (item: FlowLibraryItemDto) => {
    setBusyId(item.id)
    try {
      const report = await reproCheckBundle(item.id)
      setReproShow({ id: item.id, report })
      await refresh()
    } catch (e) {
      alert(`Repro check failed: ${e instanceof Error ? e.message : String(e)}`)
    } finally {
      setBusyId(null)
    }
  }

  const handleExport = async (item: FlowLibraryItemDto) => {
    try {
      await downloadBundle(item.id)
    } catch (e) {
      alert(`Export failed: ${e instanceof Error ? e.message : String(e)}`)
    }
  }

  const handleDelete = async () => {
    const item = confirmDelete
    if (!item) return
    setBusyId(item.id)
    try {
      await deleteBundle(item.id)
      setConfirmDelete(null)
      await refresh()
    } catch (e) {
      alert(`Delete failed: ${e instanceof Error ? e.message : String(e)}`)
    } finally {
      setBusyId(null)
    }
  }

  const handleImportInstall = async (file: File): Promise<ImportInstallResult> => {
    const res = await importBundleInstall(file)
    setHighlightId(res.id)
    await refresh()
    return res
  }

  const handleImportDraft = async (file: File): Promise<ImportDraftResult> => {
    // PR-I7 §8.1 — fresh imports never persist a row, so we stash the
    // parsed payload in sessionStorage and let Onboarding.tsx pick it up
    // via `?bundle=draft`. sessionStorage avoids the localStorage churn
    // (and survives the navigation reload exactly once).
    const res = await importBundleDraft(file)
    try { sessionStorage.setItem('bpm_draft_bundle', JSON.stringify(res)) } catch { /* ignore */ }
    const url = new URL(window.location.href)
    url.searchParams.set('bundle', 'draft')
    window.history.replaceState(null, '', url.toString())
    try {
      localStorage.setItem('bpm_admin_screen', JSON.stringify({ kind: 'onboarding' }))
    } catch { /* ignore */ }
    window.location.reload()
    return res
  }

  /**
   * PR-I7 §8.1 — Open a SAVED bundle as a draft. We don't stash a payload
   * here; the wizard fetches it via GET /{id}/hydration once it sees the
   * `?bundle=<guid>` marker. Two-stage so reload-with-the-same-id
   * re-fetches a fresh payload from server (the bundle could have been
   * re-imported / re-checksummed since last visit).
   */
  const openAsDraft = (id: string) => {
    const url = new URL(window.location.href)
    url.searchParams.set('bundle', id)
    window.history.replaceState(null, '', url.toString())
    try {
      localStorage.setItem('bpm_admin_screen', JSON.stringify({ kind: 'onboarding' }))
    } catch { /* ignore */ }
    window.location.reload()
  }

  return (
    <div className="space-y-4">
      <div className="flex items-center justify-between">
        <div className="flex items-center gap-2">
          <Library className="h-5 w-5 text-ink-muted" />
          <h1 className="text-xl font-bold text-ink">Flow Library</h1>
        </div>
        <Button variant="primary" onClick={() => setImportOpen(true)}>
          <Upload className="h-4 w-4" /> Import bundle
        </Button>
      </div>

      {loadError && (
        <div className="rounded-md border border-red-200 bg-red-50 p-3 text-sm text-red-800">
          <AlertCircle className="mr-2 inline h-4 w-4" /> Failed to load bundles: {loadError}
        </div>
      )}

      {items === null && !loadError && (
        <div className="text-sm text-ink-muted">Loading…</div>
      )}

      {items !== null && items.length === 0 && (
        <div className="rounded-lg border border-dashed border-rule bg-card p-12 text-center">
          <Library className="mx-auto mb-3 h-8 w-8 text-ink-faint" />
          <p className="text-sm font-medium text-ink">No bundles yet</p>
          <p className="mt-1 text-xs text-ink-muted">
            Import a .zip or finish a flow in Onboarding to populate the library.
          </p>
        </div>
      )}

      {items !== null && items.length > 0 && (
        <div className="space-y-3">
          {items.map(item => (
            <BundleCard
              key={item.id}
              item={item}
              busy={busyId === item.id}
              highlighted={highlightId === item.id}
              onView={() => setViewId(item.id)}
              onRepro={() => handleRepro(item)}
              onExport={() => handleExport(item)}
              onOpenAsDraft={() => openAsDraft(item.id)}
              onDelete={() => setConfirmDelete(item)}
            />
          ))}
        </div>
      )}

      {viewId && (
        <BundleDetail id={viewId} onClose={() => setViewId(null)} />
      )}

      {reproShow && (
        <ReproReportModal
          report={reproShow.report}
          onClose={() => setReproShow(null)}
        />
      )}

      {importOpen && (
        <ImportModal
          onClose={() => setImportOpen(false)}
          onInstall={handleImportInstall}
          onDraft={handleImportDraft}
        />
      )}

      <ConfirmDialog
        open={confirmDelete !== null}
        title={`Delete bundle ${confirmDelete?.flowCode ?? ''} v${confirmDelete?.flowVersion ?? ''}?`}
        titleZh="軟刪除 — 已運行的 instance 不受影響，可從 audit 還原"
        description="The bundle row will be flagged SoftDeleted. Running process instances continue to use their immutable spec snapshot, so this is non-destructive. Re-import the same .zip to undelete."
        confirmText="Delete"
        tone="danger"
        onConfirm={handleDelete}
        onCancel={() => setConfirmDelete(null)}
      />
    </div>
  )
}

interface BundleCardProps {
  item: FlowLibraryItemDto
  busy: boolean
  highlighted: boolean
  onView: () => void
  onRepro: () => void
  onExport: () => void
  onOpenAsDraft: () => void
  onDelete: () => void
}

function BundleCard({ item, busy, highlighted, onView, onRepro, onExport, onOpenAsDraft, onDelete }: BundleCardProps) {
  const exported = formatDate(item.exportedAt)
  const lastChecked = item.lastReproCheckAt ? formatDate(item.lastReproCheckAt) : 'never'
  const summary = item.lastReproCheckSummary ?? '—'
  const summaryClass = summary.startsWith('PASS')
    ? 'text-good'
    : summary === 'ERROR' || summary.startsWith('FAIL')
      ? 'text-danger'
      : 'text-ink-muted'

  return (
    <div className={[
      'flex items-start justify-between gap-4 rounded-lg border bg-card p-4 transition-colors',
      highlighted ? 'border-amber-400 ring-2 ring-amber-200' : 'border-rule',
    ].join(' ')}>
      <div className="min-w-0 flex-1 space-y-1">
        <div className="flex items-center gap-2">
          <h3 className="truncate text-base font-semibold text-ink">
            {item.flowCode} <span className="text-ink-muted">v{item.flowVersion}</span>
          </h3>
          <StatusPill status={item.status} />
          {item.parentManifestChecksum && (
            <span className="rounded bg-slate-100 px-1.5 py-0.5 font-mono text-[10px] text-slate-600" title={`Parent: ${item.parentManifestChecksum}`}>
              forked
            </span>
          )}
        </div>
        <div className="flex flex-wrap gap-x-4 gap-y-0.5 text-xs text-ink-muted">
          <span>Exported {exported}</span>
          <span>Last repro {lastChecked}</span>
          <span>
            Repro <span className={`font-semibold ${summaryClass}`}>{summary}</span>
          </span>
          <span className="font-mono opacity-70" title={item.manifestChecksum}>
            sha {item.manifestChecksum.slice(0, 12)}…
          </span>
        </div>
      </div>
      <div className="flex shrink-0 items-center gap-1.5">
        <Button variant="outline" size="xs" onClick={onView} disabled={busy}>
          <Eye className="h-3.5 w-3.5" /> View
        </Button>
        <Button variant="outline" size="xs" onClick={onRepro} disabled={busy}>
          <RefreshCw className={['h-3.5 w-3.5', busy ? 'animate-spin' : ''].join(' ')} /> Repro Check
        </Button>
        <Button variant="outline" size="xs" onClick={onExport} disabled={busy}>
          <Download className="h-3.5 w-3.5" /> Export
        </Button>
        <Button variant="outline" size="xs" onClick={onOpenAsDraft} disabled={busy} title="Hydrate this bundle into the 9-step wizard">
          <FileEdit className="h-3.5 w-3.5" /> Open as draft
        </Button>
        <Button variant="destructive" size="xs" onClick={onDelete} disabled={busy}>
          <Trash2 className="h-3.5 w-3.5" /> Delete
        </Button>
      </div>
    </div>
  )
}

function formatDate(iso: string): string {
  if (!iso) return '—'
  const d = new Date(iso)
  if (Number.isNaN(d.getTime())) return iso
  // YYYY-MM-DD HH:mm UTC offset suppressed — admin readers want at-a-glance.
  const pad = (n: number) => String(n).padStart(2, '0')
  return `${d.getFullYear()}-${pad(d.getMonth() + 1)}-${pad(d.getDate())} ${pad(d.getHours())}:${pad(d.getMinutes())}`
}
