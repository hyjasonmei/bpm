import { useEffect, useState } from 'react'

import { Button } from '@/components/ui/button'
import { SectionCard, SectionTitle } from '@/components/ui/card'
import { Field, InfoBanner, Input, Textarea } from '@/components/ui/form'
import type { PersonaCode } from '@/lib/role'
import {
  travelApi, personaToActingUserId, specUserIdToLabel,
  TRAVEL_STATE_LABEL, type TravelCaseDto, type TravelState, type ApiError,
} from '@/lib/travelApi'
import type { Screen } from '@/components/AppLayout'

interface Props {
  persona: PersonaCode
  caseId: string
  setScreen: (s: Screen) => void
}

export function TravelView({ persona, caseId, setScreen }: Props) {
  const [data, setData] = useState<TravelCaseDto | null>(null)
  const [error, setError] = useState<string | null>(null)
  const [busy, setBusy] = useState(false)

  const [rejectOpen, setRejectOpen] = useState(false)
  const [rejectReason, setRejectReason] = useState('')

  const [ticketRef, setTicketRef] = useState('')
  const [hotelRef, setHotelRef] = useState('')
  const [bookNote, setBookNote] = useState('')

  useEffect(() => {
    let cancel = false
    setError(null)
    travelApi.get(caseId)
      .then(d => { if (!cancel) setData(d) })
      .catch((e: ApiError) => { if (!cancel) setError(e.detail ?? e.title ?? `Load failed (${e.status})`) })
    return () => { cancel = true }
  }, [caseId])

  if (error) {
    return (
      <SectionCard>
        <SectionTitle>Travel case — load error</SectionTitle>
        <div className="p-5">
          <div className="border-l-4 border-red-300 bg-red-50 p-4 text-sm text-red-800">{error}</div>
          <Button variant="outline" size="sm" className="mt-3" onClick={() => setScreen({ kind: 'home' })}>Back</Button>
        </div>
      </SectionCard>
    )
  }

  if (!data) {
    return (
      <SectionCard>
        <SectionTitle>Loading…</SectionTitle>
        <div className="p-5 text-sm text-ink-muted">caseId: <code className="font-mono">{caseId}</code></div>
      </SectionCard>
    )
  }

  const stateLabel = TRAVEL_STATE_LABEL[data.state]
  const actingUserId = personaToActingUserId(persona, data.state)
  const isCurrentApprover = !!data.currentApproverUserId && actingUserId === data.currentApproverUserId
  const canBook = data.state === 3 && persona === 'admin' && actingUserId === 'u_admin_lead'

  async function refresh() {
    const d = await travelApi.get(caseId)
    setData(d)
  }

  async function onApprove() {
    if (!actingUserId) return
    setBusy(true); setError(null)
    try {
      await travelApi.approve(caseId, actingUserId)
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
      await travelApi.reject(caseId, actingUserId, rejectReason.trim())
      setRejectOpen(false); setRejectReason('')
      await refresh()
    } catch (e) {
      const err = e as ApiError
      setError(err.detail ?? err.title ?? `Reject failed (${err.status})`)
    } finally { setBusy(false) }
  }

  async function onBook() {
    if (!actingUserId || !ticketRef) return
    setBusy(true); setError(null)
    try {
      await travelApi.book(caseId, {
        adminUserId: actingUserId,
        ticketRef: ticketRef.trim(),
        hotelRef: hotelRef.trim() || null,
        bookNote: bookNote.trim() || null,
      })
      await refresh()
    } catch (e) {
      const err = e as ApiError
      setError(err.detail ?? err.title ?? `Book failed (${err.status})`)
    } finally { setBusy(false) }
  }

  return (
    <div className="space-y-4">
      <SectionCard>
        <SectionTitle right={<span className="text-xs font-normal text-ink-muted">Case <code className="font-mono">{data.id.slice(0, 8)}</code></span>}>
          差旅申請 / Travel — {stateLabel.zh} ({stateLabel.en})
        </SectionTitle>
        <div className="grid grid-cols-2 gap-x-8 gap-y-3 p-5 text-sm">
          <DataRow label="申請人 / Applicant" value={specUserIdToLabel(data.applicantUserId)} />
          <DataRow label="類型 / Type" value={data.destinationType} />
          <DataRow label="目的地 / Destination" value={data.destination} />
          <DataRow label="預估費用 / Cost" value={`${data.estimatedCost.toLocaleString()} TWD`} />
          <DataRow label="出發 / Depart" value={data.departDate} />
          <DataRow label="返回 / Return" value={data.returnDate} />
          <DataRow label="目前簽核 / Current approver" value={specUserIdToLabel(data.currentApproverUserId)} />
          <DataRow label="建立 / Created" value={new Date(data.createdAt).toLocaleString()} />
        </div>
        <div className="border-t border-rule p-5 text-sm">
          <div className="text-xs font-semibold uppercase tracking-wider text-ink-muted">出差目的 / Purpose</div>
          <p className="mt-1 text-ink whitespace-pre-wrap">{data.purpose}</p>
        </div>
      </SectionCard>

      <SectionCard>
        <SectionTitle>簽核紀錄 / Audit</SectionTitle>
        <div className="grid grid-cols-2 gap-x-8 gap-y-2 p-5 text-sm">
          <DataRow label="主管 / Manager" value={data.managerApproverUserId ? `${specUserIdToLabel(data.managerApproverUserId)} · ${formatTs(data.managerApprovedAt)}` : '—'} />
          <DataRow label="副總 / VP" value={data.vpApproverUserId ? `${specUserIdToLabel(data.vpApproverUserId)} · ${formatTs(data.vpApprovedAt)}` : '—'} />
          <DataRow label="行政 / Admin" value={data.adminBookerUserId ? `${specUserIdToLabel(data.adminBookerUserId)} · ${formatTs(data.adminBookedAt)}` : '—'} />
          {data.ticketRef && <DataRow label="票號 / Ticket" value={data.ticketRef} />}
          {data.hotelRef && <DataRow label="住宿 / Hotel" value={data.hotelRef} />}
          {data.rejectedByUserId && <DataRow label="退回 / Rejected" value={`${specUserIdToLabel(data.rejectedByUserId)}: ${data.rejectionReason ?? ''}`} />}
        </div>
      </SectionCard>

      {data.state >= 1 && data.state <= 2 && (
        <SectionCard>
          <SectionTitle>動作 / Actions</SectionTitle>
          <div className="space-y-3 p-5 text-sm">
            {!isCurrentApprover ? (
              <InfoBanner>
                Persona <strong>{persona}</strong> (acting as <code className="font-mono">{actingUserId ?? '—'}</code>) cannot act. Expected approver: <code className="font-mono">{data.currentApproverUserId}</code>.
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

      {data.state === 3 && (
        <SectionCard>
          <SectionTitle>行政訂票 / Admin booking (task_admin_book)</SectionTitle>
          <div className="space-y-3 p-5 text-sm">
            {!canBook ? (
              <InfoBanner>
                Only persona with <code className="font-mono">role:Admin</code> may book. Switch to <strong>Admin</strong> (acts as <code className="font-mono">u_admin_lead</code>).
              </InfoBanner>
            ) : (
              <div className="space-y-3">
                <div className="grid grid-cols-2 gap-3">
                  <Field label="票號 / Ticket ref" required hint="訂位代號或票號">
                    <Input name="ticket_ref" value={ticketRef} onChange={e => setTicketRef(e.target.value)} placeholder="EVA-2026-0001" />
                  </Field>
                  <Field label="住宿確認 / Hotel" hint="optional">
                    <Input name="hotel_ref" value={hotelRef} onChange={e => setHotelRef(e.target.value)} />
                  </Field>
                </div>
                <Field label="訂票備註 / Note">
                  <Textarea name="book_note" rows={2} value={bookNote} onChange={e => setBookNote(e.target.value)} />
                </Field>
                <Button variant="primary" size="md" disabled={busy || !ticketRef.trim()} onClick={onBook}>
                  {busy ? 'Booking…' : 'Confirm booking / 完成訂票'}
                </Button>
                <p className="text-xs text-ink-muted">Acting as <code className="font-mono">{actingUserId}</code></p>
              </div>
            )}
          </div>
        </SectionCard>
      )}

      {data.state === 4 && (
        <SectionCard>
          <div className="border-l-4 border-emerald-300 bg-emerald-50 p-4 text-sm text-emerald-800">
            ✓ Travel booked. 票號 <code className="font-mono">{data.ticketRef}</code>
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

function formatTs(ts: string | null) { return ts ? new Date(ts).toLocaleString() : '—' }
export type _ref = TravelState
