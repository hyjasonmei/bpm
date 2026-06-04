import { setJwt, getJwt } from '@/lib/apiFetch'
import { decodeJwt } from '@/lib/jwt'

const PRE_KEY = 'bpm_jwt_pre_impersonation'

// Save the current admin JWT, then swap in the impersonation token.
export function enterImpersonation(impersonationToken: string): void {
  const current = getJwt()
  if (current) {
    window.localStorage.setItem(PRE_KEY, current)
  }
  setJwt(impersonationToken)
}

// Restore admin JWT and clear the pre-key. Returns true if a pre-token existed.
export function exitImpersonationLocal(): boolean {
  const pre = window.localStorage.getItem(PRE_KEY)
  if (!pre) return false
  setJwt(pre)
  window.localStorage.removeItem(PRE_KEY)
  return true
}

export function isImpersonating(): boolean {
  const jwt = getJwt()
  if (!jwt) return false
  return decodeJwt(jwt)?.impersonated_by != null
}

// The privileged token behind a persona switch: the stored pre-token if we
// swapped one in, else the current token. Lets the persona switcher authorize
// against the REAL identity even while acting as a non-admin persona — so the
// operator can keep switching (and return) instead of getting locked out.
export function realIdentityToken(): string | null {
  if (typeof window === 'undefined') return null
  return window.localStorage.getItem(PRE_KEY) ?? getJwt()
}

export function impersonationExpiry(): Date | null {
  const jwt = getJwt()
  if (!jwt) return null
  const decoded = decodeJwt(jwt)
  if (!decoded?.exp) return null
  return new Date(decoded.exp * 1000)
}
