import type { FormManifest } from '@/features/registry'
import TRQ_V1_BpmnXml from './TRQ_V1.bpmn.xml?raw'
import { TRQ_V1_CaseDetail } from './TRQ_V1_CaseDetail'
import { TRQ_V1_TravelRequestForm } from './TRQ_V1_TravelRequestForm'

const manifest: FormManifest = {
  code: 'TRQ',
  version: 1,
  component: TRQ_V1_TravelRequestForm,
  detailComponent: TRQ_V1_CaseDetail,
  bpmnXml: TRQ_V1_BpmnXml,
}

export default manifest
