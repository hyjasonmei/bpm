import { useCallback, useEffect, useState } from 'react'
import { SectionCard } from '@/components/ui/card'
import { ConfirmDialog } from '@/components/ui/ConfirmDialog'
import { BpmnView } from '@/components/BpmnView'
import { ParallelApprovalPanel, type ParallelSlotView } from '@/components/ParallelApprovalPanel'
import type { CaseDetailProps } from '@/features/registry'
import { apiFetch } from '@/lib/apiFetch'
import CmBpmnXml from './COMMITTEE_REVIEW_V1.bpmn.xml?raw'
import type { CmCaseResponse } from './COMMITTEE_REVIEW_V1_types'

const STATUS_ZH: Record<string, string> = {
  PendingCommittee: '委員會審議中',
  Completed: '已通過',
  Rejected: '已退件',
}

export function COMMITTEE_REVIEW_V1_CaseDetail({ caseId }: CaseDetailProps) {
  const [data, setData] = useState<CmCaseResponse | null>(null)
  const [error, setError] = useState<string | null>(null)
  const [bpmnOpen, setBpmnOpen] = useState(false)
  const [confirm, setConfirm] = useState<{ slotId: string; approve: boolean; role?: string } | null>(null)
  const [acting, setActing] = useState(false)

  const load = useCallback(async () => {
    try {
      const res = await apiFetch(`/api/committee-review/v1/${caseId}`)
      if (!res.ok) throw new Error(`HTTP ${res.status}`)
      setData((await res.json()) as CmCaseResponse)
    } catch (e) {
      setError(e instanceof Error ? e.message : String(e))
    }
  }, [caseId])

  useEffect(() => { void load() }, [load])

  async function decide() {
    if (!confirm) return
    setActing(true)
    try {
      const res = await apiFetch(`/api/committee-review/v1/${caseId}/slots/${confirm.slotId}/decision`, {
        method: 'POST',
        headers: { 'Content-Type': 'application/json' },
        body: JSON.stringify({ approve: confirm.approve, comment: null }),
      })
      if (!res.ok) throw new Error(res.status === 403 ? '你不是這個委員會關卡的指定人' : `HTTP ${res.status}`)
      setConfirm(null)
      await load()
    } catch (e) {
      setError(e instanceof Error ? e.message : String(e))
      setConfirm(null)
    } finally {
      setActing(false)
    }
  }

  if (error) return <div className="p-4 text-sm text-red-600">載入失敗：{error}</div>
  if (!data) return <div className="p-4 text-sm text-slate-500">載入中…</div>

  const review = data.review
  const panelSlots: ParallelSlotView[] = (review?.slots ?? []).map(s => ({
    role: s.roleCode,
    name: s.deciderName ?? s.roleCode ?? s.nodeId,
    state: s.state,
    comment: s.comment,
    at: s.at ? new Date(s.at).toLocaleString('zh-TW', { hour12: false }) : undefined,
  }))
  const nodeIds = (st: string) => (review?.slots ?? []).filter(s => s.state === st).map(s => s.nodeId)

  // Structural nodes (must match .bpmn.xml). Slots only cover the branch tasks;
  // colour start+fork once submitted, and join+end once Completed, so a finished
  // case lights its 通過 end node.
  const START = 's', FORK = 'gw_committee', JOIN = 'gw_join', END = 'e'
  const structuralDone = [START, FORK]
  if (data.status === 'Completed') structuralDone.push(JOIN, END)

  return (
    <div className="mx-auto max-w-3xl space-y-4 p-4">
      <SectionCard className="space-y-2 p-5">
        <div className="flex items-center justify-between">
          <h1 className="text-lg font-semibold text-slate-800">委員會審議：{data.title}</h1>
          <span className="rounded-full bg-slate-100 px-3 py-1 text-xs font-medium text-slate-600">
            {STATUS_ZH[data.status] ?? data.status}
          </span>
        </div>
        <div className="text-sm text-slate-500">
          {data.amount.toLocaleString()} {data.currency} · 申請人 {data.submitterName ?? '—'}
        </div>
        <p className="text-sm text-slate-600">{data.purpose}</p>
        <button onClick={() => setBpmnOpen(true)} className="text-sm text-blue-600 hover:underline">檢視流程圖</button>
      </SectionCard>

      {review && (
        <SectionCard className="p-1">
          <ParallelApprovalPanel
            policyLabel={review.policyLabel}
            approvedCount={review.approvedCount}
            threshold={review.threshold}
            slots={panelSlots}
          />
          {data.status === 'PendingCommittee' && (
            <div className="space-y-2 p-4 pt-0">
              {review.slots.filter(s => s.state === 'pending').map(s => (
                <div key={s.slotId} className="flex items-center justify-between rounded-md border border-slate-200 px-3 py-2">
                  <span className="text-sm text-slate-600">{s.roleCode} 委員待審議</span>
                  <div className="flex gap-2">
                    <button onClick={() => setConfirm({ slotId: s.slotId, approve: true, role: s.roleCode })}
                      className="h-8 rounded-md bg-green-600 px-3 text-sm font-medium text-white">核准</button>
                    <button onClick={() => setConfirm({ slotId: s.slotId, approve: false, role: s.roleCode })}
                      className="h-8 rounded-md border border-red-200 px-3 text-sm font-medium text-red-600">退件</button>
                  </div>
                </div>
              ))}
            </div>
          )}
        </SectionCard>
      )}

      <BpmnView
        open={bpmnOpen}
        onClose={() => setBpmnOpen(false)}
        steps={[]}
        activeStep={0}
        ownerByStep={[]}
        formLabel="委員會審議"
        bpmnXml={CmBpmnXml}
        completedNodes={[...nodeIds('approved'), ...structuralDone]}
        currentNodes={nodeIds('pending')}
        rejectedNodes={nodeIds('rejected')}
        skippedNodes={nodeIds('skipped')}
      />

      <ConfirmDialog
        open={!!confirm}
        title={confirm?.approve ? 'Approve' : 'Reject'}
        titleZh={confirm?.approve ? `以 ${confirm?.role} 委員身分核准` : `以 ${confirm?.role} 委員身分退件`}
        description={confirm?.approve ? '確認核准此委員會案？（達門檻 2/3 即通過）' : '確認退件？任一委員退件將使整案退回。'}
        tone={confirm?.approve ? 'default' : 'danger'}
        confirmText={acting ? '處理中…' : confirm?.approve ? '核准' : '退件'}
        onConfirm={decide}
        onCancel={() => setConfirm(null)}
      />
    </div>
  )
}
