# 採購申請流程 (PURCHASE_REQUEST v1)

## Flow nodes
- start_1 [startEvent]: 開始
- task_fill [userTask]: 填寫採購申請
- approval_supervisor [approval]: 主管審核
- gateway_supervisor [gateway]: 主管審核結果
- approval_procurement [approval]: 採購審定
- gateway_procurement [gateway]: 採購審定結果
- approval_sign [approval]: 簽核
- end_1 [endEvent]: 結束

## User tasks
### task_fill
- submitter: self
- fields: vendor

## Decisions / Gateways
- gateway_supervisor (exclusive)
  - edge e5: `rejected`
  - edge e4: `approved`
- gateway_procurement (exclusive)
  - edge e8: `rejected`
  - edge e7: `approved`

## Approvers
- approval_supervisor: expr: submitter.department.head
- approval_procurement: principal
- approval_sign: expr: submitter.department.head

## Notifications
- notify_qkje (?)
- notify_qs19 (?)

## SLA

## Actors used
- expr:submitter.department.head
- self
- submitter

