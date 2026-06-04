import { apiFetch } from '@/lib/apiFetch'

export interface MyDelegation {
  id: string
  delegateUserId: string
  delegateName: string | null
  startAt: string
  endAt: string
  activeNow: boolean
}

export interface DelegationUser {
  userId: string
  name: string
  email: string | null
}

async function jsonOrThrow<T>(res: Response): Promise<T> {
  if (!res.ok) throw new Error((await res.text().catch(() => '')) || `HTTP ${res.status}`)
  return (await res.json()) as T
}

export async function getMyDelegation(): Promise<MyDelegation | null> {
  const res = await apiFetch('/api/delegation/mine')
  if (res.status === 204) return null
  if (!res.ok) throw new Error(`HTTP ${res.status}`)
  const text = await res.text()
  if (!text || text === 'null') return null
  return JSON.parse(text) as MyDelegation
}

export async function setMyDelegation(delegateUserId: string, startAt: string, endAt: string): Promise<void> {
  const res = await apiFetch('/api/delegation/mine', {
    method: 'PUT',
    headers: { 'Content-Type': 'application/json' },
    body: JSON.stringify({ delegateUserId, startAt, endAt }),
  })
  if (!res.ok) throw new Error((await res.text().catch(() => '')) || `HTTP ${res.status}`)
}

export async function clearMyDelegation(): Promise<void> {
  const res = await apiFetch('/api/delegation/mine', { method: 'DELETE' })
  if (!res.ok) throw new Error(`HTTP ${res.status}`)
}

export const getActingFor = () => apiFetch('/api/delegation/acting-for').then(r => jsonOrThrow<string[]>(r))
export const getDelegationUsers = () => apiFetch('/api/delegation/users').then(r => jsonOrThrow<DelegationUser[]>(r))
