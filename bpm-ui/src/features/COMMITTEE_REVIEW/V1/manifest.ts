import type { FormManifest } from '@/features/registry'
import COMMITTEE_REVIEW_V1_BpmnXml from './COMMITTEE_REVIEW_V1.bpmn.xml?raw'
import { COMMITTEE_REVIEW_V1_CaseDetail } from './COMMITTEE_REVIEW_V1_CaseDetail'
import { COMMITTEE_REVIEW_V1_Form } from './COMMITTEE_REVIEW_V1_Form'

const manifest: FormManifest = {
  code: 'COMMITTEE_REVIEW',
  version: 1,
  component: COMMITTEE_REVIEW_V1_Form,
  detailComponent: COMMITTEE_REVIEW_V1_CaseDetail,
  bpmnXml: COMMITTEE_REVIEW_V1_BpmnXml,
}

export default manifest
