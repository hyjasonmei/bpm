import { useMemo, useState } from 'react'
import { Button } from '@/components/ui/button'
import { SectionCard, SectionTitle } from '@/components/ui/card'
import { Field, InfoBanner, Input, Select, Textarea } from '@/components/ui/form'
import type { PersonaCode } from '@/lib/role'
import { travelApi, personaToSpecUserId, type ApiError } from '@/lib/travelApi'
import type { Screen } from '@/components/AppLayout'

const DEST_TYPES = [
  { value: 'domestic',      label: '國內 / Domestic' },
  { value: 'international', label: '國外 / International' },
] as const

interface Props {
  persona: PersonaCode
  setScreen: (s: Screen) => void
  tenantCode?: string
}

export function TravelForm({ persona, setScreen, tenantCode = 'acme' }: Props) {
  const applicantUserId = personaToSpecUserId(persona)

  const [destinationType, setDestinationType] = useState<string>('domestic')
  const [destination, setDestination] = useState('')
  const [departDate, setDepartDate] = useState('')
  const [returnDate, setReturnDate] = useState('')
  const [purpose, setPurpose] = useState('')
  const [estimatedCost, setEstimatedCost] = useState<string>('')
  const [submitting, setSubmitting] = useState(false)
  const [error, setError] = useState<string | null>(null)

  const cost = useMemo(() => Number.parseFloat(estimatedCost) || 0, [estimatedCost])
  const needVp = destinationType === 'international'

  const canSubmit =
    !!applicantUserId &&
    persona === 'employee' &&
    destination.trim().length > 0 &&
    !!departDate && !!returnDate && new Date(returnDate) >= new Date(departDate) &&
    purpose.trim().length > 0 &&
    cost > 0 && cost <= 1_000_000 &&
    !submitting

  if (persona !== 'employee') {
    return (
      <SectionCard>
        <SectionTitle>Travel Request — Spec / 差旅申請</SectionTitle>
        <div className="p-5">
          <InfoBanner>
            Persona <strong>{persona}</strong> cannot submit travel requests. Switch to <strong>Employee</strong>.
          </InfoBanner>
        </div>
      </SectionCard>
    )
  }

  async function onSubmit() {
    setSubmitting(true); setError(null)
    try {
      const dto = await travelApi.submit({
        tenantCode,
        applicantUserId: applicantUserId!,
        destinationType,
        destination: destination.trim(),
        departDate,
        returnDate,
        purpose: purpose.trim(),
        estimatedCost: cost,
      })
      window.location.hash = `#travel/${dto.id}`
      setScreen({ kind: 'form', code: 'TRAVEL', caseId: dto.id })
    } catch (e) {
      const err = e as ApiError
      const fieldErrs = err.errors ? Object.values(err.errors).flat().join('\n') : null
      setError(fieldErrs ?? err.detail ?? err.title ?? `Submit failed (${err.status ?? '?'})`)
    } finally {
      setSubmitting(false)
    }
  }

  return (
    <div className="space-y-4">
      <SectionCard>
        <SectionTitle>差旅申請 / Travel Request — Spec-driven</SectionTitle>
        <div className="space-y-1 px-5 pt-4 text-sm text-ink-muted">
          <div>Tenant: <code className="font-mono text-ink">{tenantCode}</code> · Applicant: <code className="font-mono text-ink">{applicantUserId}</code></div>
          <div className="text-[11px] text-ink-faint">From <code className="font-mono">spec.userTasks[task_request].fields</code> in <code className="font-mono">sample_specs/travel_v1.json</code>.</div>
        </div>

        <div className="grid grid-cols-2 gap-4 p-5">
          <Field label="出差類型 / Destination type" required>
            <Select value={destinationType} onChange={e => setDestinationType(e.target.value)}>
              {DEST_TYPES.map(t => <option key={t.value} value={t.value}>{t.label}</option>)}
            </Select>
          </Field>

          <Field label="路由預覽 / Routing preview (derived)">
            <div className="flex h-8 flex-wrap items-center gap-1.5 rounded-md border border-rule bg-slate-50 px-3 text-xs">
              <Pill ok>Manager</Pill>
              {needVp && <Pill warn>VP (international)</Pill>}
              <Pill ok>Admin book</Pill>
            </div>
          </Field>

          <Field label="目的地 / Destination" required hint="城市/國家">
            <Input name="destination" value={destination} onChange={e => setDestination(e.target.value)} placeholder="e.g. 東京 / Japan" />
          </Field>

          <Field label="預估費用 (TWD) / Estimated cost" required hint="0 < value ≤ 1,000,000">
            <Input
              name="estimated_cost"
              type="number"
              min={1}
              max={1_000_000}
              step={1}
              value={estimatedCost}
              onChange={e => setEstimatedCost(e.target.value)}
              placeholder="e.g. 8000"
            />
          </Field>

          <Field label="出發日 / Depart date" required>
            <Input name="depart_date" type="date" value={departDate} onChange={e => setDepartDate(e.target.value)} />
          </Field>

          <Field label="返回日 / Return date" required>
            <Input name="return_date" type="date" value={returnDate} onChange={e => setReturnDate(e.target.value)} />
          </Field>
        </div>

        <div className="border-t border-rule px-5 py-4">
          <Field label="出差目的 / Purpose" required>
            <Textarea name="purpose" rows={3} value={purpose} onChange={e => setPurpose(e.target.value)} placeholder="e.g. 客戶現場部署" />
          </Field>
        </div>
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
          {submitting ? 'Submitting…' : 'Submit travel / 提交差旅申請'}
        </Button>
      </div>
    </div>
  )
}

function Pill({ children, ok, warn }: { children: React.ReactNode; ok?: boolean; warn?: boolean }) {
  const cls = warn ? 'bg-amber-100 text-amber-800' : ok ? 'bg-blue-100 text-blue-800' : 'bg-slate-200 text-slate-700'
  return <span className={`inline-flex items-center rounded px-1.5 py-0.5 text-[10px] font-semibold uppercase tracking-wide ${cls}`}>{children}</span>
}
