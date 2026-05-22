/**
 * Bundle detail viewer — modal because the rest of the admin app doesn't
 * use URL routes for sub-screens (App.tsx switches on a flat AdminScreen
 * union). Each tab fetches its file lazily via the bundle files endpoint
 * — we don't pre-load everything because some bundles can carry assets.
 */
import { useEffect, useMemo, useState } from 'react'
import { X, FileText, FileCode, Workflow, BookOpen, Bell, Clock, Users2, FileJson, FlaskConical, Image as ImageIcon, Play, FormInput } from 'lucide-react'
import {
  getBundleDetail,
  getBundleFileText,
  getBundleFileJson,
} from '@/lib/api/flowLibrary'
import type {
  FlowLibraryDetailDto,
  BundleManifest,
  ReproReport,
} from '@/types/flowLibrary'
import { ReproReportModal } from './ReproReportModal'

interface Props {
  id: string
  onClose: () => void
}

type TabKey =
  | 'manifest'
  | 'spec'
  | 'bpmn'
  | 'specMd'
  | 'forms'
  | 'notifications'
  | 'sla'
  | 'actors'
  | 'sampleOrg'
  | 'testCases'
  | 'assets'
  | 'lastRepro'

const TAB_LABELS: { key: TabKey; label: string; icon: React.ReactNode }[] = [
  { key: 'manifest',      label: 'Manifest',     icon: <FileText className="h-3.5 w-3.5" /> },
  { key: 'spec',          label: 'spec.json',    icon: <FileJson className="h-3.5 w-3.5" /> },
  { key: 'bpmn',          label: 'bpmn.xml',     icon: <Workflow className="h-3.5 w-3.5" /> },
  { key: 'specMd',        label: 'spec.md',      icon: <BookOpen className="h-3.5 w-3.5" /> },
  { key: 'forms',         label: 'Forms',        icon: <FormInput className="h-3.5 w-3.5" /> },
  { key: 'notifications', label: 'Notifications', icon: <Bell className="h-3.5 w-3.5" /> },
  { key: 'sla',           label: 'SLA',          icon: <Clock className="h-3.5 w-3.5" /> },
  { key: 'actors',        label: 'Actors',       icon: <Users2 className="h-3.5 w-3.5" /> },
  { key: 'sampleOrg',     label: 'Sample Org',   icon: <Users2 className="h-3.5 w-3.5" /> },
  { key: 'testCases',     label: 'Test Cases',   icon: <FlaskConical className="h-3.5 w-3.5" /> },
  { key: 'assets',        label: 'Assets',       icon: <ImageIcon className="h-3.5 w-3.5" /> },
  { key: 'lastRepro',     label: 'Last Repro',   icon: <Play className="h-3.5 w-3.5" /> },
]

export function BundleDetail({ id, onClose }: Props) {
  const [detail, setDetail] = useState<FlowLibraryDetailDto | null>(null)
  const [loadError, setLoadError] = useState<string | null>(null)
  const [tab, setTab] = useState<TabKey>('manifest')

  useEffect(() => {
    let cancelled = false
    setDetail(null); setLoadError(null)
    getBundleDetail(id)
      .then(d => { if (!cancelled) setDetail(d) })
      .catch(e => { if (!cancelled) setLoadError(e instanceof Error ? e.message : String(e)) })
    return () => { cancelled = true }
  }, [id])

  return (
    <div className="fixed inset-0 z-[60] flex items-center justify-center bg-black/50 p-4" onClick={onClose}>
      <div className="flex h-[90vh] w-full max-w-6xl flex-col rounded-lg bg-white shadow-2xl" onClick={e => e.stopPropagation()}>
        <div className="flex items-center justify-between border-b border-rule px-5 py-3">
          <div className="min-w-0">
            {detail
              ? (
                <h3 className="truncate text-base font-bold text-ink">
                  {detail.summary.flowCode} <span className="text-ink-muted">v{detail.summary.flowVersion}</span>
                </h3>
              )
              : <h3 className="text-base font-bold text-ink">Loading bundle…</h3>
            }
            {detail && (
              <p className="font-mono text-[10.5px] text-ink-muted">{detail.summary.manifestChecksum}</p>
            )}
          </div>
          <button onClick={onClose} className="rounded p-1 text-slate-400 hover:bg-slate-100 hover:text-slate-700">
            <X className="h-4 w-4" />
          </button>
        </div>

        {loadError && (
          <div className="m-4 rounded border border-red-200 bg-red-50 p-3 text-sm text-red-800">
            Failed to load bundle: {loadError}
          </div>
        )}

        {detail && (
          <div className="flex min-h-0 flex-1">
            <aside className="w-44 shrink-0 overflow-y-auto border-r border-rule bg-slate-50 p-2">
              {TAB_LABELS.map(t => (
                <button
                  key={t.key}
                  onClick={() => setTab(t.key)}
                  className={[
                    'flex w-full items-center gap-2 rounded px-2.5 py-1.5 text-left text-xs font-medium transition-colors',
                    tab === t.key
                      ? 'bg-white text-ink shadow-sm'
                      : 'text-ink-muted hover:bg-white/60 hover:text-ink',
                  ].join(' ')}
                >
                  {t.icon} {t.label}
                </button>
              ))}
            </aside>
            <main className="min-w-0 flex-1 overflow-auto p-4">
              <TabContent id={id} tab={tab} detail={detail} />
            </main>
          </div>
        )}
      </div>
    </div>
  )
}

function TabContent({ id, tab, detail }: { id: string; tab: TabKey; detail: FlowLibraryDetailDto }) {
  switch (tab) {
    case 'manifest':      return <ManifestTab manifest={detail.manifest} />
    case 'spec':          return <RawJsonTab id={id} path="spec.json" />
    case 'bpmn':          return <BpmnXmlTab id={id} />
    case 'specMd':        return <RawTextTab id={id} path="spec.md" empty="spec.md not present in this bundle." />
    case 'forms':         return <FilesGroupTab id={id} manifest={detail.manifest} kindPrefix="forms/" />
    case 'notifications': return <FilesGroupTab id={id} manifest={detail.manifest} kindPrefix="notifications/" />
    case 'sla':           return <RawJsonTab id={id} path="sla.json" />
    case 'actors':        return <RawJsonTab id={id} path="actors.json" />
    case 'sampleOrg':     return <RawJsonTab id={id} path="sample-org.json" />
    case 'testCases':     return <FilesGroupTab id={id} manifest={detail.manifest} kindPrefix="test-cases/" />
    case 'assets':        return <FilesGroupTab id={id} manifest={detail.manifest} kindPrefix="assets/" listOnly />
    case 'lastRepro':     return <LastReproTab report={detail.lastReproReport} />
  }
}

/* ── Tabs ───────────────────────────────────────────────────────────── */

function ManifestTab({ manifest }: { manifest: BundleManifest }) {
  return (
    <div className="space-y-3">
      <div className="grid grid-cols-2 gap-x-6 gap-y-2 rounded border border-rule bg-slate-50 p-3 text-xs">
        <KV k="Schema version" v={`v${manifest.bundleSchemaVersion}`} />
        <KV k="Flow code" v={manifest.flowCode} />
        <KV k="Flow version" v={`v${manifest.flowVersion}`} />
        <KV k="Exported at" v={manifest.exportedAt} />
        <KV k="Source instance" v={manifest.sourceInstanceId} />
        <KV k="Parent" v={manifest.parent ?? '—'} mono />
        <KV k="File count" v={String(manifest.files.length)} />
      </div>
      <PreBlock content={JSON.stringify(manifest, null, 2)} />
    </div>
  )
}

function KV({ k, v, mono = false }: { k: string; v: string; mono?: boolean }) {
  return (
    <div className="flex flex-col">
      <span className="text-[10px] font-semibold uppercase tracking-wider text-ink-faint">{k}</span>
      <span className={[mono ? 'font-mono text-[11px]' : '', 'truncate text-ink'].join(' ')} title={v}>{v}</span>
    </div>
  )
}

function RawJsonTab({ id, path }: { id: string; path: string }) {
  const [text, setText] = useState<string | null>(null)
  const [error, setError] = useState<string | null>(null)
  useEffect(() => {
    let cancelled = false
    setText(null); setError(null)
    getBundleFileJson<unknown>(id, path)
      .then(j => { if (!cancelled) setText(JSON.stringify(j, null, 2)) })
      .catch(e => { if (!cancelled) setError(e instanceof Error ? e.message : String(e)) })
    return () => { cancelled = true }
  }, [id, path])
  if (error) return <ErrorBox message={error} />
  if (text === null) return <Loading />
  return <PreBlock content={text} />
}

function RawTextTab({ id, path, empty }: { id: string; path: string; empty: string }) {
  const [text, setText] = useState<string | null>(null)
  const [error, setError] = useState<string | null>(null)
  useEffect(() => {
    let cancelled = false
    setText(null); setError(null)
    getBundleFileText(id, path)
      .then(t => { if (!cancelled) setText(t) })
      .catch(e => { if (!cancelled) setError(e instanceof Error ? e.message : String(e)) })
    return () => { cancelled = true }
  }, [id, path])
  if (error?.includes('404') || error?.includes('Not Found')) {
    return <p className="text-sm text-ink-muted">{empty}</p>
  }
  if (error) return <ErrorBox message={error} />
  if (text === null) return <Loading />
  return <PreBlock content={text} />
}

function BpmnXmlTab({ id }: { id: string }) {
  // Render the BPMN XML as plain text — wiring the live BpmnDiagram viewer
  // requires synthesising a DraftSpec, which doesn't make sense for an
  // already-bundled spec. PR-I7 (which lands DraftSpec hydration) can
  // optionally upgrade this to a live preview.
  return <RawTextTab id={id} path="bpmn.xml" empty="bpmn.xml not present in this bundle." />
}

function FilesGroupTab({
  id, manifest, kindPrefix, listOnly = false,
}: {
  id: string
  manifest: BundleManifest
  kindPrefix: string
  listOnly?: boolean
}) {
  const matches = useMemo(
    () => manifest.files.filter(f => f.path.startsWith(kindPrefix)),
    [manifest, kindPrefix],
  )
  const [selected, setSelected] = useState<string | null>(null)
  const [text, setText] = useState<string | null>(null)
  const [error, setError] = useState<string | null>(null)

  useEffect(() => {
    if (!selected) return
    let cancelled = false
    setText(null); setError(null)
    const isJson = selected.endsWith('.json')
    const work = isJson
      ? getBundleFileJson<unknown>(id, selected).then(j => JSON.stringify(j, null, 2))
      : getBundleFileText(id, selected)
    work
      .then(t => { if (!cancelled) setText(t) })
      .catch(e => { if (!cancelled) setError(e instanceof Error ? e.message : String(e)) })
    return () => { cancelled = true }
  }, [id, selected])

  if (matches.length === 0) {
    return <p className="text-sm text-ink-muted">No files under <code className="font-mono text-xs">{kindPrefix}</code> in this bundle.</p>
  }

  return (
    <div className="grid grid-cols-[18rem_1fr] gap-3">
      <div className="rounded border border-rule">
        {matches.map(f => (
          <button
            key={f.path}
            onClick={() => !listOnly && setSelected(f.path)}
            disabled={listOnly}
            className={[
              'flex w-full items-center justify-between border-b border-rule px-2.5 py-1.5 text-left text-[11px] last:border-b-0 transition-colors',
              selected === f.path ? 'bg-blue-50 text-blue-900' : 'text-ink hover:bg-slate-50',
              listOnly ? 'cursor-default' : 'cursor-pointer',
            ].join(' ')}
          >
            <span className="truncate font-mono">{f.path.replace(kindPrefix, '')}</span>
            <span className="ml-2 shrink-0 font-mono text-[10px] text-ink-muted">{formatBytes(f.sizeBytes)}</span>
          </button>
        ))}
      </div>
      <div className="min-w-0">
        {listOnly && (
          <p className="text-xs text-ink-muted">Asset preview is not implemented in this version.</p>
        )}
        {!listOnly && !selected && (
          <p className="text-xs text-ink-muted">Pick a file from the list.</p>
        )}
        {!listOnly && selected && error && <ErrorBox message={error} />}
        {!listOnly && selected && !error && text === null && <Loading />}
        {!listOnly && selected && !error && text !== null && <PreBlock content={text} />}
      </div>
    </div>
  )
}

function LastReproTab({ report }: { report: ReproReport | null }) {
  const [open, setOpen] = useState(false)
  if (!report) {
    return <p className="text-sm text-ink-muted">No reproducibility report has been recorded yet.</p>
  }
  return (
    <div className="space-y-3">
      <button
        onClick={() => setOpen(true)}
        className="rounded border border-rule px-3 py-1.5 text-xs font-medium hover:bg-slate-50"
      >
        View as modal
      </button>
      <PreBlock content={JSON.stringify(report, null, 2)} />
      {open && <ReproReportModal report={report} onClose={() => setOpen(false)} />}
    </div>
  )
}

/* ── Helpers ────────────────────────────────────────────────────────── */

function PreBlock({ content }: { content: string }) {
  return (
    <pre className="max-h-[60vh] overflow-auto rounded border border-rule bg-slate-50 p-3 text-[11.5px] leading-snug text-slate-800">
      {content}
    </pre>
  )
}

function ErrorBox({ message }: { message: string }) {
  return (
    <div className="rounded border border-red-200 bg-red-50 p-3 text-sm text-red-800">
      <FileCode className="mr-2 inline h-4 w-4" /> Failed to load: {message}
    </div>
  )
}

function Loading() {
  return <div className="text-xs text-ink-muted">Loading…</div>
}

function formatBytes(n: number): string {
  if (n < 1024) return `${n} B`
  if (n < 1024 * 1024) return `${(n / 1024).toFixed(1)} KB`
  return `${(n / 1024 / 1024).toFixed(2)} MB`
}
