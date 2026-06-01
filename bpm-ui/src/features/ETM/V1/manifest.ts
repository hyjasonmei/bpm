import type { FormManifest } from '@/features/registry'
import ETM_V1_BpmnXml from './ETM_V1.bpmn.xml?raw'
import { ETM_V1_CaseDetail } from './ETM_V1_CaseDetail'
import { ETM_V1_TerminationForm } from './ETM_V1_TerminationForm'

const manifest: FormManifest = {
  code: 'ETM',
  version: 1,
  component: ETM_V1_TerminationForm,
  detailComponent: ETM_V1_CaseDetail,
  bpmnXml: ETM_V1_BpmnXml,
}

export default manifest
