import type { FormManifest } from '@/features/registry'
import WFH_V1_BpmnXml from './WFH_V1.bpmn.xml?raw'
import { WFH_V1_CaseDetail } from './WFH_V1_CaseDetail'
import { WFH_V1_WfhForm } from './WFH_V1_WfhForm'

const manifest: FormManifest = {
  code: 'WFH',
  version: 1,
  component: WFH_V1_WfhForm,
  detailComponent: WFH_V1_CaseDetail,
  bpmnXml: WFH_V1_BpmnXml,
}

export default manifest
