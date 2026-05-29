import { Database, FolderTree } from 'lucide-react'
import { NavLink, Navigate, Route, Routes } from 'react-router-dom'
import { cn } from '@/lib/cn'
import { FlowGroupsTab } from './sitesetting/FlowGroupsTab'
import { FeatureTablesTab } from './sitesetting/FeatureTablesTab'

const TABS = [
  { path: '/site-setting/flow-groups',    label: 'Flow Groups',    icon: FolderTree },
  { path: '/site-setting/feature-tables', label: 'Feature Tables', icon: Database },
] as const

export function SiteSettingPage() {
  return (
    <div className="flex h-full flex-col">
      <div className="mb-5 flex items-center gap-1 border-b border-rule">
        {TABS.map((t) => {
          const Icon = t.icon
          return (
            <NavLink
              key={t.path}
              to={t.path}
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
          <Route index element={<Navigate to="flow-groups" replace />} />
          <Route path="flow-groups" element={<FlowGroupsTab />} />
          <Route path="feature-tables" element={<FeatureTablesTab />} />
          <Route path="*" element={<Navigate to="flow-groups" replace />} />
        </Routes>
      </div>
    </div>
  )
}
