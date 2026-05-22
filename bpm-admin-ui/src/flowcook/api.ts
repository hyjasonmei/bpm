// Thin fetch wrapper for the bpm-admin-svc API. All calls go through the
// vite dev proxy at /api, with credentials so the session cookie is sent.
// In production, the SPA is served from the same origin as the API.

export class ApiError extends Error {
  status: number
  body: string
  constructor(status: number, body: string) {
    super(`HTTP ${status}: ${body}`)
    this.status = status
    this.body = body
  }
}

export async function api<T = unknown>(
  path: string,
  init: RequestInit & { json?: unknown } = {},
): Promise<T> {
  const { json, headers, ...rest } = init
  const finalHeaders: Record<string, string> = {
    Accept: 'application/json',
    ...(json !== undefined ? { 'Content-Type': 'application/json' } : {}),
    ...(headers as Record<string, string> | undefined),
  }
  const res = await fetch(path.startsWith('/') ? path : `/${path}`, {
    credentials: 'include',
    headers: finalHeaders,
    body: json !== undefined ? JSON.stringify(json) : (rest.body as BodyInit | null | undefined),
    ...rest,
  })
  if (!res.ok) {
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
