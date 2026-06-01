import type { APE_V1_Status, APE_V1_SubmitPayload } from './APE_V1_types'

export function emptyPayload(): APE_V1_SubmitPayload {
  return {
    expectReceiveDate: '',
    deductReturnDate: '',
    chargeDepartment: '',
    rechargeOutside: 'No',
    description: '',
    amount: '',
    currency: 'NTD',
  }
}

export const CURRENCY_OPTIONS = ['NTD', 'USD']
export const YESNO_OPTIONS = ['Yes', 'No']

export function zhStatus(s: APE_V1_Status): string {
  switch (s) {
    case 'PendingManager':   return '待主管核准'
    case 'ResubmitRequired': return '退回補件'
    case 'Completed':        return '已核准'
  }
}
