import type { FormManifest } from '@/features/registry'
import TEO_V1_BpmnXml from './TEO_V1.bpmn.xml?raw'
import { TEO_V1_CaseDetail } from './TEO_V1_CaseDetail'
import { TEO_V1_TravelExpenseForm } from './TEO_V1_TravelExpenseForm'

const manifest: FormManifest = {
  code: 'TEO',
  version: 1,
  component: TEO_V1_TravelExpenseForm,
  detailComponent: TEO_V1_CaseDetail,
  bpmnXml: TEO_V1_BpmnXml,
}

export default manifest
