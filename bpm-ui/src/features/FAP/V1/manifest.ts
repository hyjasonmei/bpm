import type { FormManifest } from '@/features/registry'
import FAP_V1_BpmnXml from './FAP_V1.bpmn.xml?raw'
import { FAP_V1_CaseDetail } from './FAP_V1_CaseDetail'
import { FAP_V1_PurchaseForm } from './FAP_V1_PurchaseForm'

const manifest: FormManifest = {
  code: 'FAP',
  version: 1,
  component: FAP_V1_PurchaseForm,
  detailComponent: FAP_V1_CaseDetail,
  bpmnXml: FAP_V1_BpmnXml,
}

export default manifest
