import type { FormManifest } from '@/features/registry'
import WFH_V3_BpmnXml from './WFH_V3.bpmn.xml?raw'
import { WFH_V3_CaseDetail } from './WFH_V3_CaseDetail'
import { WFH_V3_WfhForm } from './WFH_V3_WfhForm'

const manifest: FormManifest = {
  code: 'WFH',
  version: 3,
  component: WFH_V3_WfhForm,
  detailComponent: WFH_V3_CaseDetail,
  bpmnXml: WFH_V3_BpmnXml,
}

export default manifest
