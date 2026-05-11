/**
 * Render a ReproReport returned by POST /repro-check or stored on the
 * detail endpoint. Mirrors the shape in lib/api/flowLibrary.ts.
 */
import { X, CheckCircle2, XCircle } from 'lucide-react'
import { Button } from '@/components/ui/button'
import type { ReproReport } from '@/types/flowLibrary'

export function ReproReportModal({ report, onClose }: { report: ReproReport; onClose: () => void }) {
  const isPass = report.overallStatus === 'pass'
  const passed = report.cases.filter(c => c.status === 'pass').length
  return (
    <div className="fixed inset-0 z-[60] flex items-center justify-center bg-black/50 p-4" onClick={onClose}>
      <div className="w-full max-w-2xl rounded-lg bg-white shadow-2xl" onClick={e => e.stopPropagation()}>
        <div className="flex items-center justify-between border-b border-rule px-5 py-3">
          <div className="flex items-center gap-2">
            {isPass
              ? <CheckCircle2 className="h-5 w-5 text-good" />
              : <XCircle className="h-5 w-5 text-danger" />
            }
            <h3 className="text-base font-bold text-ink">
              Repro report — <span className={isPass ? 'text-good' : 'text-danger'}>{passed}/{report.cases.length} {isPass ? 'PASS' : 'FAIL'}</span>
            </h3>
          </div>
          <button onClick={onClose} className="rounded p-1 text-slate-400 hover:bg-slate-100 hover:text-slate-700">
            <X className="h-4 w-4" />
          </button>
        </div>
        <div className="max-h-[70vh] space-y-3 overflow-auto px-5 py-4">
          {report.cases.length === 0 && (
            <p className="text-sm text-ink-muted">No test cases were bundled.</p>
          )}
          {report.cases.map(c => (
            <ReproCaseBlock key={c.caseId} c={c} />
          ))}
        </div>
        <div className="flex justify-end border-t border-rule bg-slate-50/50 px-5 py-3">
          <Button variant="outline" onClick={onClose}>Close</Button>
        </div>
      </div>
    </div>
  )
}

function ReproCaseBlock({ c }: { c: ReproReport['cases'][number] }) {
  const ok = c.status === 'pass'
  return (
    <div className={`rounded border p-3 ${ok ? 'border-green-200 bg-green-50/40' : 'border-red-200 bg-red-50/40'}`}>
      <div className="flex items-center gap-2">
        {ok
          ? <CheckCircle2 className="h-4 w-4 text-good" />
          : <XCircle className="h-4 w-4 text-danger" />
        }
        <span className="text-sm font-semibold text-ink">{c.caseId}</span>
        <span className={`text-xs font-medium ${ok ? 'text-good' : 'text-danger'}`}>{ok ? 'PASS' : 'FAIL'}</span>
      </div>
      <div className="mt-2 grid grid-cols-1 gap-2 text-xs sm:grid-cols-2">
        <div>
          <div className="font-semibold text-ink-muted">Expected trace</div>
          <code className="mt-0.5 block rounded bg-slate-50 px-2 py-1 font-mono text-[11px] text-slate-700">
            {c.expectedTrace.join(' → ')}
          </code>
        </div>
        <div>
          <div className="font-semibold text-ink-muted">Actual trace</div>
          <code className="mt-0.5 block rounded bg-slate-50 px-2 py-1 font-mono text-[11px] text-slate-700">
            {c.actualTrace.join(' → ')}
          </code>
        </div>
      </div>
      {c.diff && (
        <pre className="mt-2 max-h-48 overflow-auto whitespace-pre-wrap rounded bg-slate-900 p-2 font-mono text-[11px] leading-snug text-slate-100">
          {c.diff}
        </pre>
      )}
    </div>
  )
}
