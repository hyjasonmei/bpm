import { apiFetch } from '@/lib/apiFetch'

export interface AdminUserSummary {
  id: string
  fullName: string
  email: string
  departmentCode: string | null
  isActive: boolean
  roleCount: number
}

export interface PagedAdminUsers {
  items: AdminUserSummary[]
  page: number
  pageSize: number
  total: number
}

export async function listAdminUsers(opts: { q?: string; page?: number; pageSize?: number; roleCode?: string } = {}): Promise<PagedAdminUsers> {
  const params = new URLSearchParams()
  if (opts.q) params.set('q', opts.q)
  if (opts.page) params.set('page', String(opts.page))
  if (opts.pageSize) params.set('pageSize', String(opts.pageSize))
  if (opts.roleCode) params.set('roleCode', opts.roleCode)
  const qs = params.toString()
  const res = await apiFetch(`/api/admin/users${qs ? `?${qs}` : ''}`)
  if (!res.ok) {
    const text = await res.text().catch(() => '')
    throw new Error(`/api/admin/users ${res.status}: ${text}`)
  }
  return await res.json() as PagedAdminUsers
}
