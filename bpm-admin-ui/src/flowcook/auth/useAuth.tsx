import { createContext, useCallback, useContext, useEffect, useState } from 'react'
import { api, ApiError } from '@/flowcook/api'
import { setJwt, clearJwt } from '@/lib/apiFetch'
import type { CurrentUser } from '@/flowcook/types'

interface AuthState {
  status: 'pending' | 'unauthenticated' | 'authenticated'
  user: CurrentUser | null
}

interface AuthContextValue extends AuthState {
  login: (username: string, password: string) => Promise<void>
  logout: () => Promise<void>
  refresh: () => Promise<void>
}

const AuthContext = createContext<AuthContextValue | null>(null)

export function AuthProvider({ children }: { children: React.ReactNode }) {
  const [state, setState] = useState<AuthState>({ status: 'pending', user: null })

  const refresh = useCallback(async () => {
    try {
      const me = await api<CurrentUser>('/api/auth/me')
      setState({ status: 'authenticated', user: me })
    } catch (err) {
      if (err instanceof ApiError && err.status === 401) {
        setState({ status: 'unauthenticated', user: null })
      } else {
        setState({ status: 'unauthenticated', user: null })
      }
    }
  }, [])

  useEffect(() => {
    void refresh()
  }, [refresh])

  const login = useCallback(async (username: string, password: string) => {
    // JWT login: store the token (key shared with lib/apiFetch so /bpmsvc calls
    // to bpm-svc carry the same bearer), then load the user via /api/auth/me.
    const res = await api<{ token: string }>('/api/auth/login', {
      method: 'POST',
      json: { username, password },
    })
    setJwt(res.token)
    await refresh()
  }, [refresh])

  const logout = useCallback(async () => {
    try {
      await api('/api/auth/logout', { method: 'POST' })
    } catch { /* tolerate failure */ }
    clearJwt()
    setState({ status: 'unauthenticated', user: null })
  }, [])

  return (
    <AuthContext.Provider value={{ ...state, login, logout, refresh }}>
      {children}
    </AuthContext.Provider>
  )
}

export function useAuth(): AuthContextValue {
  const ctx = useContext(AuthContext)
  if (!ctx) throw new Error('useAuth must be used inside <AuthProvider>')
  return ctx
}
