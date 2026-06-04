# Cash in Advance 預支現金 (APE v1)

## Flow nodes
- s [startEvent]: 預支需求
- ape [userTask]: 預支申請單 Advance Payment
- ap [approval]: 簽核流程 Approval
- e [endEvent]: 預支現金 Cash in Advance

## User tasks
### ape
- submitter: self
- fields: expectReceiveDate, deductReturnDate, chargeDepartment, rechargeOutside, description, amount, currency

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

