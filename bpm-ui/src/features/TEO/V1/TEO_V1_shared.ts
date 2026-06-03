import type { TEO_V1_ExpenseItemDto, TEO_V1_Status } from './TEO_V1_types'

export function emptyItem(): TEO_V1_ExpenseItemDto {
  return { date: '', country: '', category: 'Hotel', description: '', amount: '', amountLcy: '' }
}

export const CATEGORY_OPTIONS = ['Airfare', 'Hotel', 'Airport tax/Taxi/Bus', 'Meal', 'Other']

export function zhStatus(s: TEO_V1_Status): string {
  switch (s) {
    case 'PendingManager':   return '待主管核准'
    case 'PendingFinance':   return '待財務核准'
    case 'ResubmitRequired': return '退回補件'
    case 'Completed':        return '已核准'
    case 'Cancelled':        return '已撤回'
  }
}
