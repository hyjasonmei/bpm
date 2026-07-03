import { useState } from 'react'
import { useNavigate } from 'react-router-dom'
import { SectionCard } from '@/components/ui/card'
import type { FormComponentProps } from '@/features/registry'
import { apiFetch } from '@/lib/apiFetch'
import type { CrCaseResponse } from './CONTRACT_REVIEW_V1_types'

const inputCls =
  'h-9 w-full rounded-md border border-slate-300 bg-white px-3 text-sm text-slate-800 focus:outline-none focus:ring-2 focus:ring-blue-500'

/**
 * CONTRACT_REVIEW V1 submitter form (合約審查). Posts to
 * POST /api/contract-review/v1, which opens a LEGAL + FINANCE 並簽 gateway.
 * Approval UI lives on CONTRACT_REVIEW_V1_CaseDetail.
 */
export function CONTRACT_REVIEW_V1_Form({ onSubmitted }: FormComponentProps) {
  const navigate = useNavigate()
  const [title, setTitle] = useState('')
  const [counterparty, setCounterparty] = useState('')
  const [amount, setAmount] = useState('')
  const [currency, setCurrency] = useState('NTD')
  const [pending, setPending] = useState(false)
  const [error, setError] = useState<string | null>(null)

  const canSubmit = title.trim() && counterparty.trim() && !pending

  async function submit() {
    setPending(true)
    setError(null)
    try {
      const res = await apiFetch('/api/contract-review/v1', {
        method: 'POST',
        headers: { 'Content-Type': 'application/json' },
        body: JSON.stringify({ title, counterparty, amount: Number(amount) || 0, currency }),
      })
      if (!res.ok) throw new Error(`HTTP ${res.status}`)
      const body = (await res.json()) as CrCaseResponse
      onSubmitted?.()
      navigate(`/cases/contract-review/${body.id}`)
    } catch (e) {
      setError(e instanceof Error ? e.message : String(e))
    } finally {
      setPending(false)
    }
  }

  return (
    <div className="mx-auto max-w-2xl space-y-4 p-4">
      <SectionCard className="space-y-4 p-5">
        <h2 className="text-lg font-semibold text-slate-800">合約審查 · 送審</h2>
        <p className="text-sm text-slate-500">送出後由 <b>法務</b> 與 <b>財務</b> 並簽（兩方都核准才通過）。</p>

        <label className="block text-sm">
          <span className="mb-1 block font-medium text-slate-700">合約標題</span>
          <input className={inputCls} value={title} onChange={e => setTitle(e.target.value)} placeholder="例：ACME 供貨合約" />
        </label>
        <label className="block text-sm">
          <span className="mb-1 block font-medium text-slate-700">相對方</span>
          <input className={inputCls} value={counterparty} onChange={e => setCounterparty(e.target.value)} placeholder="例：ACME Corp" />
        </label>
        <div className="grid grid-cols-2 gap-3">
          <label className="block text-sm">
            <span className="mb-1 block font-medium text-slate-700">金額</span>
            <input className={inputCls} type="number" value={amount} onChange={e => setAmount(e.target.value)} placeholder="0" />
          </label>
          <label className="block text-sm">
            <span className="mb-1 block font-medium text-slate-700">幣別</span>
            <select className={inputCls} value={currency} onChange={e => setCurrency(e.target.value)}>
              <option>NTD</option><option>USD</option><option>EUR</option>
            </select>
          </label>
        </div>

        {error && <p className="text-sm text-red-600">送出失敗：{error}</p>}

        <div className="flex justify-end">
          <button
            onClick={submit}
            disabled={!canSubmit}
            className="h-9 rounded-md bg-blue-600 px-5 text-sm font-medium text-white disabled:opacity-40"
          >
            {pending ? '送出中…' : '送出審查'}
          </button>
        </div>
      </SectionCard>
    </div>
  )
}
