import type { VENDOR_EXPENSE_V1_InvoiceDto, VENDOR_EXPENSE_V1_Status } from './VENDOR_EXPENSE_V1_types'

/** Blank invoice line used when the user clicks "新增 invoice". */
export function emptyInvoice(): VENDOR_EXPENSE_V1_InvoiceDto {
  return {
    invoiceDate: '',
    invoiceNo: '',
    chargeTo: '',
    project: '',
    category: '',
    amount: '',
    currency: '',
    description: '',
  }
}

/** Localised display label for a backend status string. */
export function zhStatus(s: VENDOR_EXPENSE_V1_Status): string {
  switch (s) {
    case 'PendingSupervisor':  return '待主管審核'
    case 'PendingProcurement': return '待採購審定'
    case 'PendingSign':        return '待簽核'
    case 'ResubmitRequired':   return '退回重填'
    case 'Completed':          return '已核准'
    case 'Cancelled':          return '已撤回'
  }
}
