# Employee Termination 員工離職 (ETM v1)

## Flow nodes
- s [startEvent]: 離職意願發生
- req [userTask]: 申請單 Employee Request
- ap [approval]: 簽核流程 Approval
- ho [userTask]: 交接 / 基本設定 Handover
- e [endEvent]: 結案

## User tasks
### req
- submitter: self
- fields: employeeName, employeeId, lastWorkingDate, reason, provideCertificate

### ho
- submitter: self
- fields: outstandingPayment

## Decisions / Gateways
(none)

## Approvers
- ap: expr: submitter.manager

## Notifications
- n_submit (?)

## SLA

## Actors used
- current_approver
- expr:submitter.manager
- self

