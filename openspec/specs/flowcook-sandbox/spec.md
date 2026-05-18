# flowcook-sandbox Specification

## Purpose

Define the **Sandbox** feature of admin (page 3 of five) plus the runtime sandbox-mode hooks that bpm exposes. The Sandbox lets a developer or QA exercise a flow against real bpm runtime under controlled outbound + clock conditions, without leaking emails to real recipients or running against today's calendar. Supersedes legacy `bpm-acceptance-sandbox`, `bpm-sandbox-clock-and-state`, and `bpm-sandbox-message-capture` specs.

## Requirements

### Requirement: Three configurable controls

The Sandbox page SHALL expose exactly three configuration controls in v0:

1. **Scope** — `all` (all flows in this admin instance) OR `specific` (multi-select of flow IDs)
2. **Mail intercept** — on/off toggle plus a redirect address field
3. **Clock override** — on/off toggle plus a fixed timestamp

No other sandbox controls (e.g., webhook intercept, SMS, mailbox inbox UI) SHALL appear in v0.

#### Scenario: Defaults at first load
- **WHEN** the admin opens the Sandbox page for the first time
- **THEN** mail intercept is off, clock override is off, scope is `all`

### Requirement: Mail intercept redirects rather than captures

When mail intercept is on, bpm SHALL replace the actual recipient(s) of outgoing emails with the configured redirect address(es). The system SHALL NOT store mail in a sandbox mailbox UI inside admin.

#### Scenario: Multiple redirect targets via comma
- **WHEN** the redirect address is set to `qa@flowcook.com, dev@flowcook.com`
- **THEN** every intercepted email is sent to both addresses

#### Scenario: Original recipient preserved in body
- **WHEN** an email is intercepted
- **THEN** the email body SHALL include a note identifying the original recipient(s)

### Requirement: Clock override only supports freeze mode in v0

When clock override is on, bpm SHALL return the configured timestamp from every clock read. `offset` and `speed` modes are out of v0 scope.

#### Scenario: Frozen clock applies to SLA evaluation
- **WHEN** clock override is on with `fixed_time = 2026-12-31T23:59:00Z`
- **AND** an SLA timer is evaluated
- **THEN** the timer compares against the frozen time, not the real wall clock

### Requirement: Webhook and SMS are not intercepted

In v0 the sandbox SHALL NOT intercept webhook or SMS outbound calls. The SMS feature is removed entirely. Webhooks SHALL execute against the real configured endpoints.

#### Scenario: Sandbox emits a real webhook
- **WHEN** a flow under sandbox triggers an integration webhook
- **THEN** the HTTP call goes to the real `${var}`-resolved endpoint
- **AND** QA staff configure dummy endpoints themselves if needed

### Requirement: Persona switch lives in Site Setting

The legacy `Persona switch` capability (act as another user for testing) SHALL be configured on Site Setting (page 5), not on the Sandbox page. Site Setting SHALL store the list of users allowed to switch personas.

#### Scenario: Persona-switch user list on Site Setting
- **WHEN** an admin opens Site Setting
- **THEN** a control labeled "Persona-switch allow list" lets them add/remove users
- **AND** Sandbox page does NOT carry a duplicate of this control

### Requirement: State reset is replaced by manual soft delete

The legacy `IResetService` integral reset SHALL NOT exist. Instead, admins on the persona-switch allow list SHALL be able to soft-delete individual process instances, tasks, or history rows from the bpm UI's process management page. Hard delete is not exposed in production.

#### Scenario: Allow-listed admin deletes a stuck instance
- **WHEN** an admin on the persona-switch allow list opens bpm's live cases page
- **THEN** they SHALL see a "Delete" action on each instance
- **AND** invoking it sets `deleted_at` (soft delete) and emits an audit event

#### Scenario: SeedCli reset is dev-only
- **WHEN** `SeedCli clear` is run with `ASPNETCORE_ENVIRONMENT=Development`
- **THEN** the command drops and recreates both DBs
- **AND** the same command refuses to run with `Production` env

### Requirement: admin writes sandbox config via bpm API

The Sandbox page SHALL post configuration changes to a bpm endpoint. The bpm runtime SHALL read this config during mail dispatch and clock reads.

#### Scenario: Save sandbox config
- **WHEN** an admin toggles mail intercept on and clicks Save
- **THEN** admin POSTs to `bpm-svc /api/sandbox/config` with the new settings
- **AND** subsequent bpm operations honor the new config

### Requirement: Sandbox config schema

The system SHALL persist sandbox config in the following shape (subject to additive evolution):

```json
{
  "scope": { "mode": "all" | "specific", "flow_ids": ["..."] },
  "mail_intercept": {
    "enabled": bool,
    "redirect_to": "a@x, b@x",
    "preserve_original_recipient_in_body": true
  },
  "clock_override": {
    "enabled": bool,
    "mode": "freeze",
    "fixed_time": "ISO-8601"
  }
}
```

#### Scenario: Config round-trips
- **WHEN** admin saves a config then re-opens the page
- **THEN** all three control values display exactly what was saved
