# 請假 (LEAVE v1)

## Flow nodes
- start_1 [startEvent]: 開始
- task_apply [userTask]: 員工申請
- approval_manager [approval]: 主管核准
- gateway_days [gateway]: 超過 7 天？
- approval_vp [approval]: CEO核准
- task_hr_archive [userTask]: HR 備案
- end_1 [endEvent]: 完成

## User tasks
### task_apply
- submitter: self
- fields: leave_type, date_range, days, reason, cert

### task_hr_archive
- submitter: role:HR
- fields: archive_note

## Decisions / Gateways
- gateway_days (exclusive)
  - edge e4: `days >= 7`
  - edge e5: `days < 7`

## Approvers
- approval_vp: expr: submitter.department.head
- approval_manager: expr: submitter.manager

## Notifications
- notify_complete (on_complete)
- notify_assign_manager (on_assign)

## SLA
- approval_manager: 8h
- approval_vp: 24h

## Actors used
- current_approver
- expr:submitter.department.head
- expr:submitter.manager
- manager
- role:HR
- self
- submitter

