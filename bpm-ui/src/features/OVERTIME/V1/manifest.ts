import type { FormManifest } from '@/features/registry'
import OVERTIME_V1_BpmnXml from './OVERTIME_V1.bpmn.xml?raw'
import { OVERTIME_V1_CaseDetail } from './OVERTIME_V1_CaseDetail'
import { OVERTIME_V1_OvertimeForm } from './OVERTIME_V1_OvertimeForm'

const manifest: FormManifest = {
  code: 'OVERTIME',
  version: 1,
  component: OVERTIME_V1_OvertimeForm,
  detailComponent: OVERTIME_V1_CaseDetail,
  bpmnXml: OVERTIME_V1_BpmnXml,
}

export default manifest
