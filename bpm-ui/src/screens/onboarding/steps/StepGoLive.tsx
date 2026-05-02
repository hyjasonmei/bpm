import { useState } from 'react'
import { Rocket, CheckCircle2, Copy } from 'lucide-react'
import { ONBOARDING_STEPS, validators, type DraftSpec } from '@/lib/onboarding'

export function StepGoLive({ draft }: { draft: DraftSpec }) {
  const [submitted, setSubmitted] = useState(false)
  const [copied, setCopied] = useState(false)

  const allChecks = ONBOARDING_STEPS.slice(0, -1).map(s => ({
    id: s.id,
    en: s.en,
    zh: s.zh,
    ...validators[s.id](draft),
  }))
  const allValid = allChecks.every(c => c.valid)

  const submitSpec = () => {
    // Phase A: just simulate submit. Phase B will POST /api/spec.
    console.log('[Onboarding] spec submitted:', draft)
    setSubmitted(true)
  }

  const copyJson = async () => {
    await navigator.clipboard.writeText(JSON.stringify(draft, null, 2))
    setCopied(true)
    setTimeout(() => setCopied(false), 1500)
  }

  if (submitted) {
    return (
      <div className="flex flex-col items-center justify-center py-12 text-center">
        <div className="mb-4 flex h-16 w-16 items-center justify-center rounded-full bg-emerald-100 text-emerald-700">
          <CheckCircle2 className="h-8 w-8" />
        </div>
        <h3 className="text-lg font-bold text-ink">Spec 已送出</h3>
        <p className="mt-1 max-w-md text-sm text-ink-muted">
          後台 Claude Code pipeline 已收到您的 {draft.meta.flowName} spec。預計 1-2 工作天內您會收到上線通知。
        </p>
        <div className="mt-6 rounded border border-rule bg-slate-50 px-4 py-3 text-xs text-ink-muted">
          <p>📨 Tracking ID: <span className="font-mono text-ink">{draft.meta.tenant}-{draft.meta.flowCode}-v{draft.meta.flowVersion}-{Date.now().toString(36)}</span></p>
          <p className="mt-0.5">📧 通知會寄到 {draft.meta.createdBy}</p>
        </div>
        <p className="mt-4 text-[11px] italic text-ink-faint">
          (Phase A：實際的 POST /api/spec 還沒接，這裡只是 console.log。打開 DevTools console 看 spec 內容。)
        </p>
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
            </tbody>
          </table>
        </div>
      </section>

      <section>
        <h3 className="mb-2 text-sm font-semibold text-ink">Spec 摘要</h3>
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
          <Cell k="Identity Provider" v={draft.integrations.identityProvider} />
        </div>
      </section>

      <section>
        <div className="mb-2 flex items-center justify-between">
          <h3 className="text-sm font-semibold text-ink">Spec JSON 預覽</h3>
          <button onClick={copyJson} className="flex items-center gap-1 rounded border border-rule bg-white px-2 py-1 text-[11px] text-ink-muted hover:bg-slate-50">
            <Copy className="h-3 w-3" /> {copied ? 'Copied' : 'Copy JSON'}
          </button>
        </div>
        <pre className="max-h-72 overflow-auto rounded border border-rule bg-slate-900 p-3 text-[11px] text-slate-100">
          {JSON.stringify(draft, null, 2)}
        </pre>
      </section>

      <section className="flex items-center justify-between rounded-md border border-amber-200 bg-amber-50 px-4 py-3">
        <div>
          <p className="text-sm font-semibold text-amber-900">準備上線</p>
          <p className="text-xs text-amber-800">
            送出後 spec 進入後台 Claude Code pipeline，預計 1-2 工作天部署到您的 STG 環境驗收。
          </p>
        </div>
        <button
          onClick={submitSpec}
          disabled={!allValid}
          className="flex items-center gap-1.5 rounded bg-primary px-4 py-2 text-sm font-semibold text-white hover:bg-blue-700 disabled:cursor-not-allowed disabled:bg-slate-300"
        >
          <Rocket className="h-4 w-4" />
          {allValid ? 'Submit Spec' : `${allChecks.filter(c => !c.valid).length} step 未通過`}
        </button>
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
