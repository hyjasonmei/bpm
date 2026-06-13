import { useEffect, useMemo, useState } from 'react'
import { CheckCircle2, GitMerge, Loader2, Rocket, ShieldCheck, Undo2 } from 'lucide-react'
import { cn } from '@/lib/cn'
import {
  getFlow, approveFlow, publishFlow, unpublishFlow, markMerged, type FlowState,
} from '@/flowcook/api/flows'

/**
 * Serve tab — the two-stage go-live for one stack/environment:
 *   Committed ──approve──► Approved ──publish──► Published (live in launcher)
 *                              ▲                     │
 *                              └──────unpublish──────┘
 * (Per-environment deployment board removed — one stack = one environment;
 * Published here = live here.)
 */
export function ServePanel({
  flowId,
  state,
  onStateChange,
}: {
  flowId: string
  state: FlowState
  onStateChange: (next: FlowState) => void
}) {
  const [busy, setBusy] = useState(false)
  const [error, setError] = useState<string | null>(null)
  // PR-CA1: Publish is gated on the cook branch being merged to main.
  const [mergedAt, setMergedAt] = useState<string | null>(null)
  const [prUrl, setPrUrl] = useState<string | null>(null)

  useEffect(() => {
    let alive = true
    void getFlow(flowId).then((f) => {
      if (!alive) return
      setMergedAt(f.mergedAt)
      setPrUrl(f.prUrl)
    }).catch(() => { /* leave gating closed on load error */ })
    return () => { alive = false }
  }, [flowId])

  async function run(action: () => Promise<unknown>) {
    setBusy(true); setError(null)
    try {
      await action()
      const fresh = await getFlow(flowId)
      onStateChange(fresh.state)
      setMergedAt(fresh.mergedAt)
      setPrUrl(fresh.prUrl)
    } catch (err) {
      setError(err instanceof Error ? err.message : 'Action failed')
    } finally {
      setBusy(false)
    }
  }

  const publishBlocked = state === 'Approved' && !mergedAt

  const hint = useMemo(() => {
    switch (state) {
      case 'Committed': return '審核通過 → Approved（已審但還沒在這環境上線）。'
      case 'Approved':  return mergedAt
        ? '尚未上線。按 Publish 讓它在這環境的 launcher 出現。'
        : 'branch 還沒 merge 進 main，Publish 暫時鎖住。等 chef agent 偵測到 merge，或手動「Mark merged」確認。'
      case 'Published': return '已上線 — 在這環境的 launcher 顯示中。可 Unpublish 下架（保留審核）。'
      default:          return `需要 chef 先 commit 一版（目前狀態：${state}）。`
    }
  }, [state, mergedAt])

  return (
    <div className="flex h-full min-h-0 flex-col gap-4">
      <header className="flex items-center justify-between gap-3 rounded-lg border border-rule bg-card px-5 py-3 shadow-sm">
        <div className="flex items-center gap-2">
          <Rocket className="h-4 w-4 text-primary" />
          <h3 className="text-sm font-semibold text-ink">Serve</h3>
          <span className="font-mono text-[10px] tracking-[0.14em] uppercase text-ink-muted">approve → publish</span>
        </div>
        <div className="flex items-center gap-2 font-mono text-[10px] tracking-[0.14em] uppercase text-ink-muted">
          <span>flow state ·</span>
          <span className={cn(
            'rounded px-1.5 py-0.5',
            state === 'Published' ? 'bg-good/10 text-good'
              : state === 'Approved' ? 'bg-amber-500/10 text-amber-600'
                : state === 'Committed' ? 'bg-primary/10 text-primary'
                  : 'bg-ink-muted/10 text-ink-muted',
          )}>{state}</span>
        </div>
      </header>

      <div className="flex flex-1 items-center justify-center">
        <div className="w-full max-w-md rounded-lg border border-rule bg-card p-6 text-center shadow-sm">
          <div className="mx-auto mb-3 flex h-12 w-12 items-center justify-center rounded-full bg-primary/10 text-primary">
            {state === 'Published' ? <CheckCircle2 className="h-6 w-6" /> : <ShieldCheck className="h-6 w-6" />}
          </div>
          <p className="text-sm text-ink-muted">{hint}</p>
          {publishBlocked && prUrl && (
            <a href={prUrl} target="_blank" rel="noreferrer"
               className="mt-2 inline-block font-mono text-[11px] text-primary underline underline-offset-2 hover:text-blue-700">
              查看 PR ↗
            </a>
          )}
          {error && <p className="mt-2 text-xs text-danger">{error}</p>}

          <div className="mt-4 flex flex-wrap items-center justify-center gap-2">
            <button
              disabled={state !== 'Committed' || busy}
              onClick={() => void run(() => approveFlow(flowId))}
              className="inline-flex items-center gap-1.5 rounded bg-primary px-4 py-2 text-xs font-semibold text-white transition-colors hover:bg-blue-700 disabled:cursor-not-allowed disabled:bg-ink-muted/30 disabled:text-ink-muted"
            >
              {busy ? <Loader2 className="h-3.5 w-3.5 animate-spin" /> : <CheckCircle2 className="h-3.5 w-3.5" />} Approve
            </button>
            {publishBlocked && (
              <button
                disabled={busy}
                onClick={() => {
                  if (!window.confirm('手動確認此 flow 的 branch 已 merge 進 main？這會解鎖 Publish。\n（squash merge 或無遠端環境，自動偵測抓不到時用）')) return
                  void run(() => markMerged(flowId))
                }}
                className="inline-flex items-center gap-1.5 rounded border border-rule bg-card px-4 py-2 text-xs font-semibold text-ink-muted transition-colors hover:border-primary hover:text-primary disabled:cursor-not-allowed disabled:opacity-40"
              >
                {busy ? <Loader2 className="h-3.5 w-3.5 animate-spin" /> : <GitMerge className="h-3.5 w-3.5" />} Mark merged
              </button>
            )}
            <button
              disabled={state !== 'Approved' || busy || publishBlocked}
              onClick={() => void run(() => publishFlow(flowId))}
              title={publishBlocked ? 'branch 還沒 merge 進 main' : undefined}
              className="inline-flex items-center gap-1.5 rounded bg-good px-4 py-2 text-xs font-semibold text-white transition-colors hover:bg-good/90 disabled:cursor-not-allowed disabled:bg-ink-muted/30 disabled:text-ink-muted"
            >
              {busy ? <Loader2 className="h-3.5 w-3.5 animate-spin" /> : <Rocket className="h-3.5 w-3.5" />} Publish
            </button>
            <button
              disabled={state !== 'Published' || busy}
              onClick={() => void run(() => unpublishFlow(flowId))}
              className="inline-flex items-center gap-1.5 rounded border border-rule bg-card px-4 py-2 text-xs font-semibold text-ink-muted transition-colors hover:border-danger hover:text-danger disabled:cursor-not-allowed disabled:opacity-40"
            >
              {busy ? <Loader2 className="h-3.5 w-3.5 animate-spin" /> : <Undo2 className="h-3.5 w-3.5" />} Unpublish
            </button>
          </div>
        </div>
      </div>
    </div>
  )
}
