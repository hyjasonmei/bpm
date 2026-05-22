import { apiFetch } from '@/lib/apiFetch'
import type {
  AssignmentDto, AssignmentScopeValue, PagedResult, RoleSummaryDto,
  UserDetailDto, UserSummaryDto,
} from '@/types/adminRoles'

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

export async function listRoles(): Promise<RoleSummaryDto[]> {
  const res = await apiFetch('/api/admin/roles')
  return jsonOrThrow<RoleSummaryDto[]>(res)
}

export async function listUsers(opts: { q?: string; page?: number; pageSize?: number; roleCode?: string } = {}): Promise<PagedResult<UserSummaryDto>> {
  const params = new URLSearchParams()
  if (opts.q) params.set('q', opts.q)
  if (opts.page) params.set('page', String(opts.page))
  if (opts.pageSize) params.set('pageSize', String(opts.pageSize))
  if (opts.roleCode) params.set('roleCode', opts.roleCode)
  const res = await apiFetch(`/api/admin/users?${params}`)
  return jsonOrThrow<PagedResult<UserSummaryDto>>(res)
}

export async function getUserDetail(userId: string): Promise<UserDetailDto> {
  const res = await apiFetch(`/api/admin/users/${userId}`)
  return jsonOrThrow<UserDetailDto>(res)
}

export async function assignRole(userId: string, roleCode: string, scope?: AssignmentScopeValue, scopeRef?: string): Promise<AssignmentDto> {
  const res = await apiFetch(`/api/admin/users/${userId}/roles`, {
    method: 'POST',
    headers: { 'Content-Type': 'application/json' },
    body: JSON.stringify({ roleCode, scope: scope ?? null, scopeRef: scopeRef ?? null }),
  })
  return jsonOrThrow<AssignmentDto>(res)
}

export async function revokeAssignment(userId: string, assignmentId: string): Promise<void> {
  const res = await apiFetch(`/api/admin/users/${userId}/roles/${assignmentId}`, { method: 'DELETE' })
  if (!res.ok && res.status !== 204) {
    let detail = `${res.status} ${res.statusText}`
    try { const body = await res.json(); if (body?.detail) detail = body.detail; else if (body?.title) detail = body.title } catch { /* */ }
    throw new Error(detail)
  }
}
