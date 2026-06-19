import type { FormManifest } from '@/features/registry'
import WFH_V5_BpmnXml from './WFH_V5.bpmn.xml?raw'
import { WFH_V5_CaseDetail } from './WFH_V5_CaseDetail'
import { WFH_V5_WfhForm } from './WFH_V5_WfhForm'

const manifest: FormManifest = {
  code: 'WFH',
  version: 5,
  component: WFH_V5_WfhForm,
  detailComponent: WFH_V5_CaseDetail,
  bpmnXml: WFH_V5_BpmnXml,
}

export default manifest
