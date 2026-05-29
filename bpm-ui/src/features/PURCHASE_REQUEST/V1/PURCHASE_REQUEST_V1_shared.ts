import type { PURCHASE_REQUEST_V1_InvoiceDto, PURCHASE_REQUEST_V1_Status } from './PURCHASE_REQUEST_V1_types'

/** Blank invoice line used when the user clicks "Add invoice". */
export function emptyInvoice(): PURCHASE_REQUEST_V1_InvoiceDto {
  return {
    invoiceDate: '',
    invoiceNo: '',
    chargeTo: '',
    project: '',
    category: '',
    amount: '',
    currency: '',
    attachmentFileId: null,
    description: '',
  }
}

/** Localised display label for a backend status string. */
export function zhStatus(s: PURCHASE_REQUEST_V1_Status): string {
  switch (s) {
    case 'PendingDeptHead':   return '待主管核准'
    case 'PendingFinance':    return '待財務核准'
    case 'ResubmitRequired':  return '退回補件'
    case 'Completed':         return '已核准'
  }
}
