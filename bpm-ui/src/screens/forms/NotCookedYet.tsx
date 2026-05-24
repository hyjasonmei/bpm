import { ChefHat, Home } from 'lucide-react'
import { Link } from 'react-router-dom'

import type { FormCode } from '@/lib/workflow'
import { FORMS } from '@/lib/workflow'

export function NotCookedYet({ code }: { code: FormCode }) {
  const def = FORMS[code]
  return (
    <div className="mx-auto flex max-w-xl flex-col items-center gap-4 px-6 py-16 text-center">
      <div className="flex h-14 w-14 items-center justify-center rounded-full bg-amber-100 text-amber-700">
        <ChefHat className="h-7 w-7" />
      </div>
      <h2 className="text-lg font-semibold text-ink">
        {def?.zhLabel ?? code} 還沒煮好
      </h2>
      <p className="max-w-md text-sm text-ink-muted">
        這個流程的 spec 在 admin wizard 重新設計中。chef（codegen agent）
        產出對應 React component 之前，這裡先閉店。
      </p>
      <Link
        to="/"
        className="mt-4 inline-flex items-center gap-1.5 rounded border border-rule bg-card px-3 py-1.5 text-xs font-medium text-ink-muted transition-colors hover:border-primary hover:text-primary"
      >
        <Home className="h-3.5 w-3.5" />
        回首頁
      </Link>
    </div>
  )
}
