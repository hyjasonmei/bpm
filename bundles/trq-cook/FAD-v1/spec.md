# Fixed Asset Disposal 固定資產處分 (FAD v1)

## Flow nodes
- s [startEvent]: 報廢事件
- dr [userTask]: 處份申請單 Disposal Request
- ap [approval]: 固定資產判別 IT
- cf [userTask]: 領收確認 Confirmed
- e [endEvent]: 處份 Disposal

## User tasks
### dr
- submitter: self
- fields: disposalReason, assetId, assetName, description, photo

### cf
- submitter: self
- fields: handlingResult, remark

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

