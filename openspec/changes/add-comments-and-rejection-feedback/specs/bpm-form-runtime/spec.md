## ADDED Requirements

### Requirement: TaskExecution mounts CommentThread

The `TaskExecution` screen SHALL mount a `<CommentThread>` component below the form area, listing all visible comments for the current ProcessInstance. The thread SHALL include a comment editor at the bottom for posting new comments. Internal-only comments are visible only to authorized roles.

#### Scenario: Thread visible on task page

- **WHEN** Wilson opens his open task at /tasks/{id}
- **THEN** the comment thread renders below the form, showing prior comments (e.g., a Reject reason from a previous attempt)

### Requirement: Approval kind UI requires comment for Reject and Return

The Approval-kind submit UI SHALL disable the Reject and Return buttons until a non-empty comment is entered in the comment editor. Approve button stays enabled regardless.

#### Scenario: Reject button disabled until comment

- **GIVEN** the Approval-kind TaskExecution UI is open
- **AND** the comment editor is empty
- **WHEN** the user looks at the buttons
- **THEN** the Reject and Return buttons are visually disabled with a tooltip "請填寫原因再退回"

#### Scenario: Reject button enabled after comment typed

- **WHEN** the user types one character into the comment
- **THEN** Reject and Return buttons enable
