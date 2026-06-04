# Travel Request 差旅申請 (TRQ v1)

## Flow nodes
- s [startEvent]: 出差意象
- req [userTask]: 申請單 Travel Request
- ap [approval]: 簽核流程 Approval
- e [endEvent]: 結案

## User tasks
### req
- submitter: self
- fields: travelType, departureCity, destinationCity, departDate, returnDate, chargeTo, travelPurpose, passportName, seatPreference, pickupRequired

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

