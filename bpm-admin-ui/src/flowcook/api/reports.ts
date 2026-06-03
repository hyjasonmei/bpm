import { api } from '@/flowcook/api'

/** Cross-flow report summary aggregated by bpm-svc over every chef-cooked
 *  case table. Routed via the /bpmsvc dev proxy (runtime DB lives on bpm-svc). */
export interface ReportSummary {
  totalCases: number
  thisMonth: number
  completed: number
  inProgress: number
  approvalRate: number          // 0..1
  avgCycleDays: number | null
  byFlow: { flowCode: string; count: number }[]
  byStatus: { bucket: string; count: number }[]
  monthly: { month: string; count: number }[]
}

export const getReportSummary = () => api<ReportSummary>('/bpmsvc/api/reports/summary')
