import { useMemo, useState } from 'react'
import { Paperclip } from 'lucide-react'

import { Button } from '@/components/ui/button'
import { SectionCard, SectionTitle } from '@/components/ui/card'
import { Field, InfoBanner, Input, Select, Textarea } from '@/components/ui/form'
import type { PersonaCode } from '@/lib/role'
import { purchaseApi, personaToSpecUserId, type ApiError } from '@/lib/purchaseApi'
import type { Screen } from '@/components/AppLayout'

// spec.userTasks[task_request].fields[category].options — verbatim from sample_specs/purchase_v1.json
const CATEGORY_OPTIONS = [
  { value: 'office',  label: '辦公耗材 / Office' },
  { value: 'it',      label: 'IT 設備 / IT' },
  { value: 'service', label: '服務委外 / Service' },
  { value: 'other',   label: '其他 / Other' },
] as const

const QUOTE_REQUIRED_AMOUNT = 10000  // spec.userTasks[task_request].fields[quote_file].conditional

interface Props {
  persona: PersonaCode
  setScreen: (s: Screen) => void
  tenantCode?: string
}

export function PurchaseForm({ persona, setScreen, tenantCode = 'acme' }: Props) {
  const applicantUserId = personaToSpecUserId(persona)

  const [vendor, setVendor] = useState('')
  const [category, setCategory] = useState<string>('office')
  const [amount, setAmount] = useState<string>('')
  const [items, setItems] = useState('')
  const [justification, setJustification] = useState('')
  const [quoteFileName, setQuoteFileName] = useState<string | null>(null)
  const [submitting, setSubmitting] = useState(false)
  const [error, setError] = useState<string | null>(null)

  const amountNum = useMemo(() => Number.parseFloat(amount) || 0, [amount])
  const quoteRequired = amountNum >= QUOTE_REQUIRED_AMOUNT
  const needFinance = amountNum >= 10000
  const needCeo = amountNum >= 100000

  const canSubmit =
    !!applicantUserId &&
    persona === 'employee' &&
    vendor.trim().length > 0 &&
    !!category &&
    amountNum > 0 && amountNum <= 10_000_000 &&
    items.trim().length > 0 &&
    justification.trim().length > 0 &&
    (!quoteRequired || !!quoteFileName) &&
    !submitting

  if (persona !== 'employee') {
    return (
      <SectionCard>
        <SectionTitle>Purchase Request — Spec / 採購申請</SectionTitle>
        <div className="p-5">
          <InfoBanner>
            Persona <strong>{persona}</strong> cannot submit a purchase request. Switch to <strong>Employee</strong> to apply.
          </InfoBanner>
        </div>
      </SectionCard>
    )
  }

  async function onSubmit() {
    setSubmitting(true); setError(null)
    try {
      const dto = await purchaseApi.submit({
        tenantCode,
        applicantUserId: applicantUserId!,
        vendor: vendor.trim(),
        category,
        amount: amountNum,
        items: items.trim(),
        justification: justification.trim(),
        quoteFileName,
      })
      window.location.hash = `#purchase/${dto.id}`
      setScreen({ kind: 'form', code: 'PURCHASE', caseId: dto.id })
    } catch (e) {
      const err = e as ApiError
      const fieldErrs = err.errors
        ? Object.values(err.errors).flat().join('\n')
        : null
      setError(fieldErrs ?? err.detail ?? err.title ?? `Submit failed (${err.status ?? '?'})`)
    } finally {
      setSubmitting(false)
    }
  }

  return (
    <div className="space-y-4">
      <SectionCard>
        <SectionTitle>採購申請 / Purchase Request — Spec-driven</SectionTitle>
        <div className="space-y-1 px-5 pt-4 text-sm text-ink-muted">
          <div>Tenant: <code className="font-mono text-ink">{tenantCode}</code> · Applicant: <code className="font-mono text-ink">{applicantUserId}</code></div>
          <div className="text-[11px] text-ink-faint">Fields below are generated from <code className="font-mono">spec.userTasks[task_request].fields</code>; see <code className="font-mono">sample_specs/purchase_v1.json</code>.</div>
        </div>

        <div className="grid grid-cols-2 gap-4 p-5">
          <Field label="供應商 / Vendor" required>
            <Input name="vendor" value={vendor} onChange={e => setVendor(e.target.value)} placeholder="e.g. 全聯辦公用品" />
          </Field>

          <Field label="採購類別 / Category" required>
            <Select value={category} onChange={e => setCategory(e.target.value)}>
              {CATEGORY_OPTIONS.map(o => <option key={o.value} value={o.value}>{o.label}</option>)}
            </Select>
          </Field>

          <Field label="金額 (TWD) / Amount" required hint="未稅金額，整數，0 < value ≤ 10,000,000">
            <Input
              name="amount"
              type="number"
              min={1}
              max={10_000_000}
              step={1}
              value={amount}
              onChange={e => setAmount(e.target.value)}
              placeholder="e.g. 5000"
            />
          </Field>

          <Field label="路由預覽 / Routing preview (derived)">
            <div className="flex h-8 flex-wrap items-center gap-1.5 rounded-md border border-rule bg-slate-50 px-3 text-xs">
              <Pill ok>Manager</Pill>
              {needFinance && <Pill ok>Finance (≥1萬)</Pill>}
              {needCeo && <Pill warn>CEO (≥10萬)</Pill>}
              <Pill ok>Purchase exec</Pill>
            </div>
          </Field>
        </div>

        <div className="border-t border-rule px-5 py-4">
          <Field label="品項明細 / Items" required hint="一行一品項，含數量單價">
            <Textarea name="items" rows={3} value={items} onChange={e => setItems(e.target.value)} placeholder="A4 影印紙 x 50 包&#10;原子筆 x 100 支" />
          </Field>
        </div>

        <div className="border-t border-rule px-5 py-4">
          <Field label="採購理由 / Justification" required>
            <Textarea name="justification" rows={3} value={justification} onChange={e => setJustification(e.target.value)} placeholder="e.g. Q2 季度耗材補充" />
          </Field>
        </div>

        {quoteRequired && (
          <div className="border-t border-rule px-5 py-4">
            <Field label="報價單 / Quote file" required hint="1 萬以上必附正式報價單 (spec.userTasks[task_request].fields[quote_file].conditional)">
              <label className="flex h-8 cursor-pointer items-center gap-2 rounded-md border border-dashed border-rule bg-white px-3 text-sm text-ink-muted hover:bg-slate-50">
                <Paperclip className="h-3.5 w-3.5" />
                {quoteFileName ?? 'Click to attach a file'}
                <input
                  type="file"
                  className="hidden"
                  onChange={e => setQuoteFileName(e.target.files?.[0]?.name ?? null)}
                />
              </label>
            </Field>
          </div>
        )}
      </SectionCard>

      {error && (
        <SectionCard>
          <div className="border-l-4 border-red-300 bg-red-50 p-4 text-sm text-red-800 whitespace-pre-line">
            <div className="font-semibold">Submit failed</div>
            <div>{error}</div>
          </div>
        </SectionCard>
      )}

      <div className="flex items-center justify-between gap-3">
        <Button variant="outline" size="md" onClick={() => setScreen({ kind: 'home' })}>Cancel</Button>
        <Button variant="primary" size="md" disabled={!canSubmit} onClick={onSubmit}>
          {submitting ? 'Submitting…' : 'Submit purchase / 提交採購申請'}
        </Button>
      </div>
    </div>
  )
}

function Pill({ children, ok, warn }: { children: React.ReactNode; ok?: boolean; warn?: boolean }) {
  const cls = warn
    ? 'bg-amber-100 text-amber-800'
    : ok
      ? 'bg-blue-100 text-blue-800'
      : 'bg-slate-200 text-slate-700'
  return <span className={`inline-flex items-center rounded px-1.5 py-0.5 text-[10px] font-semibold uppercase tracking-wide ${cls}`}>{children}</span>
}
