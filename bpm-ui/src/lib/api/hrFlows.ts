import { apiFetch } from '@/lib/apiFetch'
import type {
  HrFlowInstanceDto,
  HrFlowSpecCode,
  HrFlowSummaryDto,
} from '@/types/hrFlows'

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

export async function startHrFlow(specCode: HrFlowSpecCode, formData: unknown): Promise<HrFlowInstanceDto> {
  const res = await apiFetch(`/api/hr-flows/${specCode}`, {
    method: 'POST',
    headers: { 'Content-Type': 'application/json' },
    body: JSON.stringify({ formData }),
  })
  return jsonOrThrow<HrFlowInstanceDto>(res)
}

export async function getHrFlow(id: string): Promise<HrFlowInstanceDto> {
  const res = await apiFetch(`/api/hr-flows/${id}`)
  return jsonOrThrow<HrFlowInstanceDto>(res)
}

export async function getMyHrFlows(): Promise<HrFlowSummaryDto[]> {
  const res = await apiFetch(`/api/hr-flows/mine`)
  return jsonOrThrow<HrFlowSummaryDto[]>(res)
}

export async function getMyHrFlowTodos(): Promise<HrFlowSummaryDto[]> {
  const res = await apiFetch(`/api/hr-flows/todo`)
  return jsonOrThrow<HrFlowSummaryDto[]>(res)
}

export async function approveHrFlow(id: string, comment?: string): Promise<HrFlowInstanceDto> {
  const res = await apiFetch(`/api/hr-flows/${id}/approve`, {
    method: 'POST',
    headers: { 'Content-Type': 'application/json' },
    body: JSON.stringify({ comment: comment ?? null }),
  })
  return jsonOrThrow<HrFlowInstanceDto>(res)
}

export async function returnHrFlow(id: string, comment: string): Promise<HrFlowInstanceDto> {
  const res = await apiFetch(`/api/hr-flows/${id}/return`, {
    method: 'POST',
    headers: { 'Content-Type': 'application/json' },
    body: JSON.stringify({ comment }),
  })
  return jsonOrThrow<HrFlowInstanceDto>(res)
}

export async function resubmitHrFlow(id: string, formData: unknown): Promise<HrFlowInstanceDto> {
  const res = await apiFetch(`/api/hr-flows/${id}/resubmit`, {
    method: 'POST',
    headers: { 'Content-Type': 'application/json' },
    body: JSON.stringify({ formData }),
  })
  return jsonOrThrow<HrFlowInstanceDto>(res)
}

export async function cancelHrFlow(id: string): Promise<HrFlowInstanceDto> {
  const res = await apiFetch(`/api/hr-flows/${id}/cancel`, { method: 'POST' })
  return jsonOrThrow<HrFlowInstanceDto>(res)
}
