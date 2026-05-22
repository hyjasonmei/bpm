export interface DecodedJwt {
  sub?: string
  email?: string
  full_name?: string
  roles?: string | string[]
  persona_code?: string
  exp?: number
  impersonated_by?: string
  imp_session_id?: string
  /** PR-J5 §10: present on tokens minted by POST /api/sandbox/persona. */
  sandbox_actor?: string | boolean
  actual_actor_id?: string
  actual_actor_email?: string
}

/** PR-J5 §10.3: drives the "now acting as <X> (sandbox)" pill. */
export function isSandboxActor(d: DecodedJwt | null): boolean {
  if (!d?.sandbox_actor) return false
  return d.sandbox_actor === true || String(d.sandbox_actor).toLowerCase() === 'true'
}

export function decodeJwt(token: string): DecodedJwt | null {
  const parts = token.split('.')
  if (parts.length < 2) return null
  try {
    const payload = parts[1].replace(/-/g, '+').replace(/_/g, '/')
    const padded = payload + '='.repeat((4 - (payload.length % 4)) % 4)
    const json = atob(padded)
    return JSON.parse(json) as DecodedJwt
  } catch {
    return null
  }
}

export function jwtRoles(d: DecodedJwt | null): string[] {
  if (!d?.roles) return []
  if (Array.isArray(d.roles)) return d.roles
  return d.roles.split(/[\s,]+/).filter(Boolean)
}

export function isAdmin(d: DecodedJwt | null): boolean {
  return jwtRoles(d).includes('admin')
}
