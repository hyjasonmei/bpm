import { ChefHat } from 'lucide-react'
import { type Screen } from '@/components/AppLayout'
import { SectionCard, SectionTitle } from '@/components/ui/card'
import { formRegistry } from '@/features/registry'
import { FORMS } from '@/lib/workflow'

interface CreateIndexProps {
  setScreen: (s: Screen) => void
}

export function CreateIndex({ setScreen }: CreateIndexProps) {
  // Registry-driven (matches Home Quick Actions). Each chef-shipped
  // manifest under features/<CODE>/V<N>/ surfaces as one option here.
  const actions = [...formRegistry.values()]
    .map(m => ({ code: m.code, label: FORMS[m.code]?.zhLabel ?? m.code }))
    .sort((a, b) => a.code.localeCompare(b.code))

  return (
    <div className="mx-auto max-w-screen-lg space-y-6 p-6">
      <div>
        <h1 className="text-2xl font-bold text-ink">Create a new case</h1>
        <p className="mt-1 text-sm text-ink-muted">Pick a form to start a new process instance.</p>
      </div>

      <SectionCard>
        <SectionTitle>Available flows</SectionTitle>
        {actions.length === 0 ? (
          <div className="px-4 py-10 text-center text-sm text-ink-faint">
            目前沒有可用的流程 — 請聯絡管理員建置新流程。
          </div>
        ) : (
          <div className="grid grid-cols-2 gap-px bg-slate-100 md:grid-cols-3">
            {actions.map(a => (
              <button
                key={a.code}
                onClick={() => setScreen({ kind: 'form', code: a.code })}
                className="flex flex-col items-start gap-1 bg-white px-4 py-3 text-left transition-colors hover:bg-blue-50"
              >
                <span className="font-mono text-[10.5px] uppercase tracking-wider text-ink-faint">{a.code}</span>
                <span className="flex items-center gap-1.5 text-sm font-medium text-ink">
                  <ChefHat className="h-3.5 w-3.5 text-primary" />
                  {a.label}
                </span>
              </button>
            ))}
          </div>
        )}
      </SectionCard>
    </div>
  )
}
