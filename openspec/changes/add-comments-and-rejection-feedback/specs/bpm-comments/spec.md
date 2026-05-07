## ADDED Requirements

### Requirement: Comment entity with Kind and IsInternal

The system SHALL persist a `Comment` entity tied to a `ProcessInstance` (and optionally to a `Task`). Each comment carries `AuthorUserId`, `Body` (max 5000 chars), `Kind` (`General` / `RejectionReason` / `ReturnInstruction` / `Clarification` / `AdminNote`), `IsInternal` flag, optional `ReplyToCommentId` (one-level threading only), `EditedAt`, `DeletedAt` (soft-delete), `CreatedAt`. Comments MAY belong to a specific Task or to the Instance overall.

#### Scenario: Instance-level comment

- **WHEN** an admin posts a Comment with TaskId = null
- **THEN** the row is associated with the ProcessInstance only

#### Scenario: Reply to existing comment

- **GIVEN** comment C1 exists at top level
- **WHEN** an author posts a comment with ReplyToCommentId = C1
- **THEN** the new comment is a 1-level reply

#### Scenario: Reply to a reply rejected

- **GIVEN** C2 is already a reply (ReplyToCommentId is set)
- **WHEN** an author tries to post a comment with ReplyToCommentId = C2
- **THEN** the validator rejects with "replies cannot be nested"

### Requirement: 15-minute edit window enforced

A comment SHALL be editable by its author for 15 minutes after creation. After 15 minutes, only `tenant_admin` or `flow_admin` may edit (with audit). The `EditedAt` field SHALL be set whenever a comment is edited; the original body is NOT preserved (replacement is the model).

#### Scenario: Author edit within window

- **GIVEN** a comment created 5 minutes ago by Wilson
- **WHEN** Wilson PATCHes with new body
- **THEN** 200 OK; EditedAt set; LastModifiedAt updated

#### Scenario: Author edit after window blocked

- **GIVEN** the comment is now 16 minutes old
- **WHEN** Wilson PATCHes
- **THEN** 403 with reason "edit window expired"

#### Scenario: Admin override edit

- **WHEN** an admin PATCHes a 16-minute-old comment
- **THEN** 200 OK; EditedAt + LastModifiedAt updated; an audit row records the admin override

### Requirement: Soft-delete preserves audit

`DELETE /api/comments/{id}` SHALL be admin-only and SHALL be a soft-delete: the row remains, `Body` is replaced with `"[已刪除]"`, `DeletedAt` is set, the original body is NOT stored separately. The frontend renders the placeholder strikethrough.

#### Scenario: Non-admin delete blocked

- **WHEN** a regular user calls DELETE /api/comments/{id}
- **THEN** 403

#### Scenario: Admin delete obscures body

- **GIVEN** a comment with body "secret stuff"
- **WHEN** admin DELETEs it
- **THEN** Body = "[已刪除]"; DeletedAt set; row preserved in DB

### Requirement: IsInternal restricts visibility

Comments with `IsInternal = true` SHALL be visible only to the author, `tenant_admin`, and `flow_admin` users. Regular instance participants (applicant, assignees) SHALL NOT see them via any API. Querying with `?include_internal=true` is filtered server-side per the user's role.

#### Scenario: Internal comment hidden from applicant

- **GIVEN** an admin posts an internal comment on Wilson's instance
- **WHEN** Wilson calls GET /api/processes/{id}/comments
- **THEN** the response excludes the internal comment

#### Scenario: Admin sees internal

- **WHEN** an admin calls the same endpoint
- **THEN** the internal comment IS in the response (assuming include_internal=true)

#### Scenario: Internal flag requires admin author

- **WHEN** a regular user tries to POST a comment with IsInternal = true
- **THEN** the validator rejects with "only admins can post internal comments"

### Requirement: Comment list ordering and pagination

`GET /api/processes/{id}/comments` SHALL return comments ordered by `CreatedAt ASC` with reply chains grouped after their parent. Pagination via `?limit=50&before=ISO8601` cursor. Default limit 50, max 200.

#### Scenario: Replies group with parent

- **GIVEN** comments [C1 (12:00), C2 (12:01, reply to C1), C3 (12:02)]
- **WHEN** the response is built
- **THEN** the order is C1, C2 (its reply), C3

### Requirement: Mark-read tracks per user

`POST /api/comments/{id}/mark-read` SHALL record that the calling user has read the comment. The system SHALL track read-state per (comment, user) pair via a `CommentRead` table or denormalized array. Future UI can show unread badges.

#### Scenario: Mark-read idempotent

- **WHEN** Wilson marks comment C1 read twice
- **THEN** the second call is idempotent (200 OK, no duplicate state)
