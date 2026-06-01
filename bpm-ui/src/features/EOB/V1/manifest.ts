import type { FormManifest } from '@/features/registry'
import EOB_V1_BpmnXml from './EOB_V1.bpmn.xml?raw'
import { EOB_V1_CaseDetail } from './EOB_V1_CaseDetail'
import { EOB_V1_OnboardingForm } from './EOB_V1_OnboardingForm'

const manifest: FormManifest = {
  code: 'EOB',
  version: 1,
  component: EOB_V1_OnboardingForm,
  detailComponent: EOB_V1_CaseDetail,
  bpmnXml: EOB_V1_BpmnXml,
}

export default manifest
