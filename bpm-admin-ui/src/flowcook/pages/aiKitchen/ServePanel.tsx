import { useEffect, useRef } from 'react'
import {
  CheckCircle2,
  CircleOff,
  CirclePlay,
  Cloud,
  Loader2,
  PowerOff,
  Rocket,
  Sparkles,
} from 'lucide-react'
import { cn } from '@/lib/cn'
import type { DeployState, DeployStatus, EnvId } from './types'
import { cookedVersionLabel } from './types'

// FE-only POC: Serve tab. 3 mock environments (DEV / STG / PRD)
// with deploy / on / off transitions. `deployments` lives on
// WizardView so flipping between Cook and Serve tabs keeps state.
// `setDeployments` accepts a function updater because the deploy
// animation uses setTimeout and needs fresh state.

const ENVS: { id: EnvId; label: string; hint: string }[] = [
  { id: 'DEV', label: 'DEV', hint: 'Developer sandbox — auto-promote first.' },
  { id: 'STG', label: 'STG', hint: 'Staging — UAT runs here.' },
  { id: 'PRD', label: 'PRD', hint: 'Production — customer-facing.' },
]

const STATUS_LABEL: Record<DeployStatus, string> = {
  'not-deployed': 'not deployed',
  'deploying': 'deploying…',
  'running': 'running',
  'off': 'off',
}

const STATUS_TONE: Record<DeployStatus, string> = {
  'not-deployed': 'border-rule bg-bg/50 text-ink-muted',
  'deploying':    'border-primary/30 bg-primary/5 text-primary',
  'running':      'border-good/30 bg-good/10 text-good',
  'off':          'border-ink-muted/30 bg-ink-muted/10 text-ink-muted',
}

export function ServePanel({
  flowVersion,
  cookedCount,
  deployments,
  setDeployments,
}: {
  flowVersion: number
  cookedCount: number
  deployments: Record<EnvId, DeployState>
  setDeployments: React.Dispatch<React.SetStateAction<Record<EnvId, DeployState>>>
}) {
  const latestVersion = cookedVersionLabel(flowVersion, cookedCount)

  // Track outstanding deploy timers so unmount doesn't fire stale setState.
  const timersRef = useRef<Map<EnvId, number>>(new Map())
  useEffect(() => () => {
    timersRef.current.forEach((t) => window.clearTimeout(t))
    timersRef.current.clear()
  }, [])

  function deploy(env: EnvId) {
    if (!latestVersion) return
    setDeployments((prev) => ({
      ...prev,
      [env]: { ...prev[env], status: 'deploying' },
    }))
    const t = window.setTimeout(() => {
      setDeployments((prev) => ({
        ...prev,
        [env]: { status: 'running', deployedVersion: latestVersion },
      }))
      timersRef.current.delete(env)
    }, 1400)
    timersRef.current.set(env, t)
  }

  function turnOff(env: EnvId) {
    setDeployments((prev) => ({
      ...prev,
      [env]: { ...prev[env], status: 'off' },
    }))
  }

  function turnOn(env: EnvId) {
    setDeployments((prev) => ({
      ...prev,
      [env]: { ...prev[env], status: 'running' },
    }))
  }

  // Banner: show when there's a cooked version newer than what any env runs.
  const needsAttention = latestVersion && ENVS.some(
    ({ id }) => deployments[id].deployedVersion !== latestVersion,
  )

  return (
    <div className="flex h-full min-h-0 flex-col gap-4">
      <header className="flex items-center justify-between gap-3 rounded-lg border border-rule bg-card px-5 py-3 shadow-sm">
        <div className="flex items-center gap-2">
          <Cloud className="h-4 w-4 text-primary" />
          <h3 className="text-sm font-semibold text-ink">Serve</h3>
          <span className="font-mono text-[10px] tracking-[0.14em] uppercase text-ink-muted">
            per-flow deployments
          </span>
        </div>
        <div className="flex items-center gap-2 font-mono text-[10px] tracking-[0.14em] uppercase text-ink-muted">
          {latestVersion ? (
            <>latest cook · <span className="text-good">{latestVersion}</span></>
          ) : (
            <span className="italic">no cook yet</span>
          )}
        </div>
      </header>

      {!latestVersion ? (
        <NoCookYet />
      ) : (
        <>
          {needsAttention && <NewVersionBanner latestVersion={latestVersion} />}

          <div className="grid flex-1 grid-cols-1 gap-4 md:grid-cols-3">
            {ENVS.map(({ id, label, hint }) => (
              <EnvCard
                key={id}
                label={label}
                hint={hint}
                state={deployments[id]}
                latestVersion={latestVersion}
                onDeploy={() => deploy(id)}
                onTurnOff={() => turnOff(id)}
                onTurnOn={() => turnOn(id)}
              />
            ))}
          </div>
        </>
      )}
    </div>
  )
}

// ──────────────────────────────────────────────────────────────
// Bits
// ──────────────────────────────────────────────────────────────

function NoCookYet() {
  return (
    <div className="flex flex-1 items-center justify-center rounded-lg border border-dashed border-rule bg-bg/30">
      <div className="max-w-md text-center">
        <div className="mx-auto mb-3 flex h-12 w-12 items-center justify-center rounded-full bg-bg">
          <Rocket className="h-6 w-6 text-ink-faint" />
        </div>
        <p className="text-sm text-ink">No cooked version yet.</p>
        <p className="mt-1 text-xs text-ink-muted">
          Once chef completes a cook in the <span className="font-medium text-ink">Cook</span> tab,
          you can deploy it to DEV / STG / PRD here.
        </p>
      </div>
    </div>
  )
}

function NewVersionBanner({ latestVersion }: { latestVersion: string }) {
  return (
    <div className="flex items-center gap-3 rounded-lg border border-accent/30 bg-accent/5 px-4 py-3">
      <div className="flex h-7 w-7 shrink-0 items-center justify-center rounded-full bg-accent/15 text-accent">
        <Sparkles className="h-4 w-4" />
      </div>
      <div className="flex-1 text-sm">
        <p className="font-medium text-ink">
          New version <span className="font-mono text-accent">{latestVersion}</span> available
        </p>
        <p className="mt-0.5 text-xs text-ink-muted">
          Deploy to DEV first to smoke-test, then promote up the chain.
        </p>
      </div>
    </div>
  )
}

function EnvCard({
  label,
  hint,
  state,
  latestVersion,
  onDeploy,
  onTurnOff,
  onTurnOn,
}: {
  label: string
  hint: string
  state: DeployState
  latestVersion: string
  onDeploy: () => void
  onTurnOff: () => void
  onTurnOn: () => void
}) {
  const { status, deployedVersion } = state
  const hasNewer = deployedVersion !== latestVersion
  const isDeploying = status === 'deploying'

  return (
    <article className="flex flex-col rounded-lg border border-rule bg-card p-5 shadow-sm">
      <div className="flex items-baseline justify-between">
        <div>
          <h4 className="font-mono text-sm font-semibold tracking-wide text-ink">{label}</h4>
          <p className="mt-0.5 text-xs text-ink-muted">{hint}</p>
        </div>
        <StatusPill status={status} />
      </div>

      <div className="my-5 flex items-baseline gap-2">
        <span className="font-mono text-[10px] tracking-[0.14em] uppercase text-ink-faint">
          version
        </span>
        {deployedVersion ? (
          <span className="font-mono text-lg font-semibold text-ink">{deployedVersion}</span>
        ) : (
          <span className="font-mono text-sm italic text-ink-faint">none</span>
        )}
        {hasNewer && deployedVersion && (
          <span className="ml-1 inline-flex items-center gap-1 rounded-full border border-accent/30 bg-accent/5 px-2 py-0.5 font-mono text-[10px] tracking-wide text-accent">
            {latestVersion} available
          </span>
        )}
      </div>

      <div className="mt-auto flex flex-wrap items-center gap-2">
        {/* Deploy: visible when there's a newer cook to push or nothing
            deployed yet. Disabled while a deploy animation is running. */}
        {hasNewer && (
          <button
            onClick={onDeploy}
            disabled={isDeploying}
            className="inline-flex items-center gap-1.5 rounded bg-primary px-3 py-1.5 text-xs font-semibold text-white transition-colors hover:bg-primary/90 disabled:opacity-50"
          >
            {isDeploying
              ? <Loader2 className="h-3.5 w-3.5 animate-spin" />
              : <Rocket className="h-3.5 w-3.5" />}
            {isDeploying
              ? 'Deploying…'
              : deployedVersion ? `Deploy ${latestVersion}` : `Deploy ${latestVersion}`}
          </button>
        )}
        {status === 'running' && (
          <button
            onClick={onTurnOff}
            className="inline-flex items-center gap-1.5 rounded border border-rule bg-card px-3 py-1.5 text-xs font-medium text-ink-muted transition-colors hover:border-danger hover:text-danger"
          >
            <PowerOff className="h-3.5 w-3.5" />
            Turn off
          </button>
        )}
        {status === 'off' && (
          <button
            onClick={onTurnOn}
            className="inline-flex items-center gap-1.5 rounded border border-rule bg-card px-3 py-1.5 text-xs font-medium text-ink-muted transition-colors hover:border-good hover:text-good"
          >
            <CirclePlay className="h-3.5 w-3.5" />
            Turn on
          </button>
        )}
        {!hasNewer && status === 'running' && (
          <span className="inline-flex items-center gap-1 font-mono text-[10px] tracking-wide text-good">
            <CheckCircle2 className="h-3 w-3" />
            up to date
          </span>
        )}
      </div>
    </article>
  )
}

function StatusPill({ status }: { status: DeployStatus }) {
  return (
    <span className={cn(
      'inline-flex items-center gap-1 rounded-full border px-2 py-0.5 font-mono text-[10px] tracking-[0.12em] uppercase',
      STATUS_TONE[status],
    )}>
      {status === 'deploying' && <Loader2 className="h-3 w-3 animate-spin" />}
      {status === 'running'   && <CheckCircle2 className="h-3 w-3" />}
      {status === 'off'       && <CircleOff className="h-3 w-3" />}
      {STATUS_LABEL[status]}
    </span>
  )
}
