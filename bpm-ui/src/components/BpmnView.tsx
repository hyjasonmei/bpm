import { useEffect } from 'react'
import { X } from 'lucide-react'
import type { Step } from '@/lib/workflow'
import type { PersonaCode } from '@/lib/role'
import { ownerLabel } from '@/lib/workflow'

interface BpmnViewProps {
  open: boolean
  steps: Step[]
  activeStep: number
  ownerByStep: (PersonaCode | null)[]
  formLabel: string
  onClose: () => void
}

export function BpmnView({ open, steps, activeStep, ownerByStep, formLabel, onClose }: BpmnViewProps) {
  useEffect(() => {
    if (!open) return
    const onEsc = (e: KeyboardEvent) => { if (e.key === 'Escape') onClose() }
    document.addEventListener('keydown', onEsc)
    return () => document.removeEventListener('keydown', onEsc)
  }, [open, onClose])

  if (!open) return null

  const NODE_W = 150
  const NODE_H = 64
  const GAP = 56
  const PAD_X = 60
  const PAD_Y = 80
  const totalW = PAD_X * 2 + 36 + NODE_W * steps.length + GAP * (steps.length + 1) + 36
  const totalH = PAD_Y * 2 + NODE_H

  return (
    <div className="fixed inset-0 z-50 flex items-center justify-center bg-black/50 p-6" onClick={onClose}>
      <div className="relative max-w-[95vw] rounded-lg bg-white shadow-2xl" onClick={e => e.stopPropagation()} role="dialog" aria-modal="true">
        <div className="flex items-center justify-between border-b border-rule px-5 py-3">
          <div>
            <h3 className="text-sm font-bold text-ink">BPMN diagram — {formLabel}</h3>
            <p className="text-xs text-ink-muted">流程圖檢視 · this view will be driven by the C# workflow engine</p>
          </div>
          <button onClick={onClose} className="rounded p-1 text-slate-400 hover:bg-slate-100 hover:text-slate-700" aria-label="Close">
            <X className="h-4 w-4" />
          </button>
        </div>

        <div className="overflow-auto px-6 py-6">
          <svg width={totalW} height={totalH} viewBox={`0 0 ${totalW} ${totalH}`} xmlns="http://www.w3.org/2000/svg">
            {/* arrows + nodes */}
            <defs>
              <marker id="arrow" viewBox="0 0 10 10" refX="9" refY="5" markerWidth="8" markerHeight="8" orient="auto">
                <path d="M0,0 L10,5 L0,10 z" fill="#94A3B8" />
              </marker>
            </defs>

            {/* start circle */}
            <g>
              <circle cx={PAD_X + 18} cy={PAD_Y + NODE_H / 2} r="14" fill="#FFFFFF" stroke="#16A34A" strokeWidth="2" />
              <circle cx={PAD_X + 18} cy={PAD_Y + NODE_H / 2} r="6" fill="#16A34A" />
              <text x={PAD_X + 18} y={PAD_Y + NODE_H + 22} textAnchor="middle" fontSize="11" fill="#64748B">Start</text>
            </g>

            {/* connector start -> first */}
            <line
              x1={PAD_X + 32}
              y1={PAD_Y + NODE_H / 2}
              x2={PAD_X + 36 + GAP}
              y2={PAD_Y + NODE_H / 2}
              stroke="#94A3B8"
              strokeWidth="1.5"
              markerEnd="url(#arrow)"
            />

            {steps.map((step, i) => {
              const x = PAD_X + 36 + GAP + i * (NODE_W + GAP)
              const y = PAD_Y
              const done = i < activeStep
              const current = i === activeStep
              const fill = current ? '#F59E0B' : done ? '#DCFCE7' : '#FFFFFF'
              const stroke = current ? '#F59E0B' : done ? '#16A34A' : '#CBD5E1'
              const text = current ? '#FFFFFF' : '#0F172A'
              const owner = ownerByStep[i]

              const nextX = i === steps.length - 1 ? x + NODE_W + GAP : x + NODE_W
              const arrowEnd = i === steps.length - 1 ? nextX + 4 : nextX + GAP - 8

              return (
                <g key={step.id}>
                  <rect x={x} y={y} width={NODE_W} height={NODE_H} rx="6" fill={fill} stroke={stroke} strokeWidth={current ? 2 : 1.5} />
                  <text x={x + NODE_W / 2} y={y + NODE_H / 2 - 4} textAnchor="middle" fontSize="12" fontWeight="700" fill={text}>{step.en}</text>
                  {step.zh && (
                    <text x={x + NODE_W / 2} y={y + NODE_H / 2 + 14} textAnchor="middle" fontSize="11" fill={current ? '#FFF' : '#64748B'}>{step.zh}</text>
                  )}
                  {/* role label */}
                  <text x={x + NODE_W / 2} y={y + NODE_H + 22} textAnchor="middle" fontSize="10.5" fill="#64748B">
                    {ownerLabel(owner)}
                  </text>
                  {/* check for done */}
                  {done && (
                    <g transform={`translate(${x + NODE_W - 18}, ${y + 6})`}>
                      <circle r="8" cx="6" cy="6" fill="#16A34A" />
                      <path d="M3.2 6.2 l2.2 2.2 l4.6 -4.6" stroke="#FFFFFF" strokeWidth="1.6" fill="none" strokeLinecap="round" strokeLinejoin="round" />
                    </g>
                  )}

                  {/* connector to next */}
                  <line
                    x1={x + NODE_W}
                    y1={y + NODE_H / 2}
                    x2={arrowEnd}
                    y2={y + NODE_H / 2}
                    stroke="#94A3B8"
                    strokeWidth="1.5"
                    markerEnd="url(#arrow)"
                  />
                </g>
              )
            })}

            {/* end circle */}
            <g>
              <circle
                cx={PAD_X + 36 + GAP + steps.length * (NODE_W + GAP) + 18}
                cy={PAD_Y + NODE_H / 2}
                r="14"
                fill="#FFFFFF"
                stroke="#0F172A"
                strokeWidth="2.5"
              />
              <text
                x={PAD_X + 36 + GAP + steps.length * (NODE_W + GAP) + 18}
                y={PAD_Y + NODE_H + 22}
                textAnchor="middle"
                fontSize="11"
                fill="#64748B"
              >
                End
              </text>
            </g>
          </svg>
        </div>
      </div>
    </div>
  )
}
