import { useCallback, useEffect, useMemo, useState } from 'react'
import {
  CheckCircle2,
  Cloud,
  Loader2,
  Rocket,
  ShieldCheck,
  Undo2,
} from 'lucide-react'
import { cn } from '@/lib/cn'
import {
  approveFlow,
  listFlowDeployments,
  setFlowDeployment,
  type FlowDeploymentDto,
  type FlowDeploymentStatus,
} from '@/flowcook/api/environments'
import { getFlow, type FlowState } from '@/flowcook/api/flows'

const POLL_MS = 30_000

const STATUS_LABEL: Record<FlowDeploymentStatus, string> = {
  NotDeployed: 'not deployed',
  Deployed:    'deployed',
}

const STATUS_TONE: Record<FlowDeploymentStatus, string> = {
  NotDeployed: 'border-rule bg-bg/50 text-ink-muted',
  Deployed:    'border-good/30 bg-good/10 text-good',
}

export function ServePanel({
  flowId,
  state,
  onStateChange,
}: {
  flowId: string
  state: FlowState
  onStateChange: (next: FlowState) => void
}) {
  const [rows, setRows] = useState<FlowDeploymentDto[] | null>(null)
  const [loadError, setLoadError] = useState<string | null>(null)
  const [pending, setPending] = useState<Set<string>>(new Set())
  const [approving, setApproving] = useState(false)
  const [approveError, setApproveError] = useState<string | null>(null)

  const fetchRows = useCallback(async () => {
    try {
      const next = await listFlowDeployments(flowId)
      setRows(next)
      setLoadError(null)
    } catch (err) {
      setLoadError(err instanceof Error ? err.message : 'Failed to load deployments')
    }
  }, [flowId])

  useEffect(() => {
    void fetchRows()
    const t = window.setInterval(() => { void fetchRows() }, POLL_MS)
    return () => window.clearInterval(t)
  }, [fetchRows])

  async function toggle(env: FlowDeploymentDto) {
    const nextStatus: FlowDeploymentStatus =
      env.status === 'Deployed' ? 'NotDeployed' : 'Deployed'
    setPending(prev => new Set(prev).add(env.environmentId))
    try {
      const updated = await setFlowDeployment(flowId, env.environmentId, { status: nextStatus })
      setRows(prev => prev?.map(r => r.environmentId === env.environmentId ? updated : r) ?? null)
    } catch (err) {
      window.alert(err instanceof Error ? err.message : 'Update failed')
    } finally {
      setPending(prev => { const n = new Set(prev); n.delete(env.environmentId); return n })
    }
  }

  async function approve() {
    setApproving(true); setApproveError(null)
    try {
      await approveFlow(flowId)
      const fresh = await getFlow(flowId)
      onStateChange(fresh.state)
    } catch (err) {
      setApproveError(err instanceof Error ? err.message : 'Approve failed')
    } finally {
      setApproving(false)
    }
  }

  const canApprove = state === 'Committed'
  const approveHint = useMemo(() => {
    if (state === 'Approved') return 'Already approved — flow is live in the launcher.'
    if (state === 'Committed') return 'Mark as customer-approved; bpm launcher starts showing this version.'
    return `Approve only available when chef has committed a cook (current state: ${state}).`
  }, [state])

  return (
    <div className="flex h-full min-h-0 flex-col gap-4">
      <header className="flex items-center justify-between gap-3 rounded-lg border border-rule bg-card px-5 py-3 shadow-sm">
        <div className="flex items-center gap-2">
          <Cloud className="h-4 w-4 text-primary" />
          <h3 className="text-sm font-semibold text-ink">Serve</h3>
          <span className="font-mono text-[10px] tracking-[0.14em] uppercase text-ink-muted">
            user-managed deployment board
          </span>
        </div>
        <div className="flex items-center gap-2 font-mono text-[10px] tracking-[0.14em] uppercase text-ink-muted">
          <span>flow state ·</span>
          <span className={cn(
            'rounded px-1.5 py-0.5',
            state === 'Approved' ? 'bg-good/10 text-good'
              : state === 'Committed' ? 'bg-primary/10 text-primary'
                : 'bg-ink-muted/10 text-ink-muted',
          )}>{state}</span>
        </div>
      </header>

      {loadError && (
        <div className="rounded border border-danger/30 bg-danger/5 px-3 py-2 text-xs text-danger">{loadError}</div>
      )}

      {rows === null ? (
        <div className="flex flex-1 items-center justify-center text-sm text-ink-muted">Loading deployments…</div>
      ) : rows.length === 0 ? (
        <div className="flex flex-1 items-center justify-center rounded-lg border border-dashed border-rule bg-bg/30">
          <div className="max-w-md text-center">
            <p className="text-sm text-ink">No environments configured.</p>
            <p className="mt-1 text-xs text-ink-muted">
              定義環境到 <span className="font-medium text-ink">Site Setting → Environments</span> 後，這裡會列出每個環境的部署狀態。
            </p>
          </div>
        </div>
      ) : (
        <div className="grid flex-1 grid-cols-1 gap-4 md:grid-cols-2 lg:grid-cols-3">
          {rows.map(env => (
            <EnvCard
              key={env.environmentId}
              env={env}
              busy={pending.has(env.environmentId)}
              onToggle={() => void toggle(env)}
            />
          ))}
        </div>
      )}

      <footer className="rounded-lg border border-rule bg-card p-4 shadow-sm">
        <div className="flex items-start justify-between gap-4">
          <div>
            <div className="flex items-center gap-2">
              <ShieldCheck className="h-4 w-4 text-primary" />
              <h4 className="text-sm font-semibold text-ink">Approve flow</h4>
              <span className="font-mono text-[10px] tracking-[0.14em] uppercase text-ink-muted">customer ship-it</span>
            </div>
            <p className="mt-1 text-xs text-ink-muted">{approveHint}</p>
            {approveError && <p className="mt-1 text-xs text-danger">{approveError}</p>}
          </div>
          <button
            disabled={!canApprove || approving}
            onClick={() => void approve()}
            title={canApprove ? undefined : `需 state=Committed (當前: ${state})`}
            className="inline-flex shrink-0 items-center gap-1.5 rounded bg-good px-4 py-2 text-xs font-semibold text-white transition-colors hover:bg-good/90 disabled:cursor-not-allowed disabled:bg-ink-muted/30 disabled:text-ink-muted"
          >
            {approving
              ? <Loader2 className="h-3.5 w-3.5 animate-spin" />
              : <CheckCircle2 className="h-3.5 w-3.5" />}
            {state === 'Approved' ? 'Approved' : approving ? 'Approving…' : 'Approve'}
          </button>
        </div>
      </footer>
    </div>
  )
}

function EnvCard({
  env, busy, onToggle,
}: {
  env: FlowDeploymentDto
  busy: boolean
  onToggle: () => void
}) {
  const deployed = env.status === 'Deployed'

  return (
    <article className="flex flex-col rounded-lg border border-rule bg-card p-5 shadow-sm">
      <div className="flex items-start justify-between">
        <div>
          <h4 className="font-mono text-sm font-semibold tracking-wide text-ink">{env.environmentDisplayName}</h4>
          <p className="mt-0.5 font-mono text-[10px] tracking-[0.14em] uppercase text-ink-faint">{env.environmentCode}</p>
        </div>
        <StatusPill status={env.status} />
      </div>

      <div className="my-4">
        <p className="font-mono text-[10px] tracking-[0.14em] uppercase text-ink-faint">deployed at</p>
        <p className="mt-1 font-mono text-xs text-ink">
          {env.deployedAt ? new Date(env.deployedAt).toLocaleString() : <span className="italic text-ink-faint">—</span>}
        </p>
      </div>

      <div className="mt-auto">
        <button
          onClick={onToggle}
          disabled={busy}
          className={cn(
            'inline-flex w-full items-center justify-center gap-1.5 rounded px-3 py-2 text-xs font-semibold transition-colors disabled:opacity-50',
            deployed
              ? 'border border-rule bg-card text-ink-muted hover:border-danger hover:text-danger'
              : 'bg-primary text-white hover:bg-primary/90',
          )}
        >
          {busy
            ? <Loader2 className="h-3.5 w-3.5 animate-spin" />
            : deployed ? <Undo2 className="h-3.5 w-3.5" /> : <Rocket className="h-3.5 w-3.5" />}
          {busy ? 'Saving…' : deployed ? 'Mark as undeployed' : 'Mark as deployed'}
        </button>
      </div>
    </article>
  )
}

function StatusPill({ status }: { status: FlowDeploymentStatus }) {
  return (
    <span className={cn(
      'inline-flex items-center gap-1 rounded-full border px-2 py-0.5 font-mono text-[10px] tracking-[0.12em] uppercase',
      STATUS_TONE[status],
    )}>
      {status === 'Deployed' && <CheckCircle2 className="h-3 w-3" />}
      {STATUS_LABEL[status]}
    </span>
  )
}
