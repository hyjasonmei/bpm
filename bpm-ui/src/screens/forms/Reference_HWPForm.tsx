import { useEffect, useState } from 'react'
import { Plus, Trash2 } from 'lucide-react'
import { SectionCard, SectionTitle } from '@/components/ui/card'
import { Input, Select, FieldLabel } from '@/components/ui/form'
import { ReadonlyField, UploadZone } from '@/components/ui/readonly'
import { FormShell, ActionBar } from './FormShell'
import { CHARGE_OPTS, HW_CATS, SHIPPING_LOCS, PURPOSES } from '@/lib/mocks'
import { PERSONAS, type PersonaCode } from '@/lib/role'
import { FlowToast, useFormRuntime, type FormRuntimeProps } from '@/hooks/useFormRuntime'

interface HWItem {
  id: number
  category: string
  item: string
  spec: string
  qty: number
}

const newItem = (): HWItem => ({ id: Date.now() + Math.random(), category: '', item: '', spec: '', qty: 1 })

interface HWPFormProps extends FormRuntimeProps {
  persona: PersonaCode
}

export function HWPForm({ persona, ...rt }: HWPFormProps) {
  const runtime = useFormRuntime('HWP', rt)
  const isTaskMode = runtime.mode === 'task'

  const [items, setItems] = useState<HWItem[]>([{ id: 1, category: '', item: '', spec: '', qty: 1 }])
  const [shippingLoc, setShippingLoc] = useState('')
  const [chargeTo, setChargeTo] = useState('TWT.1746G - Elton Yang')
  const [purpose, setPurpose] = useState('')
  const [project, setProject] = useState('')
  const [expectedDate, setExpectedDate] = useState('')
  const [note, setNote] = useState('')

  useEffect(() => {
    if (!isTaskMode || !runtime.task) return
    const fd = (runtime.task.mergedFormData ?? {}) as {
      category?: string
      item_name?: string
      spec?: string
      qty?: number | string
      shipping_loc?: string
      purpose?: string
      expected_date?: string
    }
    setItems([{
      id: 1,
      category: hwCategoryFromSpec(fd.category) ?? '',
      item: fd.item_name ?? '',
      spec: fd.spec ?? '',
      qty: fd.qty !== undefined ? Number(fd.qty) : 1,
    }])
    if (fd.shipping_loc) setShippingLoc(fd.shipping_loc)
    if (fd.purpose) setPurpose(hwPurposeFromSpec(fd.purpose) ?? fd.purpose)
    if (fd.expected_date) setExpectedDate(fd.expected_date)
  }, [isTaskMode, runtime.task])

  const ro = isTaskMode
  const upd = <K extends keyof HWItem>(id: number, f: K, v: HWItem[K]) => setItems(p => p.map(i => i.id === id ? { ...i, [f]: v } : i))
  const add = () => setItems(p => [...p, newItem()])
  const del = (id: number) => { if (items.length > 1) setItems(p => p.filter(i => i.id !== id)) }

  const u = PERSONAS[persona].user

  function toSpecPayload() {
    const first = items[0]
    return {
      category: hwCategoryToSpec(first.category),
      item_name: first.item,
      spec: first.spec,
      qty: first.qty,
      shipping_loc: shippingLoc,
      purpose: hwPurposeToSpec(purpose),
      expected_date: expectedDate || undefined,
    }
  }

  return (
    <FormShell code="HWP" activeStep={0} setActiveStep={() => {}} persona={persona} mode={runtime.mode}>
      <FlowToast message={runtime.toast} />
      {/* Hardware items table */}
      <SectionCard>
        <SectionTitle>Hardware Purchase</SectionTitle>
        <div className="space-y-3 p-4">
          <table className="w-full text-sm">
            <thead>
              <tr className="border-b border-rule">
                <th className="w-8 pb-2 text-left text-xs font-medium text-ink-muted">#</th>
                <th className="w-40 pb-2 text-left text-xs font-medium text-ink-muted">Category <span className="text-danger">*</span></th>
                <th className="pb-2 text-left text-xs font-medium text-ink-muted">Item / Spec <span className="text-danger">*</span></th>
                <th className="w-24 pb-2 text-left text-xs font-medium text-ink-muted">Qty <span className="text-danger">*</span></th>
                <th className="w-20 pb-2 text-left text-xs font-medium text-ink-muted">Action</th>
              </tr>
            </thead>
            <tbody className="divide-y divide-slate-100">
              {items.map((item, idx) => (
                <tr key={item.id} className="align-top">
                  <td className="py-2 pt-3 font-mono text-xs text-ink-faint">{idx + 1}</td>
                  <td className="py-2 pr-3">
                    <Select value={item.category} disabled={ro} onChange={e => upd(item.id, 'category', e.target.value)}>
                      <option value="">-- Please Select --</option>
                      {HW_CATS.map(c => <option key={c}>{c}</option>)}
                    </Select>
                  </td>
                  <td className="space-y-1.5 py-2 pr-3">
                    <Input placeholder="E.g. Lenovo ThinkPad X250" value={item.item} readOnly={ro} onChange={e => upd(item.id, 'item', e.target.value)} />
                    <Input placeholder='E.g. 13" / RAM 4GB / HDD 500GB 7200' value={item.spec} readOnly={ro} onChange={e => upd(item.id, 'spec', e.target.value)} />
                  </td>
                  <td className="py-2 pr-3">
                    <Input type="number" min={1} className="text-center font-mono" value={item.qty} readOnly={ro} onChange={e => upd(item.id, 'qty', Math.max(1, Number(e.target.value)))} />
                  </td>
                  <td className="py-2">
                    {!ro && (
                      <div className="flex items-center gap-1.5 pt-0.5">
                        <button onClick={add} className="inline-flex items-center gap-1 rounded border border-blue-200 px-2 py-1 text-xs font-medium text-blue-600 transition-colors hover:bg-blue-50">
                          <Plus className="h-3.5 w-3.5" /> Add
                        </button>
                        <button onClick={() => del(item.id)} disabled={items.length <= 1} className="rounded border border-red-100 p-1 text-red-400 transition-colors hover:bg-red-50 disabled:pointer-events-none disabled:opacity-30">
                          <Trash2 className="h-3.5 w-3.5" />
                        </button>
                      </div>
                    )}
                  </td>
                </tr>
              ))}
            </tbody>
          </table>
        </div>
      </SectionCard>

      {/* Request Information */}
      <SectionCard>
        <SectionTitle>Request Information</SectionTitle>
        <div className="grid grid-cols-2 gap-x-8 gap-y-4 p-4">
          <ReadonlyField label="Requestor" value={u.name} />
          <ReadonlyField label="Request Date" value={new Date().toISOString().slice(0, 10)} />
          <ReadonlyField label="Requestor Dept." value={u.dept} />
          <div>
            <FieldLabel>Request No.</FieldLabel>
            <Input disabled placeholder="Auto-generated after submit" />
          </div>
          <div>
            <FieldLabel required>Shipping Location</FieldLabel>
            <Select value={shippingLoc} disabled={ro} onChange={e => setShippingLoc(e.target.value)}>
              <option value="">-- Please Select --</option>
              {SHIPPING_LOCS.map(s => <option key={s}>{s}</option>)}
            </Select>
          </div>
          <div>
            <FieldLabel required>Project</FieldLabel>
            <Input placeholder="Click or enter keyword to search" value={project} readOnly={ro} onChange={e => setProject(e.target.value)} />
          </div>
          <div>
            <FieldLabel required>Charge to</FieldLabel>
            <Select value={chargeTo} disabled={ro} onChange={e => setChargeTo(e.target.value)}>
              {CHARGE_OPTS.map(o => <option key={o}>{o}</option>)}
            </Select>
          </div>
          <div>
            <FieldLabel>Expected Date</FieldLabel>
            <Input type="date" value={expectedDate} readOnly={ro} onChange={e => setExpectedDate(e.target.value)} />
          </div>
          <div>
            <FieldLabel required>Purpose</FieldLabel>
            <Select value={purpose} disabled={ro} onChange={e => setPurpose(e.target.value)}>
              <option value="">-- Please Select --</option>
              {PURPOSES.map(p => <option key={p}>{p}</option>)}
            </Select>
          </div>
          <div>
            <FieldLabel>Note</FieldLabel>
            <Input value={note} readOnly={ro} onChange={e => setNote(e.target.value)} />
          </div>
        </div>
      </SectionCard>

      <SectionCard>
        <SectionTitle>Attachment</SectionTitle>
        <div className="p-4"><UploadZone /></div>
      </SectionCard>

      <ActionBar code="HWP" activeStep={0} persona={persona}
        mode={runtime.mode}
        nodeKind={runtime.task?.task.nodeKind}
        pending={runtime.pending}
        onSubmit={() => {
          if (isTaskMode) { void runtime.submitUserTask(); return }
          const first = items[0]
          if (!first.category || !first.item.trim() || !first.spec.trim() || !shippingLoc || !purpose) {
            runtime.fireToast('Category, item, spec, shipping location and purpose are required.')
            return
          }
          void runtime.submitCreate(toSpecPayload())
        }}
        onApprove={c => { void runtime.approve(c) }}
        onReject={c => { void runtime.reject(c) }}
        onReturnTask={c => { void runtime.returnTask(c) }}
      />
    </FormShell>
  )
}

function hwCategoryToSpec(uiCategory: string): string {
  const lc = uiCategory.toLowerCase()
  if (lc.includes('notebook') || lc.includes('laptop')) return 'laptop'
  if (lc.includes('desktop')) return 'desktop'
  if (lc.includes('monitor')) return 'monitor'
  if (lc.includes('peripheral') || lc.includes('keyboard') || lc.includes('mouse')) return 'peripheral'
  if (lc.includes('server')) return 'server'
  return 'other'
}

function hwCategoryFromSpec(specCategory: string | undefined): string | null {
  if (!specCategory) return null
  switch (specCategory) {
    case 'laptop': return HW_CATS.find(c => c.toLowerCase().includes('laptop') || c.toLowerCase().includes('notebook')) ?? HW_CATS[0]
    case 'desktop': return HW_CATS.find(c => c.toLowerCase().includes('desktop')) ?? HW_CATS[0]
    case 'monitor': return HW_CATS.find(c => c.toLowerCase().includes('monitor')) ?? HW_CATS[0]
    case 'peripheral': return HW_CATS.find(c => c.toLowerCase().includes('peripheral')) ?? HW_CATS[0]
    case 'server': return HW_CATS.find(c => c.toLowerCase().includes('server')) ?? HW_CATS[0]
    default: return HW_CATS[HW_CATS.length - 1]
  }
}

function hwPurposeToSpec(uiPurpose: string): string {
  const lc = uiPurpose.toLowerCase()
  if (lc.includes('new') || lc.includes('hire')) return 'new_hire'
  if (lc.includes('replac')) return 'replacement'
  if (lc.includes('addition')) return 'additional'
  if (lc.includes('project')) return 'project'
  return 'other'
}

function hwPurposeFromSpec(specPurpose: string | undefined): string | null {
  if (!specPurpose) return null
  switch (specPurpose) {
    case 'new_hire': return PURPOSES.find(p => p.toLowerCase().includes('new')) ?? PURPOSES[0]
    case 'replacement': return PURPOSES.find(p => p.toLowerCase().includes('replac')) ?? PURPOSES[0]
    case 'additional': return PURPOSES.find(p => p.toLowerCase().includes('addition')) ?? PURPOSES[0]
    case 'project': return PURPOSES.find(p => p.toLowerCase().includes('project')) ?? PURPOSES[0]
    default: return PURPOSES[PURPOSES.length - 1]
  }
}
