import { apiFetch } from '@/lib/apiFetch'

export interface DatasetBinding {
  datasetKey: string
  valueColumn: string
  labelColumn?: string
  filterByColumn?: string
  distinct?: boolean
  groupByColumn?: string
  sortByColumn?: string
}

export interface DatasetOption { value: string; label: string; group?: string | null }

/** Resolve a form field's dataset binding (+ optional cascading parent value)
 *  into dropdown options via the bpm-svc resolver. */
export async function resolveDataset(binding: DatasetBinding, parentValue?: string): Promise<DatasetOption[]> {
  const res = await apiFetch('/api/datasets/resolve', {
    method: 'POST',
    headers: { 'Content-Type': 'application/json' },
    body: JSON.stringify({
      datasetKey: binding.datasetKey,
      valueColumn: binding.valueColumn,
      labelColumn: binding.labelColumn ?? null,
      filterColumn: binding.filterByColumn ?? null,
      filterValue: parentValue ?? null,
      distinct: binding.distinct ?? false,
      groupByColumn: binding.groupByColumn ?? null,
      sortByColumn: binding.sortByColumn ?? null,
    }),
  })
  if (!res.ok) return []
  return (await res.json()) as DatasetOption[]
}
