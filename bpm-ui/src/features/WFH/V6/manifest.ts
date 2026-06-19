import type { FormManifest } from '@/features/registry'
import WFH_V6_BpmnXml from './WFH_V6.bpmn.xml?raw'
import { WFH_V6_CaseDetail } from './WFH_V6_CaseDetail'
import { WFH_V6_WfhForm } from './WFH_V6_WfhForm'

const manifest: FormManifest = {
  code: 'WFH',
  version: 6,
  component: WFH_V6_WfhForm,
  detailComponent: WFH_V6_CaseDetail,
  bpmnXml: WFH_V6_BpmnXml,
}

export default manifest
