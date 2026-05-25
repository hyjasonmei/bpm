import type { FormManifest } from '@/features/registry'
import LEAVE_V1_BpmnXml from './LEAVE_V1.bpmn.xml?raw'
import { LEAVE_V1_CaseDetail } from './LEAVE_V1_CaseDetail'
import { LEAVE_V1_LeaveForm } from './LEAVE_V1_LeaveForm'

const manifest: FormManifest = {
  code: 'LEAVE',
  version: 1,
  component: LEAVE_V1_LeaveForm,
  detailComponent: LEAVE_V1_CaseDetail,
  bpmnXml: LEAVE_V1_BpmnXml,
}

export default manifest
