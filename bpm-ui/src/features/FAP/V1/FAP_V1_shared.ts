import type { FAP_V1_PurchaseItemDto, FAP_V1_Status } from './FAP_V1_types'

export function emptyItem(): FAP_V1_PurchaseItemDto {
  return { category: 'Hardware', itemSpec: '', qty: 1 }
}

export const CATEGORY_OPTIONS = ['Hardware', 'Software']
export const PURPOSE_OPTIONS = ['New', 'Replacement', 'Project']
export const RECEIVED_OPTIONS = ['Received', 'Rejected']

export function zhStatus(s: FAP_V1_Status): string {
  switch (s) {
    case 'PendingManager':      return '待主管核准'
    case 'PendingVerification': return '待驗收'
    case 'ResubmitRequired':    return '退回補件'
    case 'Completed':           return '已入帳'
    case 'Cancelled':           return '已撤回'
  }
}
