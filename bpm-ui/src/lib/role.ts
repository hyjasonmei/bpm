import { useEffect, useState, useCallback } from 'react'
import { apiFetch, setJwt, clearJwt, getJwt } from './apiFetch'
import { decodeJwt, jwtRoles } from './jwt'

export type PersonaCode = 'employee' | 'manager' | 'finance' | 'it' | 'hr' | 'admin'

export interface Persona {
  id: PersonaCode
  displayName: string
  zhName: string
  emoji: string
  user: { name: string; dept: string; id: string }
  description: string
}

// PERSONAS metadata aligned to admin-svc's seed (post unify-user-store).
// Each persona maps to the corresponding @acme.example user that
// bpm-svc's PersonaLoginService resolves /api/dev/login against. The
// `id` strings here are display-only — runtime identity always comes
// from the JWT after login.
export const PERSONAS: Record<PersonaCode, Persona> = {
  employee: {
    id: 'employee',
    displayName: 'Employee',
    zhName: '一般員工',
    emoji: '🧑‍💻',
    user: { id: 'bob', name: 'Bob', dept: 'Backend' },
    description: 'Submit and track your own requests',
  },
  manager: {
    id: 'manager',
    displayName: 'Manager',
    zhName: '主管',
    emoji: '👔',
    user: { id: 'alice', name: 'Alice', dept: 'Backend' },
    description: 'Approve cases from your direct reports',
  },
  finance: {
    id: 'finance',
    displayName: 'Finance',
    zhName: '財務',
    emoji: '💰',
    user: { id: 'frank', name: 'Frank', dept: 'Product' },
    description: 'Run financial review on expense and travel cases',
  },
  it: {
    id: 'it',
    displayName: 'IT',
    zhName: '資訊',
    emoji: '🖥️',
    user: { id: 'dave', name: 'Dave', dept: 'Frontend' },
    description: 'Spec-review and procure hardware / software',
  },
  hr: {
    id: 'hr',
    displayName: 'HR',
    zhName: '人資',
    emoji: '🧑‍💼',
    user: { id: 'henry', name: 'Henry', dept: 'HR' },
    description: 'Process onboarding, leave, and termination cases',
  },
  admin: {
    id: 'admin',
    displayName: 'Admin',
    zhName: '系統管理員',
    emoji: '🔑',
    user: { id: 'jack', name: 'Jack', dept: 'Acme Corp' },
    description: 'Full visibility and configuration access',
  },
}

/**
 * Mapping from legacy PersonaCode → admin-svc role Name. canAct() and
 * any other ownerByStep gating should consult this map. Post
 * unify-user-store, the runtime + JWT carry these role names; the
 * PersonaCode enum is a transitional display shim.
 */
export const PERSONA_TO_ADMIN_ROLE: Record<PersonaCode, string> = {
  employee: 'Submitter',
  manager:  'Approver',
  finance:  'Finance',
  it:       'Procurement',
  hr:       'HR_Manager',
  admin:    'SystemAdmin',
}

/**
 * Pick the "closest" PersonaCode given a list of role names from the
 * JWT. Priority order: admin > hr > finance > it > manager > employee.
 */
export function personaFromRoles(roles: string[]): PersonaCode {
  if (roles.includes('SystemAdmin')) return 'admin'
  if (roles.includes('HR_Manager'))  return 'hr'
  if (roles.includes('Finance'))     return 'finance'
  if (roles.includes('Procurement')) return 'it'
  if (roles.includes('Approver') || roles.includes('Director')) return 'manager'
  return 'employee'
}

const STORAGE_KEY = 'bpm_active_role'

/**
 * Persist the persona that matches the just-authenticated identity.
 *
 * The dev/auth-login JWT carries identity only (no roles claim), so on the
 * next load `useActivePersona` can't derive a persona from the token and
 * would otherwise fall back to whatever `bpm_active_role` was left at by a
 * *previous* user/session (e.g. logging in as employee-Bob after a prior
 * switch to manager-Alice would still read "manager"). Call this on a fresh
 * login with the roles from the login response so the saved persona always
 * reflects the identity actually logged in.
 */
export function rememberPersonaForRoles(roles: string[]): void {
  if (typeof window === 'undefined') return
  window.localStorage.setItem(STORAGE_KEY, personaFromRoles(roles))
}

export interface DevLoginUser {
  id: string
  fullName: string
  email: string
  departmentCode: string | null
  personaCode: PersonaCode
  roles: string[]
}

/// Calls /api/dev/login for the given persona. Stores the returned JWT in
/// localStorage and returns the user summary. Throws on non-200.
async function loginAs(personaCode: PersonaCode): Promise<DevLoginUser> {
  const res = await apiFetch('/api/dev/login', {
    method: 'POST',
    headers: { 'content-type': 'application/json' },
    body: JSON.stringify({ PersonaCode: personaCode }),
  })
  if (!res.ok) {
    const text = await res.text().catch(() => '')
    throw new Error(`dev-login failed (${res.status}): ${text}`)
  }
  const data = await res.json()
  setJwt(data.token)
  return data.user as DevLoginUser
}

function authedFromJwt(): DevLoginUser | null {
  if (typeof window === 'undefined') return null
  const token = getJwt()
  if (!token) return null
  const decoded = decodeJwt(token)
  if (!decoded?.sub) return null
  const roles = jwtRoles(decoded)
  return {
    id: decoded.sub,
    fullName: decoded.full_name ?? decoded.email ?? '(unknown)',
    email: decoded.email ?? '',
    departmentCode: null,
    personaCode: (decoded.persona_code as PersonaCode) ?? personaFromRoles(roles),
    roles,
  }
}

export function useActivePersona() {
  const initialAuthed = authedFromJwt()
  const [code, setCodeState] = useState<PersonaCode>(() => {
    if (typeof window === 'undefined') return 'employee'
    // Prefer the JWT-derived persona when available — it reflects the
    // identity actually logged in, not whatever the dropdown was on last
    // visit before login.
    if (initialAuthed?.roles?.length) return personaFromRoles(initialAuthed.roles)
    const saved = localStorage.getItem(STORAGE_KEY)
    return saved && saved in PERSONAS ? (saved as PersonaCode) : 'employee'
  })
  const [authedUser, setAuthedUser] = useState<DevLoginUser | null>(initialAuthed)
  const [error, setError] = useState<string | null>(null)
  const [pending, setPending] = useState(false)

  const setCode = useCallback(async (next: PersonaCode) => {
    setPending(true)
    setError(null)
    try {
      const user = await loginAs(next)
      setAuthedUser(user)
      localStorage.setItem(STORAGE_KEY, next)
      setCodeState(next)
      // Switching persona re-mints the JWT to a *different* identity without
      // a page reload. Anything that fetched identity-scoped data (the
      // unified inbox in particular) is now stale — it won't re-run its
      // effect just because we re-rendered. Broadcast so those listeners
      // refetch immediately instead of waiting for their next poll tick.
      window.dispatchEvent(new CustomEvent('bpm:identity-changed'))
    } catch (e: unknown) {
      const msg = e instanceof Error ? e.message : String(e)
      setError(msg)
    } finally {
      setPending(false)
    }
  }, [])

  // Re-derive persona when impersonation swap-back fires (or any other
  // event that swaps the JWT under us).
  useEffect(() => {
    const onSwap = () => {
      const next = authedFromJwt()
      setAuthedUser(next)
      if (next?.roles?.length) setCodeState(personaFromRoles(next.roles))
    }
    window.addEventListener('bpm:impersonation-ended', onSwap as EventListener)
    return () => window.removeEventListener('bpm:impersonation-ended', onSwap as EventListener)
  }, [])

  return { persona: PERSONAS[code], code, setCode, authedUser, pending, error, clearAuth: clearJwt }
}
