# Fixed Asset Purchase 固定資產採購 (FAP v1)

## Flow nodes
- s [startEvent]: 資產外購
- pr [userTask]: 請購單 Purchase Request
- ap [approval]: 簽核流程 Approval
- po [serviceTask]: 採購單 Issue PO
- vf [userTask]: 驗收 Verification
- e [endEvent]: 入資產清冊 Book List

## User tasks
### pr
- submitter: self
- fields: shippingLocation, chargeTo, purpose, expectedDate, note

### vf
- submitter: self
- fields: received, remark

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

