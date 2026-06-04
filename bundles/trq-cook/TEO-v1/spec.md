# Travel Expense 差旅費用核銷 (TEO v1)

## Flow nodes
- s [startEvent]: 出差事件發生
- exp [userTask]: 費用單 Travel Expense
- ap [approval]: 簽核流程 Approval
- fin [approval]: 財務審定 Finance Review
- e [endEvent]: 入帳

## User tasks
### exp
- submitter: self
- fields: travelRequestNo

## Decisions / Gateways
(none)

## Approvers
- ap: expr: submitter.manager
- fin: principal

## Notifications
- n_submit (?)

## SLA

## Actors used
- current_approver
- expr:submitter.manager
- self

