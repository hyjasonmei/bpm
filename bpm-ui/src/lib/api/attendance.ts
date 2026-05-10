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
