// Fetch wrapper for the admin SPA. Post unify-jwt, admin-ui reaches BOTH
// services cross-origin with a single JWT bearer (from localStorage) — no
// cookie, no dev-only proxy dependency:
//   - "/api/..."      → bpm-admin-svc  (VITE_ADMIN_SVC_URL)
//   - "/bpmsvc/api/..."→ bpm-svc       (VITE_BPM_SVC_URL, "/bpmsvc" prefix stripped,
//                        matching the old vite dev-proxy rewrite)
// The token is the same one bpm-svc accepts (shared secret + Admin_* identity),
// so the same bearer authorizes calls to either service.

import { getJwt, clearJwt } from '@/lib/apiFetch'

const ADMIN_BASE = import.meta.env.VITE_ADMIN_SVC_URL ?? 'http://localhost:5266'
const BPM_BASE = import.meta.env.VITE_BPM_SVC_URL ?? 'http://localhost:5290'

function resolveUrl(path: string): string {
  const p = path.startsWith('/') ? path : `/${path}`
  if (p.startsWith('/bpmsvc')) return `${BPM_BASE}${p.replace(/^\/bpmsvc/, '')}`
  return `${ADMIN_BASE}${p}`
}

export class ApiError extends Error {
  status: number
  body: string
  constructor(status: number, body: string) {
    super(`HTTP ${status}: ${body}`)
    this.status = status
    this.body = body
  }
}

/**
 * Like {@link api} but returns the raw `Response` so callers can read a
 * binary/blob body (e.g. the bundle .zip). Still routes through
 * `resolveUrl` (so it hits VITE_ADMIN_SVC_URL in prod, not the SPA origin)
 * and injects the JWT bearer. Does NOT throw on non-2xx — the caller
 * inspects `res.ok` / `res.status` itself. Clears the token on 401.
 */
export async function apiRaw(
  path: string,
  init: RequestInit & { json?: unknown } = {},
): Promise<Response> {
  const { json, headers, ...rest } = init
  const token = getJwt()
  const finalHeaders: Record<string, string> = {
    ...(json !== undefined ? { 'Content-Type': 'application/json' } : {}),
    ...(token ? { Authorization: `Bearer ${token}` } : {}),
    ...(headers as Record<string, string> | undefined),
  }
  const res = await fetch(resolveUrl(path), {
    headers: finalHeaders,
    body: json !== undefined ? JSON.stringify(json) : (rest.body as BodyInit | null | undefined),
    ...rest,
  })
  if (res.status === 401) clearJwt()
  return res
}

export async function api<T = unknown>(
  path: string,
  init: RequestInit & { json?: unknown } = {},
): Promise<T> {
  const { json, headers, ...rest } = init
  const token = getJwt()
  const finalHeaders: Record<string, string> = {
    Accept: 'application/json',
    ...(json !== undefined ? { 'Content-Type': 'application/json' } : {}),
    ...(token ? { Authorization: `Bearer ${token}` } : {}),
    ...(headers as Record<string, string> | undefined),
  }
  const res = await fetch(resolveUrl(path), {
    headers: finalHeaders,
    body: json !== undefined ? JSON.stringify(json) : (rest.body as BodyInit | null | undefined),
    ...rest,
  })
  if (!res.ok) {
    if (res.status === 401) clearJwt()
    const text = await res.text().catch(() => '')
    throw new ApiError(res.status, text)
  }
  if (res.status === 204) return undefined as unknown as T
  const contentType = res.headers.get('content-type') ?? ''
  if (contentType.includes('application/json')) {
    return (await res.json()) as T
  }
  return (await res.text()) as unknown as T
}
