import type { FormCode } from './workflow'
import type { PersonaCode } from './role'
import type { StatusKind } from '@/components/ui/badge'

export interface UserMock {
  id: string
  name: string
  email: string
  dept: string
  role: PersonaCode
}

export const MOCK_USERS: UserMock[] = [
  { id: 'wilson', name: 'Wilson You (游上毅)',  email: 'wilson@example.com', dept: 'TWT.1746G - Corp IS-SaaS & Digital Business', role: 'employee' },
  { id: 'alice',  name: 'Alice Chen (陳依玲)',  email: 'alice@example.com',  dept: 'TWT.1746G - Corp BAS-CRM',                  role: 'employee' },
  { id: 'bob',    name: 'Bob Lin (林子凡)',     email: 'bob@example.com',    dept: 'TWT.1746G - Corp IS-SaaS & Digital Business', role: 'employee' },
  { id: 'carol',  name: 'Carol Wu (吳佳螢)',    email: 'carol@example.com',  dept: 'GCC.1751G - Finance Operation',             role: 'employee' },
  { id: 'david',  name: 'David Huang (黃大為)', email: 'david@example.com',  dept: 'TWT.1746G - Corp BAS-CRM',                  role: 'employee' },
  { id: 'eve',    name: 'Eve Tsai (蔡雅雯)',    email: 'eve@example.com',    dept: 'TWT.1746G - Taiwan Sales',                  role: 'employee' },
  { id: 'elton',  name: 'Elton Yang (楊旭東)',  email: 'elton@example.com',  dept: 'TWT.1746G - Corp IS-SaaS & Digital Business', role: 'manager' },
  { id: 'jean',   name: 'Jean Hsu (許靜怡)',    email: 'jean@example.com',   dept: 'GCC.1751G - Finance Operation',             role: 'finance' },
  { id: 'mark',   name: 'Mark Ng (吳家銘)',     email: 'mark@example.com',   dept: 'TWT.1746G - Corp IS-Infrastructure',        role: 'it' },
  { id: 'amy',    name: 'Amy Lin (林宛靜)',     email: 'amy@example.com',    dept: 'GCC.1700G - Human Resources',               role: 'hr' },
]

export const DEPARTMENTS = [
  'TWT.1746G - Corp IS-SaaS & Digital Business',
  'TWT.1746G - Corp BAS-CRM',
  'TWT.1746G - Corp IS-Infrastructure',
  'TWT.1746G - Taiwan Sales',
  'GCC.1751G - Finance Operation',
  'GCC.1700G - Human Resources',
]

export const CHARGE_OPTS = [
  'TWT.1746G - Elton Yang',
  'TWT.1746G - Wilson You',
  'GCC.1751G - Jean Hsu',
  'TWT.1746G - Corp IS-SaaS',
]

export const PROJECT_OPTS = [
  'ISP002 - .Other Expense for Employee',
  'ISP041 - ERP & Finance Cloud',
  'N/A',
]

export const CURRENCIES = ['NTD', 'USD', 'EUR', 'JPY', 'GBP', 'AED'] as const
export const GEE_CATS = ['Internet Access, ADSL', 'Business Meal', 'Office Supplies', 'Transportation', 'Accommodation', 'Outside Service', 'Other'] as const
export const GEV_CATS = ['Outside service', 'Software maintenance', 'Hardware', 'Office supplies', 'Consulting', 'Other'] as const
export const PAYMENT_TERMS = ['Current month', 'Net 30', 'Net 60', 'Immediate'] as const
export const VAT_RATES = ['0%', '5%', '10%'] as const
export const HW_CATS = ['Laptop', 'Desktop', 'Monitor', 'Tablet', 'Peripheral', 'Other'] as const
export const SHIPPING_LOCS = ['Taipei office', 'Banqiao office', 'Remote - Home', 'Other'] as const
export const PURPOSES = ['Additional - For Finance projects', 'Replacement', 'New headcount', 'Project requirement'] as const

/* ── Cases ───────────────────────────────────────────────── */

export interface CaseMock {
  no: string
  type: FormCode
  typeLabel: string
  status: StatusKind
  requestor: string
  dept: string
  submitted: string
  updated: string
  amount: string                  // pre-formatted for display ('NTD 779' or '—')
  /** Where the case currently sits in its workflow (zero-based) */
  currentStep: number
  /** Owner of the current step (used to decide whose queue this is) */
  currentOwner: PersonaCode | null
  urgent?: boolean
}

const c = (
  no: string, type: FormCode, typeLabel: string, status: StatusKind,
  requestor: string, dept: string, submitted: string, updated: string,
  amount: string, currentStep: number, currentOwner: PersonaCode | null,
): CaseMock => ({ no, type, typeLabel, status, requestor, dept, submitted, updated, amount, currentStep, currentOwner })

export const MOCK_CASES: CaseMock[] = [
  // Pending action — Manager queue (active step needs manager)
  c('TW-LEAVE-26-000044', 'LEAVE', 'Leave Request',     'pending', 'Wilson You',  'Corp IS-SaaS',     '2026/04/24', '2026/04/24', '—',          1, 'manager'),
  c('TW-LEAVE-26-000043', 'LEAVE', 'Leave Request',     'pending', 'Alice Chen',  'Corp IS-SaaS',     '2026/04/23', '2026/04/23', '—',          1, 'manager'),
  c('TW-GEE-26-001342',   'GEE',   'Employee Expense',  'pending', 'Wilson You',  'Corp IS-SaaS',     '2026/04/22', '2026/04/23', 'NTD 779',    1, 'manager'),
  c('TW-GEE-26-001341',   'GEE',   'Employee Expense',  'pending', 'Alice Chen',  'Corp IS-SaaS',     '2026/04/23', '2026/04/23', 'NTD 1,200',  1, 'manager'),
  c('TW-GEV-26-000889',   'GEV',   'Vendor Expense',    'pending', 'Bob Lin',     'Corp IS-SaaS',     '2026/04/21', '2026/04/21', 'NTD 50,000', 1, 'manager'),
  c('TW-TRQ-26-000401',   'TRQ',   'Travel Request',    'pending', 'Carol Wu',    'Finance Operation','2026/04/20', '2026/04/20', '—',          1, 'manager'),
  c('TW-APE-26-000043',   'APE',   'Advance Payment',   'pending', 'David Huang', 'Corp BAS-CRM',     '2026/04/19', '2026/04/19', 'NTD 10,000', 1, 'manager'),
  c('TW-GEE-26-001335',   'GEE',   'Employee Expense',  'pending', 'Frank Kuo',   'Corp IS-SaaS',     '2026/04/17', '2026/04/17', 'NTD 880',    1, 'manager'),
  c('TW-GEV-26-000870',   'GEV',   'Vendor Expense',    'pending', 'Jack Su',     'Taiwan Sales',     '2026/04/13', '2026/04/13', 'NTD 8,200',  1, 'manager'),
  c('TW-GEE-26-001320',   'GEE',   'Employee Expense',  'pending', 'Iris Wang',   'Corp IS-SaaS',     '2026/04/14', '2026/04/14', 'NTD 650',    1, 'manager'),

  // FIN Review — Finance queue
  c('TW-TEO-26-000220',   'TEO',   'Travel Expense',    'fin_review', 'Wilson You', 'Corp IS-SaaS',  '2026/04/01', '2026/04/21', 'NTD 97,249', 3, 'finance'),
  c('TW-TEO-26-000218',   'TEO',   'Travel Expense',    'fin_review', 'Eve Tsai',   'Taiwan Sales',  '2026/04/18', '2026/04/18', 'NTD 45,000', 3, 'finance'),
  c('TW-TEO-26-000215',   'TEO',   'Travel Expense',    'fin_review', 'Leo Yang',   'Corp IS-SaaS',  '2026/04/11', '2026/04/11', 'NTD 62,000', 3, 'finance'),

  // IT Spec Review — IT queue
  c('TW-HWP-26-000077',   'HWP',   'Hardware Purchase', 'it_spec_review', 'Wilson You', 'Corp IS-SaaS', '2026/03/20', '2026/04/01', '—', 1, 'it'),
  c('TW-HWP-26-000076',   'HWP',   'Hardware Purchase', 'it_spec_review', 'Henry Chen', 'Corp BAS-CRM', '2026/04/15', '2026/04/15', '—', 1, 'it'),

  // HR queue (LEAVE in HR record step + onboarding)
  c('TW-LEAVE-26-000040', 'LEAVE', 'Leave Request',     'pending', 'Bob Lin',     'Corp IS-SaaS',     '2026/04/15', '2026/04/19', '—',          2, 'hr'),
  c('TW-EXTOB-26-000019', 'EXTOB', 'External Onboarding', 'pending', 'Wilson You','Corp IS-SaaS',     '2026/04/05', '2026/04/12', '—',          1, 'hr'),

  // Drafts owned by Wilson (employee) — show on his queue / shows up under My Drafts
  c('TW-APE-26-000044',   'APE',   'Advance Payment',   'draft',    'Wilson You', 'Corp IS-SaaS',    '2026/04/08', '2026/04/08', 'NTD 5,000',  0, 'employee'),
  c('TW-LEAVE-26-000041', 'LEAVE', 'Leave Request',     'draft',    'Wilson You', 'Corp IS-SaaS',    '2026/04/22', '2026/04/22', '—',          0, 'employee'),

  // Closed (history)
  c('TW-TRQ-26-000160',   'TRQ',   'Travel Request',    'closed',   'Wilson You', 'Corp IS-SaaS',    '2026/04/10', '2026/04/15', '—',          3, null),
  c('TW-GEE-26-001298',   'GEE',   'Employee Expense',  'closed',   'Wilson You', 'Corp IS-SaaS',    '2026/03/28', '2026/04/05', 'NTD 1,200',  4, null),
  c('TW-GEV-26-000780',   'GEV',   'Vendor Expense',    'closed',   'Wilson You', 'Corp IS-SaaS',    '2026/03/15', '2026/03/22', 'NTD 15,500', 4, null),
  c('TW-TRQ-26-000098',   'TRQ',   'Travel Request',    'closed',   'Wilson You', 'Corp IS-SaaS',    '2026/03/01', '2026/03/10', '—',          3, null),
  c('TW-APE-26-000031',   'APE',   'Advance Payment',   'closed',   'Wilson You', 'Corp IS-SaaS',    '2026/02/10', '2026/02/20', 'NTD 3,000',  4, null),
  c('TW-ITPR-26-000015',  'ITPR',  'IT Purchase Request','closed',  'Wilson You', 'Corp IS-SaaS',    '2026/01/20', '2026/02/05', 'NTD 27,670', 6, null),
  c('TW-LEAVE-26-000035', 'LEAVE', 'Leave Request',     'closed',   'Wilson You', 'Corp IS-SaaS',    '2026/02/14', '2026/02/16', '—',          3, null),

  // Approved (en route)
  c('TW-GEE-26-001100',   'GEE',   'Employee Expense',  'approved', 'Wilson You', 'Corp IS-SaaS',    '2026/02/25', '2026/03/01', 'NTD 650',    2, 'finance'),
  c('TW-GEV-26-000891',   'GEV',   'Vendor Expense',    'approved', 'Wilson You', 'Corp IS-SaaS',    '2026/04/18', '2026/04/20', 'NTD 333',    2, 'finance'),

  // Returned
  c('TW-GEE-26-001010',   'GEE',   'Employee Expense',  'returned', 'Frank Kuo',  'Corp IS-SaaS',    '2026/02/01', '2026/02/03', 'NTD 430',    1, 'manager'),
]

/* ── Leave balances (HR view) ───────────────────────────── */

export const MOCK_LEAVE_BALANCES: Record<string, { annual: number; sick: number; personal: number }> = {
  wilson: { annual: 14, sick: 30, personal: 7 },
  alice:  { annual: 10, sick: 30, personal: 7 },
  bob:    { annual: 12, sick: 30, personal: 7 },
}

/* ── Helpers ────────────────────────────────────────────── */

export function casesPendingForPersona(p: PersonaCode): CaseMock[] {
  if (p === 'admin') return MOCK_CASES.filter(c => c.currentOwner !== null)
  return MOCK_CASES.filter(c => c.currentOwner === p && c.status !== 'closed' && c.status !== 'rejected')
}

export function casesCreatedBy(userId: string): CaseMock[] {
  // we use the requestor display name as a soft match for the demo
  const user = MOCK_USERS.find(u => u.id === userId)
  if (!user) return []
  return MOCK_CASES.filter(c => user.name.startsWith(c.requestor.split(' ')[0]))
}

export function caseByNo(no: string): CaseMock | undefined {
  return MOCK_CASES.find(c => c.no === no)
}
