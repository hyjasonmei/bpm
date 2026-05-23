import { apiFetch, setJwt, clearJwt } from '@/lib/apiFetch'

export interface AuthedUserDto {
  id: string
  fullName: string
  email: string
  roles: string[]
  departmentCode: string | null
}

export interface LoginResponse {
  token: string
  expiresAt: string
  user: AuthedUserDto
}

export async function login(email: string, password: string): Promise<LoginResponse> {
  const res = await apiFetch('/api/auth/login', {
    method: 'POST',
    headers: { 'Content-Type': 'application/json' },
    body: JSON.stringify({ email, password }),
  })
  if (!res.ok) {
    const body = await res.json().catch(() => ({} as { error?: string }))
    throw new Error(body.error === 'invalid_credentials'
      ? 'Email 或密碼錯誤'
      : body.error ?? `login failed (${res.status})`)
  }
  const data = await res.json() as LoginResponse
  setJwt(data.token)
  return data
}

export async function logout(): Promise<void> {
  try { await apiFetch('/api/auth/logout', { method: 'POST' }) } catch { /* swallow */ }
  clearJwt()
}
