import type { FormManifest } from '@/features/registry'
import WFH_V2_BpmnXml from './WFH_V2.bpmn.xml?raw'
import { WFH_V2_CaseDetail } from './WFH_V2_CaseDetail'
import { WFH_V2_WfhForm } from './WFH_V2_WfhForm'

const manifest: FormManifest = {
  code: 'WFH',
  version: 2,
  component: WFH_V2_WfhForm,
  detailComponent: WFH_V2_CaseDetail,
  bpmnXml: WFH_V2_BpmnXml,
}

export default manifest
