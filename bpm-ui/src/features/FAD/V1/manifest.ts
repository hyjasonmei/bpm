import type { FormManifest } from '@/features/registry'
import FAD_V1_BpmnXml from './FAD_V1.bpmn.xml?raw'
import { FAD_V1_CaseDetail } from './FAD_V1_CaseDetail'
import { FAD_V1_DisposalForm } from './FAD_V1_DisposalForm'

const manifest: FormManifest = {
  code: 'FAD',
  version: 1,
  component: FAD_V1_DisposalForm,
  detailComponent: FAD_V1_CaseDetail,
  bpmnXml: FAD_V1_BpmnXml,
}

export default manifest
