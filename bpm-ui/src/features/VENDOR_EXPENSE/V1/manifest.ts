import type { FormManifest } from '@/features/registry'
import VENDOR_EXPENSE_V1_BpmnXml from './VENDOR_EXPENSE_V1.bpmn.xml?raw'
import { VENDOR_EXPENSE_V1_CaseDetail } from './VENDOR_EXPENSE_V1_CaseDetail'
import { VENDOR_EXPENSE_V1_VendorExpenseForm } from './VENDOR_EXPENSE_V1_VendorExpenseForm'

const manifest: FormManifest = {
  code: 'VENDOR_EXPENSE',
  version: 1,
  component: VENDOR_EXPENSE_V1_VendorExpenseForm,
  detailComponent: VENDOR_EXPENSE_V1_CaseDetail,
  bpmnXml: VENDOR_EXPENSE_V1_BpmnXml,
}

export default manifest
