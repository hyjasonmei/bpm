import type { EOB_V1_SetupTaskDto, EOB_V1_Status } from './EOB_V1_types'

export const LOCATION_OPTIONS = ['APAC', 'EMEA', 'AMERICAS']
export const YESNO_OPTIONS = ['Yes', 'No']
export const TASK_STATUS_OPTIONS = ['Pending', 'Complete']

/** Default onboarding checklist (門禁卡 / 帳號 / 資產 / 座位 / Mentor). */
export function defaultSetupTasks(): EOB_V1_SetupTaskDto[] {
  return [
    { task: '員工證 / 門禁卡', status: 'Pending' },
    { task: '系統帳號開立', status: 'Pending' },
    { task: '固定資產請求', status: 'Pending' },
    { task: '座位安排', status: 'Pending' },
    { task: 'Mentor Assignment', status: 'Pending' },
  ]
}

export function emptyTask(): EOB_V1_SetupTaskDto {
  return { task: '', status: 'Pending' }
}

export function zhStatus(s: EOB_V1_Status): string {
  switch (s) {
    case 'PendingManager':   return '待主管核准'
    case 'PendingSetup':     return '待基本設定'
    case 'ResubmitRequired': return '退回補件'
    case 'Completed':        return '已完成'
  }
}
