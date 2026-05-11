/**
 * Import a .zip via drag-drop or file picker. Two-stage UX:
 *
 *  1. Pick file → parse client-side (parseBundleClientSide) → preview
 *     manifest fields + listed files. No server round-trip yet.
 *  2. Choose action: Install for runtime / Open as 9-stepper draft / Cancel.
 *     Install hits POST /import?mode=install (with the same File object)
 *     and surfaces the install result + repro report. Draft hits
 *     POST /import?mode=draft and hands the parsed payload back to the
 *     parent — PR-I7 will wire the Onboarding hydration.
 *
 * Robustness: client-side parse failures show inline so the user can pick
 * a different file without the modal collapsing.
 */
import { useCallback, useRef, useState } from 'react'
import { X, Upload, CheckCircle2, AlertCircle, FileEdit } from 'lucide-react'
import { Button } from '@/components/ui/button'
import { parseBundleClientSide, type ClientSideBundlePreview } from '@/lib/bundle/parseBundleClientSide'
import type { ImportInstallResult, ImportDraftResult } from '@/types/flowLibrary'
import { ReproReportModal } from './ReproReportModal'

interface Props {
  onClose: () => void
  onInstall: (file: File) => Promise<ImportInstallResult>
  onDraft: (file: File) => Promise<ImportDraftResult>
}

type Stage =
  | { kind: 'pick' }
  | { kind: 'preview'; file: File; preview: ClientSideBundlePreview }
  | { kind: 'installing'; file: File }
  | { kind: 'installed'; result: ImportInstallResult }
  | { kind: 'error'; message: string }

export function ImportModal({ onClose, onInstall, onDraft }: Props) {
  const [stage, setStage] = useState<Stage>({ kind: 'pick' })
  const [dragOver, setDragOver] = useState(false)
  const inputRef = useRef<HTMLInputElement>(null)

  const handleFile = useCallback(async (file: File) => {
    if (!file.name.toLowerCase().endsWith('.zip')) {
      setStage({ kind: 'error', message: 'Bundle must be a .zip file.' })
      return
    }
    try {
      const preview = await parseBundleClientSide(file)
      setStage({ kind: 'preview', file, preview })
    } catch (e) {
      setStage({ kind: 'error', message: e instanceof Error ? e.message : String(e) })
    }
  }, [])

  const handleDrop = (e: React.DragEvent) => {
    e.preventDefault()
    setDragOver(false)
    const f = e.dataTransfer.files?.[0]
    if (f) void handleFile(f)
  }

  const handleInstall = async () => {
    if (stage.kind !== 'preview') return
    setStage({ kind: 'installing', file: stage.file })
    try {
      const result = await onInstall(stage.file)
      setStage({ kind: 'installed', result })
    } catch (e) {
      setStage({ kind: 'error', message: e instanceof Error ? e.message : String(e) })
    }
  }

  const handleDraft = async () => {
    if (stage.kind !== 'preview') return
    try {
      await onDraft(stage.file)
      // onDraft typically navigates / reloads — but if it returns, close.
      onClose()
    } catch (e) {
      setStage({ kind: 'error', message: e instanceof Error ? e.message : String(e) })
    }
  }

  return (
    <div className="fixed inset-0 z-[60] flex items-center justify-center bg-black/50 p-4" onClick={onClose}>
      <div className="w-full max-w-2xl rounded-lg bg-white shadow-2xl" onClick={e => e.stopPropagation()}>
        <div className="flex items-center justify-between border-b border-rule px-5 py-3">
          <div className="flex items-center gap-2">
            <Upload className="h-5 w-5 text-ink-muted" />
            <h3 className="text-base font-bold text-ink">Import bundle</h3>
          </div>
          <button onClick={onClose} className="rounded p-1 text-slate-400 hover:bg-slate-100 hover:text-slate-700">
            <X className="h-4 w-4" />
          </button>
        </div>

        <div className="max-h-[70vh] overflow-auto px-5 py-4">
          {stage.kind === 'pick' && (
            <div
              onDragOver={e => { e.preventDefault(); setDragOver(true) }}
              onDragLeave={() => setDragOver(false)}
              onDrop={handleDrop}
              className={[
                'flex flex-col items-center gap-2 rounded-lg border-2 border-dashed p-10 text-center transition-colors',
                dragOver ? 'border-primary bg-blue-50' : 'border-rule bg-slate-50',
              ].join(' ')}
            >
              <Upload className="h-8 w-8 text-ink-faint" />
              <p className="text-sm font-medium text-ink">Drop a Flow Library bundle (.zip) here</p>
              <p className="text-xs text-ink-muted">or pick one from disk</p>
              <input
                ref={inputRef}
                type="file"
                accept=".zip,application/zip"
                className="hidden"
                onChange={e => {
                  const f = e.target.files?.[0]
                  if (f) void handleFile(f)
                }}
              />
              <Button variant="primary" size="sm" onClick={() => inputRef.current?.click()}>
                Pick file
              </Button>
            </div>
          )}

          {stage.kind === 'error' && (
            <div className="space-y-3">
              <div className="rounded-md border border-red-200 bg-red-50 p-3 text-sm text-red-800">
                <AlertCircle className="mr-2 inline h-4 w-4" /> {stage.message}
              </div>
              <Button variant="outline" size="sm" onClick={() => setStage({ kind: 'pick' })}>
                Try again
              </Button>
            </div>
          )}

          {stage.kind === 'preview' && (
            <PreviewBlock file={stage.file} preview={stage.preview} />
          )}

          {stage.kind === 'installing' && (
            <div className="py-10 text-center text-sm text-ink-muted">
              Installing — running parse + validate + reproducibility…
            </div>
          )}

          {stage.kind === 'installed' && (
            <InstalledBlock result={stage.result} onClose={onClose} />
          )}
        </div>

        {stage.kind === 'preview' && (
          <div className="flex justify-end gap-2 border-t border-rule bg-slate-50/50 px-5 py-3">
            <Button variant="ghost" onClick={onClose}>Cancel</Button>
            <Button variant="outline" onClick={handleDraft}>
              <FileEdit className="h-4 w-4" /> Open as 9-stepper draft
            </Button>
            <Button variant="primary" onClick={handleInstall}>
              <CheckCircle2 className="h-4 w-4" /> Install for runtime
            </Button>
          </div>
        )}
      </div>
    </div>
  )
}

function PreviewBlock({ file, preview }: { file: File; preview: ClientSideBundlePreview }) {
  const m = preview.manifest
  const totalBytes = preview.fileList.reduce((s, f) => s + f.sizeBytes, 0)
  return (
    <div className="space-y-4">
      <div className="grid grid-cols-2 gap-3 rounded border border-rule bg-slate-50 p-3 text-xs">
        <Field label="File" value={file.name} />
        <Field label="Schema" value={`v${m.bundleSchemaVersion}`} />
        <Field label="Flow code" value={m.flowCode} />
        <Field label="Flow version" value={`v${m.flowVersion}`} />
        <Field label="Exported at" value={m.exportedAt} />
        <Field label="Source instance" value={m.sourceInstanceId} />
        {m.parent && <Field label="Parent checksum" value={m.parent} mono />}
        <Field label="Files" value={`${preview.fileList.length} (${formatBytes(totalBytes)})`} />
      </div>
      <div>
        <div className="mb-1 text-xs font-semibold text-ink-muted">Files in bundle</div>
        <div className="max-h-64 overflow-auto rounded border border-rule">
          <table className="w-full text-xs">
            <tbody>
              {preview.fileList.map(f => (
                <tr key={f.path} className="border-b border-rule last:border-b-0">
                  <td className="px-3 py-1 font-mono text-[11px] text-slate-700">{f.path}</td>
                  <td className="px-3 py-1 text-right font-mono text-[11px] text-slate-500">{formatBytes(f.sizeBytes)}</td>
                </tr>
              ))}
            </tbody>
          </table>
        </div>
      </div>
    </div>
  )
}

function InstalledBlock({ result, onClose }: { result: ImportInstallResult; onClose: () => void }) {
  const [showRepro, setShowRepro] = useState(false)
  return (
    <div className="space-y-3">
      <div className="rounded-md border border-green-200 bg-green-50 p-3 text-sm">
        <CheckCircle2 className="mr-2 inline h-4 w-4 text-good" />
        Imported as <code className="font-mono text-xs">{result.id}</code> with status{' '}
        <span className="font-semibold">{result.status}</span>.
      </div>
      {result.reproReport && (
        <div className="flex items-center gap-2">
          <Button variant="outline" size="sm" onClick={() => setShowRepro(true)}>
            View repro report
          </Button>
          <Button variant="ghost" size="sm" onClick={onClose}>Close</Button>
        </div>
      )}
      {!result.reproReport && (
        <Button variant="ghost" size="sm" onClick={onClose}>Close</Button>
      )}
      {showRepro && result.reproReport && (
        <ReproReportModal report={result.reproReport} onClose={() => setShowRepro(false)} />
      )}
    </div>
  )
}

function Field({ label, value, mono = false }: { label: string; value: string; mono?: boolean }) {
  return (
    <div className="flex flex-col">
      <span className="text-[10px] font-semibold uppercase tracking-wider text-ink-faint">{label}</span>
      <span className={[mono ? 'font-mono text-[11px]' : '', 'truncate text-ink'].join(' ')} title={value}>{value}</span>
    </div>
  )
}

function formatBytes(n: number): string {
  if (n < 1024) return `${n} B`
  if (n < 1024 * 1024) return `${(n / 1024).toFixed(1)} KB`
  return `${(n / 1024 / 1024).toFixed(2)} MB`
}
