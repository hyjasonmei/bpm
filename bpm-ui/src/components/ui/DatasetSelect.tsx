import { useEffect, useState } from 'react'
import { Select } from '@/components/ui/form'
import { resolveDataset, type DatasetBinding, type DatasetOption } from '@/lib/api/datasets'

interface Props {
  binding: DatasetBinding
  value: string
  onChange: (value: string, label: string) => void
  parentValue?: string          // cascading: the parent field's selected value
  disabled?: boolean
  placeholder?: string
}

/**
 * A dropdown whose options come from a customer-maintained dataset (resolved by
 * bpm-svc), instead of hardcoded in the form. Supports cascading (re-resolves
 * when `parentValue` changes), optgroup grouping, and returns both value AND
 * label on change so the caller can snapshot the label at submit time.
 */
export function DatasetSelect({ binding, value, onChange, parentValue, disabled, placeholder = '請選擇' }: Props) {
  const [options, setOptions] = useState<DatasetOption[]>([])
  const [loading, setLoading] = useState(false)

  useEffect(() => {
    let live = true
    setLoading(true)
    resolveDataset(binding, parentValue)
      .then(opts => { if (live) setOptions(opts) })
      .finally(() => { if (live) setLoading(false) })
    return () => { live = false }
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [binding.datasetKey, binding.valueColumn, binding.filterByColumn, parentValue])

  // cascading child is disabled until its parent has a value
  const isChildWaiting = Boolean(binding.filterByColumn) && !parentValue
  const labelFor = (v: string) => options.find(o => o.value === v)?.label ?? v

  const grouped = binding.groupByColumn
    ? Array.from(new Set(options.map(o => o.group ?? ''))).map(g => ({
        group: g, items: options.filter(o => (o.group ?? '') === g),
      }))
    : null

  return (
    <Select
      value={value}
      disabled={disabled || isChildWaiting || loading}
      onChange={e => onChange(e.target.value, labelFor(e.target.value))}
    >
      <option value="">{isChildWaiting ? '請先選擇上一層' : loading ? '載入中…' : placeholder}</option>
      {grouped
        ? grouped.map(({ group, items }) => (
            <optgroup key={group || '—'} label={group || '—'}>
              {items.map(o => <option key={o.value} value={o.value}>{o.label}</option>)}
            </optgroup>
          ))
        : options.map(o => <option key={o.value} value={o.value}>{o.label}</option>)}
    </Select>
  )
}
