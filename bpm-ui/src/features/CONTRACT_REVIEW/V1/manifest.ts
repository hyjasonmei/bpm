import type { FormManifest } from '@/features/registry'
import CONTRACT_REVIEW_V1_BpmnXml from './CONTRACT_REVIEW_V1.bpmn.xml?raw'
import { CONTRACT_REVIEW_V1_CaseDetail } from './CONTRACT_REVIEW_V1_CaseDetail'
import { CONTRACT_REVIEW_V1_Form } from './CONTRACT_REVIEW_V1_Form'

const manifest: FormManifest = {
  code: 'CONTRACT_REVIEW',
  version: 1,
  component: CONTRACT_REVIEW_V1_Form,
  detailComponent: CONTRACT_REVIEW_V1_CaseDetail,
  bpmnXml: CONTRACT_REVIEW_V1_BpmnXml,
}

export default manifest
