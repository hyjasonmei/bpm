import { useState } from 'react'
import { DatasetSelect } from '@/components/ui/DatasetSelect'
import { Field } from '@/components/ui/form'

/**
 * Dev-only proof of the custom-datasets loop: a cascading 縣市 → 行政區 pair
 * bound to the seeded `tw-regions` dataset. Changing the dataset in admin-ui is
 * reflected here on next load, with no flow re-cook.
 */
export default function DatasetDemo() {
  const [city, setCity] = useState('')
  const [cityLabel, setCityLabel] = useState('')
  const [district, setDistrict] = useState('')
  const [districtLabel, setDistrictLabel] = useState('')

  return (
    <div className="mx-auto max-w-md space-y-4 p-6">
      <h1 className="text-lg font-semibold text-ink">Dataset demo — 縣市 → 行政區</h1>
      <Field label="縣市">
        <DatasetSelect
          binding={{ datasetKey: 'tw-regions', valueColumn: 'city', distinct: true }}
          value={city}
          onChange={(v, l) => { setCity(v); setCityLabel(l); setDistrict(''); setDistrictLabel('') }}
        />
      </Field>
      <Field label="行政區">
        <DatasetSelect
          binding={{ datasetKey: 'tw-regions', valueColumn: 'district', filterByColumn: 'city' }}
          parentValue={city}
          value={district}
          onChange={(v, l) => { setDistrict(v); setDistrictLabel(l) }}
        />
      </Field>
      <pre className="rounded bg-slate-50 p-3 text-xs text-ink-muted">
        {JSON.stringify({ city, cityLabel, district, districtLabel }, null, 2)}
      </pre>
    </div>
  )
}
