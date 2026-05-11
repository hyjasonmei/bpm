import { useEffect, useState } from 'react'
import { SectionCard, SectionTitle } from '@/components/ui/card'
import { Input, Textarea, Select, Field } from '@/components/ui/form'
import { FormShell, ActionBar } from './FormShell'
import type { PersonaCode } from '@/lib/role'
import { FlowToast, useFormRuntime, type FormRuntimeProps } from '@/hooks/useFormRuntime'

const TRANSPORT_OPTIONS = [
  { value: 'hsr', label: '高鐵' },
  { value: 'flight', label: '飛機' },
  { value: 'self_drive', label: '自駕' },
  { value: 'other', label: '其他' },
] as const

const PICKUP_OPTIONS = [
  { value: 'yes', label: '是' },
  { value: 'no', label: '否' },
] as const

const BOOKING_OPTIONS = [
  { value: 'booked', label: '已訂位' },
  { value: 'self_arrange', label: '自行安排' },
  { value: 'n_a', label: '不需協助' },
] as const

interface TRQViewProps extends FormRuntimeProps {
  persona: PersonaCode
}

/**
 * Travel request. employee → manager → admin (notify) → close. Promoted from
 * the static demo view to a dual-mode form. Admin task is a UserTask (booking
 * coordination, not a decision).
 */
export function TRQView({ persona, ...rt }: TRQViewProps) {
  const runtime = useFormRuntime('TRQ', rt)
  const isTaskMode = runtime.mode === 'task'

  const [destination, setDestination] = useState('Dubai, UAE')
  const [purpose, setPurpose] = useState('DMCC 2026 RSM auditing')
  const [travelStart, setTravelStart] = useState('')
  const [travelEnd, setTravelEnd] = useState('')
  const [transport, setTransport] = useState<string>('flight')
  const [estimatedAmount, setEstimatedAmount] = useState('')
  const [needsPickup, setNeedsPickup] = useState<'yes' | 'no'>('no')
  const [pickupAddress, setPickupAddress] = useState('')

  // Admin notify task fields
  const [bookingStatus, setBookingStatus] = useState<string>('booked')
  const [adminNote, setAdminNote] = useState('')

  useEffect(() => {
    if (!isTaskMode || !runtime.task) return
    const fd = (runtime.task.mergedFormData ?? {}) as Record<string, unknown>
    if (typeof fd.destination === 'string') setDestination(fd.destination)
    if (typeof fd.purpose === 'string') setPurpose(fd.purpose)
    const dr = fd.travel_dates as { start?: string; end?: string } | undefined
    if (dr?.start) setTravelStart(dr.start)
    if (dr?.end) setTravelEnd(dr.end)
    if (typeof fd.transport === 'string') setTransport(fd.transport)
    if (fd.estimated_amount !== undefined) setEstimatedAmount(String(fd.estimated_amount))
    if (fd.needs_pickup === 'yes' || fd.needs_pickup === 'no') setNeedsPickup(fd.needs_pickup)
    if (typeof fd.pickup_address === 'string') setPickupAddress(fd.pickup_address)
    if (typeof fd.booking_status === 'string') setBookingStatus(fd.booking_status)
    if (typeof fd.admin_note === 'string') setAdminNote(fd.admin_note)
  }, [isTaskMode, runtime.task])

  const ro = isTaskMode
  const nodeId = runtime.task?.task.nodeId

  function toApplyPayload() {
    return {
      destination,
      purpose,
      travel_dates: { start: travelStart, end: travelEnd },
      transport,
      estimated_amount: estimatedAmount ? Number(estimatedAmount) : undefined,
      needs_pickup: needsPickup,
      pickup_address: needsPickup === 'yes' ? pickupAddress : undefined,
    }
  }

  function patchForAdmin() {
    return { booking_status: bookingStatus, admin_note: adminNote || undefined }
  }

  return (
    <FormShell code="TRQ" activeStep={0} setActiveStep={() => {}} persona={persona} copySelector={false} mode={runtime.mode}>
      <FlowToast message={runtime.toast} />

      <SectionCard>
        <SectionTitle>Itinerary</SectionTitle>
        <div className="grid grid-cols-2 gap-x-8 gap-y-3 p-4">
          <Field label="Destination" required>
            <Input value={destination} readOnly={ro} onChange={e => setDestination(e.target.value)} />
          </Field>
          <Field label="Transport" required>
            <Select value={transport} disabled={ro} onChange={e => setTransport(e.target.value)}>
              {TRANSPORT_OPTIONS.map(o => <option key={o.value} value={o.value}>{o.label}</option>)}
            </Select>
          </Field>
          <Field label="Travel start" required>
            <Input type="date" value={travelStart} readOnly={ro} onChange={e => setTravelStart(e.target.value)} />
          </Field>
          <Field label="Travel end" required>
            <Input type="date" value={travelEnd} readOnly={ro} onChange={e => setTravelEnd(e.target.value)} />
          </Field>
          <div className="col-span-2">
            <Field label="Purpose" required>
              <Textarea rows={2} value={purpose} readOnly={ro} onChange={e => setPurpose(e.target.value)} />
            </Field>
          </div>
          <Field label="Estimated Amount">
            <Input type="number" min={0} value={estimatedAmount} readOnly={ro} onChange={e => setEstimatedAmount(e.target.value)} />
          </Field>
          <Field label="Pickup Required">
            <Select value={needsPickup} disabled={ro} onChange={e => setNeedsPickup(e.target.value as 'yes' | 'no')}>
              {PICKUP_OPTIONS.map(o => <option key={o.value} value={o.value}>{o.label}</option>)}
            </Select>
          </Field>
          {needsPickup === 'yes' && (
            <div className="col-span-2">
              <Field label="Pickup Address">
                <Input value={pickupAddress} readOnly={ro} onChange={e => setPickupAddress(e.target.value)} />
              </Field>
            </div>
          )}
        </div>
      </SectionCard>

      {nodeId === 'task_admin_notify' && (
        <SectionCard>
          <SectionTitle>Admin — Booking Coordination</SectionTitle>
          <div className="grid grid-cols-2 gap-x-8 gap-y-3 p-4">
            <Field label="Booking Status" required>
              <Select value={bookingStatus} onChange={e => setBookingStatus(e.target.value)}>
                {BOOKING_OPTIONS.map(o => <option key={o.value} value={o.value}>{o.label}</option>)}
              </Select>
            </Field>
            <div className="col-span-2">
              <Field label="Admin Note">
                <Textarea rows={2} value={adminNote} onChange={e => setAdminNote(e.target.value)} />
              </Field>
            </div>
          </div>
        </SectionCard>
      )}

      <ActionBar
        code="TRQ"
        activeStep={0}
        persona={persona}
        mode={runtime.mode}
        nodeKind={runtime.task?.task.nodeKind}
        pending={runtime.pending}
        onSubmit={() => {
          if (nodeId === 'task_admin_notify') { void runtime.submitUserTask(patchForAdmin()); return }
          if (isTaskMode) { void runtime.submitUserTask(); return }
          if (!destination.trim() || !purpose.trim() || !travelStart || !travelEnd) {
            runtime.fireToast('Destination, purpose and travel dates are required.')
            return
          }
          void runtime.submitCreate(toApplyPayload())
        }}
        onApprove={c => { void runtime.approve(c) }}
        onReject={c => { void runtime.reject(c) }}
        onReturnTask={c => { void runtime.returnTask(c) }}
      />
    </FormShell>
  )
}
