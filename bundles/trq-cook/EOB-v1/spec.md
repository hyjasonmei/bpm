# Employee Onboarding 新進員工登入 (EOB v1)

## Flow nodes
- s [startEvent]: 招聘意願發生
- req [userTask]: 申請單 Employee Request
- ap [approval]: 簽核流程 Approval
- su [userTask]: 基本設定 Employee Setup
- e [endEvent]: 結案

## User tasks
### req
- submitter: self
- fields: firstName, lastName, businessTitle, employeeLocation, onboardDate, requireMailbox, costCenter, contractNumber, contractEffectiveDate, contractExpirationDate

### su
- submitter: self
- fields: 

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

