import { apiFetch } from '@/lib/apiFetch'
import type { DailySummaryDto, PunchDto, TodayStatusDto } from '@/types/attendance'

async function jsonOrThrow<T>(res: Response): Promise<T> {
  if (!res.ok) {
    let detail = `${res.status} ${res.statusText}`
    try {
      const body = await res.json()
      if (body?.detail) detail = body.detail
      else if (body?.title) detail = body.title
    } catch { /* ignore */ }
    throw new Error(detail)
  }
  return await res.json() as T
}

export async function checkIn(): Promise<PunchDto> {
  const res = await apiFetch('/api/attendance/checkin', { method: 'POST' })
  return jsonOrThrow<PunchDto>(res)
}

export async function checkOut(): Promise<PunchDto> {
  const res = await apiFetch('/api/attendance/checkout', { method: 'POST' })
  return jsonOrThrow<PunchDto>(res)
}

export async function getToday(): Promise<TodayStatusDto> {
  const res = await apiFetch('/api/attendance/today')
  return jsonOrThrow<TodayStatusDto>(res)
}

export async function getHistory(days = 30): Promise<DailySummaryDto[]> {
  const res = await apiFetch(`/api/attendance/history?days=${days}`)
  return jsonOrThrow<DailySummaryDto[]>(res)
}

// ── corrections (補打卡) ──────────────────────────────────────────

export async function submitCorrection(req: import('@/types/attendance').SubmitCorrectionRequest) {
  const res = await apiFetch('/api/attendance/corrections', {
    method: 'POST',
    headers: { 'Content-Type': 'application/json' },
    body: JSON.stringify(req),
  })
  return jsonOrThrow<import('@/types/attendance').CorrectionDto>(res)
}

export async function getMyCorrections() {
  const res = await apiFetch('/api/attendance/corrections/mine')
  return jsonOrThrow<import('@/types/attendance').CorrectionDto[]>(res)
}

export async function getCorrection(id: string) {
  const res = await apiFetch(`/api/attendance/corrections/${id}`)
  return jsonOrThrow<import('@/types/attendance').CorrectionDto>(res)
}

export async function decideCorrection(id: string, approve: boolean, note: string | null) {
  const res = await apiFetch(`/api/attendance/corrections/${id}/decision`, {
    method: 'POST',
    headers: { 'Content-Type': 'application/json' },
    body: JSON.stringify({ approve, note }),
  })
  return jsonOrThrow<import('@/types/attendance').CorrectionDto>(res)
}
