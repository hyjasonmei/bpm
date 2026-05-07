# Tasks

## 1. Domain

- [ ] 1.1 Create `bpm-svc/src/Domain/Entities/Comment/CommentKind.cs` enum (General, RejectionReason, ReturnInstruction, Clarification, AdminNote)
- [ ] 1.2 Create `bpm-svc/src/Domain/Entities/Comment/Comment.cs` (inherits AuditableEntity); fields per proposal

## 2. Persistence

- [ ] 2.1 EF config under `bpm-svc/src/Persistence/Configurations/Comment/`
- [ ] 2.2 Indexes: `(ProcessInstanceId, CreatedAt)`, `(TaskId)`, `(AuthorUserId, CreatedAt DESC)`, `(ReplyToCommentId)`
- [ ] 2.3 Migration `AddComments`; apply locally

## 3. Service

- [ ] 3.1 Create `ICommentService.cs` with Create / Edit / List / MarkRead / Delete
- [ ] 3.2 Implement `CommentService.cs`:
  - Auth: read = any instance reader (applicant, assignee, admin); write = same; edit = author within 15 min OR admin; delete = admin only
  - Internal flag: only flow_admin/tenant_admin can mark IsInternal = true; only same set can read internal comments
  - Reply: validate ReplyToCommentId points to a non-reply comment (no double-nesting)
- [ ] 3.3 Wire DI

## 4. Runtime integration

- [ ] 4.1 Update `ProcessRuntime.SubmitTaskAsync`:
  - When Decision = Reject, validate Comment is non-empty; reject with ValidationException if empty
  - Auto-create Comment row with Kind = RejectionReason, TaskId, AuthorUserId = actor, body = task.Comment
- [ ] 4.2 Update `ProcessRuntime.ReturnTaskAsync`:
  - Validate Comment non-empty; reject if empty
  - Auto-create Comment row with Kind = ReturnInstruction
- [ ] 4.3 For Approve with non-empty Comment: auto-create Kind = General comment row
- [ ] 4.4 Tests: rejection without comment → 400; rejection with comment → success + Comment row

## 5. Notification trigger

- [ ] 5.1 Extend `NotificationTrigger` enum with `OnCommentAdded`
- [ ] 5.2 Update spec_schema.md
- [ ] 5.3 Dispatcher: when comment created, fire matching notifications with extended NotificationContext containing `comment` block (kind, author_name, body, when)
- [ ] 5.4 Loop guard: skip dispatch when Comment.AuthorUserId = system user

## 6. API endpoints

- [ ] 6.1 Create `bpm-svc/src/Api/Comments/CommentsController.cs`:
  - `GET /api/processes/{id}/comments?include_internal=true`
  - `POST /api/processes/{id}/comments` body `{ body, kind?, taskId?, replyToCommentId?, isInternal? }`
  - `PATCH /api/comments/{id}` body `{ body }`
  - `POST /api/comments/{id}/mark-read`
  - `DELETE /api/comments/{id}` admin only
- [ ] 6.2 Tests for each (auth, edit window expiry, internal flag, soft-delete preserves row)

## 7. Frontend — types + API client

- [ ] 7.1 Create `bpm-ui/src/lib/comments.ts`: TS types + API client + `useComments(instanceId)` polling hook (60s)
- [ ] 7.2 Tracking read state per user; UI shows unread count

## 8. Frontend — components

- [ ] 8.1 Create `bpm-ui/src/components/comments/CommentItem.tsx`: single comment renderer (author chip, kind badge, body, timestamp, edit/delete actions, lock icon for internal)
- [ ] 8.2 Create `bpm-ui/src/components/comments/CommentEditor.tsx`: post + edit (15-min countdown timer hint)
- [ ] 8.3 Create `bpm-ui/src/components/comments/CommentThread.tsx`: list + reply UI; bilingual labels
- [ ] 8.4 Mount CommentThread in `TaskExecution.tsx` below the form
- [ ] 8.5 Internal-only comments visually distinct (shaded + 🔒 chip)

## 9. End-to-end verification

- [ ] 9.1 `dotnet build` clean
- [ ] 9.2 All tests pass
- [ ] 9.3 Apply migration; verify Comments table
- [ ] 9.4 Boot stack; reject a task; verify Comment row created with Kind = RejectionReason
- [ ] 9.5 Try to reject without comment via API; verify 400
- [ ] 9.6 Open TaskExecution; verify CommentThread mounts with the rejection reason; post a reply; verify it appears
- [ ] 9.7 Edit your own comment within 15 min; succeed. Wait 16 min; edit fails (manually fudged via DB)
- [ ] 9.8 Admin: post an internal comment; verify regular user does NOT see it; admin user does
- [ ] 9.9 **Demo guard**: 9 mock-up forms, Home, Search, Report, lib/workflow.ts NOT modified

## 10. Commit

- [ ] 10.1 Commit in chunks (entity + migration; service + auth; runtime hook; trigger; endpoints; frontend; tests)
- [ ] 10.2 Push via GitKraken
