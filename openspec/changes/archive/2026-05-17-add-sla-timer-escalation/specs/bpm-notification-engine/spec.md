## ADDED Requirements

### Requirement: NotificationTrigger includes on_sla_warning

The `NotificationTrigger` enum SHALL include `OnSlaWarning` (in addition to the existing `OnSlaBreach`). The dispatcher SHALL fire any notification spec carrying this trigger when the SLA timer detects a 50%-elapsed task.

#### Scenario: 50% threshold fires on_sla_warning

- **GIVEN** spec.notifications contains a notification with `trigger = on_sla_warning`, recipient = `current_assignee`
- **WHEN** the SLA timer detects task T1 reached 50% elapsed
- **THEN** the dispatcher is called for that notification with NotificationContext.CurrentAssigneeUserId = T1.ActualAssigneeUserId

### Requirement: on_sla_breach fires on full breach

The dispatcher SHALL fire notifications matching `trigger = on_sla_breach` when the SLA timer detects 100% elapsed AND the configured escalation action involves notification (which all five actions effectively do, since `notify` is one of them and the others fire informational notifications too).

#### Scenario: Breach fires on_sla_breach

- **GIVEN** spec defines a notification with `trigger = on_sla_breach`, recipient = `expr:submitter.manager.manager`
- **WHEN** task T1 breaches
- **THEN** the dispatcher fires that notification regardless of the escalation action chosen
