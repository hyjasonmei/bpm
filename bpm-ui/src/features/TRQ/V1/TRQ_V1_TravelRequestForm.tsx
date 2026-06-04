import { useEffect, useState } from 'react'
import { CalendarIcon } from 'lucide-react'
import { useNavigate, useSearchParams } from 'react-router-dom'

import { SectionCard, SectionTitle } from '@/components/ui/card'
import { Field, InfoBanner, Input, Select, Textarea } from '@/components/ui/form'
import { ConfirmDialog } from '@/components/ui/ConfirmDialog'
import { ActionFooter } from '@/components/ui/action-footer/ActionFooter'
import { FormShell } from '@/screens/forms/FormShell'
import type { FormComponentProps } from '@/features/registry'
import type { PersonaCode } from '@/lib/role'
import { apiFetch } from '@/lib/apiFetch'
import { emptyPayload, PICKUP_OPTIONS, SEAT_OPTIONS, TRAVEL_TYPE_OPTIONS } from './TRQ_V1_shared'
import type { TRQ_V1_CaseResponse, TRQ_V1_SubmitPayload } from './TRQ_V1_types'

/**
 * TRQ V1 — submitter form (userTask `req`). Posts to
 * <c>POST /api/trq/v1</c> on first submit; when entered with
 * <c>?resubmit=&lt;caseId&gt;</c> on the URL, pre-fills with the
 * existing case data and POSTs to <c>/{caseId}/resubmit</c>.
 *
 * Approval / Resubmit decision UI lives on
 * <see cref="TRQ_V1_CaseDetail" />.
 */
export function TRQ_V1_TravelRequestForm({ persona, mode = 'create', onSubmitted }: FormComponentProps) {
  const navigate = useNavigate()
  const [params] = useSearchParams()
  const resubmitCaseId = params.get('resubmit')
  const isResubmit = !!resubmitCaseId

  const [form, setForm] = useState<TRQ_V1_SubmitPayload>(emptyPayload())
  const [loading, setLoading] = useState(isResubmit)
  const [pending, setPending] = useState(false)
  const [error, setError] = useState<string | null>(null)
  const [confirmOpen, setConfirmOpen] = useState(false)

  useEffect(() => {
    if (!isResubmit) return
    let cancelled = false
    void (async () => {
      try {
        const res = await apiFetch(`/api/trq/v1/${resubmitCaseId}`)
        if (!res.ok) throw new Error(`HTTP ${res.status}`)
        const body = (await res.json()) as TRQ_V1_CaseResponse
        if (cancelled) return
        setForm({
          travelType: body.travelType,
          departureCity: body.departureCity,
          destinationCity: body.destinationCity,
          departDate: body.departDate?.slice(0, 10) ?? '',
          returnDate: body.returnDate?.slice(0, 10) ?? '',
          chargeTo: body.chargeTo,
          travelPurpose: body.travelPurpose,
          passportName: body.passportName ?? '',
          seatPreference: body.seatPreference ?? 'No preference',
          pickupRequired: body.pickupRequired ?? 'No',
        })
      } catch (e) {
        if (!cancelled) setError(e instanceof Error ? e.message : String(e))
      } finally {
        if (!cancelled) setLoading(false)
      }
    })()
    return () => { cancelled = true }
  }, [isResubmit, resubmitCaseId])

  function patch(p: Partial<TRQ_V1_SubmitPayload>) {
    setForm(prev => ({ ...prev, ...p }))
  }

  if (mode !== 'create') {
    return (
      <FormShell code="TRQ" activeStep={0} persona={persona as PersonaCode} mode="task">
        <SectionCard>
          <div className="px-5 py-8 text-center text-sm text-ink-muted">
            開啟此申請的詳細頁面以核准 / 退件。請從首頁的「Pending My Approval」進入。
          </div>
        </SectionCard>
      </FormShell>
    )
  }

  const valid =
    !!form.travelType &&
    !!form.departureCity.trim() &&
    !!form.destinationCity.trim() &&
    !!form.departDate &&
    !!form.chargeTo.trim() &&
    !!form.travelPurpose.trim()

  function attemptSubmit() {
    if (!valid) { setError('請填寫所有必填欄位（差旅類型 / 出發地 / 目的地 / 出發日 / 費用歸屬 / 出差目的）。'); return }
    setError(null)
    setConfirmOpen(true)
  }

  async function doSubmit() {
    setConfirmOpen(false)
    setPending(true)
    setError(null)
    const payload: TRQ_V1_SubmitPayload = {
      ...form,
      departureCity: form.departureCity.trim(),
      destinationCity: form.destinationCity.trim(),
      chargeTo: form.chargeTo.trim(),
      travelPurpose: form.travelPurpose.trim(),
      returnDate: form.returnDate || null,
      passportName: form.passportName?.trim() || null,
    }
    const url = isResubmit ? `/api/trq/v1/${resubmitCaseId}/resubmit` : '/api/trq/v1'
    try {
      const res = await apiFetch(url, {
        method: 'POST',
        headers: { 'Content-Type': 'application/json' },
        body: JSON.stringify(payload),
      })
      if (!res.ok) {
        const text = await res.text()
        throw new Error(text || `HTTP ${res.status}`)
      }
      const body = (await res.json()) as TRQ_V1_CaseResponse
      onSubmitted?.()
      navigate(`/cases/trq/${body.id}`)
    } catch (e) {
      setError(e instanceof Error ? e.message : String(e))
    } finally {
      setPending(false)
    }
  }

  return (
    <FormShell code="TRQ" activeStep={0} persona={persona as PersonaCode} mode="create">
      <SectionCard>
        <SectionTitle>行程 / Itinerary</SectionTitle>

        <div className="border-b border-rule px-5 py-3">
          <InfoBanner>
            請填寫差旅行程；出發地 / 目的地 / 出發日 / 費用歸屬 / 出差目的為必填。
            {isResubmit && (
              <span className="mt-1 block text-amber-900">
                此案件先前被退回，請依照簽核意見修正後重新送出（這將進入新的審核回合）。
              </span>
            )}
          </InfoBanner>
        </div>

        {loading ? (
          <div className="px-5 py-10 text-center text-sm text-ink-muted">載入中…</div>
        ) : (
          <div className="grid grid-cols-12 gap-3 px-5 py-4">
            <Field label="差旅類型 / Travel Type" required className="col-span-6">
              <Select value={form.travelType} onChange={e => patch({ travelType: e.target.value })} disabled={pending}>
                {TRAVEL_TYPE_OPTIONS.map(o => <option key={o} value={o}>{o}</option>)}
              </Select>
            </Field>
            <Field label="費用歸屬 / Charge To" required className="col-span-6">
              <Input value={form.chargeTo} onChange={e => patch({ chargeTo: e.target.value })} disabled={pending} placeholder="e.g. HQ-IT" />
            </Field>

            <Field label="出發地 / Departure City" required className="col-span-6">
              <Input value={form.departureCity} onChange={e => patch({ departureCity: e.target.value })} disabled={pending} placeholder="Taipei" />
            </Field>
            <Field label="目的地 / Destination City" required className="col-span-6">
              <Input value={form.destinationCity} onChange={e => patch({ destinationCity: e.target.value })} disabled={pending} placeholder="Tokyo" />
            </Field>

            <Field label="出發日 / Depart Date" required className="col-span-6">
              <div className="relative">
                <Input type="date" value={form.departDate} onChange={e => patch({ departDate: e.target.value })} disabled={pending} />
                <CalendarIcon className="pointer-events-none absolute right-2 top-2 h-4 w-4 text-ink-faint" />
              </div>
            </Field>
            <Field label="回程日 / Return Date" className="col-span-6">
              <div className="relative">
                <Input type="date" value={form.returnDate ?? ''} onChange={e => patch({ returnDate: e.target.value })} disabled={pending} />
                <CalendarIcon className="pointer-events-none absolute right-2 top-2 h-4 w-4 text-ink-faint" />
              </div>
            </Field>

            <Field label="出差目的 / Travel Purpose" required className="col-span-12">
              <Textarea
                rows={3}
                value={form.travelPurpose}
                onChange={e => patch({ travelPurpose: e.target.value })}
                disabled={pending}
                placeholder="e.g. Client kickoff for the Acme integration project."
              />
            </Field>
          </div>
        )}
      </SectionCard>

      <SectionCard>
        <SectionTitle>旅客資訊 / Traveller</SectionTitle>
        <div className="grid grid-cols-12 gap-3 px-5 py-4">
          <Field label="護照姓名 / Passport Name" className="col-span-6">
            <Input value={form.passportName ?? ''} onChange={e => patch({ passportName: e.target.value })} disabled={pending} />
          </Field>
          <Field label="座位偏好 / Seat Preference" className="col-span-3">
            <Select value={form.seatPreference ?? ''} onChange={e => patch({ seatPreference: e.target.value })} disabled={pending}>
              {SEAT_OPTIONS.map(o => <option key={o} value={o}>{o}</option>)}
            </Select>
          </Field>
          <Field label="需接送 / Pickup" className="col-span-3">
            <Select value={form.pickupRequired ?? ''} onChange={e => patch({ pickupRequired: e.target.value })} disabled={pending}>
              {PICKUP_OPTIONS.map(o => <option key={o} value={o}>{o}</option>)}
            </Select>
          </Field>
        </div>
      </SectionCard>

      <ActionFooter
        hint={error
          ? <span className="text-danger">{error}</span>
          : <span>{isResubmit ? '送出後將重新通知主管。' : '送出後將通知您的主管。'}</span>}
        actions={[
          { id: 'cancel', label: '取消', variant: 'ghost', disabled: pending, onClick: () => navigate('/') },
          { id: 'submit', label: isResubmit ? '重新送出' : '送出申請', variant: 'primary', pending, disabled: !valid, onClick: attemptSubmit },
        ]}
      />

      <ConfirmDialog
        open={confirmOpen}
        title={isResubmit ? 'Resubmit travel request?' : 'Submit travel request?'}
        titleZh={isResubmit ? '重新送出差旅申請？' : '送出差旅申請？'}
        description={`${form.departureCity} → ${form.destinationCity} will be sent to your manager for approval.`}
        tone="default"
        confirmText={isResubmit ? '確認重新送出' : '確認送出'}
        onCancel={() => setConfirmOpen(false)}
        onConfirm={doSubmit}
      />
    </FormShell>
  )
}
