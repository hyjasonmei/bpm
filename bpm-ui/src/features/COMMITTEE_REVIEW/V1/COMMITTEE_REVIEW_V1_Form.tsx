import { useState } from 'react'
import { useNavigate } from 'react-router-dom'
import { SectionCard } from '@/components/ui/card'
import type { FormComponentProps } from '@/features/registry'
import { apiFetch } from '@/lib/apiFetch'
import type { CmCaseResponse } from './COMMITTEE_REVIEW_V1_types'

const inputCls =
  'h-9 w-full rounded-md border border-slate-300 bg-white px-3 text-sm text-slate-800 focus:outline-none focus:ring-2 focus:ring-blue-500'

/**
 * COMMITTEE_REVIEW V1 submitter form (委員會審議). Opens a 3-member committee
 * parallel gateway (財務/法務/採購) with a 2/3 quorum threshold.
 */
export function COMMITTEE_REVIEW_V1_Form({ onSubmitted }: FormComponentProps) {
  const navigate = useNavigate()
  const [title, setTitle] = useState('')
  const [amount, setAmount] = useState('')
  const [currency, setCurrency] = useState('NTD')
  const [purpose, setPurpose] = useState('')
  const [pending, setPending] = useState(false)
  const [error, setError] = useState<string | null>(null)

  const canSubmit = title.trim() && purpose.trim() && !pending

  async function submit() {
    setPending(true)
    setError(null)
    try {
      const res = await apiFetch('/api/committee-review/v1', {
        method: 'POST',
        headers: { 'Content-Type': 'application/json' },
        body: JSON.stringify({ title, amount: Number(amount) || 0, currency, purpose }),
      })
      if (!res.ok) throw new Error(`HTTP ${res.status}`)
      const body = (await res.json()) as CmCaseResponse
      onSubmitted?.()
      navigate(`/cases/committee-review/${body.id}`)
    } catch (e) {
      setError(e instanceof Error ? e.message : String(e))
    } finally {
      setPending(false)
    }
  }

  return (
    <div className="mx-auto max-w-2xl space-y-4 p-4">
      <SectionCard className="space-y-4 p-5">
        <h2 className="text-lg font-semibold text-slate-800">委員會審議 · 送審</h2>
        <p className="text-sm text-slate-500">送出後由 <b>財務 / 法務 / 採購</b> 三位委員審議，<b>任 2 位核准</b>即通過（門檻 2/3）。</p>

        <label className="block text-sm">
          <span className="mb-1 block font-medium text-slate-700">案由標題</span>
          <input className={inputCls} value={title} onChange={e => setTitle(e.target.value)} placeholder="例：Q3 行銷預算追加" />
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
        <label className="block text-sm">
          <span className="mb-1 block font-medium text-slate-700">案由說明</span>
          <textarea className={`${inputCls} h-24 py-2`} value={purpose} onChange={e => setPurpose(e.target.value)} placeholder="說明審議事項…" />
        </label>

        {error && <p className="text-sm text-red-600">送出失敗：{error}</p>}

        <div className="flex justify-end">
          <button onClick={submit} disabled={!canSubmit}
            className="h-9 rounded-md bg-blue-600 px-5 text-sm font-medium text-white disabled:opacity-40">
            {pending ? '送出中…' : '送出審議'}
          </button>
        </div>
      </SectionCard>
    </div>
  )
}
