import { FORM_GROUPS, type Screen } from '@/components/AppLayout'
import { SectionCard, SectionTitle } from '@/components/ui/card'

interface CreateIndexProps {
  setScreen: (s: Screen) => void
}

export function CreateIndex({ setScreen }: CreateIndexProps) {
  return (
    <div className="mx-auto max-w-screen-lg space-y-6 p-6">
      <div>
        <h1 className="text-2xl font-bold text-ink">Create a new case</h1>
        <p className="mt-1 text-sm text-ink-muted">Pick a form to start a new process instance.</p>
      </div>

      <div className="space-y-4">
        {FORM_GROUPS.map(g => (
          <SectionCard key={g.group}>
            <SectionTitle>{g.group}</SectionTitle>
            <div className="grid grid-cols-2 gap-px bg-slate-100 md:grid-cols-3">
              {g.items.map(item => (
                <button
                  key={item.id}
                  onClick={() => setScreen({ kind: 'form', code: item.id })}
                  className="flex flex-col items-start gap-1 bg-white px-4 py-3 text-left transition-colors hover:bg-blue-50"
                >
                  <span className="font-mono text-[10.5px] uppercase tracking-wider text-ink-faint">{item.id}</span>
                  <span className="text-sm font-medium text-ink">{item.label}</span>
                </button>
              ))}
            </div>
          </SectionCard>
        ))}
      </div>
    </div>
  )
}
