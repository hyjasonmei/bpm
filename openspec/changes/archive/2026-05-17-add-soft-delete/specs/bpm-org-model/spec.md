## ADDED Requirements

### Requirement: Department and Group support soft-delete

The system SHALL extend `Department` and `Group` entities with `DeletedAt` and `DeletedByUserId` columns. EF query filters SHALL exclude soft-deleted Departments and Groups from default queries. Existing User.IsActive convention is preserved (HR-driven deactivation distinct from administrative soft-delete).

#### Scenario: Soft-deleted dept hidden from picker

- **GIVEN** Department "工程部" is soft-deleted
- **WHEN** the admin opens the User edit dialog's department picker
- **THEN** "工程部" is NOT in the dropdown options
- **AND** an existing user whose department_id points to "工程部" still loads correctly with the dept name + deleted indicator

#### Scenario: Member count blocks delete

- **GIVEN** Group G has 3 GroupMember rows
- **WHEN** admin tries to delete G
- **THEN** 409 with "remove 3 members first"
