## ADDED Requirements

### Requirement: ProcessRuntime emits webhook events at every state transition

The runtime SHALL invoke `IWebhookService.EnqueueAsync` for every state event. Mapping:

- InstanceStarted → `instance.started`
- InstanceCompleted → `instance.completed`
- InstanceCancelled → `instance.cancelled`
- TaskSpawned → `task.spawned`
- TaskCompleted → `task.completed`
- ApprovalApproved → `approval.approved`
- ApprovalRejected → `approval.rejected`

The enqueue happens in the same DB transaction as the state mutation. Channel adapter (HTTP POST) runs asynchronously via worker.

#### Scenario: Instance complete enqueues delivery

- **GIVEN** subscription S1 has filter `{trigger: "instance.completed"}`
- **WHEN** an instance reaches Completed status
- **THEN** WebhookDelivery row inserted for S1 with event_type = "instance.completed", payload includes instance details

#### Scenario: Subscription with no matching filter skipped

- **GIVEN** subscription S2 has filter `{trigger: "approval.approved"}` only
- **WHEN** an instance starts (event_type = "instance.started")
- **THEN** no Delivery row created for S2

### Requirement: Webhook payloads include relevant runtime context

Each webhook payload SHALL include:

- `event_id` (ULID, unique per event)
- `event_type` (matches above mapping)
- `tenant_id`
- `timestamp`
- `data` block — event-specific (instance details, task details, etc.)

For events tied to an instance, `data.instance_id`, `data.spec_code`, `data.spec_version`, `data.initiator_user_id`, `data.initiator_email`, and `data.form_data` SHALL be present. Form data SHALL be truncated to 100 KB if larger; truncation is indicated by `data._truncated: true`.

#### Scenario: Compact instance.completed payload

- **WHEN** an instance completes
- **THEN** the payload includes the standard envelope + data block with the listed fields

#### Scenario: Large form data truncated

- **GIVEN** an instance whose form_data serializes to 200 KB
- **WHEN** the webhook fires
- **THEN** the payload's data.form_data is omitted; data._truncated is true; data.data_url contains the URL to fetch full state via API
