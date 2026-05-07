## ADDED Requirements

### Requirement: Email is the immutable identity key for User upsert

For HR sync purposes, the system SHALL treat `User.Email` as the immutable identity key. Upsert operations match by email; if email is missing or changes, the importer treats the row as a new identity. Email is unique per tenant; the existing unique index on Email is reaffirmed.

When a person's email genuinely changes (rare — name change, marriage), the importer creates a new User row and (with opt-in) deactivates the old. Manual merge tooling for "join these two identities" is out of scope.

#### Scenario: Re-import same emails recognizes existing

- **GIVEN** a User with Email = wilson@x.com exists
- **WHEN** a CSV with the same email is imported
- **THEN** the existing User row is updated (not duplicated); Email value unchanged

#### Scenario: Email change creates new + deactivates old (opt-in)

- **GIVEN** existing User wilson@x.com (UserId = U1)
- **WHEN** a CSV brings the same person under wilson_lin@x.com
- **AND** `deactivate_missing = true` is set
- **THEN** a new User row is INSERTED (UserId = U2, Email = wilson_lin@x.com); U1 is deactivated (IsActive = false)
- **AND** an admin tool (out of scope here) can later merge U1's history into U2

### Requirement: Soft-delete pattern preserves history

The HR sync importer SHALL only ever set `IsActive = false` for missing users — never hard-delete. This preserves all foreign-key relationships from past tasks, comments, file ownership, etc. The runtime's existing handling of inactive users (resolver excludes, delegation falls back) handles the downstream effects gracefully.

#### Scenario: Inactive User retains FK references

- **GIVEN** wilson@x.com had submitted an LEAVE instance last month
- **AND** wilson is now deactivated via HR sync
- **WHEN** an admin queries the historical instance
- **THEN** the instance still references wilson's UserId; instance.InitiatorUserId remains valid; wilson's ProcessTask rows still exist with their original ActualAssigneeUserId
