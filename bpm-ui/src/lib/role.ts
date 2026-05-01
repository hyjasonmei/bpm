import { useEffect, useState } from 'react'

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

export function useActivePersona() {
  const [code, setCode] = useState<PersonaCode>(() => {
    if (typeof window === 'undefined') return 'employee'
    const saved = localStorage.getItem(STORAGE_KEY)
    return (saved && saved in PERSONAS ? (saved as PersonaCode) : 'employee')
  })

  useEffect(() => {
    localStorage.setItem(STORAGE_KEY, code)
  }, [code])

  return { persona: PERSONAS[code], code, setCode }
}
