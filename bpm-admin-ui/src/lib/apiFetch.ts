/**
 * Thin fetch wrapper that prefixes the bpm-svc base URL and injects the
 * JWT bearer from localStorage. The JWT is minted by /api/dev/login when
 * the RoleSwitcher picks a persona (dev mode), or by an external IdP in
 * prod mode.
 *
 * On 401, the stored token is cleared and a `bpm:auth-cleared` event is
 * dispatched on `window` so any listener (RoleSwitcher) can re-mint or
 * surface a login prompt.
 */

const BASE = import.meta.env.VITE_BPM_SVC_URL ?? 'http://localhost:5290'
const JWT_KEY = 'bpm_jwt'

export const BPM_SVC_URL = BASE

export function getJwt(): string | null {
  if (typeof window === 'undefined') return null
  return window.localStorage.getItem(JWT_KEY)
}

export function setJwt(token: string): void {
  if (typeof window === 'undefined') return
  window.localStorage.setItem(JWT_KEY, token)
}

export function clearJwt(): void {
  if (typeof window === 'undefined') return
  window.localStorage.removeItem(JWT_KEY)
  window.dispatchEvent(new CustomEvent('bpm:auth-cleared'))
}

export async function apiFetch(path: string, init: RequestInit = {}): Promise<Response> {
  const headers = new Headers(init.headers)
  const token = getJwt()
  if (token && !headers.has('Authorization')) {
    headers.set('Authorization', `Bearer ${token}`)
  }
  const res = await fetch(`${BASE}${path}`, { ...init, headers })
  if (res.status === 401) clearJwt()
  return res
}
