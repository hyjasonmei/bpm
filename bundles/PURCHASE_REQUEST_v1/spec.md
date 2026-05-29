# 採購申請流程 (PURCHASE_REQUEST v1)

## Flow nodes
- start_1 [startEvent]: 開始
- task_apply [userTask]: 填寫採購申請
- approval_dept_head [approval]: 部門主管審核
- gateway_dept [gateway]: 主管審核結果
- approval_finance [approval]: 財務審核
- gateway_finance [gateway]: 財務審核結果
- end_1 [endEvent]: 結束

## User tasks
### task_apply
- submitter: self
- fields: field_631m

## Decisions / Gateways
- gateway_dept (exclusive)
  - edge e_gw_dept_reject: `approved == false`
  - edge e_gw_dept_approve: `approved == true`
- gateway_finance (exclusive)
  - edge e_gw_finance_reject: `approved == false`
  - edge e_gw_finance_approve: `approved == true`

## Approvers
- approval_dept_head: expr: submitter.department.head
- approval_finance: principal

## Notifications
- notify_ckqd (?)
- notify_d3c1 (?)
- notify_crme (?)

## SLA

## Actors used
- current_approver
- expr:submitter.department.head
- self
- submitter

