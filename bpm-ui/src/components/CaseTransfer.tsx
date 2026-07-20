import { useEffect, useMemo, useState } from 'react'

import type { ActionFooterItem } from '@/components/ui/action-footer/ActionFooter'
import { Button } from '@/components/ui/button'
import { Modal } from '@/components/ui/Modal'
import {
  searchTransferCandidates,
  transferCase,
  transferErrorMessage,
  type TransferCandidate,
} from '@/lib/api/transfer'

export interface UseCaseTransferArgs {
  flowCode: string
  caseId: string
  /** case 還在跑（per-flow lifecycle 判斷後傳入）。 */
  isOpen: boolean
  currentAssigneeUserId: string | null | undefined
  currentAssigneeRoleCode: string | null | undefined
  viewerUserId: string | null | undefined
  /** 我已接受代理的 delegator userIds（useDelegatedFor）。 */
  delegatedFor: string[]
  /** 成功後 refetch case。 */
  onTransferred: () => void
}

/**
 * Shared 轉簽 action（spec：docs/superpowers/specs/2026-07-19-case-transfer-design.md）。
 * 只在「個人指派關卡 + 我（或我代理的人）是當前簽核人 + 案件未結」時
 * 回傳 ActionFooterItem。角色佇列關卡（currentAssigneeRoleCode 非空）不出現。
 *
 * 用法（CaseDetail 三行）：
 *   const transfer = useCaseTransfer({ flowCode: 'OVERTIME', caseId, isOpen, ... })
 *   // footerActions：if (transfer.action) actions.push(transfer.action)
 *   // JSX：{transfer.modal}
 */
export function useCaseTransfer(args: UseCaseTransferArgs): {
  action: ActionFooterItem | null
  modal: React.ReactNode
} {
  const [open, setOpen] = useState(false)

  const canTransfer =
    args.isOpen &&
    !args.currentAssigneeRoleCode &&
    !!args.currentAssigneeUserId &&
    !!args.viewerUserId &&
    (args.currentAssigneeUserId === args.viewerUserId ||
      args.delegatedFor.includes(args.currentAssigneeUserId))

  return useMemo(() => {
    const action: ActionFooterItem | null = canTransfer
      // confirm:false — the button only opens the transfer modal, which is
      // itself the styled confirm step (per the ActionFooter product rule).
      ? { id: 'transfer', label: '轉簽', variant: 'outline', confirm: false, onClick: () => setOpen(true) }
      : null
    const modal = open ? (
      <TransferModal
        flowCode={args.flowCode}
        caseId={args.caseId}
        onClose={() => setOpen(false)}
        onDone={() => {
          setOpen(false)
          args.onTransferred()
        }}
      />
    ) : null
    return { action, modal }
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [canTransfer, open, args.flowCode, args.caseId])
}

function TransferModal({
  flowCode,
  caseId,
  onClose,
  onDone,
}: {
  flowCode: string
  caseId: string
  onClose: () => void
  onDone: () => void
}) {
  const [query, setQuery] = useState('')
  const [results, setResults] = useState<TransferCandidate[]>([])
  const [searching, setSearching] = useState(false)
  const [picked, setPicked] = useState<TransferCandidate | null>(null)
  const [reason, setReason] = useState('')
  const [busy, setBusy] = useState(false)
  const [err, setErr] = useState<string | null>(null)

  useEffect(() => {
    let stale = false
    const t = setTimeout(async () => {
      setSearching(true)
      try {
        const hits = await searchTransferCandidates(query)
        // Ignore a slow response whose query has since changed, so it can't
        // overwrite a newer query's results.
        if (!stale) setResults(hits)
      } catch {
        if (!stale) setResults([])
      } finally {
        if (!stale) setSearching(false)
      }
    }, 300)
    return () => {
      stale = true
      clearTimeout(t)
    }
  }, [query])

  const submit = async () => {
    if (!picked) return
    if (!reason.trim()) {
      setErr(transferErrorMessage('reason_required'))
      return
    }
    setBusy(true)
    setErr(null)
    try {
      const r = await transferCase(flowCode, caseId, picked.userId, reason.trim())
      if (r.ok) onDone()
      else setErr(transferErrorMessage(r.error))
    } catch {
      setErr(transferErrorMessage(null))
    } finally {
      setBusy(false)
    }
  }

  return (
    <Modal open onClose={onClose} ariaLabelledBy="transfer-modal-title">
      <div className="space-y-4 p-5">
        <h2 id="transfer-modal-title" className="text-base font-semibold text-ink">
          轉簽
        </h2>
        <p className="text-sm text-ink-muted">
          將這張案件轉給其他同仁簽核。轉出後您將不再是簽核人。
        </p>

        <div className="space-y-2">
          <label className="text-sm font-medium text-ink" htmlFor="transfer-search">
            轉簽對象
          </label>
          <input
            id="transfer-search"
            className="h-9 w-full rounded-md border border-rule bg-card px-3 text-sm"
            placeholder="搜尋姓名或 email…"
            value={query}
            onChange={e => {
              setQuery(e.target.value)
              setPicked(null)
            }}
          />
          <div className="max-h-44 overflow-y-auto rounded-md border border-rule">
            {searching && <div className="px-3 py-2 text-sm text-ink-muted">搜尋中…</div>}
            {!searching && results.length === 0 && (
              <div className="px-3 py-2 text-sm text-ink-muted">沒有符合的使用者</div>
            )}
            {!searching &&
              results.map(u => (
                <button
                  key={u.userId}
                  type="button"
                  onClick={() => setPicked(u)}
                  className={`flex w-full items-center justify-between px-3 py-2 text-left text-sm hover:bg-slate-50 ${
                    picked?.userId === u.userId ? 'bg-blue-50 text-primary' : 'text-ink'
                  }`}
                >
                  <span>{u.name}</span>
                  <span className="text-xs text-ink-muted">{u.email ?? ''}</span>
                </button>
              ))}
          </div>
        </div>

        <div className="space-y-2">
          <label className="text-sm font-medium text-ink" htmlFor="transfer-reason">
            轉簽理由 <span className="text-danger">*</span>
          </label>
          <textarea
            id="transfer-reason"
            className="min-h-20 w-full rounded-md border border-rule bg-card px-3 py-2 text-sm"
            placeholder="例如：本週出差，請代為審核"
            value={reason}
            onChange={e => setReason(e.target.value)}
          />
        </div>

        {err && <p className="text-sm text-danger">{err}</p>}

        <div className="flex justify-end gap-2">
          <Button variant="outline" onClick={onClose} disabled={busy}>
            取消
          </Button>
          <Button variant="primary" onClick={submit} disabled={busy || !picked}>
            {busy ? '轉簽中…' : '確認轉簽'}
          </Button>
        </div>
      </div>
    </Modal>
  )
}
