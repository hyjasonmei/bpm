import {
  Briefcase, Coffee, FileText, Folder, HeartPulse, Plane,
  Settings, ShoppingCart, Sparkles, Users, Wallet, Wrench,
  type LucideIcon,
} from 'lucide-react'

/**
 * Curated launcher icon catalog — must mirror the admin-ui
 * `ICON_CATALOG` (bpm-admin-ui FlowGroupsTab) name-for-name. The admin
 * Site Setting → Flow Groups picker and the AI Kitchen flow-catalog
 * panel both store icon names from this set; bpm-ui maps the name back
 * to a component when rendering launcher tiles and group headers.
 *
 * Falls back to Folder for an unknown / null name so a typo or a future
 * admin-side addition never breaks the launcher.
 */
const CATALOG: Record<string, LucideIcon> = {
  Users, ShoppingCart, Wrench, Plane, FileText, Briefcase,
  HeartPulse, Coffee, Wallet, Settings, Folder, Sparkles,
}

export const DEFAULT_LAUNCHER_ICON: LucideIcon = Folder

export function resolveLauncherIcon(name: string | null | undefined): LucideIcon {
  if (!name) return DEFAULT_LAUNCHER_ICON
  return CATALOG[name] ?? DEFAULT_LAUNCHER_ICON
}
