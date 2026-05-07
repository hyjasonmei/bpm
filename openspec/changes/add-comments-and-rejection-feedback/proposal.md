## Why

Real flows have conversations:

- Manager rejects a leave request — applicant needs to know *why* ("Your medical certificate is missing the doctor's stamp")
- Approver returns a form for revision — needs a clear instruction ("Please add the project budget code")
- Applicant clarifies after rejection ("The certificate IS stamped — see updated upload")
- HR archives an unusual case with a note for future reference

Today:

- `Task.Comment` (string, max 2000) was added in `add-process-runtime` — single string per submit
- Hand-coded LeaveForm has placeholder text ("Applicant will be notified") but no actual mechanism
- No multi-comment thread on a case
- No way for an applicant to reply to a rejection without starting a new flow

This change ships a typed comment / annotation thread per ProcessInstance plus structured "return with reason" semantics in the runtime.

## What Changes

### Comments capability (NEW `bpm-comments`)

**Entity** — `Comment`:

- `Id`, `TenantId`, `ProcessInstanceId`, `TaskId` (nullable — instance-level comments allowed)
- `AuthorUserId`
- `Body` (text, max 5000)
- `Kind` (enum): `General` / `RejectionReason` / `ReturnInstruction` / `Clarification` / `AdminNote`
- `IsInternal` (bool) — when true, only flow_admin/tenant_admin/Author can read; intended for HR / IT internal commentary not visible to applicant
- `ReplyToCommentId` (nullable Guid) — flat threading; one level only (no nested-replies tree)
- `EditedAt` (nullable) — set if the comment was edited; only the author or admin can edit, within 15 minutes of post; immutable thereafter
- `CreatedAt`, `LastModifiedAt`

**Service** `ICommentService`:

- `CreateAsync(CreateCommentCommand)` — author posts a comment
- `EditAsync(EditCommentCommand)` — author edits within 15 minutes
- `ListByInstanceAsync(instanceId, includeInternal)` — auth-aware list
- `MarkAsReadAsync(commentIds, userId)` — track per-user read state
- `DeleteAsync(commentId, actorUserId)` — admin only soft-delete (sets DeletedAt; comment text replaced by "[已刪除]" but row preserved for audit)

### Runtime integration: rejection requires reason; return requires instruction

`add-process-runtime` shipped `Task.Comment` as optional. This change:

- Enforces **rejection** requires a Comment (validation: when Decision = Reject, Comment.Length > 0)
- Enforces **return** requires a Comment (a separate, runtime-validated check)
- Auto-creates a Comment row of `Kind = RejectionReason` (with TaskId) on every Reject
- Auto-creates a Comment row of `Kind = ReturnInstruction` on every Return
- Both auto-comments are visible to: the applicant, all task-assignees, and admins

For Approve, Comment is still optional but if provided, auto-creates a `Kind = General` Comment.

### Frontend — comment thread on TaskExecution

`bpm-ui/src/components/comments/CommentThread.tsx`:

- Renders comments newest first
- Shows author name + relative timestamp + Kind chip
- Reply input at bottom (only when feature enabled / not approval-restricted)
- Edit / delete affordances based on auth
- Internal comments shown with a 🔒 badge to authorized viewers

`bpm-ui/src/screens/TaskExecution.tsx` (updated): mounts CommentThread below the form.

### Frontend — comment thread on instance detail

A future ProcessAdmin "case detail" page will mount the same component. For now, an instance's comments are accessible via `GET /api/processes/{id}/comments`.

### API endpoints

- `GET /api/processes/{id}/comments?include_internal=true` — auth-aware
- `POST /api/processes/{id}/comments` — body: `{ body, kind?, taskId?, replyToCommentId?, isInternal? }`
- `PATCH /api/comments/{id}` — author edits within 15 minutes
- `POST /api/comments/{id}/mark-read` — current user
- `DELETE /api/comments/{id}` — admin only, soft

### Notification integration

A new internal notification trigger `on_comment_added` fires when a comment is created. Spec authors can opt into notifying interested parties (e.g., applicant on every rejection).

### Out of scope (future changes)

- @mentions inside comments (parsing / notifications)
- Rich text formatting (Markdown / HTML)
- Comment reactions (👍)
- Threaded multi-level replies (this proposal: 1-level reply only)
- Attachments inline within comments (use the file storage capability separately)
- Read-receipt notifications back to author
- Comment search across instances (defer to global search proposal)
- Auto-summarize long threads (AI feature, deferred)
- Comments on individual form fields (annotation per field)

## Capabilities

### New Capabilities

- `bpm-comments` — Comment entity, ICommentService, REST API (list/create/edit/mark-read/delete), 5000-char body, 5 Kinds, 1-level replies, 15-minute edit window, soft-delete with author/admin auth.

### Modified Capabilities

- `bpm-process-runtime` — Reject requires non-empty comment; Return requires non-empty comment; auto-creates a Comment row tied to the Task on every Reject/Return/Approve-with-comment; raises `on_comment_added` notification trigger.
- `bpm-form-runtime` — TaskExecution screen mounts `<CommentThread>` below the form; rejection / return UI surfaces required comment input.

## Impact

- **bpm-svc/src/Domain/Entities/Comment/Comment.cs**: new entity + Kind / IsInternal fields
- **bpm-svc/src/Application/Comments/ICommentService.cs / CommentService.cs**: orchestration
- **bpm-svc/src/Persistence/Configurations/Comment/**: EF config; migration `AddComments`
- **bpm-svc/src/Api/Comments/CommentsController.cs**: 5 endpoints
- **bpm-svc/src/Application/Process/Runtime/ProcessRuntime.cs**: enforces required comments on Reject/Return; auto-creates Comment rows
- **bpm-svc/src/Domain/Spec/NotificationTrigger.cs**: add `OnCommentAdded`
- **bpm-ui/src/components/comments/CommentThread.tsx**: list + reply input
- **bpm-ui/src/components/comments/CommentItem.tsx**: single comment rendering
- **bpm-ui/src/components/comments/CommentEditor.tsx**: post / edit
- **bpm-ui/src/screens/TaskExecution.tsx**: integration
- **bpm-ui/src/lib/comments.ts**: types + API client
- **DB migration** additive: 1 new table
- **No NuGet additions**
- **Demo guard**: 9 mock-up forms NOT modified
