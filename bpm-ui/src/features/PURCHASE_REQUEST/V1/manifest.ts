import type { FormManifest } from '@/features/registry'
import PURCHASE_REQUEST_V1_BpmnXml from './PURCHASE_REQUEST_V1.bpmn.xml?raw'
import { PURCHASE_REQUEST_V1_CaseDetail } from './PURCHASE_REQUEST_V1_CaseDetail'
import { PURCHASE_REQUEST_V1_PurchaseRequestForm } from './PURCHASE_REQUEST_V1_PurchaseRequestForm'

const manifest: FormManifest = {
  code: 'PURCHASE_REQUEST',
  version: 1,
  component: PURCHASE_REQUEST_V1_PurchaseRequestForm,
  detailComponent: PURCHASE_REQUEST_V1_CaseDetail,
  bpmnXml: PURCHASE_REQUEST_V1_BpmnXml,
}

export default manifest
