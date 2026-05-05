import { useEffect, useState, useCallback } from 'react'
import { apiFetch, setJwt, clearJwt, getJwt } from './apiFetch'

export type PersonaCode = 'employee' | 'manager' | 'finance' | 'it' | 'hr' | 'admin'

export interface Persona {
  id: PersonaCode
  displayName: string
  zhName: string
  emoji: string
  user: { name: string; dept: string; id: string }
  description: string
}

export const PERSONAS: Record<PersonaCode, Persona> = {
  employee: {
    id: 'employee',
    displayName: 'Employee',
    zhName: '一般員工',
    emoji: '🧑‍💻',
    user: { id: 'wilson', name: 'Wilson You (游上毅) - 31781', dept: 'TWT.1746G - Corp IS-SaaS & Digital Business' },
    description: 'Submit and track your own requests',
  },
  manager: {
    id: 'manager',
    displayName: 'Manager',
    zhName: '主管',
    emoji: '👔',
    user: { id: 'elton', name: 'Elton Yang (楊旭東) - 31412', dept: 'TWT.1746G - Corp IS-SaaS & Digital Business' },
    description: 'Approve cases from your direct reports',
  },
  finance: {
    id: 'finance',
    displayName: 'Finance',
    zhName: '財務',
    emoji: '💰',
    user: { id: 'jean', name: 'Jean Hsu (許靜怡) - 30287', dept: 'GCC.1751G - Finance Operation' },
    description: 'Run financial review on expense and travel cases',
  },
  it: {
    id: 'it',
    displayName: 'IT',
    zhName: '資訊',
    emoji: '🖥️',
    user: { id: 'mark', name: 'Mark Ng (吳家銘) - 28911', dept: 'TWT.1746G - Corp IS-Infrastructure' },
    description: 'Spec-review and procure hardware / software',
  },
  hr: {
    id: 'hr',
    displayName: 'HR',
    zhName: '人資',
    emoji: '🧑‍💼',
    user: { id: 'amy', name: 'Amy Lin (林宛靜) - 27714', dept: 'GCC.1700G - Human Resources' },
    description: 'Process onboarding, leave, and termination cases',
  },
  admin: {
    id: 'admin',
    displayName: 'Admin',
    zhName: '系統管理員',
    emoji: '🔑',
    user: { id: 'admin', name: 'System Admin', dept: 'BPM Platform Ops' },
    description: 'Full visibility and configuration access',
  },
}

const STORAGE_KEY = 'bpm_active_role'

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

export function useActivePersona() {
  const [code, setCodeState] = useState<PersonaCode>(() => {
    if (typeof window === 'undefined') return 'employee'
    const saved = localStorage.getItem(STORAGE_KEY)
    return saved && saved in PERSONAS ? (saved as PersonaCode) : 'employee'
  })
  const [authedUser, setAuthedUser] = useState<DevLoginUser | null>(null)
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
    } catch (e: unknown) {
      const msg = e instanceof Error ? e.message : String(e)
      setError(msg)
      // Don't clear existing token — keep prior session alive on switch failure.
    } finally {
      setPending(false)
    }
  }, [])

  // On first mount: if no JWT yet, mint one for the saved persona so demo
  // works zero-click. If we already have a JWT we trust it (validated on
  // first API call; 401 clears it).
  useEffect(() => {
    if (typeof window === 'undefined') return
    if (getJwt()) return
    void setCode(code)
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [])

  // Re-mint when something downstream clears the token (401 from any call).
  useEffect(() => {
    const onCleared = () => { void setCode(code) }
    window.addEventListener('bpm:auth-cleared', onCleared as EventListener)
    return () => window.removeEventListener('bpm:auth-cleared', onCleared as EventListener)
  }, [code, setCode])

  return { persona: PERSONAS[code], code, setCode, authedUser, pending, error, clearAuth: clearJwt }
}
