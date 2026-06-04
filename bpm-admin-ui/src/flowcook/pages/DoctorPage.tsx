import { useCallback, useEffect, useState } from 'react'
import {
  Stethoscope, RefreshCw, Loader2, AlertTriangle, UserX, Clock, UserMinus,
  ArrowRightLeft, XCircle, ExternalLink, Users,
} from 'lucide-react'
import { Link } from 'react-router-dom'
import { cn } from '@/lib/cn'
import { SectionCard, SectionTitle } from '@/components/ui/card'
import { Button } from '@/components/ui/button'
import { Select } from '@/components/ui/form'
import { Badge } from '@/components/ui/badge'
import { ConfirmDialog } from '@/components/ui/confirm-dialog'
import {
  scanDoctor, getCandidates, reassignCase, batchReassign, cancelCase,
  type DoctorReport, type CaseFinding, type DepartedPerson, type OrgFinding,
  type DoctorCandidate,
} from '@/flowcook/api/doctor'

const RULE_LABEL: Record<string, string> = {
  resigned_approver: '簽核者已離職',
  ownerless: '無人持有',
  stalled: '停滯過久',
  no_dept_head: '部門無主管',
  empty_role: '角色無在職成員',
  empty_group: '群組無在職成員',
}

function sevTone(s: string): 'danger' | 'warn' | 'info' | 'default' {
  return s === 'high' ? 'danger' : s === 'medium' ? 'warn' : 'info'
}

export function DoctorPage() {
  const [report, setReport] = useState<DoctorReport | null>(null)
  const [loading, setLoading] = useState(true)
  const [err, setErr] = useState<string | null>(null)
  const [days, setDays] = useState(14)

  const load = useCallback(async () => {
    setLoading(true); setErr(null)
    try { setReport(await scanDoctor(days)) }
    catch (e) { setErr(e instanceof Error ? e.message : 'scan failed') }
    finally { setLoading(false) }
  }, [days])
  useEffect(() => { void load() }, [load])

  const caseCount = report?.caseFindings.length ?? 0
  const orgCount = report?.orgFindings.length ?? 0

  return (
    <div className="space-y-4">
      <div className="flex flex-wrap items-end justify-between gap-3">
        <div className="flex items-center gap-3">
          <div className={cn('flex h-10 w-10 items-center justify-center rounded-lg', caseCount ? 'bg-red-50 text-danger' : 'bg-green-50 text-good')}>
            <Stethoscope className="h-5 w-5" />
          </div>
          <div>
            <h1 className="text-lg font-bold text-ink">Doctor</h1>
            <p className="text-sm text-ink-muted">
              {loading ? '健檢中…' : `${caseCount} 件卡住的案件 · ${orgCount} 個組織設定問題`}
            </p>
          </div>
        </div>
        <div className="flex items-center gap-2">
          <label className="flex items-center gap-1.5 text-xs text-ink-muted">
            停滯門檻
            <Select value={String(days)} onChange={e => setDays(Number(e.target.value))} className="h-8 w-20 text-xs">
              <option value="7">7 天</option>
              <option value="14">14 天</option>
              <option value="30">30 天</option>
            </Select>
          </label>
          <Button variant="outline" size="sm" disabled={loading} onClick={() => void load()}>
            {loading ? <Loader2 className="h-3.5 w-3.5 animate-spin" /> : <RefreshCw className="h-3.5 w-3.5" />} 重新健檢
          </Button>
        </div>
      </div>

      {err && (
        <div className="flex items-center gap-2 rounded-md border border-red-200 bg-red-50 px-3 py-2 text-sm text-danger">
          <AlertTriangle className="h-4 w-4" /> {err}
        </div>
      )}

      {report && caseCount === 0 && orgCount === 0 && !loading && (
        <div className="rounded-md border border-green-200 bg-green-50 px-4 py-3 text-sm text-green-800">
          ✅ 一切正常 — 沒有卡住的案件，組織設定也沒有問題。
        </div>
      )}

      {/* Departed people → batch reassign */}
      {report && report.departedWithCases.length > 0 && (
        <SectionCard>
          <SectionTitle><span className="inline-flex items-center gap-2"><UserMinus className="h-4 w-4 text-danger" /> 離職但仍卡著案件的人</span></SectionTitle>
          <div className="divide-y divide-slate-100">
            {report.departedWithCases.map(p => <DepartedRow key={p.userId} p={p} onDone={load} />)}
          </div>
        </SectionCard>
      )}

      {/* Case findings */}
      {report && caseCount > 0 && (
        <SectionCard>
          <SectionTitle right={<span className="text-xs text-ink-faint">{caseCount} 件</span>}>
            <span className="inline-flex items-center gap-2"><AlertTriangle className="h-4 w-4 text-danger" /> 卡住的案件</span>
          </SectionTitle>
          <div className="divide-y divide-slate-100">
            {report.caseFindings.map(f => <CaseRow key={f.flowCode + f.caseId} f={f} onDone={load} />)}
          </div>
        </SectionCard>
      )}

      {/* Org health */}
      {report && orgCount > 0 && (
        <SectionCard>
          <SectionTitle right={<span className="text-xs text-ink-faint">{orgCount} 項</span>}>
            <span className="inline-flex items-center gap-2"><Users className="h-4 w-4 text-amber-600" /> 組織設定健檢</span>
          </SectionTitle>
          <div className="divide-y divide-slate-100">
            {report.orgFindings.map((o, i) => <OrgRow key={o.rule + o.principalId + i} o={o} />)}
          </div>
        </SectionCard>
      )}
    </div>
  )
}

// ---------- case finding row (reassign / cancel) ----------

function CaseRow({ f, onDone }: { f: CaseFinding; onDone: () => Promise<void> }) {
  const [picking, setPicking] = useState(false)
  const [confirmCancel, setConfirmCancel] = useState(false)
  const [busy, setBusy] = useState(false)

  const RuleIcon = f.rule === 'resigned_approver' ? UserX : f.rule === 'ownerless' ? UserMinus : Clock

  async function doCancel() {
    setBusy(true)
    try { await cancelCase(f.flowCode, f.caseId, 'doctor force-cancel'); await onDone() }
    finally { setBusy(false); setConfirmCancel(false) }
  }

  return (
    <div className="px-4 py-3">
      <div className="flex flex-wrap items-center gap-x-3 gap-y-1">
        <Badge tone={sevTone(f.severity)}>{RULE_LABEL[f.rule] ?? f.rule}</Badge>
        <RuleIcon className="h-4 w-4 text-ink-faint" />
        <span className="font-medium text-ink">{f.flowCode}</span>
        <span className="font-mono text-[11px] text-ink-faint">{f.caseId.slice(0, 8)}</span>
        {f.statusName && <span className="text-xs text-ink-muted">{f.statusName}</span>}
        <span className="ml-auto text-xs text-ink-faint">{f.daysStuck} 天沒動</span>
      </div>
      <div className="mt-1 text-xs text-ink-muted">
        {f.assigneeName
          ? <>卡在 <span className={cn('font-medium', f.assigneeGone ? 'text-danger' : 'text-ink')}>{f.assigneeName}</span>{f.assigneeGone && '（已離職/停用）'}</>
          : <span className="text-danger">無人持有</span>}
        {f.submitterName && <> · 申請人 {f.submitterName}</>}
      </div>

      <div className="mt-2 flex flex-wrap items-center gap-2">
        <Button variant="outline" size="xs" disabled={busy} onClick={() => setPicking(p => !p)}>
          <ArrowRightLeft className="h-3.5 w-3.5" /> 改派
        </Button>
        <Button variant="ghost" size="xs" disabled={busy} onClick={() => setConfirmCancel(true)}>
          <XCircle className="h-3.5 w-3.5" /> 強制取消
        </Button>
      </div>

      {picking && (
        <ReassignPicker
          forUserId={f.assigneeUserId}
          onCancel={() => setPicking(false)}
          onPick={async (toUserId) => {
            setBusy(true)
            try { await reassignCase(f.flowCode, f.caseId, toUserId, 'doctor reassign'); setPicking(false); await onDone() }
            finally { setBusy(false) }
          }}
        />
      )}

      <ConfirmDialog
        open={confirmCancel}
        title={`Force-cancel ${f.flowCode} ${f.caseId.slice(0, 8)}`}
        titleZh="強制取消此案件"
        description="會把案件標記為已取消並結案、清掉指派。此動作不可復原。"
        confirmText="強制取消"
        onCancel={() => setConfirmCancel(false)}
        onConfirm={() => void doCancel()}
      />
    </div>
  )
}

// ---------- departed person row (batch reassign) ----------

function DepartedRow({ p, onDone }: { p: DepartedPerson; onDone: () => Promise<void> }) {
  const [picking, setPicking] = useState(false)
  const [busy, setBusy] = useState(false)
  return (
    <div className="px-4 py-3">
      <div className="flex flex-wrap items-center gap-3">
        <UserX className="h-4 w-4 text-danger" />
        <span className="font-medium text-ink">{p.name}</span>
        <Badge tone="danger">{p.deleted ? '已刪除' : '已停用'}</Badge>
        <span className="text-xs text-ink-muted">名下 {p.openCaseCount} 件在途案件</span>
        <Button className="ml-auto" variant="outline" size="xs" disabled={busy} onClick={() => setPicking(v => !v)}>
          <ArrowRightLeft className="h-3.5 w-3.5" /> 批次改派
        </Button>
      </div>
      {picking && (
        <ReassignPicker
          forUserId={p.userId}
          label={`把 ${p.name} 名下 ${p.openCaseCount} 件全部改派給：`}
          onCancel={() => setPicking(false)}
          onPick={async (toUserId) => {
            setBusy(true)
            try { await batchReassign(p.userId, toUserId, 'doctor batch reassign'); setPicking(false); await onDone() }
            finally { setBusy(false) }
          }}
        />
      )}
    </div>
  )
}

// ---------- org finding row ----------

function OrgRow({ o }: { o: OrgFinding }) {
  return (
    <div className="flex flex-wrap items-center gap-3 px-4 py-2.5">
      <Badge tone="warn">{RULE_LABEL[o.rule] ?? o.rule}</Badge>
      <span className="font-medium text-ink">{o.name}</span>
      <span className="text-xs text-ink-muted">{o.detail}</span>
      <Link to="/user-role" className="ml-auto inline-flex items-center gap-1 text-xs text-blue-600 hover:underline">
        <ExternalLink className="h-3.5 w-3.5" /> 到 User &amp; Role 處理
      </Link>
    </div>
  )
}

// ---------- shared reassign picker ----------

function ReassignPicker({ forUserId, label, onPick, onCancel }: {
  forUserId: string | null
  label?: string
  onPick: (toUserId: string) => Promise<void>
  onCancel: () => void
}) {
  const [users, setUsers] = useState<DoctorCandidate[]>([])
  const [suggested, setSuggested] = useState<DoctorCandidate | null>(null)
  const [sel, setSel] = useState('')
  const [busy, setBusy] = useState(false)
  const [loading, setLoading] = useState(true)

  useEffect(() => {
    let cancelled = false
    void (async () => {
      try {
        const c = await getCandidates(forUserId ?? undefined)
        if (cancelled) return
        setUsers(c.users); setSuggested(c.suggested)
        if (c.suggested) setSel(c.suggested.userId)
      } finally { if (!cancelled) setLoading(false) }
    })()
    return () => { cancelled = true }
  }, [forUserId])

  async function go(id: string) {
    if (!id) return
    setBusy(true)
    try { await onPick(id) } finally { setBusy(false) }
  }

  return (
    <div className="mt-2 rounded-md border border-rule bg-slate-50/60 p-3">
      {label && <p className="mb-2 text-xs text-ink-muted">{label}</p>}
      {loading ? (
        <p className="text-xs text-ink-faint">載入人選…</p>
      ) : (
        <div className="flex flex-wrap items-center gap-2">
          {suggested && (
            <Button variant="primary" size="xs" disabled={busy} onClick={() => void go(suggested.userId)}>
              建議：{suggested.name}{suggested.hint && `（${suggested.hint}）`}
            </Button>
          )}
          <Select value={sel} onChange={e => setSel(e.target.value)} className="h-8 w-48 text-xs">
            <option value="">選擇改派對象…</option>
            {users.map(u => <option key={u.userId} value={u.userId}>{u.name}{u.email ? ` · ${u.email}` : ''}</option>)}
          </Select>
          <Button variant="outline" size="xs" disabled={busy || !sel} onClick={() => void go(sel)}>
            {busy ? <Loader2 className="h-3.5 w-3.5 animate-spin" /> : '確認改派'}
          </Button>
          <Button variant="ghost" size="xs" disabled={busy} onClick={onCancel}>取消</Button>
        </div>
      )}
    </div>
  )
}
