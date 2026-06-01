import type { ETM_V1_ReturnItemDto, ETM_V1_Status } from './ETM_V1_types'

export const REASON_OPTIONS = ['Resignation', 'Retirement', 'Termination']
export const YESNO_OPTIONS = ['Yes', 'No']
export const ACTION_OPTIONS = ['Transfer', 'Disable', 'Return']
export const ITEM_STATUS_OPTIONS = ['Pending', 'Complete']

export function defaultReturnItems(): ETM_V1_ReturnItemDto[] {
  return [
    { assetType: '員工證 / 門禁卡', action: 'Return', status: 'Pending' },
    { assetType: '公司資產 (Laptop 等)', action: 'Return', status: 'Pending' },
    { assetType: '系統帳號', action: 'Disable', status: 'Pending' },
  ]
}

export function emptyItem(): ETM_V1_ReturnItemDto {
  return { assetType: '', action: 'Return', status: 'Pending' }
}

export function zhStatus(s: ETM_V1_Status): string {
  switch (s) {
    case 'PendingManager':   return '待主管核准'
    case 'PendingHandover':  return '待交接'
    case 'ResubmitRequired': return '退回補件'
    case 'Completed':        return '已完成'
  }
}
