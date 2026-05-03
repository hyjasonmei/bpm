import { useEffect, useState } from 'react'

import { Button } from '@/components/ui/button'
import { SectionCard, SectionTitle } from '@/components/ui/card'
import { Field, InfoBanner, Input, Textarea } from '@/components/ui/form'
import type { PersonaCode } from '@/lib/role'
import {
  purchaseApi, personaToActingUserId, specUserIdToLabel,
  PURCHASE_STATE_LABEL, type PurchaseCaseDto, type PurchaseState, type ApiError,
} from '@/lib/purchaseApi'
import type { Screen } from '@/components/AppLayout'

interface Props {
  persona: PersonaCode
  caseId: string
  setScreen: (s: Screen) => void
}

export function PurchaseView({ persona, caseId, setScreen }: Props) {
  const [data, setData] = useState<PurchaseCaseDto | null>(null)
  const [error, setError] = useState<string | null>(null)
  const [busy, setBusy] = useState(false)

  // approve/reject UI state
  const [rejectOpen, setRejectOpen] = useState(false)
  const [rejectReason, setRejectReason] = useState('')

  // execute UI state (final task_purchase_exec)
  const [poNumber, setPoNumber] = useState('')
  const [expectedDelivery, setExpectedDelivery] = useState('')
  const [execNote, setExecNote] = useState('')

  useEffect(() => {
    let cancel = false
    setError(null)
    purchaseApi.get(caseId)
      .then(d => { if (!cancel) setData(d) })
      .catch((e: ApiError) => { if (!cancel) setError(e.detail ?? e.title ?? `Load failed (${e.status})`) })
    return () => { cancel = true }
  }, [caseId])

  if (error) {
    return (
      <SectionCard>
        <SectionTitle>Purchase case — load error</SectionTitle>
        <div className="p-5">
          <div className="border-l-4 border-red-300 bg-red-50 p-4 text-sm text-red-800">{error}</div>
          <Button variant="outline" size="sm" className="mt-3" onClick={() => setScreen({ kind: 'home' })}>Back to home</Button>
        </div>
      </SectionCard>
    )
  }

  if (!data) {
    return (
      <SectionCard>
        <SectionTitle>Loading purchase case…</SectionTitle>
        <div className="p-5 text-sm text-ink-muted">caseId: <code className="font-mono">{caseId}</code></div>
      </SectionCard>
    )
  }

  const stateLabel = PURCHASE_STATE_LABEL[data.state]
  const actingUserId = personaToActingUserId(persona, data.state)
  const isCurrentApprover =
    !!data.currentApproverUserId && actingUserId === data.currentApproverUserId
  const canExecute =
    data.state === 4 /* PendingPurchaseExec */ && persona === 'admin' && actingUserId === 'u_purchase_lead'

  async function refresh() {
    const d = await purchaseApi.get(caseId)
    setData(d)
  }

  async function onApprove() {
    if (!actingUserId) return
    setBusy(true); setError(null)
    try {
      await purchaseApi.approve(caseId, actingUserId)
      await refresh()
    } catch (e) {
      const err = e as ApiError
      setError(err.detail ?? err.title ?? `Approve failed (${err.status})`)
    } finally { setBusy(false) }
  }

  async function onReject() {
    if (!actingUserId || !rejectReason.trim()) return
    setBusy(true); setError(null)
    try {
      await purchaseApi.reject(caseId, actingUserId, rejectReason.trim())
      setRejectOpen(false); setRejectReason('')
      await refresh()
    } catch (e) {
      const err = e as ApiError
      setError(err.detail ?? err.title ?? `Reject failed (${err.status})`)
    } finally { setBusy(false) }
  }

  async function onExecute() {
    if (!actingUserId || !poNumber || !expectedDelivery) return
    setBusy(true); setError(null)
    try {
      await purchaseApi.execute(caseId, {
        execUserId: actingUserId,
        poNumber: poNumber.trim(),
        expectedDelivery,
        execNote: execNote.trim() || null,
      })
      await refresh()
    } catch (e) {
      const err = e as ApiError
      setError(err.detail ?? err.title ?? `Execute failed (${err.status})`)
    } finally { setBusy(false) }
  }

  return (
    <div className="space-y-4">
      <SectionCard>
        <SectionTitle right={<span className="text-xs font-normal text-ink-muted">Case <code className="font-mono">{data.id.slice(0, 8)}</code></span>}>
          採購申請 / Purchase — {stateLabel.zh} ({stateLabel.en})
        </SectionTitle>
        <div className="grid grid-cols-2 gap-x-8 gap-y-3 p-5 text-sm">
          <DataRow label="申請人 / Applicant"   value={specUserIdToLabel(data.applicantUserId)} />
          <DataRow label="供應商 / Vendor"       value={data.vendor} />
          <DataRow label="類別 / Category"        value={data.category} />
          <DataRow label="金額 / Amount"          value={`${data.amount.toLocaleString()} TWD`} />
          <DataRow label="目前簽核 / Current approver" value={specUserIdToLabel(data.currentApproverUserId)} />
          <DataRow label="建立時間 / Created"      value={new Date(data.createdAt).toLocaleString()} />
        </div>
        <div className="border-t border-rule p-5 text-sm">
          <div className="text-xs font-semibold uppercase tracking-wider text-ink-muted">品項 / Items</div>
          <pre className="mt-1 whitespace-pre-wrap font-mono text-xs text-ink">{data.items}</pre>
          <div className="mt-3 text-xs font-semibold uppercase tracking-wider text-ink-muted">採購理由 / Justification</div>
          <p className="mt-1 text-ink">{data.justification}</p>
          {data.quoteFileName && (
            <div className="mt-3 text-xs text-ink-muted">📎 報價單: <code className="font-mono">{data.quoteFileName}</code></div>
          )}
        </div>
      </SectionCard>

      <SectionCard>
        <SectionTitle>簽核紀錄 / Approval audit</SectionTitle>
        <div className="grid grid-cols-2 gap-x-8 gap-y-2 p-5 text-sm">
          <DataRow label="主管 / Manager"      value={data.managerApproverUserId ? `${specUserIdToLabel(data.managerApproverUserId)} · ${formatTs(data.managerApprovedAt)}` : '—'} />
          <DataRow label="財務 / Finance"      value={data.financeApproverUserId ? `${specUserIdToLabel(data.financeApproverUserId)} · ${formatTs(data.financeApprovedAt)}` : '—'} />
          <DataRow label="CEO"                 value={data.ceoApproverUserId ? `${specUserIdToLabel(data.ceoApproverUserId)} · ${formatTs(data.ceoApprovedAt)}` : '—'} />
          <DataRow label="採購處理 / Exec"      value={data.purchaseExecUserId ? `${specUserIdToLabel(data.purchaseExecUserId)} · ${formatTs(data.purchaseExecAt)}` : '—'} />
          {data.poNumber && <DataRow label="採購單號 / PO" value={data.poNumber} />}
          {data.expectedDelivery && <DataRow label="預計到貨 / ETA" value={data.expectedDelivery} />}
          {data.rejectedByUserId && <DataRow label="退回 / Rejected" value={`${specUserIdToLabel(data.rejectedByUserId)}: ${data.rejectionReason ?? ''}`} />}
        </div>
      </SectionCard>

      {data.state >= 1 && data.state <= 3 && (
        <SectionCard>
          <SectionTitle>動作 / Actions</SectionTitle>
          <div className="space-y-3 p-5 text-sm">
            {!isCurrentApprover ? (
              <InfoBanner>
                Persona <strong>{persona}</strong> (acting as <code className="font-mono">{actingUserId ?? '—'}</code>)
                cannot act on this case. Expected approver: <code className="font-mono">{data.currentApproverUserId}</code>.
              </InfoBanner>
            ) : !rejectOpen ? (
              <div className="flex items-center gap-2">
                <Button variant="good" size="md" disabled={busy} onClick={onApprove}>Approve / 核准</Button>
                <Button variant="destructive" size="md" disabled={busy} onClick={() => setRejectOpen(true)}>Reject / 退回</Button>
                <span className="text-xs text-ink-muted">Acting as <code className="font-mono">{actingUserId}</code></span>
              </div>
            ) : (
              <div className="space-y-2">
                <Field label="退回原因 / Reason" required>
                  <Textarea name="reject_reason" rows={2} value={rejectReason} onChange={e => setRejectReason(e.target.value)} />
                </Field>
                <div className="flex gap-2">
                  <Button variant="destructive" size="md" disabled={busy || !rejectReason.trim()} onClick={onReject}>Confirm reject</Button>
                  <Button variant="outline" size="md" onClick={() => { setRejectOpen(false); setRejectReason('') }}>Cancel</Button>
                </div>
              </div>
            )}
          </div>
        </SectionCard>
      )}

      {data.state === 4 && (
        <SectionCard>
          <SectionTitle>採購處理 / Purchase execution (task_purchase_exec)</SectionTitle>
          <div className="space-y-3 p-5 text-sm">
            {!canExecute ? (
              <InfoBanner>
                Only persona with <code className="font-mono">role:Purchase</code> may execute. Switch to <strong>Admin</strong> (which acts as <code className="font-mono">u_purchase_lead</code>) to open the PO form.
              </InfoBanner>
            ) : (
              <div className="space-y-3">
                <div className="grid grid-cols-2 gap-3">
                  <Field label="採購單號 / PO Number" required hint="ERP 開立後填回">
                    <Input name="po_number" value={poNumber} onChange={e => setPoNumber(e.target.value)} placeholder="PO-2026-001" />
                  </Field>
                  <Field label="預計到貨日 / Expected delivery" required>
                    <Input name="expected_delivery" type="date" value={expectedDelivery} onChange={e => setExpectedDelivery(e.target.value)} />
                  </Field>
                </div>
                <Field label="處理備註 / Note">
                  <Textarea name="exec_note" rows={2} value={execNote} onChange={e => setExecNote(e.target.value)} />
                </Field>
                <Button variant="primary" size="md" disabled={busy || !poNumber.trim() || !expectedDelivery} onClick={onExecute}>
                  {busy ? 'Executing…' : 'Execute purchase / 完成採購'}
                </Button>
                <p className="text-xs text-ink-muted">Acting as <code className="font-mono">{actingUserId}</code></p>
              </div>
            )}
          </div>
        </SectionCard>
      )}

      {data.state === 5 && (
        <SectionCard>
          <div className="border-l-4 border-emerald-300 bg-emerald-50 p-4 text-sm text-emerald-800">
            ✓ Purchase complete. PO <code className="font-mono">{data.poNumber}</code> · expected <code className="font-mono">{data.expectedDelivery}</code>
          </div>
        </SectionCard>
      )}

      {error && (
        <SectionCard>
          <div className="border-l-4 border-red-300 bg-red-50 p-4 text-sm text-red-800">{error}</div>
        </SectionCard>
      )}
    </div>
  )
}

function DataRow({ label, value }: { label: string; value: string }) {
  return (
    <div className="flex flex-col gap-0.5">
      <div className="text-[11px] font-semibold uppercase tracking-wider text-ink-muted">{label}</div>
      <div className="text-ink">{value}</div>
    </div>
  )
}

function formatTs(ts: string | null) {
  return ts ? new Date(ts).toLocaleString() : '—'
}

// satisfy unused-import lint when narrow PurchaseState is referenced via type only
export type _purchaseStateRef = PurchaseState
