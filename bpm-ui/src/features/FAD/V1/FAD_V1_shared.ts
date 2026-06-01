import type { FAD_V1_Status } from './FAD_V1_types'

export const REASON_OPTIONS = ['損壞報廢 Damaged', '老舊報廢 Obsolete', '遺失報廢 Lost']
export const HANDLING_OPTIONS = ['零件轉備品 To spare parts', '失效 Scrap']

export function zhStatus(s: FAD_V1_Status): string {
  switch (s) {
    case 'PendingManager':   return '待判別'
    case 'PendingConfirm':   return '待領收確認'
    case 'ResubmitRequired': return '退回補件'
    case 'Completed':        return '已處份'
  }
}
