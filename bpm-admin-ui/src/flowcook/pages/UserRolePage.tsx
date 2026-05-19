import { useCallback, useEffect, useState } from 'react'
import { ShieldCheck, Users } from 'lucide-react'
import { NavLink, Navigate, Route, Routes } from 'react-router-dom'
import { cn } from '@/lib/cn'
import { api } from '@/flowcook/api'
import { type Delegation, type Principal, type Role } from '@/flowcook/types'
import { PrincipalsTab } from '@/flowcook/pages/userrole/PrincipalsTab'
import { RolesTab } from '@/flowcook/pages/userrole/RolesTab'

const TABS = [
  { path: '/user-role/principals', label: 'Principals', icon: Users },
  { path: '/user-role/roles',      label: 'Roles',      icon: ShieldCheck },
] as const

export function UserRolePage() {
  const [principals, setPrincipals] = useState<Principal[]>([])
  const [roles, setRoles] = useState<Role[]>([])
  const [delegations, setDelegations] = useState<Delegation[]>([])
  const [loading, setLoading] = useState(true)
  const [error, setError] = useState<string | null>(null)

  const refreshAll = useCallback(async () => {
    setLoading(true)
    setError(null)
    try {
      const [principalsRes, rolesRes, delegationsRes] = await Promise.all([
        api<Principal[]>('/api/principals'),
        api<Role[]>('/api/roles'),
        api<Delegation[]>('/api/delegations'),
      ])
      setPrincipals(principalsRes)
      setRoles(rolesRes)
      setDelegations(delegationsRes)
    } catch (err) {
      setError(err instanceof Error ? err.message : 'Failed to load')
    } finally {
      setLoading(false)
    }
  }, [])

  const refreshRoles = useCallback(async () => {
    try {
      const rolesRes = await api<Role[]>('/api/roles')
      setRoles(rolesRes)
    } catch {
      /* silent */
    }
  }, [])

  useEffect(() => { void refreshAll() }, [refreshAll])

  return (
    <div className="flex h-full flex-col">
      <div className="mb-5 flex items-center gap-1 border-b border-rule">
        {TABS.map((t) => {
          const Icon = t.icon
          return (
            <NavLink
              key={t.path}
              to={t.path}
              data-testid={`tab-${t.path}`}
              className={({ isActive }) => cn(
                '-mb-px inline-flex items-center gap-2 border-b-2 px-4 py-2 text-sm font-medium transition-colors',
                isActive
                  ? 'border-primary text-primary'
                  : 'border-transparent text-ink-muted hover:text-ink',
              )}
            >
              <Icon className="h-4 w-4" />
              {t.label}
            </NavLink>
          )
        })}
      </div>

      <div className="flex-1 min-h-0">
        <Routes>
          <Route index element={<Navigate to="principals" replace />} />
          <Route path="principals" element={
            <PrincipalsTab
              principals={principals}
              roles={roles}
              delegations={delegations}
              loading={loading}
              error={error}
              refreshAll={refreshAll}
            />
          } />
          <Route path="roles" element={
            <RolesTab roles={roles} refreshRoles={refreshRoles} />
          } />
          <Route path="*" element={<Navigate to="principals" replace />} />
        </Routes>
      </div>
    </div>
  )
}
