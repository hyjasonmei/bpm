import { useCallback, useEffect, useState } from 'react'
import { ShieldCheck, Users } from 'lucide-react'
import { cn } from '@/lib/cn'
import { api } from '@/flowcook/api'
import { type Delegation, type Principal, type Role } from '@/flowcook/types'
import { PrincipalsTab } from '@/flowcook/pages/userrole/PrincipalsTab'
import { RolesTab } from '@/flowcook/pages/userrole/RolesTab'

type Tab = 'principals' | 'roles'

const TABS: Array<{ id: Tab; label: string; icon: React.ComponentType<{ className?: string }> }> = [
  { id: 'principals', label: 'Principals', icon: Users },
  { id: 'roles',      label: 'Roles',      icon: ShieldCheck },
]

export function UserRolePage() {
  const [tab, setTab] = useState<Tab>('principals')
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
          const active = tab === t.id
          return (
            <button
              key={t.id}
              onClick={() => setTab(t.id)}
              data-testid={`tab-${t.id}`}
              className={cn(
                '-mb-px inline-flex items-center gap-2 border-b-2 px-4 py-2 text-sm font-medium transition-colors',
                active
                  ? 'border-primary text-primary'
                  : 'border-transparent text-ink-muted hover:text-ink',
              )}
            >
              <Icon className="h-4 w-4" />
              {t.label}
            </button>
          )
        })}
      </div>

      <div className="flex-1 min-h-0">
        {tab === 'principals' && (
          <PrincipalsTab
            principals={principals}
            roles={roles}
            delegations={delegations}
            loading={loading}
            error={error}
            refreshAll={refreshAll}
          />
        )}
        {tab === 'roles' && (
          <RolesTab roles={roles} refreshRoles={refreshRoles} />
        )}
      </div>
    </div>
  )
}
