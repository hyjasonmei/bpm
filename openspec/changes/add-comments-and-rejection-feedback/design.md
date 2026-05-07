# Design notes

## 1. Why a separate Comment entity, not Task.CommentText[]

Task.Comment was a single string per task — fine for "the manager's note on this approval". A real conversation needs:

- Multi-author replies on the same task ("Manager: missing stamp" → "Applicant: see updated upload" → "Manager: approved")
- Instance-level comments not tied to a specific task ("HR note: this case escalated to legal review")
- Internal-only commentary not seen by the applicant

A separate Comment entity with TaskId nullable + IsInternal flag covers all these cleanly. Task.Comment stays as a *first* comment shorthand on submit; if the user wants to follow up, they post additional Comment rows.

## 2. Auto-comments on Reject / Return / Approve-with-comment

Currently `Task.Comment` is recorded in the Task row itself. Mirror it as a Comment row of `Kind = RejectionReason / ReturnInstruction / General`. This keeps the conversation log in one place — the CommentThread reads from the Comments table and shows everything chronologically (including the inline-from-task ones).

To avoid duplication: the Task row's Comment field is *the source of truth* for the submit comment; the auto-Comment row is a denormalized projection inserted in the same transaction. The CommentThread reads only from Comments table.

## 3. 15-minute edit window

Common UX pattern (Slack / Teams). After 15 minutes:

- Author can no longer edit (UI hides the edit button; API rejects)
- Admin still can edit (rare, with audit row in TaskHistory)
- Author can still soft-delete via admin support

15 min is enough for typo fixes; preserves audit trail beyond that.

## 4. Soft-delete preserves audit

Deleted comments don't disappear — body is replaced with `"[已刪除]"` but row remains. UI shows the strikethrough'd placeholder. This satisfies compliance ("don't actually erase, but obscure"); auditors can still see who deleted when.

## 5. IsInternal vs visibility

Two-layer model:

- `IsInternal = false` (default): visible to anyone with read access to the ProcessInstance (applicant + assignees + admins)
- `IsInternal = true`: visible to author + all flow_admin + tenant_admin only — not the applicant or other assignees

Use case: HR notes a case escalated to legal; the applicant doesn't need to see the internal back-and-forth.

`include_internal` query parameter on `GET /api/processes/{id}/comments` controls the response — but server still filters per the user's role; passing `include_internal=true` while not having the role returns the same list as `false`.

## 6. Reply threading — one level only

Slack-style "thread under a message". One level: a top-level comment may have replies; a reply may NOT have its own replies. Avoids the deep-tree-rendering complexity.

If a customer needs deeper threading, they can post a fresh top-level comment quoting the relevant context. v2 may add multi-level if demand exists.

## 7. on_comment_added notification trigger

Adds another item to NotificationTrigger enum. Spec authors who want "notify applicant on rejection" can write:

```jsonc
{
  "id": "notify_applicant_rejection",
  "trigger": "on_comment_added",
  "channel": ["email", "in_app"],
  "recipients": [{ "type": "submitter" }],
  "template": {
    "subject": { "zh-TW": "您的申請有新回覆: {{comment.kind}}" },
    "body": { "zh-TW": "{{comment.author_name}} 在 {{when}} 留言:\n\n{{comment.body}}" },
    "variables": ["comment.kind", "comment.author_name", "when", "comment.body"]
  }
}
```

The dispatcher receives an extended NotificationContext with `comment` field. Variables prefixed with `comment.` resolve from there.

To prevent feedback loops (notification posted → triggers comment → another notification → ...), the dispatcher detects when a Comment is system-generated (Kind = AdminNote with author = system user) and short-circuits.

## 8. Open questions

- **Comment edit count**: should we display "(edited)" next to edited comments? Probably yes — minor polish for v1.
- **Bulk operations** for admins: select multiple comments, mark read / dismiss. Defer.
- **Comment counts in case lists**: showing a "3 comments" badge per case in the user's pending list — useful UX hint. Defer to admin UI proposal.
- **Mention notification when @username**: future feature; needs autocomplete for user picker inside comment editor.
