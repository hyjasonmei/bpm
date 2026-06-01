import type { FormManifest } from '@/features/registry'
import APE_V1_BpmnXml from './APE_V1.bpmn.xml?raw'
import { APE_V1_CaseDetail } from './APE_V1_CaseDetail'
import { APE_V1_AdvancePaymentForm } from './APE_V1_AdvancePaymentForm'

const manifest: FormManifest = {
  code: 'APE',
  version: 1,
  component: APE_V1_AdvancePaymentForm,
  detailComponent: APE_V1_CaseDetail,
  bpmnXml: APE_V1_BpmnXml,
}

export default manifest
