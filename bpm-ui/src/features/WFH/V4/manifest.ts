import type { FormManifest } from '@/features/registry'
import WFH_V4_BpmnXml from './WFH_V4.bpmn.xml?raw'
import { WFH_V4_CaseDetail } from './WFH_V4_CaseDetail'
import { WFH_V4_WfhForm } from './WFH_V4_WfhForm'

const manifest: FormManifest = {
  code: 'WFH',
  version: 4,
  component: WFH_V4_WfhForm,
  detailComponent: WFH_V4_CaseDetail,
  bpmnXml: WFH_V4_BpmnXml,
}

export default manifest
