import { useState } from 'react'
import { Rocket, CheckCircle2, AlertTriangle, Library, Download } from 'lucide-react'
import { ONBOARDING_STEPS, validators, type DraftSpec } from '@/lib/onboarding'
import { buildAndSaveBundle, downloadBundle } from '@/lib/api/flowLibrary'
import { flowToBpmnXml } from '@/lib/bpmnXml'
import { Button } from '@/components/ui/button'
import type { AdminScreen } from '@/components/AdminLayout'

interface BuildResult {
  id: string
  status: string
  manifestChecksum: string
}

interface StepGoLiveProps {
  draft: DraftSpec
  onNavigate?: (s: AdminScreen) => void
}

/**
 * Step 9 — GO LIVE. PR-I7 §9 rewrites this from "submit JSON to backend
 * Claude pipeline" to "build a Flow Library bundle and persist it".
 *
 * Two terminal actions:
 *   - Save to Flow Library (POST /api/admin/flow-library/build) — creates
 *     a Pending bundle row the user can then Repro Check / install.
 *   - Download .zip — same build call, then `downloadBundle(id)` streams
 *     the persisted blob back. Cheaper and more correct than a parallel
 *     "build but don't persist" path because the persisted row IS the
 *     download source of truth.
 */
export function StepGoLive({ draft, onNavigate }: StepGoLiveProps) {
  const [building, setBuilding] = useState(false)
  const [result, setResult] = useState<BuildResult | null>(null)
  const [error, setError] = useState<string | null>(null)

  // StepGoLive is no longer in ONBOARDING_STEPS (11-step refactor moved
  // Submit into the wizard header). The legacy view kept here only as a
  // dead component reference; revisit if anything still imports it.
  const allChecks = ONBOARDING_STEPS.map(s => ({
    id: s.id, en: s.en, zh: s.zh, ...validators[s.id](draft),
  }))
  const goLiveCheck = { valid: true, errors: [] as string[] }
  const allValid = allChecks.every(c => c.valid) && goLiveCheck.valid

  /**
   * Build a fresh bundle from the in-memory draft and POST it to
   * `/api/admin/flow-library/build`. The server runs the same builder
   * the import path uses, so the resulting zip is identical to one a
   * user could have uploaded — the persistence layer is the only
   * round-trip.
   */
  const buildBundle = async (): Promise<BuildResult | null> => {
    setBuilding(true)
    setError(null)
    try {
      const bpmnXml = await flowToBpmnXml(draft)
      const res = await buildAndSaveBundle({
        specJson: draft,
        bpmnXml,
        sampleOrg: draft.sampleOrg,
        testCases: draft.testCases,
      })
      setResult(res)
      return res
    } catch (e: unknown) {
      const msg = e instanceof Error ? e.message : String(e)
      setError(msg)
      return null
    } finally {
      setBuilding(false)
    }
  }

  const handleSave = async () => {
    const r = await buildBundle()
    if (!r) return
    // Flash-highlight the new row when Flow Library mounts.
    try { localStorage.setItem('bpm_admin_screen_focus', r.id) } catch { /* ignore */ }
  }

  const handleDownload = async () => {
    const r = result ?? await buildBundle()
    if (!r) return
    try {
      await downloadBundle(r.id)
    } catch (e) {
      setError(`Download failed: ${e instanceof Error ? e.message : String(e)}`)
    }
  }

  const openLibrary = () => {
    if (result?.id) {
      try { localStorage.setItem('bpm_admin_screen_focus', result.id) } catch { /* ignore */ }
    }
    onNavigate?.({ kind: 'flow-library' })
  }

  if (result) {
    return (
      <div className="flex flex-col items-center justify-center py-12 text-center">
        <div className="mb-4 flex h-16 w-16 items-center justify-center rounded-full bg-emerald-100 text-emerald-700">
          <CheckCircle2 className="h-8 w-8" />
        </div>
        <h3 className="text-lg font-bold text-ink">Bundle saved as v{draft.meta.flowVersion}</h3>
        <p className="mt-1 max-w-md text-sm text-ink-muted">
          {draft.meta.flowName}（{draft.meta.flowCode}）已存入 Flow Library，狀態 <span className="font-mono">{result.status}</span>。可從 Flow Library 跑 Repro Check 或下載 .zip 部署到客戶環境。
        </p>
        <div className="mt-4 w-full max-w-2xl rounded border border-rule bg-slate-50 px-4 py-3 text-left text-xs text-ink-muted">
          <p>📦 Bundle ID: <span className="font-mono text-ink">{result.id}</span></p>
          <p className="mt-0.5">🔒 Manifest sha256: <span className="font-mono text-ink break-all">{result.manifestChecksum}</span></p>
        </div>
        <div className="mt-5 flex gap-2">
          <Button variant="primary" onClick={openLibrary}>
            <Library className="h-4 w-4" /> Open in Flow Library →
          </Button>
          <Button variant="outline" onClick={handleDownload} disabled={building}>
            <Download className="h-4 w-4" /> {building ? '下載中…' : 'Download .zip'}
          </Button>
        </div>
      </div>
    )
  }

  return (
    <div className="flex flex-col gap-5">
      <section>
        <h3 className="mb-2 text-sm font-semibold text-ink">Validator 總覽</h3>
        <div className="overflow-hidden rounded border border-rule">
          <table className="w-full text-xs">
            <thead className="bg-slate-50">
              <tr className="text-left text-[10px] uppercase tracking-wider text-ink-muted">
                <th className="px-3 py-2 w-12">#</th>
                <th className="px-3 py-2">Step</th>
                <th className="px-3 py-2">Status</th>
                <th className="px-3 py-2">Issues</th>
              </tr>
            </thead>
            <tbody>
              {allChecks.map((c, i) => (
                <tr key={c.id} className="border-t border-rule">
                  <td className="px-3 py-1.5 font-mono text-ink-muted">{i + 1}</td>
                  <td className="px-3 py-1.5"><span className="font-mono uppercase tracking-wider text-ink">{c.en}</span> <span className="text-ink-muted">{c.zh}</span></td>
                  <td className="px-3 py-1.5">
                    {c.valid
                      ? <span className="rounded bg-emerald-50 px-2 py-0.5 font-medium text-emerald-700">PASS</span>
                      : <span className="rounded bg-rose-50 px-2 py-0.5 font-medium text-rose-700">FAIL</span>}
                  </td>
                  <td className="px-3 py-1.5 text-ink-muted">
                    {c.errors.length === 0 ? '—' : c.errors.join('；')}
                  </td>
                </tr>
              ))}
              <tr className="border-t border-rule bg-slate-50/50">
                <td className="px-3 py-1.5 font-mono text-ink-muted">9</td>
                <td className="px-3 py-1.5"><span className="font-mono uppercase tracking-wider text-ink">GO LIVE</span> <span className="text-ink-muted">上線</span></td>
                <td className="px-3 py-1.5">
                  {goLiveCheck.valid
                    ? <span className="rounded bg-emerald-50 px-2 py-0.5 font-medium text-emerald-700">PASS</span>
                    : <span className="rounded bg-rose-50 px-2 py-0.5 font-medium text-rose-700">FAIL</span>}
                </td>
                <td className="px-3 py-1.5 text-ink-muted">
                  {goLiveCheck.errors.length === 0 ? '—' : goLiveCheck.errors.join('；')}
                </td>
              </tr>
            </tbody>
          </table>
        </div>
      </section>

      <section>
        <h3 className="mb-2 text-sm font-semibold text-ink">Bundle 內容預覽 · what we'll package</h3>
        <div className="grid grid-cols-2 gap-3 text-xs">
          <Cell k="Tenant" v={draft.meta.tenant || '—'} />
          <Cell k="Flow Code" v={draft.meta.flowCode || '—'} />
          <Cell k="Flow 名稱" v={draft.meta.flowName || '—'} />
          <Cell k="Version" v={`v${draft.meta.flowVersion}`} />
          <Cell k="節點" v={`${draft.flow.nodes.length} 個`} />
          <Cell k="邊" v={`${draft.flow.edges.length} 條`} />
          <Cell k="User tasks" v={`${draft.userTasks.length} 個`} />
          <Cell k="Approvals" v={`${draft.flow.nodes.filter(n => n.type === 'approval').length} 個`} />
          <Cell k="Test cases" v={`${draft.testCases.length} 張`} />
          <Cell k="Sample org" v={`${draft.sampleOrg.users.length} users · ${draft.sampleOrg.departments.length} dept`} />
        </div>
        <p className="mt-2 text-[11px] text-ink-faint">
          Bundle 會包含 manifest.json + spec.json + bpmn.xml + spec.md + README.md + walkthrough.md + forms/* + notifications/* + sla.json + actors.json + sample-org.json + test-cases/*。Server 端 builder 計算每檔 sha256 並打入 manifest，存進 Flow Library。
        </p>
      </section>

      {error && (
        <div className="flex items-start gap-2 rounded-md border border-rose-300 bg-rose-50 px-3 py-2 text-xs text-rose-800">
          <AlertTriangle className="mt-0.5 h-4 w-4 shrink-0" />
          <div className="break-all">{error}</div>
        </div>
      )}

      <section className="flex items-center justify-between rounded-md border border-emerald-200 bg-emerald-50 px-4 py-3">
        <div>
          <p className="text-sm font-semibold text-emerald-900">準備上線 · save to Flow Library</p>
          <p className="text-xs text-emerald-800">
            存入後可直接從 Flow Library 跑 Repro Check（在乾淨環境重放 test cases）或下載 .zip 給客戶安裝。
          </p>
        </div>
        <div className="flex gap-2">
          <Button variant="outline" onClick={handleDownload} disabled={!allValid || building}>
            <Download className="h-4 w-4" /> {building ? '建置中…' : 'Download .zip'}
          </Button>
          <Button variant="primary" onClick={handleSave} disabled={!allValid || building}>
            <Rocket className="h-4 w-4" /> {building ? '送出中…' : allValid ? 'Save to Flow Library' : `${allChecks.filter(c => !c.valid).length + (goLiveCheck.valid ? 0 : 1)} step 未通過`}
          </Button>
        </div>
      </section>
    </div>
  )
}

function Cell({ k, v }: { k: string; v: string }) {
  return (
    <div className="rounded border border-rule bg-white px-2.5 py-1.5">
      <p className="text-[10px] uppercase tracking-wider text-ink-faint">{k}</p>
      <p className="font-medium text-ink">{v}</p>
    </div>
  )
}
