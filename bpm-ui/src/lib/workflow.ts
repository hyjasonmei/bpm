/**
 * FORMS = display metadata, NOT a registry gate.
 *
 * What lives here: Chinese / English label, persona ownership per
 * step, and step-label / step-id mapping used by inbox rows + the
 * activity feed to render a flow's row regardless of whether chef
 * has cooked the form component yet.
 *
 * What does NOT live here: which flows are available to launch. That
 * is decided by `features/registry.ts`, which globs the chef-shipped
 * `manifest.ts` files. Adding a key to this map does NOT make the
 * flow appear in Quick Actions or in the form dispatch path — only
 * shipping a manifest does.
 *
 * Why both: registry tells the runtime what to render; FORMS tells
 * the UI what to call it. Inbox / activity rows store a `code` string
 * and need a static label lookup even before chef has shipped that
 * flow's component.
 */
import type { PersonaCode } from './role'

export interface Step {
  id: string
  en: string
  zh?: string
}

export interface FormDef {
  code: FormCode
  label: string
  zhLabel: string
  steps: Step[]
  ownerByStep: (PersonaCode | null)[]   // null = no owner (e.g., terminal CLOSE)
  /** mock 'current' position for the demo when this form is opened standalone */
  initialActive: number
}

export type FormCode = 'LEAVE' | 'GEE' | 'GEV' | 'APE' | 'TRQ' | 'TEO' | 'HWP' | 'ITPR' | 'EXTOB' | 'RESIGN' | 'DEPTX' | 'PURCHASE_REQUEST' | 'VENDOR_EXPENSE' | 'FAP' | 'FAD' | 'EOB' | 'ETM' | 'WFH'

const STEP = (id: string, en: string, zh: string): Step => ({ id, en, zh })

export const FORMS: Record<FormCode, FormDef> = {
  LEAVE: {
    code: 'LEAVE',
    label: 'Leave Request',
    zhLabel: '請假申請',
    steps: [
      STEP('apply', 'APPLY', '提出申請'),
      STEP('manager_approve', 'MANAGER APPROVE', '主管簽核'),
      STEP('hr_record', 'HR RECORD', '人資登錄'),
      STEP('closed', 'CLOSED', '結案'),
    ],
    ownerByStep: ['employee', 'manager', 'hr', null],
    initialActive: 0,
  },
  GEE: {
    code: 'GEE',
    label: 'Employee Expense (GEE)',
    zhLabel: '員工費用申請',
    steps: [
      STEP('apply', 'APPLY', '申請'),
      STEP('approve', 'APPROVE', '主管簽核'),
      STEP('confirm', 'CONFIRM & PRINT', '確認列印'),
      STEP('fin_review', 'FIN REVIEW', '財務審查'),
      STEP('close', 'CLOSE', '結案'),
    ],
    ownerByStep: ['employee', 'manager', 'finance', 'finance', null],
    initialActive: 0,
  },
  GEV: {
    code: 'GEV',
    label: 'Vendor Expense (GEV)',
    zhLabel: '廠商費用申請',
    steps: [
      STEP('apply', 'APPLY', '申請'),
      STEP('approve', 'APPROVE', '主管簽核'),
      STEP('confirm', 'CONFIRM & PRINT', '確認列印'),
      STEP('fin_review', 'FIN REVIEW', '財務審查'),
      STEP('close', 'CLOSE', '結案'),
    ],
    ownerByStep: ['employee', 'manager', 'finance', 'finance', null],
    initialActive: 0,
  },
  APE: {
    code: 'APE',
    label: 'Advance Payment (APE)',
    zhLabel: '預支費用申請',
    // APE's cooked state machine is single-gate (manager approve → done); the
    // confirm/fin-review stages were copied from the GEE/GEV reference forms but
    // never implemented, so the stepper now matches the real flow.
    steps: [
      STEP('apply', 'APPLY', '申請'),
      STEP('approve', 'APPROVE', '主管簽核'),
      STEP('close', 'CLOSE', '結案'),
    ],
    ownerByStep: ['employee', 'manager', null],
    initialActive: 0,
  },
  HWP: {
    code: 'HWP',
    label: 'Hardware Purchase (HWP)',
    zhLabel: '硬體採購申請',
    steps: [
      STEP('apply', 'APPLY', '申請'),
      STEP('it_spec', 'IT SPEC REVIEW', 'IT 規格審查'),
      STEP('quote', 'QUOTATION', '比價報價'),
      STEP('confirm', 'CONFIRM & PRINT', '確認列印'),
      STEP('approve', 'APPROVE', '主管簽核'),
      STEP('po', 'PO PROCESSING', '採購單處理'),
      STEP('close', 'CLOSE', '結案'),
    ],
    ownerByStep: ['employee', 'it', 'it', 'manager', 'manager', 'finance', null],
    initialActive: 0,
  },
  ITPR: {
    code: 'ITPR',
    label: 'IT Purchase Request (ITPR)',
    zhLabel: 'IT 軟體 / 服務採購',
    steps: [
      STEP('apply', 'APPLY', '申請'),
      STEP('it_spec', 'IT SPEC REVIEW', 'IT 規格審查'),
      STEP('quote', 'QUOTATION', '比價報價'),
      STEP('confirm', 'CONFIRM & PRINT', '確認列印'),
      STEP('approve', 'APPROVE', '主管簽核'),
      STEP('po', 'PO PROCESSING', '採購單處理'),
      STEP('close', 'CLOSE', '結案'),
    ],
    ownerByStep: ['employee', 'it', 'it', 'manager', 'manager', 'finance', null],
    initialActive: 6,
  },
  TRQ: {
    code: 'TRQ',
    label: 'Travel Request (TRQ)',
    zhLabel: '差旅申請',
    // TRQ is single-gate (manager approve → done). The notify-admin stage has no
    // matching step in the cooked state machine (no admin notification is sent).
    steps: [
      STEP('apply', 'APPLY', '申請'),
      STEP('approve', 'APPROVE', '主管簽核'),
      STEP('close', 'CLOSE', '結案'),
    ],
    ownerByStep: ['employee', 'manager', null],
    initialActive: 0,
  },
  TEO: {
    code: 'TEO',
    label: 'Travel Expense (TEO)',
    zhLabel: '差旅費報銷',
    // TEO is manager → finance (fin_review is real); the confirm/print stage was
    // copied from the reference forms but isn't in the cooked state machine.
    steps: [
      STEP('apply', 'APPLY', '申請'),
      STEP('approve', 'APPROVE', '主管簽核'),
      STEP('fin_review', 'FIN REVIEW', '財務審查'),
      STEP('close', 'CLOSE', '結案'),
    ],
    ownerByStep: ['employee', 'manager', 'finance', null],
    initialActive: 0,
  },
  EXTOB: {
    code: 'EXTOB',
    label: 'External Employee Onboarding',
    zhLabel: '外部員工到職',
    steps: [
      STEP('submit', 'SUBMIT', '提交申請'),
      STEP('account', 'CREATING ACCOUNT', '帳號建立'),
      STEP('closed', 'CLOSED', '結案'),
    ],
    ownerByStep: ['manager', 'hr', null],
    initialActive: 2,
  },
  RESIGN: {
    code: 'RESIGN',
    label: 'Resignation',
    zhLabel: '離職申請',
    steps: [
      STEP('apply', 'APPLY', '提出申請'),
      STEP('manager_approve', 'MANAGER APPROVE', '主管簽核'),
      STEP('hr_approve', 'HR APPROVE', '人資簽核'),
      STEP('closed', 'CLOSED', '結案'),
    ],
    ownerByStep: ['employee', 'manager', 'hr', null],
    initialActive: 0,
  },
  DEPTX: {
    code: 'DEPTX',
    label: 'Department Transfer',
    zhLabel: '部門異動申請',
    steps: [
      STEP('apply', 'APPLY', '提出申請'),
      STEP('manager_approve', 'MANAGER APPROVE', '主管簽核'),
      STEP('hr_approve', 'HR APPROVE', '人資簽核'),
      STEP('closed', 'CLOSED', '結案'),
    ],
    ownerByStep: ['employee', 'manager', 'hr', null],
    initialActive: 0,
  },
  PURCHASE_REQUEST: {
    code: 'PURCHASE_REQUEST',
    label: 'Purchase Request',
    zhLabel: '採購申請',
    steps: [
      STEP('apply', 'APPLY', '填寫申請'),
      STEP('dept_head', 'DEPT HEAD APPROVE', '部門主管核准'),
      STEP('finance', 'FINANCE APPROVE', '財務核准'),
      STEP('closed', 'CLOSED', '結案'),
    ],
    ownerByStep: ['employee', 'manager', 'finance', null],
    initialActive: 0,
  },
  VENDOR_EXPENSE: {
    code: 'VENDOR_EXPENSE',
    label: 'Vendor Expense / Purchase',
    zhLabel: '採購申請',
    steps: [
      STEP('apply', 'APPLY', '填寫採購申請'),
      STEP('supervisor', 'SUPERVISOR', '主管審核'),
      STEP('procurement', 'PROCUREMENT', '採購審定'),
      STEP('sign', 'SIGN', '簽核'),
      STEP('closed', 'CLOSED', '結案'),
    ],
    ownerByStep: ['employee', 'manager', 'finance', 'manager', null],
    initialActive: 0,
  },
  FAP: {
    code: 'FAP',
    label: 'Fixed Asset Purchase',
    zhLabel: '固定資產採購',
    steps: [
      STEP('apply', 'APPLY', '請購單'),
      STEP('approve', 'APPROVE', '主管簽核'),
      STEP('po', 'PO', '採購單成立'),
      STEP('verify', 'VERIFICATION', '驗收'),
      STEP('closed', 'CLOSED', '入帳'),
    ],
    ownerByStep: ['employee', 'manager', null, 'employee', null],
    initialActive: 0,
  },
  FAD: {
    code: 'FAD',
    label: 'Fixed Asset Disposal',
    zhLabel: '固定資產處分',
    steps: [
      STEP('apply', 'APPLY', '處份申請單'),
      STEP('judge', 'MANAGER', '主管判別'),
      STEP('confirm', 'CONFIRMED', '領收確認'),
      STEP('closed', 'CLOSED', '處份'),
    ],
    ownerByStep: ['employee', 'manager', 'employee', null],
    initialActive: 0,
  },
  EOB: {
    code: 'EOB',
    label: 'Employee Onboarding',
    zhLabel: '新進員工登入',
    steps: [
      STEP('apply', 'APPLY', '申請單'),
      STEP('approve', 'APPROVE', '主管簽核'),
      STEP('setup', 'SETUP', '基本設定'),
      STEP('closed', 'CLOSED', '結案'),
    ],
    ownerByStep: ['employee', 'manager', 'employee', null],
    initialActive: 0,
  },
  ETM: {
    code: 'ETM',
    label: 'Employee Termination',
    zhLabel: '員工離職',
    steps: [
      STEP('apply', 'APPLY', '申請單'),
      STEP('approve', 'APPROVE', '主管簽核'),
      STEP('handover', 'HANDOVER', '交接'),
      STEP('closed', 'CLOSED', '結案'),
    ],
    ownerByStep: ['employee', 'manager', 'employee', null],
    initialActive: 0,
  },
  WFH: {
    code: 'WFH',
    label: 'Work From Home',
    zhLabel: '居家辦公申請',
    // Cooked state machine: apply → manager approve → (gateway days>7) senior
    // approve → close. The senior step only runs when consecutive days > 7;
    // ≤ 7-day cases complete at the manager step (stepper skips to CLOSE).
    steps: [
      STEP('apply', 'APPLY', '提出申請'),
      STEP('manager_approve', 'MANAGER APPROVE', '主管核准'),
      STEP('senior_approve', 'SENIOR APPROVE', '上級主管核准'),
      STEP('closed', 'CLOSED', '結案'),
    ],
    ownerByStep: ['employee', 'manager', 'manager', null],
    initialActive: 0,
  },
}

/** Whether the active persona is allowed to act on the case at the given step. */
export function canAct(formCode: FormCode, activeStep: number, persona: PersonaCode): boolean {
  if (persona === 'admin') return true
  const owner = FORMS[formCode].ownerByStep[activeStep]
  return owner === persona
}

export function ownerLabel(persona: PersonaCode | null): string {
  if (!persona) return '—'
  return ({
    employee: 'Employee / 員工',
    manager:  'Manager / 主管',
    finance:  'Finance / 財務',
    it:       'IT / 資訊',
    hr:       'HR / 人資',
    admin:    'Admin / 管理員',
  } as const)[persona]
}
