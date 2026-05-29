import { useCallback, useEffect, useMemo, useState } from 'react'
import { AlertTriangle, Archive, ArchiveRestore, CheckCircle2, Database, Link2 } from 'lucide-react'
import { cn } from '@/lib/cn'
import {
  archiveFeature,
  restoreFeature,
  scanFeatureTables,
  type FeatureTableGroupDto,
  type FeatureTableStatus,
} from '@/flowcook/api/featureTables'

const STATUS_META: Record<FeatureTableStatus, { label: string; tone: string; Icon: typeof Database }> = {
  Linked:   { label: 'Linked',   tone: 'bg-good/10 text-good',           Icon: Link2 },
  Orphan:   { label: 'Orphan',   tone: 'bg-warn/15 text-warn',           Icon: AlertTriangle },
  Archived: { label: 'Archived', tone: 'bg-slate-200 text-slate-600',    Icon: Archive },
  Dangling: { label: 'Dangling', tone: 'bg-primary/10 text-primary',     Icon: CheckCircle2 },
}

export function FeatureTablesTab() {
  const [rows, setRows] = useState<FeatureTableGroupDto[]>([])
  const [loading, setLoading] = useState(true)
  const [error, setError] = useState<string | null>(null)
  const [busy, setBusy] = useState<string | null>(null)
  const [confirm, setConfirm] = useState<FeatureTableGroupDto | null>(null)

  const refresh = useCallback(async () => {
    setLoading(true)
    setError(null)
    try {
      setRows(await scanFeatureTables())
    } catch (err) {
      setError(err instanceof Error ? err.message : 'Failed to load')
    } finally {
      setLoading(false)
    }
  }, [])

  useEffect(() => { void refresh() }, [refresh])

  const live = useMemo(() => rows.filter(r => r.status === 'Linked' || r.status === 'Orphan'), [rows])
  const archived = useMemo(() => rows.filter(r => r.status === 'Archived'), [rows])
  const dangling = useMemo(() => rows.filter(r => r.status === 'Dangling'), [rows])

  async function handleArchive(g: FeatureTableGroupDto) {
    setBusy(`archive:${g.flowCode}:${g.version}`)
    setError(null)
    try {
      await archiveFeature({ flowCode: g.flowCode, version: g.version, flowId: g.flowId })
      await refresh()
      setConfirm(null)
    } catch (err) {
      setError(err instanceof Error ? err.message : String(err))
    } finally {
      setBusy(null)
    }
  }

  async function handleRestore(g: FeatureTableGroupDto) {
    setBusy(`restore:${g.flowCode}:${g.version}`)
    setError(null)
    try {
      await restoreFeature({ flowCode: g.flowCode, version: g.version })
      await refresh()
    } catch (err) {
      setError(err instanceof Error ? err.message : String(err))
    } finally {
      setBusy(null)
    }
  }

  return (
    <div className="flex h-full min-h-0 flex-col gap-4">
      <div>
        <h2 className="text-base font-semibold text-ink">Feature Tables</h2>
        <p className="text-xs text-ink-muted">
          chef cook 出來的實體 table 跟 admin Flow row 對應狀態。Archive = 改名加 hash 後綴
          （資料保留、namespace 釋放）；Restore = 改回原名（前提是 namespace 還沒被新 cook 佔走）。
        </p>
      </div>

      {error && (
        <div className="rounded border border-danger/30 bg-danger/5 px-3 py-2 text-xs text-danger">{error}</div>
      )}

      <div className="flex-1 overflow-auto space-y-4">
        <Section
          title="Linked / Orphan"
          subtitle="chef cook 過、目前 table 名為原名，可 Archive。"
          rows={live}
          loading={loading}
          emptyHint="沒有任何 live 的 chef table。"
        >
          {(g) => (
            <FeatureRow
              row={g}
              actionLabel="Archive"
              ActionIcon={Archive}
              busy={busy === `archive:${g.flowCode}:${g.version}`}
              tone="risky"
              onAction={() => setConfirm(g)}
            />
          )}
        </Section>

        <Section
          title="Archived"
          subtitle="改名後尚未撤銷的 group；Restore 必須在原名 namespace 仍空時才能用。"
          rows={archived}
          loading={loading}
          emptyHint="沒有 archived 的 chef table。"
        >
          {(g) => (
            <FeatureRow
              row={g}
              actionLabel="Restore"
              ActionIcon={ArchiveRestore}
              busy={busy === `restore:${g.flowCode}:${g.version}`}
              tone="productive"
              onAction={() => void handleRestore(g)}
            />
          )}
        </Section>

        <Section
          title="Dangling"
          subtitle="Admin_Flow 存在但 chef 還沒 cook（沒對應實體 table）— 等 chef 跑完即可。"
          rows={dangling}
          loading={loading}
          emptyHint=""
        >
          {(g) => <FeatureRow row={g} infoOnly />}
        </Section>
      </div>

      {confirm && (
        <ConfirmArchiveModal
          row={confirm}
          busy={busy === `archive:${confirm.flowCode}:${confirm.version}`}
          onCancel={() => setConfirm(null)}
          onConfirm={() => void handleArchive(confirm)}
        />
      )}
    </div>
  )
}

function Section({
  title, subtitle, rows, loading, emptyHint, children,
}: {
  title: string
  subtitle: string
  rows: FeatureTableGroupDto[]
  loading: boolean
  emptyHint: string
  children: (row: FeatureTableGroupDto) => React.ReactNode
}) {
  return (
    <div className="rounded-lg border border-rule bg-card shadow-sm">
      <header className="border-b border-rule px-4 py-2">
        <div className="flex items-baseline justify-between">
          <h3 className="text-sm font-semibold text-ink">{title}</h3>
          <span className="font-mono text-[10px] tracking-wide text-ink-faint">{rows.length}</span>
        </div>
        <p className="mt-0.5 text-[11px] text-ink-muted">{subtitle}</p>
      </header>
      <div className="divide-y divide-rule">
        {loading && rows.length === 0 && (
          <div className="px-4 py-6 text-center text-xs text-ink-muted">Loading…</div>
        )}
        {!loading && rows.length === 0 && (
          <div className="px-4 py-6 text-center text-xs text-ink-faint">{emptyHint || '—'}</div>
        )}
        {rows.map(g => (
          <div key={`${g.flowCode}-v${g.version}`}>
            {children(g)}
          </div>
        ))}
      </div>
    </div>
  )
}

function FeatureRow({
  row, actionLabel, ActionIcon, busy, tone, onAction, infoOnly,
}: {
  row: FeatureTableGroupDto
  actionLabel?: string
  ActionIcon?: typeof Archive
  busy?: boolean
  tone?: 'risky' | 'productive'
  onAction?: () => void
  infoOnly?: boolean
}) {
  const meta = STATUS_META[row.status]
  const Icon = meta.Icon
  const tables = row.status === 'Archived' ? row.archivedTableNames : row.tableNames
  return (
    <div className="grid grid-cols-12 items-center gap-3 px-4 py-3 text-sm">
      <div className="col-span-3">
        <div className="flex items-center gap-2">
          <span className={cn('inline-flex items-center gap-1 rounded-full px-2 py-0.5 font-mono text-[10px] tracking-wide', meta.tone)}>
            <Icon className="h-3 w-3" />
            {meta.label}
          </span>
        </div>
        <div className="mt-0.5 font-mono text-[11px] text-ink-muted">{row.flowCode}_V{row.version}</div>
      </div>
      <div className="col-span-4">
        {row.flowDisplayName ? (
          <>
            <div className="text-ink">{row.flowDisplayName}</div>
            <div className="font-mono text-[11px] text-ink-faint">
              {row.flowState ?? '—'}{row.archivedAt && ` · archived ${formatDateTime(row.archivedAt)}`}
            </div>
          </>
        ) : (
          <span className="text-xs italic text-ink-faint">沒對應 Admin_Flow row</span>
        )}
      </div>
      <div className="col-span-3 truncate text-[11px] text-ink-muted" title={tables.join(', ')}>
        <Database className="mr-1 inline h-3 w-3" />
        {tables.length} table{tables.length === 1 ? '' : 's'}
      </div>
      <div className="col-span-2 text-right">
        {!infoOnly && actionLabel && ActionIcon && onAction && (
          <button
            onClick={onAction}
            disabled={busy}
            className={cn(
              'inline-flex items-center gap-1 rounded border px-2.5 py-1 text-xs font-medium transition-colors disabled:opacity-50',
              tone === 'risky'
                ? 'border-warn/30 text-warn hover:bg-warn/5'
                : 'border-primary/30 text-primary hover:bg-primary/5',
            )}
          >
            <ActionIcon className="h-3 w-3" />
            {busy ? 'Working…' : actionLabel}
          </button>
        )}
      </div>
    </div>
  )
}

function ConfirmArchiveModal({
  row, busy, onCancel, onConfirm,
}: {
  row: FeatureTableGroupDto
  busy: boolean
  onCancel: () => void
  onConfirm: () => void
}) {
  const required = `${row.flowCode}_V${row.version}`
  const [typed, setTyped] = useState('')
  const match = typed.trim() === required

  return (
    <div className="fixed inset-0 z-50 flex items-center justify-center bg-black/40 p-4" onClick={onCancel}>
      <div
        onClick={e => e.stopPropagation()}
        className="w-full max-w-md rounded-lg border border-rule bg-card p-5 shadow-xl"
      >
        <h3 className="mb-2 text-sm font-semibold text-ink">Archive {required}</h3>
        <p className="text-xs text-ink-muted">
          以下 chef table 將被改名加 <code className="font-mono">__arch_&lt;hash&gt;</code> 後綴：
        </p>
        <ul className="my-2 max-h-32 overflow-auto rounded border border-rule bg-bg p-2 text-[11px] font-mono text-ink-muted">
          {row.tableNames.map(t => <li key={t}>{t}</li>)}
        </ul>
        <div className="my-3 flex gap-2 rounded border border-warn/30 bg-warn/5 p-2.5 text-[11px] text-ink">
          <AlertTriangle className="mt-0.5 h-3.5 w-3.5 shrink-0 text-warn" />
          <div className="space-y-1">
            <p>
              <span className="font-semibold">bpm 端衝擊：</span>
              改名後 bpm-svc 的 inbox 會 skip 這隻 flow 的 cases（lead 端有 graceful catch，不會整個炸），但
              <span className="font-mono"> /cases/{row.flowCode}/&lt;id&gt; </span> 等直接打 case detail 的 URL 會 500。
            </p>
            {row.flowState && row.flowState !== 'Retired' && (
              <p>
                <span className="font-semibold">建議：</span>
                目前 flow state 是 <code className="font-mono">{row.flowState}</code>，
                可先 Retire（launcher 隱藏 + 拒收新 case）再 archive。
              </p>
            )}
          </div>
        </div>
        <p className="text-[11px] text-ink-faint">
          資料保留，原 namespace 釋放。輸入 <code className="font-mono text-ink">{required}</code> 確認：
        </p>
        <input
          value={typed}
          onChange={e => setTyped(e.target.value)}
          autoFocus
          className="mt-2 w-full rounded border border-rule bg-white px-2 py-1.5 font-mono text-sm text-ink outline-none focus:border-primary"
          placeholder={required}
        />
        <div className="mt-4 flex justify-end gap-2">
          <button
            onClick={onCancel}
            className="rounded border border-rule bg-card px-3 py-1.5 text-xs font-medium text-ink-muted hover:bg-bg"
          >
            Cancel
          </button>
          <button
            disabled={!match || busy}
            onClick={onConfirm}
            className="rounded bg-warn px-3 py-1.5 text-xs font-semibold text-white hover:bg-warn/90 disabled:opacity-50"
          >
            {busy ? 'Archiving…' : 'Archive'}
          </button>
        </div>
      </div>
    </div>
  )
}

function formatDateTime(iso: string): string {
  try { return new Date(iso).toISOString().slice(0, 16).replace('T', ' ') }
  catch { return iso }
}
