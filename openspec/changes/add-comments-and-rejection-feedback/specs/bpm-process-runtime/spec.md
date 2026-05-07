## ADDED Requirements

### Requirement: Reject and Return require non-empty Comment

The runtime SHALL validate that a `Task.Comment` is non-empty (after trim) when:

- The submit Decision is `Reject`
- The submit is via `ReturnTaskAsync`

If empty, the runtime MUST throw `ValidationException` returning HTTP 400 with a clear message ("rejection requires a reason" / "return requires an instruction").

#### Scenario: Reject without comment blocked

- **WHEN** an approver submits with Decision = Reject and Comment = ""
- **THEN** 400 with "rejection requires a reason"; task remains Pending

#### Scenario: Reject with comment succeeds

- **WHEN** the approver submits with Decision = Reject and Comment = "missing stamp"
- **THEN** 200 OK; task Status = Completed with Decision = Reject; comment row auto-created

### Requirement: Submit / Reject / Return auto-creates Comment row

For every submit that includes a non-empty Comment, the runtime SHALL create a `Comment` row in the same transaction:

- Reject → Kind = `RejectionReason`
- Return → Kind = `ReturnInstruction`
- Approve with comment → Kind = `General`

The auto-created comment carries `TaskId` set to the submitting task, `AuthorUserId` = the actor, `IsInternal = false`. This duplicates the Task's text into the Comment thread for unified display.

#### Scenario: Reject auto-creates RejectionReason comment

- **WHEN** approval submits Reject with comment "請補上醫生簽章"
- **THEN** a Comment row is inserted with Kind = RejectionReason, Body = "請補上醫生簽章", TaskId set, AuthorUserId = approver

#### Scenario: Approve without comment does NOT create a Comment row

- **WHEN** approval submits Approve with empty Comment
- **THEN** no auto Comment row inserted (Task.Comment stays null; that's the canonical record)
