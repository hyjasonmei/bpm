/**
 * Thin fetch wrapper that prefixes the bpm-svc base URL and injects the
 * demo bearer token when one is configured.
 *
 * The token comes from `VITE_BPM_DEMO_TOKEN` at Vite build time. When unset
 * (local dev with the matching backend env unset), no Authorization header
 * is sent — the backend then bypasses auth and the call works as before.
 *
 * Use `apiFetch('/api/whatever', init)` instead of `fetch(URL + '/api/...')`.
 */

const BASE = import.meta.env.VITE_BPM_SVC_URL ?? 'http://localhost:5290'
const TOKEN = import.meta.env.VITE_BPM_DEMO_TOKEN ?? ''

export const BPM_SVC_URL = BASE

export function apiFetch(path: string, init: RequestInit = {}): Promise<Response> {
  const headers = new Headers(init.headers)
  if (TOKEN && !headers.has('Authorization')) {
    headers.set('Authorization', `Bearer ${TOKEN}`)
  }
  return fetch(`${BASE}${path}`, { ...init, headers })
}
